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
            Visual3D.Transform = new MatrixTransform3D(Matrix3D.Identity);
        }

        public void SetSelected(bool isSelected)
        {
            IsSelected = isSelected;

            // 线框模式下不显示面，因此这里要尊重 display mode
            if (_displayMode == VisualDisplayMode.Normal)
            {
                MainModel.Material = isSelected ? _selectedMaterial : _defaultMaterial;
                MainModel.BackMaterial = isSelected ? _selectedMaterial : _defaultMaterial;
            }

            // 线框颜色也可以跟随选中状态变化（可选）
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
                // 显示面
                MainModel.Material = IsSelected ? _selectedMaterial : _defaultMaterial;
                MainModel.BackMaterial = IsSelected ? _selectedMaterial : _defaultMaterial;

                // 隐藏线框
                if (_wireframe != null)
                {
                    Visual3D.Children.Remove(_wireframe);
                }
            }
            else // Wireframe
            {
                // 隐藏面（不写入深度，从而不遮挡 gimbal）
                MainModel.Material = null;
                MainModel.BackMaterial = null;

                EnsureWireframe();
                RebuildWireframeFromCurrentMesh();

                if (_wireframe != null && !Visual3D.Children.Contains(_wireframe))
                {
                    Visual3D.Children.Add(_wireframe);
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

            // 线框颜色策略：选中橙色，否则用当前色（也可以固定灰色）
            var baseColor = IsSelected ? Colors.Orange : _current_color;

            // 线框一般不需要很高 alpha，否则不清晰
            _wireframe.Color = Color.FromArgb(255, baseColor.R, baseColor.G, baseColor.B);
        }

        /// <summary>
        /// 派生类 UpdateGeometry() 改完 MainModel.Geometry 后，务必调用这个方法刷新线框（如果在线框模式）。
        /// </summary>
        protected void NotifyGeometryChanged()
        {
            if (_displayMode == VisualDisplayMode.Wireframe)
            {
                EnsureWireframe();
                RebuildWireframeFromCurrentMesh();
            }
        }

        private void RebuildWireframeFromCurrentMesh()
        {
            if (_wireframe == null) return;
            if (MainModel.Geometry is not MeshGeometry3D mesh) return;
            if (mesh.Positions == null || mesh.TriangleIndices == null) return;

            var positions = mesh.Positions;
            var indices = mesh.TriangleIndices;

            // 用无向边去重：min-max 作为 key
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

    public enum AnchorMode
    {
        AxonCylinder,
        SomaCylinder
    }

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