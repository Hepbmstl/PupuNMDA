using System.Windows.Media;
using System.Windows.Media.Media3D;
using HelixToolkit.Wpf;
using System;

namespace NeuronCAD.Visuals.Tabs.Modeling.Visuals
{
    public class AxonVisual : VisualEntityBase, IAnchoredEntity
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

        public override void AlignTo(Point3D position, Vector3D normal) // 添加入画板当中
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
            NotifyGeometryChanged();
        }

        public bool TryWorldPointToAnchor(Point3D worldPoint, out AnchorRef anchor)
        {
            anchor = new AnchorRef { Mode = AnchorMode.AxonCylinder, AxialT = 0.5, Angle = 0.0 };

            // Transform 可能为空或不可逆，做保护
            if (Visual3D.Transform == null) return false;
            var inv = Visual3D.Transform.Value;
            if (!inv.HasInverse) return false;
            inv.Invert();

            var local = inv.Transform(worldPoint);

            // 轴向参数：local.Z / Length
            var t = _length <= 1e-9 ? 0.5 : local.Z / _length;
            t = Math.Clamp(t, 0.0, 1.0);

            // 圆周角：atan2(y,x)
            double angle;
            double r2 = local.X * local.X + local.Y * local.Y;

            // 在圆柱轴线附近 angle 不稳定：用上一次角度，避免跳到“里面/另一侧”
            if (r2 < 1e-6)
            {
                angle = _lastAnchorAngle;
            }
            else
            {
                angle = Math.Atan2(local.Y, local.X);
                _lastAnchorAngle = angle;
            }


            anchor.Mode = AnchorMode.AxonCylinder;
            anchor.AxialT = t;
            anchor.Angle = angle;
            return true;
        }

        public bool TryAnchorToWorldPoint(AnchorRef anchor, out Point3D worldPoint)
        {
            worldPoint = new Point3D();

            if (Visual3D.Transform == null) return false;

            double r = GetMeshRadiusFallbackToField();
            // 落在表面：x = r cos, y = r sin, z = t*Length
            double z = Math.Clamp(anchor.AxialT, 0.0, 1.0) * _length;
            double x = r * Math.Cos(anchor.Angle);
            double y = r * Math.Sin(anchor.Angle);

            var local = new Point3D(x, y, z);
            worldPoint = Visual3D.Transform.Transform(local);
            return true;
        }

        private double GetMeshRadiusFallbackToField()
        {
            if (MainModel.Geometry is MeshGeometry3D mesh && mesh.Positions != null && mesh.Positions.Count > 0)
            {
                double maxR2 = 0.0;
                foreach (var p in mesh.Positions)
                {
                    double r2 = p.X * p.X + p.Y * p.Y;
                    if (r2 > maxR2) maxR2 = r2;
                }
                var r = Math.Sqrt(maxR2);
                if (r > 1e-9) return r;
            }

            return _radius;
        }


    }
}