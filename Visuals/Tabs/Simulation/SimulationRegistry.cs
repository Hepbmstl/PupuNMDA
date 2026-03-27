using System;
using System.Collections.Generic;
using NeuronCAD.Visuals.Tabs.Modeling.Visuals;

namespace NeuronCAD.Visuals.Tabs.Simulation
{
    /// <summary>
    /// 区室化切分模式枚举。
    /// </summary>
    public enum SegmentationMode
    {
        /// <summary>按区室数量切分：每个组件固定切分为 NSeg 个区室。</summary>
        NSeg,
        /// <summary>按区室长度切分：每隔 LSeg µm 切分一个区室。</summary>
        LSeg
    }

    /// <summary>
    /// 仿真探针数据类，对应 Hines_method.py 的 insert_probe(probe_id, segment_id, probe_start_ms, probe_duration_ms)。
    /// 在按下 Begin 时由 SimulationRegistry.ResolveDevices 根据 ProbeDevice 空间位置自动创建。
    /// </summary>
    public class SimProbe
    {
        /// <summary>探针自动分配的全局编号 (0-based)。</summary>
        public int ProbeId { get; set; }

        /// <summary>探针采样开始时间 (ms)，对应 Hines_method 中的 probe_start_ms。</summary>
        public double StartMs { get; set; }

        /// <summary>探针采样持续时间 (ms)，对应 Hines_method 中的 probe_duration_ms。</summary>
        public double DurationMs { get; set; }

        /// <summary>探针绑定的区室全局编号 (Compartment.GlobalId)。</summary>
        public int SegmentId { get; set; }

        /// <summary>关联的视觉设备 ID (GUID)，用于追溯到 ProbeDevice。</summary>
        public string SourceDeviceId { get; set; } = string.Empty;
    }

    /// <summary>
    /// 仿真刺激数据类，对应 Hines_method.py 的 insert_stimulation(stimulation_id, segment_id, stimulation_uA, stim_start, stim_duration)。
    /// 在按下 Begin 时由 SimulationRegistry.ResolveDevices 根据 StimulationDevice 空间位置自动创建。
    /// </summary>
    public class SimStimulation
    {
        /// <summary>刺激自动分配的全局编号 (0-based)。</summary>
        public int StimulationId { get; set; }

        /// <summary>刺激绑定的区室全局编号 (Compartment.GlobalId)。</summary>
        public int SegmentId { get; set; }

        /// <summary>刺激电流强度 (µA)。</summary>
        public double Stimulation_uA { get; set; }

        /// <summary>刺激开始时间 (ms)。</summary>
        public double StimStart { get; set; }

        /// <summary>刺激持续时间 (ms)。</summary>
        public double StimDuration { get; set; }

        /// <summary>关联的视觉设备 ID (GUID)，用于追溯到 StimulationDevice。</summary>
        public string SourceDeviceId { get; set; } = string.Empty;
    }

    /// <summary>
    /// 电压钳协议步骤数据。
    /// </summary>
    public class SimVCStep
    {
        public double Duration { get; set; }
        public double Amplitude { get; set; }
    }

    /// <summary>
    /// 仿真电压钳数据类，对应 Hines_method.py 的 insert_voltage_clamp(vc_id, segment_id, rs_MOhm, protocol)。
    /// </summary>
    public class SimVoltageClamp
    {
        /// <summary>电压钳自动分配的全局编号 (0-based)。</summary>
        public int VCId { get; set; }

        /// <summary>电压钳绑定的区室全局编号 (Compartment.GlobalId)。</summary>
        public int SegmentId { get; set; }

        /// <summary>串联电阻 (MΩ)。</summary>
        public double Rs { get; set; }

        /// <summary>电压钳协议步骤列表。</summary>
        public List<SimVCStep> Protocol { get; set; } = new();

        /// <summary>关联的视觉设备 ID (GUID)。</summary>
        public string SourceDeviceId { get; set; } = string.Empty;
    }

    /// <summary>
    /// 仿真完整数据包，包含区室化切分结果和设备绑定结果。
    /// 由 SimulationRegistry.BuildSimulationData 一次性生成，可直接传入 Hines_method.py。
    /// </summary>
    public class SimulationData
    {
        /// <summary>所有区室列表（按 GlobalId 顺序）。</summary>
        public List<Compartment> Compartments { get; set; } = new();

        /// <summary>所有探针绑定列表。</summary>
        public List<SimProbe> Probes { get; set; } = new();

        /// <summary>所有电流钳绑定列表。</summary>
        public List<SimStimulation> Stimulations { get; set; } = new();

        /// <summary>所有电压钳绑定列表。</summary>
        public List<SimVoltageClamp> VoltageClamps { get; set; } = new();
    }

    /// <summary>
    /// 区室数据类，表示切分后的一个离散区室（对应 Hines_method.py 中的 Segment）。
    /// 所有物理量纲统一为微级别国际单位：
    ///   长度: µm, 直径: µm, 比膜电容: µF/cm², 轴向电阻率: Ω·cm, 电压: mV, 时间: ms
    /// </summary>
    public class Compartment
    {
        /// <summary>全局唯一编号 (0-based)，用于 Hines 矩阵索引。</summary>
        public int GlobalId { get; set; }

        /// <summary>所属建模实体 ID (GUID)。</summary>
        public string ParentEntityId { get; set; } = string.Empty;

        /// <summary>所属实体类型标识（"Soma" / "Axon" / "Dend"）。</summary>
        public string ParentEntityType { get; set; } = string.Empty;

        /// <summary>在所属实体中的序号 (0-based)。</summary>
        public int Index { get; set; }

        /// <summary>区室长度 (µm)。</summary>
        public double Length_um { get; set; }

        /// <summary>区室直径 (µm)，取圆柱中心位置处所属组件的直径。</summary>
        public double Diameter_um { get; set; }

        /// <summary>比膜电容 (µF/cm²)。</summary>
        public double Cm { get; set; }

        /// <summary>轴向电阻率 (Ω·cm)。</summary>
        public double Ra { get; set; }

        /// <summary>离子通道属性字典，Key 为通道名称，Value 为 ChannelProperty 引用。</summary>
        public Dictionary<string, ChannelProperty> Channels { get; set; } = new();

        /// <summary>连接的其他区室的全局编号列表（用于构建 Hines 矩阵的耦合关系）。</summary>
        public List<int> ConnectedIds { get; set; } = new();
    }

    /// <summary>
    /// 仿真注册表，负责管理所有建模组件的全局登记和区室化切分。
    /// 当 Modeling 模式下创建/删除实体时，通过 Register/Unregister 自动同步。
    /// 当 Simulation 模式下按下 Begin 时，调用 Compartmentalize 执行区室化切分。
    /// 切分结果可直接用于对接 Hines_method.py 的 init_segment / add_connection 接口。
    /// </summary>
    public class SimulationRegistry
    {
        /// <summary>当前切分模式（按数量或按长度）。</summary>
        public SegmentationMode Mode { get; set; } = SegmentationMode.NSeg;

        /// <summary>每组件区室数量（NSeg 模式下使用）。</summary>
        public int NSeg { get; set; } = 5;

        /// <summary>区室间距 (µm)（LSeg 模式下使用）。</summary>
        public double LSeg { get; set; } = 20.0;

        /// <summary>
        /// 已注册的建模实体字典，Key 为实体 ID。
        /// 由 InteractionController.OnEntityAdded/OnEntityRemoved 事件驱动更新。
        /// </summary>
        public Dictionary<string, IVisualEntity> RegisteredEntities { get; } = new();

        /// <summary>将实体登记到全局注册表。</summary>
        public void Register(IVisualEntity entity)
        {
            RegisteredEntities[entity.Id] = entity;
        }

        /// <summary>将实体从全局注册表移除。</summary>
        public void Unregister(string entityId)
        {
            RegisteredEntities.Remove(entityId);
        }

        /// <summary>
        /// 对所有已注册实体执行区室化切分。
        /// 每个组件被切分成若干圆柱体区室，直径为圆柱中心位置处组件的直径。
        /// 同一组件内的区室线性连接，实体间的连接通过锚点映射到最近区室。
        /// </summary>
        /// <param name="connections">实体间连接字典（来自 ConnectionController.ConnectionsById）。</param>
        /// <returns>所有区室的列表，按 GlobalId 顺序排列。</returns>
        public List<Compartment> Compartmentalize(Dictionary<string, Connection> connections)
        {
            var compartments = new List<Compartment>();
            var entityCompartmentMap = new Dictionary<string, List<int>>();
            int globalId = 0;

            foreach (var kvp in RegisteredEntities)
            {
                var entity = kvp.Value;
                var ids = new List<int>();

                if (entity is AxonVisual axon)
                {
                    double totalLength = axon.Length;
                    int n = ComputeSegmentCount(totalLength);
                    double segLen = totalLength / n;

                    for (int i = 0; i < n; i++)
                    {
                        // t: 当前区室中心在组件轴向方向上的归一化位置 [0, 1]
                        double t = (i + 0.5) / n;
                        // 在底面半径和顶面半径之间线性插值得到中心处半径
                        double radius = axon.BaseRadius + (axon.TopRadius - axon.BaseRadius) * t;

                        var comp = new Compartment
                        {
                            GlobalId = globalId,
                            ParentEntityId = entity.Id,
                            ParentEntityType = axon.VisualType, // "Axon" 或 "Dend"
                            Index = i,
                            Length_um = segLen,
                            Diameter_um = radius * 2.0,
                            Cm = entity.Cm,
                            Ra = entity.Ra,
                            Channels = new Dictionary<string, ChannelProperty>(entity.Channels)
                        };
                        ids.Add(globalId);
                        compartments.Add(comp);
                        globalId++;
                    }

                    // 同一实体内相邻区室线性连接
                    for (int i = 0; i < ids.Count - 1; i++)
                    {
                        compartments[ids[i]].ConnectedIds.Add(ids[i + 1]);
                        compartments[ids[i + 1]].ConnectedIds.Add(ids[i]);
                    }
                }

                entityCompartmentMap[entity.Id] = ids;
            }

            // 处理实体间连接：将 Connection 的锚点位置映射到对应区室
            foreach (var conn in connections.Values)
            {
                if (entityCompartmentMap.TryGetValue(conn.A.Id, out var compA) &&
                    entityCompartmentMap.TryGetValue(conn.B.Id, out var compB) &&
                    compA.Count > 0 && compB.Count > 0)
                {
                    int idA = ResolveCompartmentByAnchor(conn.AnchorA, compA);
                    int idB = ResolveCompartmentByAnchor(conn.AnchorB, compB);

                    if (!compartments[idA].ConnectedIds.Contains(idB))
                        compartments[idA].ConnectedIds.Add(idB);
                    if (!compartments[idB].ConnectedIds.Contains(idA))
                        compartments[idB].ConnectedIds.Add(idA);
                }
            }

            return compartments;
        }

        /// <summary>根据切分模式计算给定长度的区室数量。</summary>
        private int ComputeSegmentCount(double totalLength)
        {
            if (Mode == SegmentationMode.NSeg)
                return Math.Max(1, NSeg);
            return Math.Max(1, (int)Math.Ceiling(totalLength / LSeg));
        }

        /// <summary>
        /// 根据锚点的轴向位置参数 (AxialT) 映射到最近的区室全局编号。
        /// 用于将实体间连接端点定位到具体的区室。
        /// </summary>
        private static int ResolveCompartmentByAnchor(AnchorRef anchor, List<int> compartmentIds)
        {
            if (compartmentIds.Count <= 1)
                return compartmentIds[0];

            double t = Math.Clamp(anchor.AxialT, 0.0, 1.0);
            int idx = (int)(t * compartmentIds.Count);
            return compartmentIds[Math.Clamp(idx, 0, compartmentIds.Count - 1)];
        }

        /// <summary>
        /// 一次性构建完整的仿真数据包：区室化切分 + 设备绑定。
        /// 在按下 Begin 时由 MainWindow.OnBeginSimulationClick 调用。
        /// </summary>
        /// <param name="connections">实体间连接字典。</param>
        /// <param name="devices">场景中所有已放置的设备列表。</param>
        /// <returns>包含区室、探针和刺激数据的 SimulationData。</returns>
        public SimulationData BuildSimulationData(
            Dictionary<string, Connection> connections,
            List<IAttachedDevice> devices)
        {
            // 1. 区室化切分（同时得到 entityCompartmentMap 用于设备绑定）
            var compartments = new List<Compartment>();
            var entityCompartmentMap = new Dictionary<string, List<int>>();
            int globalId = 0;

            foreach (var kvp in RegisteredEntities)
            {
                var entity = kvp.Value;
                var ids = new List<int>();

                if (entity is AxonVisual axon)
                {
                    double totalLength = axon.Length;
                    int n = ComputeSegmentCount(totalLength);
                    double segLen = totalLength / n;

                    for (int i = 0; i < n; i++)
                    {
                        double t = (i + 0.5) / n;
                        double radius = axon.BaseRadius + (axon.TopRadius - axon.BaseRadius) * t;

                        var comp = new Compartment
                        {
                            GlobalId = globalId,
                            ParentEntityId = entity.Id,
                            ParentEntityType = axon.VisualType,
                            Index = i,
                            Length_um = segLen,
                            Diameter_um = radius * 2.0,
                            Cm = entity.Cm,
                            Ra = entity.Ra,
                            Channels = new Dictionary<string, ChannelProperty>(entity.Channels)
                        };
                        ids.Add(globalId);
                        compartments.Add(comp);
                        globalId++;
                    }

                    for (int i = 0; i < ids.Count - 1; i++)
                    {
                        compartments[ids[i]].ConnectedIds.Add(ids[i + 1]);
                        compartments[ids[i + 1]].ConnectedIds.Add(ids[i]);
                    }
                }

                entityCompartmentMap[entity.Id] = ids;

                // 将区室信息写回实体
                entity.CompartmentCount = ids.Count;
                entity.CompartmentIds = new List<int>(ids);
            }

            // 实体间连接
            foreach (var conn in connections.Values)
            {
                if (entityCompartmentMap.TryGetValue(conn.A.Id, out var compA) &&
                    entityCompartmentMap.TryGetValue(conn.B.Id, out var compB) &&
                    compA.Count > 0 && compB.Count > 0)
                {
                    int idA = ResolveCompartmentByAnchor(conn.AnchorA, compA);
                    int idB = ResolveCompartmentByAnchor(conn.AnchorB, compB);

                    if (!compartments[idA].ConnectedIds.Contains(idB))
                        compartments[idA].ConnectedIds.Add(idB);
                    if (!compartments[idB].ConnectedIds.Contains(idA))
                        compartments[idB].ConnectedIds.Add(idA);
                }
            }

            // 2. 设备绑定：将 Probe/Stimulation/VoltageClamp 按空间位置映射到最近的区室
            var probes = new List<SimProbe>();
            var stimulations = new List<SimStimulation>();
            var voltageClamps = new List<SimVoltageClamp>();
            int probeId = 0;
            int stimId = 0;
            int vcId = 0;

            foreach (var device in devices)
            {
                // 查找设备所属实体对应的区室列表
                if (!entityCompartmentMap.TryGetValue(device.TargetEntity.Id, out var compIds) || compIds.Count == 0)
                    continue;

                int segmentId = ResolveCompartmentByAnchor(device.Anchor, compIds);

                if (device is ProbeDevice probe)
                {
                    probes.Add(new SimProbe
                    {
                        ProbeId = probeId++,
                        StartMs = probe.StartMs,
                        DurationMs = probe.DurationMs,
                        SegmentId = segmentId,
                        SourceDeviceId = device.Id
                    });
                }
                else if (device is StimulationDevice stim)
                {
                    stimulations.Add(new SimStimulation
                    {
                        StimulationId = stimId++,
                        SegmentId = segmentId,
                        Stimulation_uA = stim.Stimulation_uA,
                        StimStart = stim.StimStart,
                        StimDuration = stim.StimDuration,
                        SourceDeviceId = device.Id
                    });
                }
                else if (device is VoltageClampDevice vc)
                {
                    var simVC = new SimVoltageClamp
                    {
                        VCId = vcId++,
                        SegmentId = segmentId,
                        Rs = vc.Rs,
                        SourceDeviceId = device.Id
                    };
                    foreach (var step in vc.Protocol)
                    {
                        simVC.Protocol.Add(new SimVCStep
                        {
                            Duration = step.Duration,
                            Amplitude = step.Amplitude
                        });
                    }
                    voltageClamps.Add(simVC);
                }
            }

            return new SimulationData
            {
                Compartments = compartments,
                Probes = probes,
                Stimulations = stimulations,
                VoltageClamps = voltageClamps
            };
        }
    }
}
