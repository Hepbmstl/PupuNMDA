using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows.Media;
using System.Windows.Media.Media3D;
using HelixToolkit.Wpf;

namespace NeuronCAD.Visuals.Tabs.Modeling.Visuals
{
    public abstract class VisualEntityBase : IVisualEntity
    {
        public string Id { get; private set; }
        public ModelVisual3D Visual3D { get; private set; }

        public bool IsSelected { get; private set; }
        public bool IsHitTestVisible { get; private set; } = true;

        public abstract Point3D CenterPosition { get; }

        protected GeometryModel3D MainModel;
        protected Material _defaultMaterial;
        protected Material _selectedMaterial;
        protected Color _current_color = Colors.Gray;

        private LinesVisual3D? _wireframe;
        private VisualDisplayMode _displayMode = VisualDisplayMode.Normal;

        // IVisualEntity 接口契约实现
        public Color CurrentColor => _current_color;
        public Dictionary<string, ChannelProperty> Channels { get; set; } = new Dictionary<string, ChannelProperty>();

        // 维护不同离子的散点图层
        private Dictionary<string, PointsVisual3D> _channelVisuals = new Dictionary<string, PointsVisual3D>();
        private static readonly Random Rnd = new Random();

        public float capacitance;

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

            // Initialize transform so CombinedManipulator.Bind has a valid target
            Visual3D.Transform = new System.Windows.Media.Media3D.MatrixTransform3D(Matrix3D.Identity);
        }

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

        public void SetHitTestVisible(bool isVisible)
        {
            IsHitTestVisible = isVisible;
        }

        public void SetDisplayMode(VisualDisplayMode mode)
        {
            if (_displayMode == mode) return;
            _displayMode = mode;

            if (_displayMode == VisualDisplayMode.Normal)
            {
                MainModel.Material = IsSelected ? _selectedMaterial : _defaultMaterial;
                MainModel.BackMaterial = IsSelected ? _selectedMaterial : _defaultMaterial;

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
            else // Wireframe
            {
                MainModel.Material = null;
                MainModel.BackMaterial = null;

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

            // 3. 预计算所有三角面的面积和累积概率密度
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

            // 4. 重建点集分布
            foreach (var kvp in Channels)
            {
                var channel = kvp.Value;
                int pointCount = (int)(totalArea * channel.C_ion_channel);
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

                    // 生成重心坐标
                    double r1 = Rnd.NextDouble();
                    double r2 = Rnd.NextDouble();
                    double sqrtR1 = Math.Sqrt(r1);

                    double u = 1 - sqrtR1;
                    double v = sqrtR1 * (1 - r2);
                    double w = sqrtR1 * r2;

                    double px = u * p0.X + v * p1.X + w * p2.X;
                    double py = u * p0.Y + v * p1.Y + w * p2.Y;
                    double pz = u * p0.Z + v * p1.Z + w * p2.Z;

                    // 计算法线偏移，避免 Z-Fighting
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

        private void EnsureWireframe()
        {
            if (_wireframe != null) return;

            _wireframe = new LinesVisual3D
            {
                Thickness = 1.0
            };

            UpdateWireframeAppearance();
        }

        private void UpdateWireframeAppearance()
        {
            if (_wireframe == null) return;
            var baseColor = IsSelected ? Colors.Orange : _current_color;
            _wireframe.Color = Color.FromArgb(255, baseColor.R, baseColor.G, baseColor.B);
        }

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

        private void RebuildWireframeFromCurrentMesh()
        {
            if (_wireframe == null) return;
            if (MainModel.Geometry is not MeshGeometry3D mesh) return;
            if (mesh.Positions == null || mesh.TriangleIndices == null) return;

            var positions = mesh.Positions;
            var indices = mesh.TriangleIndices;

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

            var pts = new Point3DCollection(edges.Count * 2);
            foreach (var (a, b) in edges)
            {
                pts.Add(positions[a]);
                pts.Add(positions[b]);
            }

            _wireframe.Points = pts;
        }

        // ====== 派生类合同 ======
        public abstract string GetDimensionInfo();
        public abstract void AlignTo(Point3D position, Vector3D normal);

        protected abstract void UpdateGeometry();
    }

    public enum AnchorMode { AxonCylinder, SomaCylinder, SomaUniform }

    public sealed class AnchorRef
    {
        public AnchorMode Mode { get; set; }
        public double AxialT { get; set; }
        public double Angle { get; set; }
    }

    public class Connection
    {
        public string Id { get; } = Guid.NewGuid().ToString();

        public IVisualEntity A { get; }
        public IVisualEntity B { get; }

        public AnchorRef AnchorA { get; set; }
        public AnchorRef AnchorB { get; set; }

        public double Weight { get; set; } = 1.0;

        public Connection(IVisualEntity a, IVisualEntity b, AnchorRef anchorA, AnchorRef anchorB, double weight = 1.0)
        {
            A = a; B = b;
            AnchorA = anchorA; AnchorB = anchorB;
            Weight = weight;
        }
    }
}