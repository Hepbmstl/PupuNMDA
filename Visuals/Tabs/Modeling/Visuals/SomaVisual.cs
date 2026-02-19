using System.Globalization;
using System.Windows.Media;
using System.Windows.Media.Media3D;
using HelixToolkit.Wpf;

namespace NeuronCAD.Visuals.Tabs.Modeling.Visuals
{
    public class SomaVisual : VisualEntityBase
    {
        private Point3D _center;
        private double _radius;

        public override Point3D CenterPosition => _center;
        
        // 公开半径属性供修改
        public double Radius 
        { 
            get => _radius; 
            set { _radius = value; UpdateGeometry(); } 
        }

        public SomaVisual(Point3D center, double radius, Color color) : base()
        {
            _center = center;
            _radius = radius;
            SetColor(color);
            UpdateGeometry();
        }

        public override void AlignTo(Point3D position, Vector3D normal)
        {
            _center = position;
            UpdateGeometry();
        }

        public override string GetDimensionInfo()
        {
            return $"Radius: {_radius:F2}";
        }

        protected override void UpdateGeometry()
        {
            var builder = new MeshBuilder();
            builder.AddSphere(_center, _radius, 24, 24);
            MainModel.Geometry = builder.ToMesh();
        }
    }
}