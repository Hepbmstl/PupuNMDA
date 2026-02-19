using System.Windows.Media;
using System.Windows.Media.Media3D;
using HelixToolkit.Wpf;

namespace NeuronCAD.Visuals.Tabs.Modeling.Visuals
{
    public class AxonVisual : VisualEntityBase
    {
        private double _length;
        private double _radius;

        // Length is stored directly; geometry is along local Z axis
        public double Length
        {
            get => _length;
            set { _length = value; UpdateGeometry(); }
        }

        public double Radius
        {
            get => _radius;
            set { _radius = value; UpdateGeometry(); }
        }

        // CenterPosition is the midpoint of the local Z-axis cylinder, transformed to world space
        public override Point3D CenterPosition
        {
            get
            {
                var localCenter = new Point3D(0, 0, _length / 2);
                return Visual3D.Transform?.Transform(localCenter) ?? localCenter;
            }
        }

        public AxonVisual(Point3D start, Point3D end, double radius, Color color) : base()
        {
            var direction = end - start;
            _length = direction.Length > 0 ? direction.Length : 1.0;
            _radius = radius;
            SetColor(color);
            UpdateGeometry();
            // Use a default direction when start == end to avoid normalizing a zero vector in AlignTo
            var alignNormal = direction.Length > 0 ? direction : new Vector3D(0, 0, 1);
            AlignTo(start, alignNormal);
        }

        public override void AlignTo(Point3D position, Vector3D normal)
        {
            normal.Normalize();
            var localZ = new Vector3D(0, 0, 1);

            // Build a rotation matrix that aligns local Z with the desired normal
            var matrix = Matrix3D.Identity;
            var axis = Vector3D.CrossProduct(localZ, normal);
            if (axis.LengthSquared > 1e-10)
            {
                axis.Normalize();
                double angle = Vector3D.AngleBetween(localZ, normal);
                matrix.Rotate(new Quaternion(axis, angle));
            }
            else if (Vector3D.DotProduct(localZ, normal) < 0)
            {
                // 180-degree flip
                matrix.Rotate(new Quaternion(new Vector3D(1, 0, 0), 180));
            }

            // Apply translation so the cylinder starts at 'position'
            matrix.Translate(new Vector3D(position.X, position.Y, position.Z));
            Visual3D.Transform = new MatrixTransform3D(matrix);
        }

        // 实现抽象成员：使用 override
        public override string GetDimensionInfo()
        {
            return $"Length: {_length:F2}, Radius: {_radius:F2}";
        }

        protected override void UpdateGeometry()
        {
            // Geometry is built along local Z axis from (0,0,0) to (0,0,_length)
            var builder = new MeshBuilder();
            builder.AddCylinder(new Point3D(0, 0, 0), new Point3D(0, 0, _length), _radius * 2, 18);
            MainModel.Geometry = builder.ToMesh();
        }
    }
}