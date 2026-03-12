using System.Globalization;
using System.Windows.Media;
using System.Windows.Media.Media3D;
using HelixToolkit.Wpf;

namespace NeuronCAD.Visuals.Tabs.Modeling.Visuals
{
    /// <summary>
    /// 细胞体 (Soma) 可视化实体，以球体形式渲染。
    /// 继承 VisualEntityBase 并实现 IAnchoredEntity 接口。
    /// 由 MainWindow.OnAddSomaClick 创建，通过 InteractionController.StartPlacing 进入放置流程。
    /// </summary>
    public class SomaVisual : VisualEntityBase, IAnchoredEntity
    {
        /// <summary>球体半径，修改时自动触发 UpdateGeometry 重建网格。</summary>
        private double _radius;

        /// <summary>
        /// 世界坐标系中的中心位置，通过 Visual3D 的变换矩阵计算得出。
        /// 被 Connection 端点回退和 AttachedDeviceBase 法线计算引用。
        /// </summary>
        public override Point3D CenterPosition =>
            Visual3D.Transform?.Transform(new Point3D(0, 0, 0)) ?? new Point3D(0, 0, 0);

        /// <summary>
        /// 球体半径公开属性。设置时自动调用 UpdateGeometry 重建网格。
        /// 被 MainWindow.OnApplyEdit 和 PropertiesPanelController 面板编辑修改。
        /// </summary>
        public double Radius
        {
            get => _radius;
            set { _radius = value; UpdateGeometry(); }
        }

        /// <summary>
        /// 构造函数，创建指定中心、半径和颜色的球体可视化实体。
        /// 由 MainWindow.OnAddSomaClick 调用。
        /// </summary>
        /// <param name="center">初始中心位置</param>
        /// <param name="radius">球体半径</param>
        /// <param name="color">实体颜色</param>
        public SomaVisual(Point3D center, double radius, Color color) : base()
        {
            _radius = radius;
            SetColor(color);
            UpdateGeometry();
            AlignTo(center, new Vector3D(0, 0, 1));
        }

        /// <summary>
        /// 将球体对齐到指定位置。球体仅需平移，法线参数被忽略。
        /// 被 InteractionController.UpdateObjectPosition 在放置/移动时调用。
        /// </summary>
        public override void AlignTo(Point3D position, Vector3D normal)
        {
            Visual3D.Transform = new TranslateTransform3D(position.X, position.Y, position.Z);
        }

        /// <summary>返回球体尺寸信息字符串。预留用于状态栏或提示信息。</summary>
        public override string GetDimensionInfo()
        {
            return $"Radius: {_radius:F2}";
        }

        /// <summary>
        /// 重建球体网格。几何体总是在局部原点构建，世界位置由 Transform 承载。
        /// 在 Radius 属性 setter 和构造函数中调用。
        /// </summary>
        protected override void UpdateGeometry()
        {
            var builder = new MeshBuilder();
            builder.AddSphere(new Point3D(0, 0, 0), _radius, 24, 24);
            MainModel.Geometry = builder.ToMesh();
            MainModel.Geometry = builder.ToMesh();
            MainModel.Geometry = builder.ToMesh();
            NotifyGeometryChanged();
        }

        /// <summary>
        /// 将世界坐标点转换为球体表面锚点（简化版本，返回统一锚点）。
        /// 被 InteractionController.ConfirmAction 和 SimulationInteractionController.UpdatePlacingDevice 调用。
        /// </summary>
        public bool TryWorldPointToAnchor(Point3D worldPoint, out AnchorRef anchor)
        {
            anchor = new AnchorRef { Mode = AnchorMode.SomaUniform, AxialT = 0.5, Angle = 0.0 };
            return true;
        }

        /// <summary>
        /// 将锚点引用转换回世界坐标（简化版本，始终返回球心位置）。
        /// 被 ConnectionController.Update 和 AttachedDeviceBase.UpdatePosition 调用。
        /// </summary>
        public bool TryAnchorToWorldPoint(AnchorRef anchor, out Point3D worldPoint)
        {
            worldPoint = CenterPosition;
            return true;
        }
    }
}