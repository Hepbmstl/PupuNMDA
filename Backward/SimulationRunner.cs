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
    /// 仿真运行器，通过 pythonnet 调用 Hines_method.py 执行完整仿真流程。
    /// 负责按正确顺序调用 Python 接口：
    ///   clear_environment → set_env → set_E → init_segment (+ add_channel_to_segment)
    ///   → add_connection → insert_stimulation → insert_probe → start_simulation
    /// 提供实时步数回调和结果导出。
    /// </summary>
    public class SimulationRunner
    {
        private int _currentStep = -1;
        private volatile bool _isRunning;
        private int _totalSteps;
        private volatile bool _abortRequested;

        /// <summary>当前仿真步数（通过 progress_callback 更新），可在 UI 线程安全读取。</summary>
        public int CurrentStep => Volatile.Read(ref _currentStep);

        /// <summary>仿真是否正在运行。</summary>
        public bool IsRunning => _isRunning;

        /// <summary>总步数。</summary>
        public int TotalSteps => _totalSteps;

        /// <summary>仿真完成后的探针 JSON 数据。</summary>
        public string? ProbeResultJson { get; private set; }

        /// <summary>是否已请求终止仿真（供外部可靠检测 abort 状态）。</summary>
        public bool WasAborted => _abortRequested;

        /// <summary>
        /// 请求终止正在运行的仿真。下一次 Python 回调时会抛出异常中断执行。
        /// </summary>
        public void Abort()
        {
            _abortRequested = true;
        }



        /// <summary>
        /// 异步执行完整仿真流程。
        /// 在后台线程中持有 GIL 调用 Python，通过 progress_callback 回写步数到 _currentStep。
        /// </summary>
        /// <param name="simData">已由 SimulationRegistry.BuildSimulationData 构建的仿真数据包。</param>
        /// <param name="vInit">初始膜电位 (mV)。</param>
        /// <param name="dt">时间步长 (ms)。</param>
        /// <param name="steps">总仿真步数。</param>
        /// <param name="eNa">钠离子平衡电位 (mV)。</param>
        /// <param name="eK">钾离子平衡电位 (mV)。</param>
        /// <param name="eLeak">漏电流平衡电位 (mV)。</param>
        public async Task RunAsync(
            SimulationData simData,
            double vInit, double dt, int steps,
            double eNa, double eK, double eLeak)
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

                    // ── 1. 清除上一次仿真状态 ──
                    sim.clear_environment();

                    // ── 2. set_env ──
                    sim.set_env(
                        V_init: vInit,
                        dt: dt,
                        steps: steps,
                        n_node: simData.Compartments.Count);

                    // ── 3. set_E（通过 JSON 传递字典） ──
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

                    // ── 4. init_segment + add_channel_to_segment（按 GlobalId 顺序） ──
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

                    // ── 5. add_connection（按 GlobalId 顺序） ──
                    foreach (var comp in simData.Compartments)
                    {
                        foreach (int connId in comp.ConnectedIds)
                        {
                            sim.add_connection(comp.GlobalId, connId);
                        }
                    }

                    // ── 6. insert_stimulation ──
                    foreach (var stim in simData.Stimulations)
                    {
                        sim.insert_stimulation(
                            stim.StimulationId,
                            stim.SegmentId,
                            stim.Stimulation_uA,
                            stim.StimStart,
                            stim.StimDuration);
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

                    // ── 8. start_simulation（带步数回调） ──
                    Action<int> callback = step =>
                    {
                        Volatile.Write(ref _currentStep, step);
                        if (_abortRequested)
                            throw new OperationCanceledException("仿真已被用户终止。");
                    };
                    sim.start_simulation(callback);

                    // ── 9. 导出探针数据 ──
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
        /// 定位 Hines_method.py 所在的脚本目录。
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
        /// 异步调用 Hines_method.plot_variable_over_time，在 matplotlib 窗口中显示结果。
        /// 必须在 start_simulation 成功执行后调用（依赖 HISTORY_* 全局状态）。
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
        /// 异步调用 Hines_method.show_dynamic_phase_portrait，在 matplotlib 窗口中显示动态相图。
        /// 必须在 start_simulation 成功执行后调用（依赖 HISTORY_* 和 PROBE_LIST 全局状态）。
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
