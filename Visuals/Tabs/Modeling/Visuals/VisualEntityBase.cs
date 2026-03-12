using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows.Media;
using System.Windows.Media.Media3D;
using HelixToolkit.Wpf;

namespace NeuronCAD.Visuals.Tabs.Modeling.Visuals
{
    /// <summary>
    /// 可视化实体抽象基类，实现 IVisualEntity 接口的公共逻辑。
    /// 持有 HelixToolkit 三维模型 (GeometryModel3D)、材质管理、线框模式切换、
    /// 离子通道表面散点可视化等功能。
    /// 派生类：SomaVisual（球体）、AxonVisual（圆台/圆柱）、DendVisual（AxonVisual 套壳）。
    /// 调用者：InteractionController（放置/选择/移动/显示模式切换）、
    /// PropertiesPanelController（属性编辑和通道管理）、MainWindow（编辑弹窗）。
    /// </summary>
    public abstract class VisualEntityBase : IVisualEntity
    {
        /// <summary>实体唯一标识符 (GUID)，在构造时自动生成。用于面板节点索引和连接字典 Key。</summary>
        public string Id { get; private set; }

        /// <summary>HelixToolkit 三维视觉对象根节点，包含主网格模型和子级散点/线框。</summary>
        public ModelVisual3D Visual3D { get; private set; }

        /// <summary>是否处于选中状态。由 SetSelected 方法管理。</summary>
        public bool IsSelected { get; private set; }

        /// <summary>是否参与射线命中测试。被 InteractionController 在放置/移动时禁用以避免自命中。</summary>
        public bool IsHitTestVisible { get; private set; } = true;

        /// <summary>实体在世界坐标系中的中心位置（抽象属性，由派生类根据变换矩阵计算）。</summary>
        public abstract Point3D CenterPosition { get; }

        /// <summary>主几何模型，持有 MeshGeometry3D 和材质。由派生类的 UpdateGeometry 方法更新。</summary>
        protected GeometryModel3D MainModel;

        /// <summary>默认材质（未选中时使用），颜色由 SetColor 设置。</summary>
        protected Material _defaultMaterial;

        /// <summary>选中状态材质（橙色高亮），在 SetSelected(true) 时应用。</summary>
        protected Material _selectedMaterial;
        protected Color _current_color = Colors.Gray;

        private LinesVisual3D? _wireframe;
        private VisualDisplayMode _displayMode = VisualDisplayMode.Normal;

        /// <summary>当前实体颜色，被 SetColor 更新，被面板读取 (CurrentColor 属性)。</summary>
        protected Color _current_color = Colors.Gray;

        /// <summary>线框模式下的线段可视化对象。在 Wireframe 模式时从网格三角面提取边线。</summary>
        private LinesVisual3D? _wireframe;

        /// <summary>当前显示模式（Normal/Wireframe）。由 SetDisplayMode 管理。</summary>
        private VisualDisplayMode _displayMode = VisualDisplayMode.Normal;

        /// <summary>当前颜色的公开只读属性，供 PropertiesPanelController 面板读取。</summary>
        public Color CurrentColor => _current_color;

        /// <summary>
        /// 实体绑定的离子通道字典。
        /// 被 PropertiesPanelController 的通道选择器弹窗操作添加/删除。
        /// </summary>
        public Dictionary<string, ChannelProperty> Channels { get; set; } = new Dictionary<string, ChannelProperty>();

        /// <summary>离子通道散点图层字典，Key 为通道名称，Value 为 PointsVisual3D。由 UpdateChannelVisuals 管理。</summary>
        private Dictionary<string, PointsVisual3D> _channelVisuals = new Dictionary<string, PointsVisual3D>();

        /// <summary>散点随机数生成器（全局共享），用于 UpdateChannelVisuals 中的蒙特卡洛采样。</summary>
        private static readonly Random Rnd = new Random();

        /// <summary>比膜电容 (µF/cm²)，标准值 1.0。</summary>
        public double Cm { get; set; } = 1.0;

        /// <summary>轴向电阻率 (Ω·cm)，标准值 35.4。</summary>
        public double Ra { get; set; } = 35.4;

        /// <summary>仿真后该实体被切分的区室数量。未仿真时为 0。</summary>
        public int CompartmentCount { get; set; } = 0;

        /// <summary>仿真后该实体拥有的区室全局 ID 列表。未仿真时为空。</summary>
        public List<int> CompartmentIds { get; set; } = new List<int>();

        /// <summary>
        /// 基类构造函数，初始化 GUID、Visual3D 容器、主几何模型和默认材质。
        /// 由派生类构造函数通过 base() 调用。
        /// </summary>
        protected VisualEntityBase()
        {
            Id = Guid.NewGuid().ToString();
            Visual3D = new ModelVisual3D();
            MainModel = new GeometryModel3D();

            _defaultMaterial = MaterialHelper.CreateMaterial(Colors.Gray);
            _selectedMaterial = MaterialHelper.CreateMaterial(Colors.Orange);

            MainModel.Material = _defaultMaterial;
            MainModel.BackMaterial = _defaultMaterial;

            Visual3D.Content = MainModel;

            // 初始化变换矩阵为单位矩阵，确保 CombinedManipulator.Bind 有合法目标
            Visual3D.Transform = new System.Windows.Media.Media3D.MatrixTransform3D(Matrix3D.Identity);
        }

        /// <summary>
        /// 设置实体选中状态，切换材质为选中高亮色或默认色。
        /// 同时更新线框颜色。
        /// 被 InteractionController.ForceSelect 和 StartPlacing/ConfirmAction 等方法调用。
        /// </summary>
        public void SetSelected(bool isSelected)
        {
            IsSelected = isSelected;

            if (_displayMode == VisualDisplayMode.Normal)
            {
                MainModel.Material = isSelected ? _selectedMaterial : _defaultMaterial;
                MainModel.BackMaterial = isSelected ? _selectedMaterial : _defaultMaterial;
            }

            UpdateWireframeAppearance();
        }

        /// <summary>
        /// 设置实体颜色并更新默认材质。
        /// 被 PropertiesPanelController 中颜色编辑文本框的 LostFocus 回调调用。
        /// </summary>
        public void SetColor(Color color)
        {
            _current_color = color;
            _defaultMaterial = MaterialHelper.CreateMaterial(color);

            if (!IsSelected && _displayMode == VisualDisplayMode.Normal)
            {
                MainModel.Material = _defaultMaterial;
                MainModel.BackMaterial = _defaultMaterial;
            }

            UpdateWireframeAppearance();
        }

        /// <summary>
        /// 设置实体透明度 (0.0~1.0)，通过调整颜色 Alpha 通道和重建材质实现。
        /// 预留接口，可用于半透明可视化效果。
        /// </summary>
        public void SetOpacity(double opacity)
        {
            opacity = Math.Clamp(opacity, 0.0, 1.0);

            var a = (byte)(opacity * 255);
            var c = _current_color;
            var colorWithAlpha = Color.FromArgb(a, c.R, c.G, c.B);

            var select = Colors.Orange;
            var selectColor = Color.FromArgb(a, select.R, select.G, select.B);

            _defaultMaterial = new DiffuseMaterial(new SolidColorBrush(colorWithAlpha));
            _selectedMaterial = new DiffuseMaterial(new SolidColorBrush(selectColor));

            if (_displayMode == VisualDisplayMode.Normal)
            {
                MainModel.Material = IsSelected ? _selectedMaterial : _defaultMaterial;
                MainModel.BackMaterial = IsSelected ? _selectedMaterial : _defaultMaterial;
            }

            UpdateWireframeAppearance();
        }

        /// <summary>
        /// 设置实体是否参与射线命中测试。
        /// 在放置/移动模式中禁用以避免鼠标射线命中自身。
        /// 被 InteractionController.StartPlacing/ConfirmAction/ShowGimbal/HideGimbal 调用。
        /// </summary>
        public void SetHitTestVisible(bool isVisible)
        {
            IsHitTestVisible = isVisible;
        }

        /// <summary>
        /// 切换实体显示模式。Normal 模式显示材质和散点，Wireframe 模式显示线框并隐藏材质和散点。
        /// 被 InteractionController.ShowGimbal（切为 Wireframe）和 HideGimbal（切为 Normal）调用。
        /// </summary>
        public void SetDisplayMode(VisualDisplayMode mode)
        {
            if (_displayMode == mode) return;
            _displayMode = mode;

            if (_displayMode == VisualDisplayMode.Normal)
            {
                // 恢复正常材质
                MainModel.Material = IsSelected ? _selectedMaterial : _defaultMaterial;
                MainModel.BackMaterial = IsSelected ? _selectedMaterial : _defaultMaterial;

                // 移除线框
                if (_wireframe != null)
                {
                    Visual3D.Children.Remove(_wireframe);
                }

                // 恢复通道散点显示
                foreach (var visual in _channelVisuals.Values)
                {
                    if (!Visual3D.Children.Contains(visual))
                        Visual3D.Children.Add(visual);
                }
            }
            else // Wireframe 模式
            {
                // 清除材质使模型透明
                MainModel.Material = null;
                MainModel.BackMaterial = null;

                // 构建并显示线框
                EnsureWireframe();
                RebuildWireframeFromCurrentMesh();

                if (_wireframe != null && !Visual3D.Children.Contains(_wireframe))
                {
                    Visual3D.Children.Add(_wireframe);
                }

                // 隐藏通道散点，保证线框模式视线不被遮挡
                foreach (var visual in _channelVisuals.Values)
                {
                    Visual3D.Children.Remove(visual);
                }
            }
        }

        /// <summary>
        /// 刷新离子通道表面散点可视化。
        /// 根据 Channels 字典中的每个通道，使用蒙特卡洛方法在网格表面按面积加权随机采样生成散点。
        /// 散点数量 = 三角面总面积 × GlobalBiophysics.ConductanceToRenderDensity(通道电导 G_ion_channel)（渲染专用转换）。
        /// 被 PropertiesPanelController 中添加/删除通道后调用，以及 NotifyGeometryChanged 在几何变更时调用。
        /// </summary>
        public void UpdateChannelVisuals()
        {
            // 1. 清理当前图层引用的显存资源
            foreach (var vis in _channelVisuals.Values)
            {
                Visual3D.Children.Remove(vis);
            }
            _channelVisuals.Clear();

            // 2. 拦截空数据
            if (MainModel.Geometry is not MeshGeometry3D mesh ||
                mesh.Positions == null || mesh.TriangleIndices == null || mesh.Positions.Count == 0)
                return;

            var positions = mesh.Positions;
            var indices = mesh.TriangleIndices;
            int triangleCount = indices.Count / 3;

            // 3. 预计算所有三角面的面积和累积概率密度（用于面积加权采样）
            double[] cumulativeAreas = new double[triangleCount];
            double totalArea = 0;

            for (int i = 0; i < triangleCount; i++)
            {
                Point3D p0 = positions[indices[i * 3]];
                Point3D p1 = positions[indices[i * 3 + 1]];
                Point3D p2 = positions[indices[i * 3 + 2]];

                Vector3D v1 = p1 - p0;
                Vector3D v2 = p2 - p0;
                double area = Vector3D.CrossProduct(v1, v2).Length * 0.5;

                totalArea += area;
                cumulativeAreas[i] = totalArea;
            }

            if (totalArea <= 0) return;

            // 4. 为每个通道重建表面散点分布
            foreach (var kvp in Channels)
            {
                var channel = kvp.Value;
                int pointCount = (int)(totalArea * GlobalBiophysics.ConductanceToRenderDensity(channel.G_ion_channel));
                if (pointCount <= 0) continue;

                var points = new Point3DCollection(pointCount);

                for (int p = 0; p < pointCount; p++)
                {
                    // 二分查找选中一个具备面积权重的三角形面片
                    double randomArea = Rnd.NextDouble() * totalArea;
                    int triIndex = Array.BinarySearch(cumulativeAreas, randomArea);
                    if (triIndex < 0) triIndex = ~triIndex;
                    if (triIndex >= triangleCount) triIndex = triangleCount - 1;

                    Point3D p0 = positions[indices[triIndex * 3]];
                    Point3D p1 = positions[indices[triIndex * 3 + 1]];
                    Point3D p2 = positions[indices[triIndex * 3 + 2]];

                    // 生成重心坐标（均匀三角采样算法）
                    double r1 = Rnd.NextDouble();
                    double r2 = Rnd.NextDouble();
                    double sqrtR1 = Math.Sqrt(r1);

                    double u = 1 - sqrtR1;
                    double v = sqrtR1 * (1 - r2);
                    double w = sqrtR1 * r2;

                    double px = u * p0.X + v * p1.X + w * p2.X;
                    double py = u * p0.Y + v * p1.Y + w * p2.Y;
                    double pz = u * p0.Z + v * p1.Z + w * p2.Z;

                    // 沿法线方向偏移 0.05，避免 Z-Fighting
                    Vector3D normal = Vector3D.CrossProduct(p1 - p0, p2 - p0);
                    if (normal.LengthSquared > 1e-10)
                    {
                        normal.Normalize();
                        px += normal.X * 0.05;
                        py += normal.Y * 0.05;
                        pz += normal.Z * 0.05;
                    }

                    points.Add(new Point3D(px, py, pz));
                }

                var pointsVis = new PointsVisual3D
                {
                    Points = points,
                    Color = channel.Color,
                    Size = 5
                };

                _channelVisuals[kvp.Key] = pointsVis;

                if (_displayMode == VisualDisplayMode.Normal)
                {
                    Visual3D.Children.Add(pointsVis);
                }
            }
        }

        /// <summary>
        /// 懒初始化线框对象。在首次进入 Wireframe 模式时调用。
        /// </summary>
        private void EnsureWireframe()
        {
            if (_wireframe != null) return;

            _wireframe = new LinesVisual3D
            {
                Thickness = 1.0
            };

            UpdateWireframeAppearance();
        }

        /// <summary>
        /// 更新线框颜色：选中时橙色，未选中时使用实体当前颜色。
        /// 被 SetSelected、SetColor、SetOpacity 调用。
        /// </summary>
        private void UpdateWireframeAppearance()
        {
            if (_wireframe == null) return;
            var baseColor = IsSelected ? Colors.Orange : _current_color;
            _wireframe.Color = Color.FromArgb(255, baseColor.R, baseColor.G, baseColor.B);
        }

        /// <summary>
        /// 通知几何数据已变更。当派生类的半径/长度等参数修改触发网格重建后调用。
        /// 在 Wireframe 模式下同步重建线框数据，同时强制刷新通道散点。
        /// 被派生类的 UpdateGeometry 方法在末尾调用。
        /// </summary>
        protected void NotifyGeometryChanged()
        {
            if (_displayMode == VisualDisplayMode.Wireframe)
            {
                EnsureWireframe();
                RebuildWireframeFromCurrentMesh();
            }

            // 当尺寸（半径、长度）修改触发网格变更时，必须强制同步点云数据
            UpdateChannelVisuals();
        }

        /// <summary>
        /// 从当前 MeshGeometry3D 的三角面数据中提取所有唯一边线，重建线框顶点集合。
        /// 在 Wireframe 模式下由 NotifyGeometryChanged 和 SetDisplayMode 调用。
        /// </summary>
        private void RebuildWireframeFromCurrentMesh()
        {
            if (_wireframe == null) return;
            if (MainModel.Geometry is not MeshGeometry3D mesh) return;
            if (mesh.Positions == null || mesh.TriangleIndices == null) return;

            var positions = mesh.Positions;
            var indices = mesh.TriangleIndices;

            // 使用 HashSet 去重边线（无向边：始终取较小索引在前）
            var edges = new HashSet<(int a, int b)>();

            void AddEdge(int i1, int i2)
            {
                if (i1 == i2) return;
                var a = Math.Min(i1, i2);
                var b = Math.Max(i1, i2);
                edges.Add((a, b));
            }

            for (int i = 0; i + 2 < indices.Count; i += 3)
            {
                int i0 = indices[i];
                int i1 = indices[i + 1];
                int i2 = indices[i + 2];
                AddEdge(i0, i1);
                AddEdge(i1, i2);
                AddEdge(i2, i0);
            }

            // 将去重后的边线转为线段顶点对
            var pts = new Point3DCollection(edges.Count * 2);
            foreach (var (a, b) in edges)
            {
                pts.Add(positions[a]);
                pts.Add(positions[b]);
            }

            _wireframe.Points = pts;
        }

        // ====== 派生类抽象契约 ======

        /// <summary>获取实体尺寸信息字符串（由派生类实现）。</summary>
        public abstract string GetDimensionInfo();

        /// <summary>将实体对齐到指定世界坐标和法线方向（由派生类根据几何特征实现）。</summary>
        public abstract void AlignTo(Point3D position, Vector3D normal);

        /// <summary>更新几何网格（由派生类在参数变更时调用，末尾应调用 NotifyGeometryChanged）。</summary>
        protected abstract void UpdateGeometry();
    }

    /// <summary>
    /// 锚点模式枚举，描述锚点在实体表面的定位方式。
    /// 被 AnchorRef.Mode 使用，由 AxonVisual.TryWorldPointToAnchor 和
    /// AttachedDeviceBase.CalculateWorldNormal 根据命中位置决定。
    /// </summary>
    public enum AnchorMode
    {
        /// <summary>锚点在圆柱/圆台侧面（Axon/Dend），使用 AxialT 和 Angle 定位。</summary>
        AxonCylinder,
        /// <summary>锚点在 Soma 圆柱表面（预留，当前 SomaVisual 使用 SomaUniform）。</summary>
        SomaCylinder,
        /// <summary>锚点在 Soma 表面均匀分布（简化版本，返回球心）。</summary>
        SomaUniform,
        /// <summary>锚点在 Axon/Dend 底面端盖 (Z=0)。</summary>
        AxonCapStart,
        /// <summary>锚点在 Axon/Dend 顶面端盖 (Z=Length)。</summary>
        AxonCapEnd
    }

    /// <summary>
    /// 锚点引用数据类，描述实体表面上一个精确位置。
    /// 通过 (Mode, AxialT, Angle) 三元组唯一确定表面位置。
    /// 被 Connection（实体间连接线端点）和 IAttachedDevice（仿真设备吸附点）持有。
    /// </summary>
    public sealed class AnchorRef
    {
        /// <summary>锚点定位模式（侧面圆柱/端盖/球面等）。</summary>
        public AnchorMode Mode { get; set; }

        /// <summary>
        /// 轴向参数 (0.0~1.0)，0.0 表示底端 (Z=0)，1.0 表示顶端 (Z=Length)。
        /// 对 Soma 类型无实际意义。
        /// </summary>
        public double AxialT { get; set; }

        /// <summary>
        /// 周向角度 (弧度)，表示在横截面上的旋转位置。
        /// 对端盖锚点和 Soma 类型无实际意义。
        /// </summary>
        public double Angle { get; set; }
    }

    /// <summary>
    /// 两个实体之间的连接数据类，持有连接两端的实体引用和锚点信息。
    /// 由 InteractionController.ConfirmAction 或 ShowContextMenu 中的 "Connect" 操作创建。
    /// 被 ConnectionController 管理生命周期和可视化更新。
    /// </summary>
    public class Connection
    {
        /// <summary>连接唯一标识符 (GUID)，用于 ConnectionController 字典索引。</summary>
        public string Id { get; } = Guid.NewGuid().ToString();

        /// <summary>连接端点 A 的实体引用。</summary>
        public IVisualEntity A { get; }

        /// <summary>连接端点 B 的实体引用。</summary>
        public IVisualEntity B { get; }

        /// <summary>端点 A 在实体表面的锚点位置。可被拖拽修改。</summary>
        public AnchorRef AnchorA { get; set; }

        /// <summary>端点 B 在实体表面的锚点位置。可被拖拽修改。</summary>
        public AnchorRef AnchorB { get; set; }

        /// <summary>连接权重，预留用于仿真计算中的突触强度等参数。</summary>
        public double Weight { get; set; } = 1.0;

        /// <summary>
        /// 构造函数，创建连接两个实体的 Connection 实例。
        /// 由 InteractionController.ConfirmAction 和右键菜单 Connect 操作调用。
        /// </summary>
        public Connection(IVisualEntity a, IVisualEntity b, AnchorRef anchorA, AnchorRef anchorB, double weight = 1.0)
        {
            A = a; B = b;
            AnchorA = anchorA; AnchorB = anchorB;
            Weight = weight;
        }
    }
}