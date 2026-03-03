using System;
using System.Windows.Media;
using System.Windows.Media.Media3D;
using HelixToolkit.Wpf;

namespace NeuronCAD.Visuals.Tabs.Modeling.Visuals
{
    public enum DeviceType
    {
        Stimulation,
        Probe
    }

    /// <summary>
    /// 附属设备接口定义
    /// </summary>
    public interface IAttachedDevice
    {
        string Id { get; }
        DeviceType Type { get; }
        IVisualEntity TargetEntity { get; }
        AnchorRef Anchor { get; set; }
        ModelVisual3D Visual3D { get; }

        void UpdatePosition();
    }

    /// <summary>
    /// 附属设备抽象基类，负责处理箭头几何体的生成与法线对齐逻辑
    /// </summary>
    public abstract class AttachedDeviceBase : IAttachedDevice
    {
        public string Id { get; } = Guid.NewGuid().ToString();
        public abstract DeviceType Type { get; }
        public IVisualEntity TargetEntity { get; }
        public AnchorRef Anchor { get; set; }

        public ModelVisual3D Visual3D { get; } = new ModelVisual3D();
        protected ArrowVisual3D Arrow { get; }

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
        /// 更新箭头在三维空间中的位置和姿态
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
        /// 逆向计算目标实体在特定 Anchor 处的表面法线
        /// </summary>
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
    /// 刺激设备实体，持有刺激参数与黄色箭头外观
    /// </summary>
    public class StimulationDevice : AttachedDeviceBase
    {
        public override DeviceType Type => DeviceType.Stimulation;

        // 刺激的业务属性
        public double Voltage { get; set; } = 10.0;
        public double StartTime { get; set; } = 0.0;
        public double Duration { get; set; } = 5.0;

        public StimulationDevice(IVisualEntity target, AnchorRef anchor)
            : base(target, anchor, Colors.Yellow)
        {
        }
    }

    /// <summary>
    /// 探针设备实体，持有探针参数与青色箭头外观
    /// </summary>
    public class ProbeDevice : AttachedDeviceBase
    {
        public override DeviceType Type => DeviceType.Probe;

        // 探针的业务属性
        public double Threshold { get; set; } = -55.0;

        public ProbeDevice(IVisualEntity target, AnchorRef anchor)
            : base(target, anchor, Colors.Cyan)
        {
        }
    }
}