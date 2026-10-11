using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;

namespace AiWebGateway.Services;

public class GatewayService : IDisposable
{
    private Process? _process;
    private readonly object _lock = new();

    public event Action<string>? LogReceived;
    public event Action<string, int>? Started;
    public event Action? Stopped;
    public event Action<string>? ErrorOccurred;

    public bool IsRunning
    {
        get
        {
            lock (_lock)
            {
                return _process is { HasExited: false };
            }
        }
    }

    public void Start(string host, int port)
    {
        lock (_lock)
        {
            if (_process is { HasExited: false })
            {
                ErrorOccurred?.Invoke("服务已在运行中");
                return;
            }
        }

        // 后台清理旧进程
        Task.Run(() => CleanupStaleProcesses());

        var baseDir = AppDomain.CurrentDomain.BaseDirectory;
        var pythonExe = Path.Combine(baseDir, "envs", "python_embed", "python.exe");
        var workDir = Path.Combine(baseDir, "envs", "python_embed", "Python");

        if (!File.Exists(pythonExe))
        {
            ErrorOccurred?.Invoke($"找不到嵌入式 Python: {pythonExe}");
            return;
        }
        if (!Directory.Exists(workDir))
        {
            ErrorOccurred?.Invoke($"找不到 Python 源码目录: {workDir}");
            return;
        }

        var psi = new ProcessStartInfo
        {
            FileName = pythonExe,
            Arguments = $"-X utf8 -m uvicorn main:app --host {host} --port {port}",
            WorkingDirectory = workDir,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
            StandardOutputEncoding = System.Text.Encoding.UTF8,
            StandardErrorEncoding = System.Text.Encoding.UTF8,
            WindowStyle = ProcessWindowStyle.Hidden,
        };

        var proc = new Process { StartInfo = psi, EnableRaisingEvents = true };
        proc.OutputDataReceived += (_, e) => { if (e.Data is not null) LogReceived?.Invoke(e.Data); };
        proc.ErrorDataReceived += (_, e) => { if (e.Data is not null) LogReceived?.Invoke($"[ERR] {e.Data}"); };
        proc.Exited += (_, _) => { try { Stopped?.Invoke(); } catch { } };

        try
        {
            LogReceived?.Invoke($"[Gateway] 启动: {pythonExe}");
            LogReceived?.Invoke($"[Gateway] 工作目录: {workDir}");
            LogReceived?.Invoke($"[Gateway] 参数: {psi.Arguments}");

            proc.Start();
            proc.BeginOutputReadLine();
            proc.BeginErrorReadLine();

            lock (_lock) { _process = proc; }

            Started?.Invoke(host, port);
        }
        catch (Exception ex)
        {
            try { proc.Dispose(); } catch { }
            ErrorOccurred?.Invoke($"启动失败: {ex.Message}\n路径: {pythonExe}\n目录: {workDir}");
        }
    }

    /// <summary>同步 Stop，用于退出流程；不改动 UI，直接返回</summary>
    public void Stop()
    {
        Process? proc;
        lock (_lock)
        {
            proc = _process;
            _process = null;
        }
        if (proc == null) return;

        KillQuietly(proc);
    }

    /// <summary>异步 Stop，不阻塞 UI</summary>
    public Task StopAsync()
    {
        return Task.Run(() => Stop());
    }

    private void KillQuietly(Process proc)
    {
        try
        {
            if (!proc.HasExited)
            {
                // 1. 先尝试标准关闭
                try { proc.Kill(entireProcessTree: true); } catch { }

                // 2. 只等 500ms，超时就不管
                try { proc.WaitForExit(500); } catch { }
            }
        }
        catch { }
        finally
        {
            // 3. 清理进程对象
            Task.Run(() =>
            {
                try { proc.Dispose(); } catch { }
            });
        }
    }

    private void CleanupStaleProcesses()
    {
        try
        {
            // 清理日志（7 天前）
            var logDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "logs");
            if (Directory.Exists(logDir))
            {
                var cutoff = DateTime.Now.AddDays(-7);
                int deleted = 0;
                foreach (var file in Directory.GetFiles(logDir, "gateway-*.log"))
                {
                    try
                    {
                        if (File.GetLastWriteTime(file) < cutoff)
                        {
                            File.Delete(file);
                            deleted++;
                        }
                    }
                    catch { }
                }
                if (deleted > 0)
                    LogReceived?.Invoke($"[Gateway] 清理了 {deleted} 个过期日志");
            }

            // 清理残留 python 进程
            var stale = Process.GetProcessesByName("python");
            int killed = 0;
            foreach (var p in stale)
            {
                try
                {
                    p.Kill(entireProcessTree: true);
                    killed++;
                }
                catch { }
                finally
                {
                    try { p.Dispose(); } catch { }
                }
            }
            if (killed > 0)
                LogReceived?.Invoke($"[Gateway] 已清理 {killed} 个残留 Python 进程");
        }
        catch { }
    }

    public void Dispose()
    {
        Stop();
    }
}