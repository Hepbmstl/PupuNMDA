using System.Windows.Media;
using System.Windows.Media.Media3D;
using HelixToolkit.Wpf;

namespace NeuronCAD.Visuals.Tabs.Modeling.Visuals
{
    public class AxonVisual : VisualEntityBase
    {
        private Point3D _startPoint;
        private Point3D _endPoint;
        private double _radius;
        
        // 动态计算长度
        public double Length
        {
            get => (_endPoint - _startPoint).Length;
            set
            {
                var direction = (_endPoint - _startPoint);
                direction.Normalize();
                if (direction.Length == 0) direction = new Vector3D(0, 0, 1);
                _endPoint = _startPoint + (direction * value);
                UpdateGeometry();
            }
        }

        public double Radius
        {
            get => _radius;
            set { _radius = value; UpdateGeometry(); }
        }

        public override Point3D CenterPosition => 
            new Point3D((_startPoint.X + _endPoint.X)/2, (_startPoint.Y + _endPoint.Y)/2, (_startPoint.Z + _endPoint.Z)/2);

        public AxonVisual(Point3D start, Point3D end, double radius, Color color) : base()
        {
            _startPoint = start;
            _endPoint = end;
            _radius = radius;
            SetColor(color);
            UpdateGeometry();
        }

        public override void AlignTo(Point3D position, Vector3D normal)
        {
            double currentLen = Length;
            _startPoint = position;
            normal.Normalize();
            _endPoint = _startPoint + (normal * currentLen);
            UpdateGeometry();
        }

        // 实现抽象成员：使用 override
        public override string GetDimensionInfo()
        {
            return $"Length: {Length:F2}, Radius: {_radius:F2}";
        }

        protected override void UpdateGeometry()
        {
            var builder = new MeshBuilder();
            builder.AddCylinder(_startPoint, _endPoint, _radius * 2, 18);
            MainModel.Geometry = builder.ToMesh();
        }
    }
}