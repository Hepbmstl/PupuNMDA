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
 *
 * Scientific and Algorithmic Foundations:
 * This software's biophysical organization and core numerical methods are 
 * fundamentally informed by the following works:
 * * 1. Destexhe, A., Neubig, M., Ulrich, D., & Huguenard, J. (1998). 
 * Dendritic Low-Threshold Calcium Currents in Thalamic Relay Cells. 
 * The Journal of Neuroscience, 18(10), 3574-3588.
 * * 2. Hines, M. (1984). Efficient computation of branched nerve equations. 
 * International Journal of Bio-Medical Computing, 15(1), 69-76.
 *
 */

using System;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Python.Runtime;
using NeuronCAD.Visuals.Tabs.Simulation;
using NeuronCAD.Visuals.Windows;

namespace NeuronCAD.Backward
{
    /// <summary>
    /// Simulation runner that calls Hines_method.py via pythonnet to perform the
    /// full simulation workflow.
    /// Responsible for invoking Python interfaces in the correct order:
    ///   clear_environment → set_env → set_E → init_segment (+ add_channel_to_segment)
    ///   → add_connection → insert_stimulation → insert_probe → start_simulation
    /// Provides real-time step callbacks and result export.
    /// </summary>
    public class SimulationRunner
    {
        private int _currentStep = -1;
        private volatile bool _isRunning;
        private int _totalSteps;
        private volatile bool _abortRequested;

        /// <summary>Current simulation step (updated via progress_callback), safe to read from the UI thread.</summary>
        public int CurrentStep => Volatile.Read(ref _currentStep);

        /// <summary>Whether the simulation is currently running.</summary>
        public bool IsRunning => _isRunning;

        /// <summary>Total number of steps.</summary>
        public int TotalSteps => _totalSteps;

        /// <summary>Probe JSON data after the simulation completes.</summary>
        public string? ProbeResultJson { get; private set; }

        /// <summary>Whether an abort of the simulation has been requested (for external abort checks).</summary>
        public bool WasAborted => _abortRequested;

        /// <summary>
        /// Request termination of the running simulation. An exception will be thrown
        /// on the next Python callback to interrupt execution.
        /// </summary>
        public void Abort()
        {
            _abortRequested = true;
        }



        /// <summary>
        /// Run the full simulation asynchronously.
        /// Acquires the GIL on a background thread to call Python and writes step
        /// updates back to _currentStep via progress_callback.
        /// </summary>
        /// <param name="simData">Simulation data package built by SimulationRegistry.BuildSimulationData.</param>
        /// <param name="vInit">Initial membrane potential (mV).</param>
        /// <param name="dt">Time step (ms).</param>
        /// <param name="steps">Total number of simulation steps.</param>
        /// <param name="eNa">Sodium reversal potential (mV).</param>
        /// <param name="eK">Potassium reversal potential (mV).</param>
        /// <param name="eLeak">Leak reversal potential (mV).</param>
        /// <param name="celsius">Simulation temperature (°C), default 24.0.</param>
        /// <param name="caOut">Extracellular calcium concentration (mM), default 2.0.</param>
        /// <param name="caInf">Intracellular steady-state calcium concentration (mM), default 2.4e-4.</param>
        /// <param name="tauCa">Calcium decay time constant (ms), default 5.0.</param>
        public async Task RunAsync(
            SimulationData simData,
            double vInit, double dt, int steps,
            double eNa, double eK, double eLeak,
            double celsius = 24.0, double caOut = 2.0,
            double caInf = 2.4e-4, double tauCa = 5.0)
        {
            _totalSteps = steps;
            _isRunning = true;
            _abortRequested = false;
            Volatile.Write(ref _currentStep, 0);
            ProbeResultJson = null;

            await PythonWorker.EnsureStartedAsync();
            string scriptDir = FindScriptDir();

            try
            {
                await PythonWorker.RunAsync(() =>
                {
                    dynamic sys = Py.Import("sys");
                    if (!sys.path.__contains__(scriptDir))
                        sys.path.append(scriptDir);

                    dynamic sim = Py.Import("Hines_method");

                    // ── 1. Clear previous simulation state ──
                    sim.clear_environment();

                    // ── 2. set_env ──
                    sim.set_env(
                        V_init: vInit,
                        dt: dt,
                        steps: steps,
                        n_node: simData.Compartments.Count,
                        celsius: celsius,
                        ca_out: caOut,
                        ca_inf: caInf,
                        tau_ca: tauCa);

                    // ── 3. set_E (pass dictionary via JSON) ──
                    dynamic json = Py.Import("json");
                    string eJson = string.Format(
                        CultureInfo.InvariantCulture,
                        "{{\"Na\":{{\"E\":{0}}},\"K\":{{\"E\":{1}}},\"L\":{{\"E\":{2}}}}}",
                        eNa, eK, eLeak);
                    sim.set_E(json.loads(eJson));

                    // ── 3b. set ion channel params ──
                    dynamic json2 = Py.Import("json");
                    sim.set_hh_params(json2.loads(IonChannelParams.GetHHParamsJson()));
                    sim.set_ca_params(json2.loads(IonChannelParams.GetCaParamsJson()));

                    // ── 4. init_segment + add_channel_to_segment (in GlobalId order) ──
                    foreach (var comp in simData.Compartments)
                    {
                        sim.init_segment(
                            uid: comp.ParentEntityId,
                            Ra: comp.Ra,
                            D: comp.Diameter_um,
                            L: comp.Length_um,
                            Cm: comp.Cm,
                            id: comp.GlobalId);

                        foreach (var ch in comp.Channels)
                        {
                            sim.add_channel_to_segment(
                                comp.GlobalId,
                                ch.Key,
                                (double)ch.Value.G_ion_channel);
                        }
                    }

                    // ── 5. add_connection (in GlobalId order) ──
                    foreach (var comp in simData.Compartments)
                    {
                        foreach (int connId in comp.ConnectedIds)
                        {
                            sim.add_connection(comp.GlobalId, connId);
                        }
                    }

                    // ── 6. insert_stimulation (current clamp) ──
                    foreach (var stim in simData.Stimulations)
                    {
                        sim.insert_stimulation(
                            stim.StimulationId,
                            stim.SegmentId,
                            stim.Stimulation_uA,
                            stim.StimStart,
                            stim.StimDuration);
                    }

                    // ── 6b. insert_voltage_clamp (voltage clamp) ──
                    foreach (var vc in simData.VoltageClamps)
                    {
                        // Build protocol list required by Python: [[dur, amp], ...]
                        dynamic builtins = Py.Import("builtins");
                        using var pyProtocol = new PyList();
                        foreach (var step in vc.Protocol)
                        {
                            using var pyStep = new PyList();
                            pyStep.Append(new PyFloat(step.Duration));
                            pyStep.Append(new PyFloat(step.Amplitude));
                            pyProtocol.Append(pyStep);
                        }

                        sim.insert_voltage_clamp(
                            vc.VCId,
                            vc.SegmentId,
                            vc.Rs,
                            pyProtocol);
                    }

                    // ── 7. insert_probe ──
                    foreach (var probe in simData.Probes)
                    {
                        sim.insert_probe(
                            probe.ProbeId,
                            probe.SegmentId,
                            probe.StartMs,
                            probe.DurationMs);
                    }

                    // ── 8. start_simulation (with step callback) ──
                    Action<int> callback = step =>
                    {
                        Volatile.Write(ref _currentStep, step);
                        if (_abortRequested)
                            throw new OperationCanceledException("Simulation aborted by user.");
                    };
                    sim.start_simulation(callback);

                    // ── 9. Export probe data ──
                    ProbeResultJson = (string)sim.export_probe_data_json();
                });
            }
            finally
            {
                _isRunning = false;
                Volatile.Write(ref _currentStep, _totalSteps);
            }
        }

        #region Static Plotting API

        /// <summary>
        /// Locate the script directory containing Hines_method.py.
        /// </summary>
        private static string FindScriptDir()
        {
            string baseDir = AppDomain.CurrentDomain.BaseDirectory;
            string scriptDir = Path.Combine(baseDir, "Backward");
            if (!File.Exists(Path.Combine(scriptDir, "Hines_method.py")))
            {
                string projectDir = Path.GetFullPath(Path.Combine(baseDir, "..", "..", ".."));
                scriptDir = Path.Combine(projectDir, "Backward");
            }
            return scriptDir;
        }

        /// <summary>
        /// Asynchronously call Hines_method.plot_variable_over_time and show the result in a matplotlib window.
        /// Must be called after start_simulation has completed successfully (depends on HISTORY_* global state).
        /// </summary>
        public static async Task CallPlotVariableOverTime(int segmentId, string varLabel, double startMs, double endMs)
        {
            await PythonWorker.EnsureStartedAsync();
            string scriptDir = FindScriptDir();

            await PythonWorker.RunAsync(() =>
            {
                dynamic sys = Py.Import("sys");
                if (!sys.path.__contains__(scriptDir))
                    sys.path.append(scriptDir);
                dynamic sim = Py.Import("Hines_method");
                sim.plot_variable_over_time(segmentId, varLabel, startMs, endMs);
            });
        }

        /// <summary>
        /// Asynchronously call Hines_method.show_dynamic_phase_portrait and display the dynamic phase portrait in a matplotlib window.
        /// Must be called after start_simulation has completed successfully (depends on HISTORY_* and PROBE_LIST global state).
        /// </summary>
        public static async Task CallShowPhasePortrait(int probeId, string xVar, string yVar)
        {
            await PythonWorker.EnsureStartedAsync();
            string scriptDir = FindScriptDir();

            await PythonWorker.RunAsync(() =>
            {
                dynamic sys = Py.Import("sys");
                if (!sys.path.__contains__(scriptDir))
                    sys.path.append(scriptDir);
                dynamic sim = Py.Import("Hines_method");
                sim.show_dynamic_phase_portrait(probeId, xVar, yVar);
            });
        }

        #endregion
    }
}
