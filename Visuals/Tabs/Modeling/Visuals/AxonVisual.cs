using System.Windows.Media;
using System.Windows.Media.Media3D;
using HelixToolkit.Wpf;
using System;

namespace NeuronCAD.Visuals.Tabs.Modeling.Visuals
{
    public class AxonVisual : VisualEntityBase, IAnchoredEntity
    {
        // 新增类别标识字段
        public string VisualType { get; private set; }

        private double _length;
        private double _baseRadius;
        private double _topRadius;
        private double _lastAnchorAngle = 0.0;

        public double Length
        {
            get => _length;
            set { _length = value; UpdateGeometry(); }
        }

        public double BaseRadius
        {
            get => _baseRadius;
            set { _baseRadius = value; UpdateGeometry(); }
        }

        public double TopRadius
        {
            get => _topRadius;
            set { _topRadius = value; UpdateGeometry(); }
        }

        public override Point3D CenterPosition
        {
            get
            {
                var localCenter = new Point3D(0, 0, _length / 2);
                return Visual3D.Transform?.Transform(localCenter) ?? localCenter;
            }
        }

        // 构造函数新增 visualType 传入变量
        public AxonVisual(Point3D start, Point3D end, double radius, Color color, string visualType = "Axon") : base()
        {
            VisualType = visualType;
            var direction = end - start;
            _length = direction.Length > 0 ? direction.Length : 1.0;
            _baseRadius = radius;
            _topRadius = radius;
            
            SetColor(color);
            UpdateGeometry();
            
            var alignNormal = direction.Length > 0 ? direction : new Vector3D(0, 0, 1);
            AlignTo(start, alignNormal);
        }

        public override void AlignTo(Point3D position, Vector3D normal)
        {
            normal.Normalize();
            var localZ = new Vector3D(0, 0, 1);

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
                matrix.Rotate(new Quaternion(new Vector3D(1, 0, 0), 180));
            }

            matrix.Translate(new Vector3D(position.X, position.Y, position.Z));
            Visual3D.Transform = new MatrixTransform3D(matrix);
        }

        public override string GetDimensionInfo()
        {
            return $"L: {_length:F2}, BaseR: {_baseRadius:F2}, TopR: {_topRadius:F2}";
        }

        protected override void UpdateGeometry()
        {
            var mesh = new MeshGeometry3D();
            bool hasNormals = true;
            int segments = 18;

            for (int i = 0; i < segments; i++)
            {
                double angle = 2 * Math.PI * i / segments;
                double cos = Math.Cos(angle);
                double sin = Math.Sin(angle);

                mesh.Positions.Add(new Point3D(_baseRadius * cos, _baseRadius * sin, 0));
                mesh.Positions.Add(new Point3D(_topRadius * cos, _topRadius * sin, _length));

                double dz = _baseRadius - _topRadius;
                double slopeLen = Math.Sqrt(dz * dz + _length * _length);
                double nz = slopeLen > 0 ? dz / slopeLen : 0;
                double nxy = slopeLen > 0 ? _length / slopeLen : 1;

                mesh.Normals.Add(new Vector3D(cos * nxy, sin * nxy, nz));
                mesh.Normals.Add(new Vector3D(cos * nxy, sin * nxy, nz));
            }

            for (int i = 0; i < segments; i++)
            {
                int next_i = (i + 1) % segments;
                int b0 = i * 2;
                int t0 = i * 2 + 1;
                int b1 = next_i * 2;
                int t1 = next_i * 2 + 1;

                mesh.TriangleIndices.Add(b0);
                mesh.TriangleIndices.Add(b1);
                mesh.TriangleIndices.Add(t0);

                mesh.TriangleIndices.Add(t0);
                mesh.TriangleIndices.Add(b1);
                mesh.TriangleIndices.Add(t1);
            }

            int startIndex = mesh.Positions.Count;
            
            mesh.Positions.Add(new Point3D(0, 0, 0));
            if (hasNormals) mesh.Normals.Add(new Vector3D(0, 0, -1));

            mesh.Positions.Add(new Point3D(0, 0, _length));
            if (hasNormals) mesh.Normals.Add(new Vector3D(0, 0, 1));

            int capVertsStart = startIndex + 2;
            for (int i = 0; i < segments; i++)
            {
                double angle = 2 * Math.PI * i / segments;
                double x = _baseRadius * Math.Cos(angle);
                double y = _baseRadius * Math.Sin(angle);
                mesh.Positions.Add(new Point3D(x, y, 0));
                if (hasNormals) mesh.Normals.Add(new Vector3D(0, 0, -1));
            }

            int capVertsEnd = mesh.Positions.Count;
            for (int i = 0; i < segments; i++)
            {
                double angle = 2 * Math.PI * i / segments;
                double x = _topRadius * Math.Cos(angle);
                double y = _topRadius * Math.Sin(angle);
                mesh.Positions.Add(new Point3D(x, y, _length));
                if (hasNormals) mesh.Normals.Add(new Vector3D(0, 0, 1));
            }

            int centerStartIdx = startIndex;
            int centerEndIdx = startIndex + 1;

            for (int i = 0; i < segments; i++)
            {
                int next = (i + 1) % segments;
                mesh.TriangleIndices.Add(centerStartIdx);
                mesh.TriangleIndices.Add(capVertsStart + next);
                mesh.TriangleIndices.Add(capVertsStart + i);

                mesh.TriangleIndices.Add(centerEndIdx);
                mesh.TriangleIndices.Add(capVertsEnd + i);
                mesh.TriangleIndices.Add(capVertsEnd + next);
            }
            
            // 修正上方一点语法小错，替换成正确变量名：
            for (int i = 0; i < segments; i++)
            {
                int next = (i + 1) % segments;
                // 底面 (Z=0)
                mesh.TriangleIndices.Add(centerStartIdx);
                mesh.TriangleIndices.Add(capVertsStart + next);
                mesh.TriangleIndices.Add(capVertsStart + i);

                // 顶面 (Z=Length)
                mesh.TriangleIndices.Add(centerEndIdx);
                mesh.TriangleIndices.Add(capVertsEnd + i);
                mesh.TriangleIndices.Add(capVertsEnd + next);
            }

            MainModel.Geometry = mesh;
            NotifyGeometryChanged();
        }

        public bool TryWorldPointToAnchor(Point3D worldPoint, out AnchorRef anchor)
        {
            anchor = new AnchorRef { Mode = AnchorMode.AxonCylinder, AxialT = 0.5, Angle = 0.0 };

            if (Visual3D.Transform == null) return false;
            var inv = Visual3D.Transform.Value;
            if (!inv.HasInverse) return false;
            inv.Invert();

            var local = inv.Transform(worldPoint);

            if (local.Z <= 1e-3)
            {
                anchor.Mode = AnchorMode.AxonCapStart;
                anchor.AxialT = 0.0;
                anchor.Angle = 0.0;
                return true;
            }
            if (local.Z >= _length - 1e-3)
            {
                anchor.Mode = AnchorMode.AxonCapEnd;
                anchor.AxialT = 1.0;
                anchor.Angle = 0.0;
                return true;
            }

            var t = _length <= 1e-9 ? 0.5 : local.Z / _length;
            t = Math.Clamp(t, 0.0, 1.0);

            double angle;
            double r2 = local.X * local.X + local.Y * local.Y;

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

            Point3D local;

            if (anchor.Mode == AnchorMode.AxonCapStart)
            {
                local = new Point3D(0, 0, 0);
            }
            else if (anchor.Mode == AnchorMode.AxonCapEnd)
            {
                local = new Point3D(0, 0, _length);
            }
            else
            {
                double t = Math.Clamp(anchor.AxialT, 0.0, 1.0);
                double z = t * _length;
                double r = _baseRadius + (_topRadius - _baseRadius) * t; 

                double x = r * Math.Cos(anchor.Angle);
                double y = r * Math.Sin(anchor.Angle);
                local = new Point3D(x, y, z);
            }

            worldPoint = Visual3D.Transform.Transform(local);
            return true;
        }
    }

    // ====== 简单的 Dend 类套壳 ======
    // 直接继承自 AxonVisual，在初始化时自动向父类注入 "Dend" 字符串
    public class DendVisual : AxonVisual
    {
        public DendVisual(Point3D start, Point3D end, double radius, Color color) 
            : base(start, end, radius, color, "Dend")
        {
        }
    }
}