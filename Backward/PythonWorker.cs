/*
 * Copyright 2026 [Hepbmstl Hepupu]
 *
 * Pupu NMDA / NeuronCAD
 * A Multi-Compartment Neuron Physiological Simulation and Dynamics Analysis Platform
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
using System.Linq;
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
        private const string BundledPythonVersion = "312";
        private const string BundledBackendArchiveName = "Backward.zip";
        private const bool AllowSystemPythonFallback = true;

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
            string? bundledDllPath = FindBundledPythonDll();
            if (!string.IsNullOrEmpty(bundledDllPath))
            {
                Runtime.PythonDLL = bundledDllPath;
            }
            else if (string.IsNullOrEmpty(Runtime.PythonDLL))
            {
                string? dllPath = AllowSystemPythonFallback ? FindSystemPythonDll() : null;
                if (string.IsNullOrEmpty(dllPath))
                {
                    throw new InvalidOperationException(
                        "Missing Python runtime. Provide a bundled runtime payload or configure a compatible system Python.");
                }
                Runtime.PythonDLL = dllPath;
            }

            ConfigureBundledEnvironment();
            PythonEngine.Initialize();
            PythonEngine.BeginAllowThreads();
            PrimePythonPaths();
            PrimePythonEnvironment();
            VerifyScriptImportPath();
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

        public static void ValidateBundledRuntimeLayout()
        {
            string? pythonRoot = FindBundledPythonRoot();
            string? dllPath = pythonRoot == null
                ? (AllowSystemPythonFallback ? FindSystemPythonDll() : null)
                : Path.Combine(pythonRoot, $"python{BundledPythonVersion}.dll");

            ConfigureBundledEnvironment();

            if (string.IsNullOrEmpty(dllPath) || !File.Exists(dllPath))
                throw new InvalidOperationException("Missing Python DLL. Provide a runtime payload or configure PYTHONNET_PYDLL.");

            if (!string.IsNullOrEmpty(pythonRoot))
            {
                string libDir = Path.Combine(pythonRoot, "Lib");
                string sitePackagesDir = Path.Combine(libDir, "site-packages");
                string tclRoot = Path.Combine(pythonRoot, "tcl");

                if (!Directory.Exists(libDir))
                    throw new InvalidOperationException($"Missing Python standard library directory: {libDir}");
                if (!Directory.Exists(sitePackagesDir))
                    throw new InvalidOperationException($"Missing Python site-packages directory: {sitePackagesDir}");
                if (!Directory.Exists(tclRoot))
                    throw new InvalidOperationException($"Missing Tcl/Tk directory: {tclRoot}");
            }

            if (GetBackendImportPaths().Length == 0)
            {
                throw new InvalidOperationException(
                    "Missing Python backend. Expected a runtime payload Backward.zip or a Backward/Hines_method.py source directory.");
            }
        }

        private static string GetBundledPythonRoot()
        {
            return FindBundledPythonRoot() ?? GetOutputBundledPythonRoot();
        }

        private static string GetBundledPythonDllPath()
        {
            return Path.Combine(GetBundledPythonRoot(), $"python{BundledPythonVersion}.dll");
        }

        private static string GetBundledBackendArchivePath()
        {
            return Path.Combine(GetBundledPythonRoot(), "app", BundledBackendArchiveName);
        }

        private static string GetExternalScriptDir()
        {
            return Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Backward");
        }

        private static string GetOutputBundledPythonRoot()
        {
            return Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "runtime", "python");
        }

        private static string? FindProjectRoot()
        {
            string? current = AppDomain.CurrentDomain.BaseDirectory;
            while (!string.IsNullOrEmpty(current))
            {
                if (File.Exists(Path.Combine(current, "NeuronCAD.csproj")))
                    return current;

                string? parent = Directory.GetParent(current)?.FullName;
                if (string.Equals(parent, current, StringComparison.OrdinalIgnoreCase))
                    break;
                current = parent;
            }

            string candidate = Path.GetFullPath(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "..", "..", ".."));
            return File.Exists(Path.Combine(candidate, "NeuronCAD.csproj")) ? candidate : null;
        }

        private static string? FindBundledPythonRoot()
        {
            string? projectRoot = FindProjectRoot();
            string[] candidates = projectRoot == null
                ? new[] { GetOutputBundledPythonRoot() }
                : new[]
                {
                    GetOutputBundledPythonRoot(),
                    Path.Combine(projectRoot, "runtime-payload", "runtime", "python"),
                };

            return candidates
                .Select(Path.GetFullPath)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .FirstOrDefault(path => File.Exists(Path.Combine(path, $"python{BundledPythonVersion}.dll")));
        }

        private static string? FindBundledPythonDll()
        {
            string? pythonRoot = FindBundledPythonRoot();
            if (string.IsNullOrEmpty(pythonRoot))
                return null;

            string dllPath = Path.Combine(pythonRoot, $"python{BundledPythonVersion}.dll");
            return File.Exists(dllPath) ? dllPath : null;
        }

        private static string[] GetBackendImportPaths()
        {
            string? projectRoot = FindProjectRoot();
            string[] archiveCandidates = projectRoot == null
                ? new[] { GetBundledBackendArchivePath() }
                : new[]
                {
                    GetBundledBackendArchivePath(),
                    Path.Combine(projectRoot, "runtime-payload", "runtime", "python", "app", BundledBackendArchiveName),
                };
            string[] sourceCandidates = projectRoot == null
                ? new[] { GetExternalScriptDir() }
                : new[]
                {
                    Path.Combine(projectRoot, "Backward"),
                    GetExternalScriptDir(),
                };

            return sourceCandidates.Concat(archiveCandidates)
                .Select(Path.GetFullPath)
                .Where(path => File.Exists(path) || Directory.Exists(path))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();
        }

        private static void ConfigureBundledEnvironment()
        {
            string? pythonRoot = FindBundledPythonRoot();
            string localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            string mplConfigDir = Path.Combine(localAppData, "NeuronCAD", "mplconfig");
            string[] backendImportPaths = GetBackendImportPaths();
            Directory.CreateDirectory(mplConfigDir);

            Environment.SetEnvironmentVariable("MPLCONFIGDIR", mplConfigDir);

            if (string.IsNullOrEmpty(pythonRoot))
            {
                if (backendImportPaths.Length > 0)
                    Environment.SetEnvironmentVariable("PYTHONPATH", string.Join(Path.PathSeparator, backendImportPaths));
                return;
            }

            string libDir = Path.Combine(pythonRoot, "Lib");
            string sitePackagesDir = Path.Combine(libDir, "site-packages");

            Environment.SetEnvironmentVariable("PYTHONHOME", pythonRoot);
            Environment.SetEnvironmentVariable(
                "PYTHONPATH",
                string.Join(Path.PathSeparator, new[] { libDir, sitePackagesDir }.Concat(backendImportPaths)));
            Environment.SetEnvironmentVariable("PYTHONNET_PYDLL", Path.Combine(pythonRoot, $"python{BundledPythonVersion}.dll"));

            string tclRoot = Path.Combine(pythonRoot, "tcl");
            string? tclDir = FindTclLibrary(tclRoot);
            string? tkDir = FindTkLibrary(tclRoot);
            if (!string.IsNullOrEmpty(tclDir))
                Environment.SetEnvironmentVariable("TCL_LIBRARY", tclDir);
            if (!string.IsNullOrEmpty(tkDir))
                Environment.SetEnvironmentVariable("TK_LIBRARY", tkDir);
        }

        private static void PrimePythonPaths()
        {
            string? pythonRoot = FindBundledPythonRoot();
            string[] backendImportPaths = GetBackendImportPaths();

            using (Py.GIL())
            {
                dynamic sys = Py.Import("sys");
                if (!string.IsNullOrEmpty(pythonRoot))
                {
                    string libDir = Path.Combine(pythonRoot, "Lib");
                    string sitePackagesDir = Path.Combine(libDir, "site-packages");

                    if (!sys.path.__contains__(pythonRoot))
                        sys.path.append(pythonRoot);
                    if (Directory.Exists(libDir) && !sys.path.__contains__(libDir))
                        sys.path.append(libDir);
                    if (Directory.Exists(sitePackagesDir) && !sys.path.__contains__(sitePackagesDir))
                        sys.path.append(sitePackagesDir);
                }

                foreach (string importPath in backendImportPaths.Reverse())
                {
                    if (sys.path.__contains__(importPath))
                        sys.path.remove(importPath);
                    sys.path.insert(0, importPath);
                }
            }
        }

        private static void PrimePythonEnvironment()
        {
            string? tclDir = Environment.GetEnvironmentVariable("TCL_LIBRARY");
            string? tkDir = Environment.GetEnvironmentVariable("TK_LIBRARY");
            string? pythonHome = Environment.GetEnvironmentVariable("PYTHONHOME");
            string? pythonPath = Environment.GetEnvironmentVariable("PYTHONPATH");
            string? mplConfigDir = Environment.GetEnvironmentVariable("MPLCONFIGDIR");

            using (Py.GIL())
            {
                dynamic os = Py.Import("os");
                if (!string.IsNullOrEmpty(tclDir))
                    os.environ.__setitem__("TCL_LIBRARY", tclDir);
                if (!string.IsNullOrEmpty(tkDir))
                    os.environ.__setitem__("TK_LIBRARY", tkDir);
                if (!string.IsNullOrEmpty(pythonHome))
                    os.environ.__setitem__("PYTHONHOME", pythonHome);
                if (!string.IsNullOrEmpty(pythonPath))
                    os.environ.__setitem__("PYTHONPATH", pythonPath);
                if (!string.IsNullOrEmpty(mplConfigDir))
                    os.environ.__setitem__("MPLCONFIGDIR", mplConfigDir);
            }
        }

        private static void VerifyScriptImportPath()
        {
            string[] expectedImportRoots = GetBackendImportPaths()
                .Select(Path.GetFullPath)
                .ToArray();

            using (Py.GIL())
            {
                dynamic sim = Py.Import("Hines_method");
                string importedPath = Path.GetFullPath((string)sim.__file__.ToString());
                Debug.WriteLine($"Python runtime root: {FindBundledPythonRoot() ?? "system Python"}");
                Debug.WriteLine($"Imported Hines_method: {importedPath}");

                bool fromExpectedBackend = expectedImportRoots
                    .Any(path => importedPath.StartsWith(path, StringComparison.OrdinalIgnoreCase));

                if (!fromExpectedBackend)
                {
                    throw new InvalidOperationException(
                        $"Hines_method.py was imported from an unexpected location: {importedPath}\n" +
                        $"Expected it under: {string.Join(" or ", expectedImportRoots)}");
                }
            }
        }

        private static string? FindTclLibrary(string tclRoot)
        {
            return FindDirectoryContainingFile(tclRoot, "init.tcl", "tcl8*");
        }

        private static string? FindTkLibrary(string tclRoot)
        {
            return FindDirectoryContainingFile(tclRoot, "tk.tcl", "tk8*");
        }

        private static string? FindDirectoryContainingFile(string root, string fileName, string directoryPattern)
        {
            if (!Directory.Exists(root))
                return null;

            foreach (string dir in Directory.GetDirectories(root, directoryPattern).OrderBy(p => p.Length).ThenBy(p => p))
            {
                string candidate = Path.Combine(dir, fileName);
                if (File.Exists(candidate))
                    return dir;
            }

            try
            {
                return Directory.GetFiles(root, fileName, SearchOption.AllDirectories)
                    .Select(Path.GetDirectoryName)
                    .Where(p => !string.IsNullOrEmpty(p))
                    .OrderBy(p => p!.Length)
                    .ThenBy(p => p)
                    .FirstOrDefault();
            }
            catch
            {
                return null;
            }
        }

        /// <summary>
        /// Automatically detect an available Python DLL path on the system.
        /// Priority: PYTHONNET_PYDLL env var -> infer from 'where python' -> scan common install locations.
        /// pythonnet 3.0.x supports Python 3.8-3.12.
        /// </summary>
        private static string? FindSystemPythonDll()
        {
            string? envDll = Environment.GetEnvironmentVariable("PYTHONNET_PYDLL");
            if (!string.IsNullOrEmpty(envDll) && File.Exists(envDll))
                return envDll;

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
            catch
            {
            }

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
                    catch
                    {
                    }
                }
            }

            return null;
        }
    }
}
