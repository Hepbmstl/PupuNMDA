using System.Globalization;
using System.Windows.Media;
using System.Windows.Media.Media3D;
using HelixToolkit.Wpf;

namespace NeuronCAD.Visuals.Tabs.Modeling.Visuals
{
    public class SomaVisual : VisualEntityBase
    {
        private double _radius;

        // CenterPosition is derived from the world transform applied to local origin
        public override Point3D CenterPosition =>
            Visual3D.Transform?.Transform(new Point3D(0, 0, 0)) ?? new Point3D(0, 0, 0);

        // 公开半径属性供修改
        public double Radius 
        { 
            get => _radius; 
            set { _radius = value; UpdateGeometry(); } 
        }

        public SomaVisual(Point3D center, double radius, Color color) : base()
        {
            _radius = radius;
            SetColor(color);
            UpdateGeometry();
            AlignTo(center, new Vector3D(0, 0, 1));
        }

        public override void AlignTo(Point3D position, Vector3D normal)
        {
            // For a sphere, only translation matters
            Visual3D.Transform = new TranslateTransform3D(position.X, position.Y, position.Z);
        }

        public override string GetDimensionInfo()
        {
            return $"Radius: {_radius:F2}";
        }

        protected override void UpdateGeometry()
        {
            // Geometry is always built at local origin; the world position is carried by the Transform
            var builder = new MeshBuilder();
            builder.AddSphere(new Point3D(0, 0, 0), _radius, 24, 24);
            MainModel.Geometry = builder.ToMesh();
        }
    }
}