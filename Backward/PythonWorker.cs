/*
 * Copyright 2026 [Hepbmstl Hepupu]
 *
 * Pupu NMDA / NeuronCAD
 * A Multi-Compartment Neuron Modeling and Dynamics Analysis Platform
 *
 * Licensed under the Apache License, Version 2.0 (the "License");
 * you may not use this file except in compliance with the License.
 * You may obtain a copy of the License at
 *
 * http://www.apache.org/licenses/LICENSE-2.0
 *
 * Unless required by applicable law or agreed to in writing, software
 * distributed under the License is distributed on an "AS IS" BASIS,
 * WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
 * See the License for the specific language governing permissions and
 * limitations under the License.
 */

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
    /// Dedicated Python worker thread manager.
    /// All Python operations are dispatched to this dedicated thread to ensure the
    /// GIL is always acquired/released on the same thread and to satisfy Matplotlib
    /// GUI requirements for a stable thread.
    /// The Python runtime lives with this thread until Shutdown() is called at
    /// application exit.
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
        /// Ensure the Python worker thread is started and initialized.
        /// Thread-safe and callable multiple times; only the first call performs startup.
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
                        "Cannot locate Python DLL automatically. Please install Python 3.8-3.12 or set the PYTHONNET_PYDLL environment variable.");
                Runtime.PythonDLL = dllPath;
            }
            PythonEngine.Initialize();
            PythonEngine.BeginAllowThreads();
        }

        /// <summary>
        /// Dispatch Python actions to the dedicated thread.
        /// Actions run under GIL protection; callers await completion.
        /// </summary>
        public static Task RunAsync(Action action)
        {
            if (_queue == null || _queue.IsAddingCompleted)
                throw new InvalidOperationException("Python worker thread is not started or has been shut down.");
            var item = new WorkItem { Work = action };
            _queue.Add(item);
            return item.Tcs.Task;
        }

        /// <summary>
        /// Shut down the worker thread. Should be called on application exit.
        /// </summary>
        public static void Shutdown()
        {
            if (!_started) return;
            _queue?.CompleteAdding();
            _workerThread?.Join(TimeSpan.FromSeconds(5));
        }

        /// <summary>
        /// Automatically detect an available Python DLL path on the system.
        /// Priority: PYTHONNET_PYDLL env var → infer from 'where python' → scan common install locations.
        /// pythonnet 3.0.x supports Python 3.8-3.12.
        /// </summary>
        private static string? FindPythonDll()
        {
            // 1. Environment variable
            string? envDll = Environment.GetEnvironmentVariable("PYTHONNET_PYDLL");
            if (!string.IsNullOrEmpty(envDll) && File.Exists(envDll))
                return envDll;

            // 2. Use 'where python' to get executable path and infer DLL
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
            catch { /* ignore and continue trying */ }

            // 3. Scan common installation paths
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
                    catch { /* ignore permission issues */ }
                }
            }

            return null;
        }
    }
}
