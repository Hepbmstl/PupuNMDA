using System;
using System.Windows.Media;
using System.Windows.Media.Media3D;
using HelixToolkit.Wpf;

namespace NeuronCAD.Visuals.Tabs.Modeling.Visuals
{
    /// <summary>
    /// 仿真设备类型枚举，区分刺激和探针。
    /// 由 SimulationInteractionController.StartPlacingDevice 和 IAttachedDevice.Type 使用。
    /// </summary>
    public enum DeviceType
    {
        /// <summary>刺激设备（黄色箭头）</summary>
        Stimulation,
        /// <summary>探针设备（青色箭头）</summary>
        Probe
    }

    /// <summary>
    /// 附属设备接口定义，所有仿真设备（刺激/探针）必须实现。
    /// 被 SharedSceneState.Devices 列表持有，被 SimulationInteractionController/SimulationPanelController 操作。
    /// </summary>
    public interface IAttachedDevice
    {
        /// <summary>设备唯一标识符 (GUID)。</summary>
        string Id { get; }

        /// <summary>设备类型（刺激/探针）。</summary>
        DeviceType Type { get; }

        /// <summary>设备依附的目标实体。</summary>
        IVisualEntity TargetEntity { get; }

        /// <summary>设备在目标实体表面的锚点引用。</summary>
        AnchorRef Anchor { get; set; }

        /// <summary>设备的 3D 可视化对象。</summary>
        ModelVisual3D Visual3D { get; }

        /// <summary>更新设备在 3D 空间中的位置和姿态。被 SimulationInteractionController 和 InteractionController.UpdateObjectPosition 调用。</summary>
        void UpdatePosition();
    }

    /// <summary>
    /// 附属设备抽象基类，负责处理箭头几何体的生成与法线对齐逻辑。
    /// 由 StimulationDevice 和 ProbeDevice 继承。
    /// </summary>
    public abstract class AttachedDeviceBase : IAttachedDevice
    {
        /// <summary>设备唯一标识符 (GUID)，创建时自动生成。</summary>
        public string Id { get; } = Guid.NewGuid().ToString();

        /// <summary>设备类型，由子类实现。</summary>
        public abstract DeviceType Type { get; }

        /// <summary>设备依附的目标实体。由构造函数设置。</summary>
        public IVisualEntity TargetEntity { get; }

        /// <summary>设备在目标实体表面的锚点引用，拖拽时可更新。</summary>
        public AnchorRef Anchor { get; set; }

        /// <summary>设备的 3D 可视化对象容器。</summary>
        public ModelVisual3D Visual3D { get; } = new ModelVisual3D();

        /// <summary>箭头可视化对象，显示设备方向。</summary>
        protected ArrowVisual3D Arrow { get; }

        /// <summary>
        /// 构造函数。初始化箭头可视化对象并调用 UpdatePosition 设置初始位置。
        /// 由 StimulationDevice/ProbeDevice 构造函数调用。
        /// </summary>
        /// <param name="target">目标实体</param>
        /// <param name="anchor">初始锚点</param>
        /// <param name="color">箭头颜色</param>
        protected AttachedDeviceBase(IVisualEntity target, AnchorRef anchor, Color color)
        {
            TargetEntity = target;
            Anchor = anchor;

            // 初始化箭头可视化对象
            Arrow = new ArrowVisual3D
            {
                Fill = new SolidColorBrush(color),
                HeadLength = 1.5,
                Diameter = 0.4
            };

            Visual3D.Children.Add(Arrow);
            UpdatePosition();
        }

        /// <summary>
        /// 更新箭头在三维空间中的位置和姿态，由锚点和法线计算箭尾/箭尖。
        /// 被 SimulationInteractionController（放置/拖拽时）和 InteractionController.UpdateObjectPosition（移动实体时同步更新）调用。
        /// </summary>
        public void UpdatePosition()
        {
            if (TargetEntity is not IAnchoredEntity anchoredTarget) return;

            // 1. 获取表面接触点 (世界坐标系)
            if (anchoredTarget.TryAnchorToWorldPoint(Anchor, out Point3D tipPoint))
            {
                // 2. 计算接触点表面的法线向量 (世界坐标系)
                Vector3D normal = CalculateWorldNormal();

                // 3. 设定箭头长度，并让箭头指向表面 (从外向内)
                double arrowLength = 4.0;
                Point3D tailPoint = tipPoint + normal * arrowLength;

                Arrow.Point1 = tailPoint; // 箭尾 (远离表面)
                Arrow.Point2 = tipPoint;  // 箭头尖端 (贴合表面)
            }
        }

        /// <summary>
        /// 逆向计算目标实体在特定 Anchor 处的表面法线（世界坐标系）。
        /// 对 AxonVisual 使用圆台几何计算，对 SomaVisual 等球体使用球心指向表面向量。
        /// 由 UpdatePosition 内部调用。
        /// </summary>
        /// <returns>世界坐标系下的单位法线向量</returns>
        private Vector3D CalculateWorldNormal()
        {
            Vector3D normal = new Vector3D(0, 0, 1);

            // 如果依附对象是圆柱体/圆台 (Axon 或 Dend)
            if (TargetEntity is AxonVisual axon)
            {
                double cos = Math.Cos(Anchor.Angle);
                double sin = Math.Sin(Anchor.Angle);

                // 考虑圆台的倾斜度 (斜率)
                double dz = axon.BaseRadius - axon.TopRadius;
                double slopeLen = Math.Sqrt(dz * dz + axon.Length * axon.Length);
                double nz = slopeLen > 0 ? dz / slopeLen : 0;
                double nxy = slopeLen > 0 ? axon.Length / slopeLen : 1;

                Vector3D localNormal;
                if (Anchor.Mode == AnchorMode.AxonCapStart)
                    localNormal = new Vector3D(0, 0, -1);
                else if (Anchor.Mode == AnchorMode.AxonCapEnd)
                    localNormal = new Vector3D(0, 0, 1);
                else
                    localNormal = new Vector3D(cos * nxy, sin * nxy, nz);

                // 使用刚体变换矩阵将局部法线转换为世界法线
                if (axon.Visual3D.Transform != null)
                {
                    normal = axon.Visual3D.Transform.Value.Transform(localNormal);
                    normal.Normalize();
                }
                else
                {
                    normal = localNormal;
                }
            }
            // 如果依附对象是细胞体 (Soma) 等球状物体
            else
            {
                if (TargetEntity is IAnchoredEntity aEnt && aEnt.TryAnchorToWorldPoint(Anchor, out Point3D tip))
                {
                    // 球体表面的法线即为从球心指向表面的向量
                    normal = tip - TargetEntity.CenterPosition;
                    if (normal.LengthSquared > 1e-6)
                        normal.Normalize();
                    else
                        normal = new Vector3D(0, 1, 0);
                }
            }

            return normal;
        }
    }

    /// <summary>
    /// 刺激设备实体，持有刺激电流、开始时间和持续时间参数，黄色箭头外观。
    /// 对应 Hines_method.py: insert_stimulation(stimulation_id, segment_id, stimulation_uA, stim_start, stim_duration)
    /// 由 SimulationInteractionController.UpdatePlacingDevice 创建，被 SimulationPanelController.BuildDeviceNode 构建参数卡片。
    /// </summary>
    public class StimulationDevice : AttachedDeviceBase
    {
        /// <summary>设备类型：刺激。</summary>
        public override DeviceType Type => DeviceType.Stimulation;

        /// <summary>刺激电流强度 (µA)，默认 0.1。由 SimulationPanelController 参数卡片编辑。</summary>
        public double Stimulation_uA { get; set; } = 0.1;

        /// <summary>刺激开始时间 (ms)，默认 0.0。由 SimulationPanelController 参数卡片编辑。</summary>
        public double StimStart { get; set; } = 0.0;

        /// <summary>刺激持续时间 (ms)，默认 5.0。由 SimulationPanelController 参数卡片编辑。</summary>
        public double StimDuration { get; set; } = 5.0;

        /// <summary>
        /// 构造函数。由 SimulationInteractionController.UpdatePlacingDevice 创建。
        /// </summary>
        /// <param name="target">目标实体</param>
        /// <param name="anchor">初始锚点</param>
        public StimulationDevice(IVisualEntity target, AnchorRef anchor)
            : base(target, anchor, Colors.Yellow)
        {
        }
    }

    /// <summary>
    /// 探针设备实体，持有采样起始时间与持续时间参数（单位 ms），青色箭头外观。
    /// 对应 Hines_method.py: insert_probe(probe_id, segment_id, probe_start_ms, probe_duration_ms)
    /// 由 SimulationInteractionController.UpdatePlacingDevice 创建，被 SimulationPanelController.BuildDeviceNode 构建参数卡片。
    /// </summary>
    public class ProbeDevice : AttachedDeviceBase
    {
        /// <summary>设备类型：探针。</summary>
        public override DeviceType Type => DeviceType.Probe;

        /// <summary>探针采样开始时间（ms）。按照 Hines_method 约定传入 insert_probe 的 probe_start_ms。</summary>
        public double StartMs { get; set; } = 0.0;

        /// <summary>探针采样持续时间（ms）。按照 Hines_method 约定传入 insert_probe 的 probe_duration_ms。</summary>
        public double DurationMs { get; set; } = 1.0;

        /// <summary>
        /// 构造函数。由 SimulationInteractionController.UpdatePlacingDevice 创建。
        /// </summary>
        /// <param name="target">目标实体</param>
        /// <param name="anchor">初始锚点</param>
        public ProbeDevice(IVisualEntity target, AnchorRef anchor)
            : base(target, anchor, Colors.Cyan)
        {
        }
    }
}