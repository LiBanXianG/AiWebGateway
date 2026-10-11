# Python/core/pow_solver.py
import json
import base64
import asyncio
import hashlib
import ctypes
from pathlib import Path
from typing import Optional

import wasmtime


class DeepSeekPowSolver:
    DEFAULT_WASM = "sha3_wasm_bg.wasm"

    def __init__(self, wasm_path: Optional[str] = None):
        if wasm_path is None:
            wasm_path = str(Path(__file__).parent.parent / self.DEFAULT_WASM)
        self.wasm_path = wasm_path
        self.engine = None
        self.store = None
        self.instance = None
        self.exports = None
        self.memory = None
        self.memory_view = None
        self._wasm_solve = None
        self._wasm_hash = None
        self._malloc = None
        self._add_to_stack = None
        self._init_wasm()

    def _init_wasm(self):
        if not Path(self.wasm_path).exists():
            raise FileNotFoundError(f"WASM file not found: {self.wasm_path}")

        self.engine = wasmtime.Engine()
        module = wasmtime.Module.from_file(self.engine, self.wasm_path)
        self.store = wasmtime.Store(self.engine)
        self.instance = wasmtime.Instance(self.store, module, [])
        self.exports = self.instance.exports(self.store)

        export_names = list(self.exports)
        self.memory = self.exports["memory"]
        self._wasm_solve = self.exports["wasm_solve"]
        self._wasm_hash = self.exports["wasm_deepseek_hash_v1"]

        _raw_ptr = self.memory.data_ptr(self.store)
        self.memory_view = ctypes.cast(_raw_ptr, ctypes.c_void_p).value

        for name in ["__wbindgen_malloc", "__wbindgen_export_0", "malloc"]:
            if name in export_names:
                self._malloc = self.exports[name]
                break
        if self._malloc is None:
            raise RuntimeError("WASM malloc not found")

        for name in ["__wbindgen_add_to_stack_pointer", "add_to_stack_pointer"]:
            if name in export_names:
                self._add_to_stack = self.exports[name]
                break
        if self._add_to_stack is None:
            raise RuntimeError("WASM stack pointer not found")

    def _write_bytes(self, data: bytes) -> tuple[int, int]:
        length = len(data)
        ptr = self._malloc(self.store, length, 1)
        if ptr == 0:
            raise RuntimeError("WASM malloc returned 0")
        ctypes.memmove(self.memory_view + ptr, data, length)
        return ptr, length

    def _read_u32(self, ptr: int) -> int:
        return ctypes.c_uint32.from_address(self.memory_view + ptr).value

    def _read_f64(self, ptr: int) -> float:
        return ctypes.c_double.from_address(self.memory_view + ptr).value

    async def solve(self, algorithm, challenge, salt, difficulty, expire_at,
                    signature: str = "") -> str:
        if algorithm != "DeepSeekHashV1":
            raise ValueError(f"unsupported algorithm: {algorithm}")

        prefix = f"{salt}_{expire_at}_"

        challenge_ptr, challenge_len = self._write_bytes(challenge.encode("utf-8"))
        prefix_ptr, prefix_len = self._write_bytes(prefix.encode("utf-8"))

        retptr = self._add_to_stack(self.store, -16)
        try:
            self._wasm_solve(
                self.store, retptr,
                challenge_ptr, challenge_len,
                prefix_ptr, prefix_len,
                float(difficulty),
            )
            status = self._read_u32(retptr)
            if status == 0:
                raise RuntimeError("PoW solve failed (status=0)")
            answer = int(self._read_f64(retptr + 8))
        finally:
            self._add_to_stack(self.store, 16)

        # signature 由服务端下发，直接复用；若为空则回退计算
        if not signature:
            signature = hashlib.sha3_256(
                f"{challenge}{salt}{answer}".encode()
            ).hexdigest()

        response = {
            "algorithm": algorithm,
            "challenge": challenge,
            "salt": salt,
            "answer": answer,
            "signature": signature,
            "target_path": "/api/v0/chat/completion",
        }
        return base64.b64encode(
            json.dumps(response, separators=(",", ":")).encode()
        ).decode()