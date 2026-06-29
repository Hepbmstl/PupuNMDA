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

using System.Diagnostics;
using System.Globalization;
using System.IO;

namespace NeuronCAD.Visuals.Tabs.VTK
{
    /// <summary>
    /// Launches the standalone Python VTK geometry viewer.
    /// This keeps the WPF shell independent from ActiViz while preserving a stable place for future native VTK hosting.
    /// </summary>
    public static class VtkViewerLauncher
    {
        public static Process LaunchEmbedded(
            IntPtr parentHwnd,
            string scenePayloadPath,
            string? channel = null,
            bool showConnections = false,
            bool showDevices = true,
            double shadowStrength = 0.35,
            int width = 1100,
            int height = 850)
        {
            _ = parentHwnd;

            if (string.IsNullOrWhiteSpace(scenePayloadPath))
                throw new ArgumentException("A C# scene payload path is required.", nameof(scenePayloadPath));

            string scriptPath = ResolveViewerScriptPath();
            var psi = CreateViewerProcessStartInfo(Path.GetDirectoryName(scriptPath)!);

            psi.ArgumentList.Add(scriptPath);
            psi.ArgumentList.Add("--scene-payload");
            psi.ArgumentList.Add(Path.GetFullPath(scenePayloadPath));

            if (!string.IsNullOrWhiteSpace(channel))
            {
                psi.ArgumentList.Add("--channel");
                psi.ArgumentList.Add(channel);
            }

            if (showConnections)
                psi.ArgumentList.Add("--connections");
            if (!showDevices)
                psi.ArgumentList.Add("--hide-devices");

            psi.ArgumentList.Add("--shadow-strength");
            psi.ArgumentList.Add(shadowStrength.ToString("0.###", CultureInfo.InvariantCulture));
            psi.ArgumentList.Add("--window-size");
            psi.ArgumentList.Add($"{Math.Max(1, width)}x{Math.Max(1, height)}");

            return Process.Start(psi)
                ?? throw new InvalidOperationException("Failed to start the VTK viewer process.");
        }

        public static Process LaunchHistoryPlayback(
            IntPtr parentHwnd,
            string scenePayloadPath,
            string historyNpzPath,
            string historyVariable,
            bool showConnections = false,
            bool showDevices = true,
            double shadowStrength = 0.35,
            double fps = 20.0,
            int width = 1100,
            int height = 850)
        {
            _ = parentHwnd;

            if (string.IsNullOrWhiteSpace(scenePayloadPath))
                throw new ArgumentException("A C# scene payload path is required.", nameof(scenePayloadPath));
            if (string.IsNullOrWhiteSpace(historyNpzPath))
                throw new ArgumentException("A history NPZ path is required.", nameof(historyNpzPath));

            string scriptPath = ResolveViewerScriptPath();
            var psi = CreateViewerProcessStartInfo(Path.GetDirectoryName(scriptPath)!);

            psi.ArgumentList.Add(scriptPath);
            psi.ArgumentList.Add("--scene-payload");
            psi.ArgumentList.Add(Path.GetFullPath(scenePayloadPath));
            psi.ArgumentList.Add("--history-npz");
            psi.ArgumentList.Add(Path.GetFullPath(historyNpzPath));
            psi.ArgumentList.Add("--history-var");
            psi.ArgumentList.Add(historyVariable);
            psi.ArgumentList.Add("--history-fps");
            psi.ArgumentList.Add(fps.ToString("0.###", CultureInfo.InvariantCulture));

            if (showConnections)
                psi.ArgumentList.Add("--connections");
            if (!showDevices)
                psi.ArgumentList.Add("--hide-devices");

            psi.ArgumentList.Add("--shadow-strength");
            psi.ArgumentList.Add(shadowStrength.ToString("0.###", CultureInfo.InvariantCulture));
            psi.ArgumentList.Add("--window-size");
            psi.ArgumentList.Add($"{Math.Max(1, width)}x{Math.Max(1, height)}");

            return Process.Start(psi)
                ?? throw new InvalidOperationException("Failed to start the VTK history viewer process.");
        }

        private static string ResolveViewerScriptPath()
        {
            string outputCandidate = Path.Combine(
                AppDomain.CurrentDomain.BaseDirectory,
                "Visuals",
                "Tabs",
                "VTK",
                "VTK_render.py");
            if (File.Exists(outputCandidate))
                return outputCandidate;

            string? projectRoot = FindProjectRoot();
            if (projectRoot != null)
            {
                string sourceCandidate = Path.Combine(projectRoot, "Visuals", "Tabs", "VTK", "VTK_render.py");
                if (File.Exists(sourceCandidate))
                    return sourceCandidate;
            }

            throw new FileNotFoundException("VTK renderer script was not found.", outputCandidate);
        }

        private static ProcessStartInfo CreateViewerProcessStartInfo(string workingDirectory)
        {
            var psi = new ProcessStartInfo
            {
                FileName = "python",
                WorkingDirectory = workingDirectory,
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
            };

            SanitizePythonEnvironment(psi);
            psi.Environment["PYTHONUNBUFFERED"] = "1";
            return psi;
        }

        private static void SanitizePythonEnvironment(ProcessStartInfo psi)
        {
            // PythonWorker configures process-wide variables for pythonnet. A normal
            // VTK viewer process must not inherit that embedded-runtime environment.
            foreach (string key in new[]
            {
                "PYTHONHOME",
                "PYTHONPATH",
                "PYTHONNET_PYDLL",
                "TCL_LIBRARY",
                "TK_LIBRARY"
            })
            {
                psi.Environment.Remove(key);
            }
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
    }
}
