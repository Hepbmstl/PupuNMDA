using System;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Python.Runtime;

namespace NeuronCAD.Backward
{
    /// <summary>
    /// 专用 Python 工作线程管理器。
    /// 所有 Python 操作均调度到该固定线程执行，保证 GIL 始终在同一线程获取/释放，
    /// 同时满足 Matplotlib GUI 需要稳定线程的要求。
    /// Python 运行时随此线程存活，直到应用程序关闭时调用 Shutdown()。
    /// </summary>
    public static class PythonWorker
    {
        private static Thread? _workerThread;
        private static BlockingCollection<WorkItem>? _queue;
        private static volatile bool _started;
        private static readonly object _startLock = new();
        private static TaskCompletionSource? _initTcs;

        private sealed class WorkItem
        {
            public required Action Work { get; init; }
            public TaskCompletionSource<object?> Tcs { get; } =
                new(TaskCreationOptions.RunContinuationsAsynchronously);
        }

        /// <summary>
        /// 确保 Python 工作线程已启动并完成初始化。
        /// 线程安全，可多次调用；仅首次调用执行实际启动。
        /// </summary>
        public static Task EnsureStartedAsync()
        {
            if (_started) return _initTcs?.Task ?? Task.CompletedTask;
            lock (_startLock)
            {
                if (_started) return _initTcs?.Task ?? Task.CompletedTask;
                _queue = new BlockingCollection<WorkItem>();
                _initTcs = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                _workerThread = new Thread(WorkerLoop)
                {
                    Name = "PythonWorkerThread",
                    IsBackground = true
                };
                _workerThread.Start();
                _started = true;
                return _initTcs.Task;
            }
        }

        private static void WorkerLoop()
        {
            try
            {
                InitializePython();
                _initTcs?.TrySetResult();
            }
            catch (Exception ex)
            {
                _initTcs?.TrySetException(ex);
                return;
            }

            foreach (var item in _queue!.GetConsumingEnumerable())
            {
                try
                {
                    using (Py.GIL())
                    {
                        item.Work();
                    }
                    item.Tcs.TrySetResult(null);
                }
                catch (Exception ex)
                {
                    item.Tcs.TrySetException(ex);
                }
            }
        }

        private static void InitializePython()
        {
            if (string.IsNullOrEmpty(Runtime.PythonDLL))
            {
                string? dllPath = FindPythonDll();
                if (string.IsNullOrEmpty(dllPath))
                    throw new InvalidOperationException(
                        "无法自动定位 Python DLL，请安装 Python 3.8-3.12 或设置 PYTHONNET_PYDLL 环境变量。");
                Runtime.PythonDLL = dllPath;
            }
            PythonEngine.Initialize();
            PythonEngine.BeginAllowThreads();
        }

        /// <summary>
        /// 将 Python 操作调度到专用线程执行。
        /// 操作在 GIL 保护下运行，调用方通过 await 等待完成。
        /// </summary>
        public static Task RunAsync(Action action)
        {
            if (_queue == null || _queue.IsAddingCompleted)
                throw new InvalidOperationException("Python 工作线程未启动或已关闭。");
            var item = new WorkItem { Work = action };
            _queue.Add(item);
            return item.Tcs.Task;
        }

        /// <summary>
        /// 关闭工作线程。应在应用程序退出时调用。
        /// </summary>
        public static void Shutdown()
        {
            if (!_started) return;
            _queue?.CompleteAdding();
            _workerThread?.Join(TimeSpan.FromSeconds(5));
        }

        /// <summary>
        /// 自动检测系统中可用的 Python DLL 路径。
        /// 优先级：PYTHONNET_PYDLL 环境变量 → where python 推断 → 常见安装路径扫描。
        /// pythonnet 3.0.x 支持 Python 3.8-3.12。
        /// </summary>
        private static string? FindPythonDll()
        {
            // 1. 环境变量
            string? envDll = Environment.GetEnvironmentVariable("PYTHONNET_PYDLL");
            if (!string.IsNullOrEmpty(envDll) && File.Exists(envDll))
                return envDll;

            // 2. 通过 where python 获取可执行文件路径，推断 DLL
            try
            {
                var psi = new ProcessStartInfo("where", "python")
                {
                    RedirectStandardOutput = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                };
                using var proc = Process.Start(psi);
                if (proc != null)
                {
                    string output = proc.StandardOutput.ReadToEnd();
                    proc.WaitForExit(5000);
                    foreach (string line in output.Split('\n', StringSplitOptions.RemoveEmptyEntries))
                    {
                        string exePath = line.Trim();
                        if (!File.Exists(exePath)) continue;
                        string? dir = Path.GetDirectoryName(exePath);
                        if (dir == null) continue;

                        for (int minor = 12; minor >= 8; minor--)
                        {
                            string candidate = Path.Combine(dir, $"python3{minor}.dll");
                            if (File.Exists(candidate)) return candidate;
                        }
                    }
                }
            }
            catch { /* 忽略，继续尝试 */ }

            // 3. 常见安装路径扫描
            string userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            string localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            string[] searchRoots = new[]
            {
                Path.Combine(localAppData, "Programs", "Python"),
                Path.Combine(userProfile, "miniconda3"),
                Path.Combine(userProfile, "anaconda3"),
                @"C:\Python",
                @"C:\Program Files\Python",
            };

            for (int minor = 12; minor >= 8; minor--)
            {
                string dllName = $"python3{minor}.dll";
                foreach (string root in searchRoots)
                {
                    if (!Directory.Exists(root)) continue;
                    string directPath = Path.Combine(root, dllName);
                    if (File.Exists(directPath)) return directPath;
                    try
                    {
                        foreach (string dir in Directory.GetDirectories(root))
                        {
                            string candidate = Path.Combine(dir, dllName);
                            if (File.Exists(candidate)) return candidate;
                        }
                    }
                    catch { /* 忽略权限问题 */ }
                }
            }

            return null;
        }
    }
}
