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
        private double _lastAnchorAngle = 0.0;

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
            var builder = new MeshBuilder();
            // 先构建圆柱侧面
            builder.AddCylinder(new Point3D(0, 0, 0), new Point3D(0, 0, _length), _radius * 2, 18);
            
            // 获取生成的网格结构并进行盖板面的硬拓扑追加
            var mesh = builder.ToMesh();
            bool hasNormals = mesh.Normals != null && mesh.Normals.Count > 0;

            int startIndex = mesh.Positions.Count;
            
            // 压入顶/底面中心点
            mesh.Positions.Add(new Point3D(0, 0, 0));
            if (hasNormals) mesh.Normals.Add(new Vector3D(0, 0, -1));

            mesh.Positions.Add(new Point3D(0, 0, _length));
            if (hasNormals) mesh.Normals.Add(new Vector3D(0, 0, 1));

            // 压入顶/底面边缘点
            int capVertsStart = startIndex + 2;
            for (int i = 0; i < 18; i++)
            {
                double angle = 2 * Math.PI * i / 18;
                double x = _radius * Math.Cos(angle);
                double y = _radius * Math.Sin(angle);
                mesh.Positions.Add(new Point3D(x, y, 0));
                if (hasNormals) mesh.Normals.Add(new Vector3D(0, 0, -1));
            }

            int capVertsEnd = mesh.Positions.Count;
            for (int i = 0; i < 18; i++)
            {
                double angle = 2 * Math.PI * i / 18;
                double x = _radius * Math.Cos(angle);
                double y = _radius * Math.Sin(angle);
                mesh.Positions.Add(new Point3D(x, y, _length));
                if (hasNormals) mesh.Normals.Add(new Vector3D(0, 0, 1));
            }

            // 压入三角面索引，基于顶点序列进行缠绕
            int centerStartIdx = startIndex;
            int centerEndIdx = startIndex + 1;

            for (int i = 0; i < 18; i++)
            {
                int next = (i + 1) % 18;
                // 底面 (Z=0)：朝向 -Z
                mesh.TriangleIndices.Add(centerStartIdx);
                mesh.TriangleIndices.Add(capVertsStart + next);
                mesh.TriangleIndices.Add(capVertsStart + i);

                // 顶面 (Z=Length)：朝向 +Z
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

            // 接触域阈值判定：一旦击中点逼近 Z=0 或 Z=Length 阈值内，直接定义为盖板模式并强制吸附到轴心
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

            // 余下按圆柱侧面状态集计算处理
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

            // 坐标还原：根据已存储的模式流分配正确的相对坐标
            if (anchor.Mode == AnchorMode.AxonCapStart)
            {
                local = new Point3D(0, 0, 0); // 固定返回底部中心
            }
            else if (anchor.Mode == AnchorMode.AxonCapEnd)
            {
                local = new Point3D(0, 0, _length); // 固定返回顶部中心
            }
            else
            {
                // 圆柱侧面状态下恢复柱坐标
                double r = GetMeshRadiusFallbackToField();
                double z = Math.Clamp(anchor.AxialT, 0.0, 1.0) * _length;
                double x = r * Math.Cos(anchor.Angle);
                double y = r * Math.Sin(anchor.Angle);
                local = new Point3D(x, y, z);
            }

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