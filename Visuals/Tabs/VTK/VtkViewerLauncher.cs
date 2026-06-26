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
    /// Launches the Python VTK geometry viewer and optionally embeds its render window in a Win32 parent HWND.
    /// This keeps the WPF shell independent from ActiViz while preserving a stable place for future native VTK hosting.
    /// </summary>
    public static class VtkViewerLauncher
    {
        public static string GetDefaultJsonPath()
        {
            string projectRoot = FindProjectRoot()
                ?? throw new InvalidOperationException("Could not locate NeuronCAD.csproj from the application directory.");

            return Path.Combine(projectRoot, "dev 626", "tc200", "tc200_NeuronCAD.json");
        }

        public static Process LaunchEmbedded(
            IntPtr parentHwnd,
            string? jsonPath = null,
            string? channel = null,
            bool renderJsonColors = true,
            bool showConnections = false,
            bool showDevices = true,
            double shadowStrength = 0.35,
            int width = 1100,
            int height = 850)
        {
            string projectRoot = FindProjectRoot()
                ?? throw new InvalidOperationException("Could not locate NeuronCAD.csproj from the application directory.");

            string devDir = Path.Combine(projectRoot, "dev 626");
            string scriptPath = Path.Combine(devDir, "test_negeo.py");
            if (!File.Exists(scriptPath))
                throw new FileNotFoundException("VTK viewer script was not found.", scriptPath);

            string resolvedJsonPath = string.IsNullOrWhiteSpace(jsonPath)
                ? GetDefaultJsonPath()
                : Path.GetFullPath(jsonPath);

            var psi = new ProcessStartInfo
            {
                FileName = "python",
                WorkingDirectory = devDir,
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
            };

            psi.Environment["PYTHONUNBUFFERED"] = "1";

            psi.ArgumentList.Add(scriptPath);
            psi.ArgumentList.Add(resolvedJsonPath);
            if (!string.IsNullOrWhiteSpace(channel))
            {
                psi.ArgumentList.Add("--channel");
                psi.ArgumentList.Add(channel);
            }
            else if (renderJsonColors)
            {
                psi.ArgumentList.Add("--json-colors");
            }

            if (showConnections)
                psi.ArgumentList.Add("--connections");
            if (!showDevices)
                psi.ArgumentList.Add("--hide-devices");

            psi.ArgumentList.Add("--shadow-strength");
            psi.ArgumentList.Add(shadowStrength.ToString("0.###", CultureInfo.InvariantCulture));
            psi.ArgumentList.Add("--window-size");
            psi.ArgumentList.Add($"{Math.Max(1, width)}x{Math.Max(1, height)}");
            psi.ArgumentList.Add("--parent-hwnd");
            psi.ArgumentList.Add(parentHwnd.ToInt64().ToString());

            return Process.Start(psi)
                ?? throw new InvalidOperationException("Failed to start the VTK viewer process.");
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
