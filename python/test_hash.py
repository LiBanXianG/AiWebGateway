# Python/test_hash.py
import wasmtime
import ctypes
import struct
import json
import base64

wasm_path = "sha3_wasm_bg.wasm"
engine = wasmtime.Engine()
module = wasmtime.Module.from_file(engine, wasm_path)
store = wasmtime.Store(engine)
instance = wasmtime.Instance(store, module, [])
exports = instance.exports(store)

memory = exports["memory"]
malloc = exports["__wbindgen_export_0"]
wasm_hash = exports["wasm_deepseek_hash_v1"]
add_to_stack = exports["__wbindgen_add_to_stack_pointer"]

mem_base = ctypes.cast(memory.data_ptr(store), ctypes.c_void_p).value

def write_bytes(data: bytes):
    ptr = malloc(store, len(data), 1)
    ctypes.memmove(mem_base + ptr, data, len(data))
    return ptr, len(data)

def read_u32(ptr):
    return ctypes.c_uint32.from_address(mem_base + ptr).value

def read_bytes(ptr, n):
    return bytes((ctypes.c_uint8 * n).from_address(mem_base + ptr))

def wasm_hash_str(text: str) -> str:
    ptr, length = write_bytes(text.encode("utf-8"))
    retptr = add_to_stack(store, -16)
    try:
        wasm_hash(store, retptr, ptr, length)
        ptr_out = read_u32(retptr)
        len_out = read_u32(retptr + 4)
        if len_out == 0 or len_out > 200:
            raise RuntimeError(f"异常: ptr={ptr_out}, len={len_out}")
        return read_bytes(ptr_out, len_out).decode("utf-8")
    finally:
        add_to_stack(store, 16)

# ─── 真实数据 ───
challenge = "be524001179ca7aeadb8e4b470b7379d2aa5b223d91990ba3c31fe7d2be80a3c"
salt = "eaf6f58438a6b6e0abc6"
answer = 81656
expire_at = 1791464822404
target_sig = "c46b858861060d93e0bdfb89e7bbfa12f59a98302f1b3c1b1809530b6c589c1f"

prefix = f"{salt}_{expire_at}_"
a = str(answer)

print("=" * 60)
print("测试 1：完整拼接字符串")
print("=" * 60)
candidates_1 = [
    f"{challenge}{prefix}{a}",
    f"{prefix}{a}",
    f"{challenge}{a}",
    f"{challenge}{prefix}",
]
for text in candidates_1:
    try:
        sig = wasm_hash_str(text)
        mark = "匹配" if sig == target_sig else "错误"
        print(f"{mark}  in={text[:60]}...")
        print(f"       out={sig}")
    except Exception as e:
        print(f"in={text[:60]}...  异常: {e}")

print()
print("=" * 60)
print("测试 2：answer 作为二进制 Float64")
print("=" * 60)
answer_le = struct.pack("<d", float(answer))
answer_be = struct.pack(">d", float(answer))

candidates_2 = [
    ("challenge+prefix+le", challenge.encode() + prefix.encode() + answer_le),
    ("challenge+prefix+be", challenge.encode() + prefix.encode() + answer_be),
    ("prefix+le", prefix.encode() + answer_le),
    ("challenge+le", challenge.encode() + answer_le),
]
for name, data in candidates_2:
    try:
        sig = wasm_hash_str(data.decode("latin-1"))
        mark = "匹配" if sig == target_sig else "错误"
        print(f"{mark}  {name}")
        print(f"       out={sig}")
    except Exception as e:
        print(f"{name}  异常: {e}")

print()
print("=" * 60)
print("测试 3：仅对 answer 的多种表示求哈希")
print("=" * 60)
candidates_3 = [
    a,
    f"{answer}",
    str(answer * 1000),
    f"{answer}\n",
    f"{answer} ",
]
for text in candidates_3:
    try:
        sig = wasm_hash_str(text)
        mark = "匹配" if sig == target_sig else "错误"
        print(f"{mark}  in={text!r}")
        print(f"       out={sig}")
    except Exception as e:
        print(f"in={text!r}  异常: {e}")

print()
print("=" * 60)
print("测试 4：对 challenge 和 salt 的多种组合求哈希")
print("=" * 60)
candidates_4 = [
    challenge,
    salt,
    f"{challenge}{salt}",
    f"{salt}{challenge}",
    f"{challenge}_{salt}",
]
for text in candidates_4:
    try:
        sig = wasm_hash_str(text)
        mark = "匹配" if sig == target_sig else "错误"
        print(f"{mark}  in={text[:60]}...")
        print(f"       out={sig}")
    except Exception as e:
        print(f"in={text[:60]}...  异常: {e}")