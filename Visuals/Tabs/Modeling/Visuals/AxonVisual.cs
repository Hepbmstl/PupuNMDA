using System.Windows.Media;
using System.Windows.Media.Media3D;
using HelixToolkit.Wpf;
using System;

namespace NeuronCAD.Visuals.Tabs.Modeling.Visuals
{
    /// <summary>
    /// 轴突 (Axon) 可视化实体，以圆台/圆柱形式渲染。
    /// 继承 VisualEntityBase 并实现 IAnchoredEntity 接口。
    /// 局部坐标系：底面在 Z=0，顶面在 Z=Length，轴向沿局部 Z 轴。
    /// 由 MainWindow.OnAddAxonClick 创建，也被 DendVisual 子类复用。
    /// </summary>
    public class AxonVisual : VisualEntityBase, IAnchoredEntity
    {
        /// <summary>
        /// 可视化类型标识字符串（"Axon" 或 "Dend"），用于面板中区分显示名称。
        /// 在构造时注入，被 PropertiesPanelController.BuildEntityNode 读取。
        /// </summary>
        public string VisualType { get; private set; }

        /// <summary>圆台长度（局部 Z 轴方向），修改时自动重建网格。</summary>
        private double _length;

        /// <summary>底面半径，修改时自动重建网格。</summary>
        private double _baseRadius;

        /// <summary>顶面半径，修改时自动重建网格。当与 _baseRadius 相同时为圆柱。</summary>
        private double _topRadius;

        /// <summary>上一次有效的锚点角度缓存。当命中点过于靠近轴心时使用此缓存值避免角度突变。</summary>
        private double _lastAnchorAngle = 0.0;

        /// <summary>
        /// 圆台长度属性。设置时自动调用 UpdateGeometry 重建网格。
        /// 被 MainWindow.OnApplyEdit 和 PropertiesPanelController 面板修改。
        /// </summary>
        public double Length
        {
            get => _length;
            set { _length = value; UpdateGeometry(); }
        }

        /// <summary>
        /// 底面半径属性。设置时自动调用 UpdateGeometry 重建网格。
        /// 被 MainWindow.OnApplyEdit 和 PropertiesPanelController 面板修改。
        /// </summary>
        public double BaseRadius
        {
            get => _baseRadius;
            set { _baseRadius = value; UpdateGeometry(); }
        }

        /// <summary>
        /// 顶面半径属性。设置时自动调用 UpdateGeometry 重建网格。
        /// 被 MainWindow.OnApplyEdit 和 PropertiesPanelController 面板修改。
        /// </summary>
        public double TopRadius
        {
            get => _topRadius;
            set { _topRadius = value; UpdateGeometry(); }
        }

        /// <summary>
        /// 世界坐标系中的中心位置，为局部中心点 (0, 0, Length/2) 经变换矩阵映射后的结果。
        /// 被 Connection 端点回退和设备法线计算引用。
        /// </summary>
        public override Point3D CenterPosition
        {
            get
            {
                var localCenter = new Point3D(0, 0, _length / 2);
                return Visual3D.Transform?.Transform(localCenter) ?? localCenter;
            }
        }

        /// <summary>
        /// 构造函数，根据起止点、半径和颜色创建圆台/圆柱可视化实体。
        /// 长度由 start→end 距离决定，方向由 start→end 向量决定。
        /// </summary>
        /// <param name="start">圆台底面世界坐标</param>
        /// <param name="end">圆台顶面世界坐标</param>
        /// <param name="radius">初始半径（底面和顶面相同）</param>
        /// <param name="color">实体颜色</param>
        /// <param name="visualType">可视化类型标识，默认为 "Axon"，DendVisual 传入 "Dend"</param>
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

        /// <summary>
        /// 将圆台对齐到指定位置和方向。通过四元数旋转使局部 Z 轴对齐到 normal 方向，
        /// 然后平移到 position 位置。
        /// 被 InteractionController.UpdateObjectPosition 在放置/移动时调用。
        /// </summary>
        public override void AlignTo(Point3D position, Vector3D normal)
        {
            normal.Normalize();
            var localZ = new Vector3D(0, 0, 1);

            var matrix = Matrix3D.Identity;
            // 计算从局部 Z 轴到目标法线的旋转
            var axis = Vector3D.CrossProduct(localZ, normal);
            if (axis.LengthSquared > 1e-10)
            {
                axis.Normalize();
                double angle = Vector3D.AngleBetween(localZ, normal);
                matrix.Rotate(new Quaternion(axis, angle));
            }
            else if (Vector3D.DotProduct(localZ, normal) < 0)
            {
                // 局部 Z 与目标法线反向时，绕 X 轴旋转 180°
                matrix.Rotate(new Quaternion(new Vector3D(1, 0, 0), 180));
            }

            matrix.Translate(new Vector3D(position.X, position.Y, position.Z));
            Visual3D.Transform = new MatrixTransform3D(matrix);
        }

        /// <summary>返回圆台尺寸信息字符串。预留用于状态栏或提示信息。</summary>
        public override string GetDimensionInfo()
        {
            return $"L: {_length:F2}, BaseR: {_baseRadius:F2}, TopR: {_topRadius:F2}";
        }

        /// <summary>
        /// 重建圆台/圆柱三维网格，包含侧面和两个端盖。
        /// 局部坐标系：底面在 Z=0，顶面在 Z=_length。
        /// 在 Length/BaseRadius/TopRadius 属性 setter 和构造函数中调用。
        /// </summary>
        protected override void UpdateGeometry()
        {
            var mesh = new MeshGeometry3D();
            bool hasNormals = true;
            int segments = 18;

            // ---- 侧面顶点和法线 ----
            for (int i = 0; i < segments; i++)
            {
                double angle = 2 * Math.PI * i / segments;
                double cos = Math.Cos(angle);
                double sin = Math.Sin(angle);

                // 底面顶点和顶面顶点交替排列
                mesh.Positions.Add(new Point3D(_baseRadius * cos, _baseRadius * sin, 0));
                mesh.Positions.Add(new Point3D(_topRadius * cos, _topRadius * sin, _length));

                // 计算圆台侧面法线（考虑半径变化导致的倾斜）
                double dz = _baseRadius - _topRadius;
                double slopeLen = Math.Sqrt(dz * dz + _length * _length);
                double nz = slopeLen > 0 ? dz / slopeLen : 0;
                double nxy = slopeLen > 0 ? _length / slopeLen : 1;

                mesh.Normals.Add(new Vector3D(cos * nxy, sin * nxy, nz));
                mesh.Normals.Add(new Vector3D(cos * nxy, sin * nxy, nz));
            }

            // ---- 侧面三角面索引 ----
            for (int i = 0; i < segments; i++)
            {
                int next_i = (i + 1) % segments;
                int b0 = i * 2;       // 当前底面顶点
                int t0 = i * 2 + 1;   // 当前顶面顶点
                int b1 = next_i * 2;   // 下一底面顶点
                int t1 = next_i * 2 + 1; // 下一顶面顶点

                mesh.TriangleIndices.Add(b0);
                mesh.TriangleIndices.Add(b1);
                mesh.TriangleIndices.Add(t0);

                mesh.TriangleIndices.Add(t0);
                mesh.TriangleIndices.Add(b1);
                mesh.TriangleIndices.Add(t1);
            }

            // ---- 端盖中心点和边缘顶点 ----
            int startIndex = mesh.Positions.Count;

            // 底面中心点
            mesh.Positions.Add(new Point3D(0, 0, 0));
            if (hasNormals) mesh.Normals.Add(new Vector3D(0, 0, -1));

            // 顶面中心点
            mesh.Positions.Add(new Point3D(0, 0, _length));
            if (hasNormals) mesh.Normals.Add(new Vector3D(0, 0, 1));

            // 底面端盖边缘顶点
            int capVertsStart = startIndex + 2;
            for (int i = 0; i < segments; i++)
            {
                double angle = 2 * Math.PI * i / segments;
                double x = _baseRadius * Math.Cos(angle);
                double y = _baseRadius * Math.Sin(angle);
                mesh.Positions.Add(new Point3D(x, y, 0));
                if (hasNormals) mesh.Normals.Add(new Vector3D(0, 0, -1));
            }

            // 顶面端盖边缘顶点
            int capVertsEnd = mesh.Positions.Count;
            for (int i = 0; i < segments; i++)
            {
                double angle = 2 * Math.PI * i / segments;
                double x = _topRadius * Math.Cos(angle);
                double y = _topRadius * Math.Sin(angle);
                mesh.Positions.Add(new Point3D(x, y, _length));
                if (hasNormals) mesh.Normals.Add(new Vector3D(0, 0, 1));
            }

            int centerStartIdx = startIndex;      // 底面中心索引
            int centerEndIdx = startIndex + 1;     // 顶面中心索引

            // ---- 端盖三角面索引 ----
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

            // 重复端盖面片（双面渲染保证）
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

        /// <summary>
        /// 将世界坐标点转换为圆台表面锚点引用。
        /// 通过逆变换矩阵将世界坐标转为局部坐标，然后根据 Z 值判断端盖或侧面，
        /// 侧面锚点用 (AxialT, Angle) 参数化表示。
        /// 被 InteractionController.ConfirmAction（创建连接）和 SimulationInteractionController 调用。
        /// </summary>
        public bool TryWorldPointToAnchor(Point3D worldPoint, out AnchorRef anchor)
        {
            anchor = new AnchorRef { Mode = AnchorMode.AxonCylinder, AxialT = 0.5, Angle = 0.0 };

            if (Visual3D.Transform == null) return false;
            var inv = Visual3D.Transform.Value;
            if (!inv.HasInverse) return false;
            inv.Invert();

            var local = inv.Transform(worldPoint);

            // 底端盖判定
            if (local.Z <= 1e-3)
            {
                anchor.Mode = AnchorMode.AxonCapStart;
                anchor.AxialT = 0.0;
                anchor.Angle = 0.0;
                return true;
            }
            // 顶端盖判定
            if (local.Z >= _length - 1e-3)
            {
                anchor.Mode = AnchorMode.AxonCapEnd;
                anchor.AxialT = 1.0;
                anchor.Angle = 0.0;
                return true;
            }

            // 侧面：计算轴向比例和周向角度
            var t = _length <= 1e-9 ? 0.5 : local.Z / _length;
            t = Math.Clamp(t, 0.0, 1.0);

            double angle;
            double r2 = local.X * local.X + local.Y * local.Y;

            if (r2 < 1e-6)
            {
                // 命中点过于靠近轴心，使用缓存角度避免突变
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

        /// <summary>
        /// 将锚点引用转换回世界坐标。根据锚点模式在局部坐标系中计算点位，
        /// 然后通过变换矩阵映射到世界坐标系。
        /// 被 ConnectionController.Update（刷新连接线）和 AttachedDeviceBase.UpdatePosition 调用。
        /// </summary>
        public bool TryAnchorToWorldPoint(AnchorRef anchor, out Point3D worldPoint)
        {
            worldPoint = new Point3D();
            if (Visual3D.Transform == null) return false;

            Point3D local;

            if (anchor.Mode == AnchorMode.AxonCapStart)
            {
                // 底面中心
                local = new Point3D(0, 0, 0);
            }
            else if (anchor.Mode == AnchorMode.AxonCapEnd)
            {
                // 顶面中心
                local = new Point3D(0, 0, _length);
            }
            else
            {
                // 侧面：使用 AxialT 和 Angle 参数化定位
                double t = Math.Clamp(anchor.AxialT, 0.0, 1.0);
                double z = t * _length;
                double r = _baseRadius + (_topRadius - _baseRadius) * t; // 沿轴向线性插值半径

                double x = r * Math.Cos(anchor.Angle);
                double y = r * Math.Sin(anchor.Angle);
                local = new Point3D(x, y, z);
            }

            worldPoint = Visual3D.Transform.Transform(local);
            return true;
        }
    }

    /// <summary>
    /// 树突 (Dend) 可视化实体，直接继承自 AxonVisual 并在构造时注入 "Dend" 类型标识。
    /// 几何形状和行为与 AxonVisual 完全一致，仅在面板中以不同名称显示。
    /// 由 MainWindow.OnAddDendClick 创建。
    /// </summary>
    public class DendVisual : AxonVisual
    {
        /// <summary>
        /// 构造函数，创建树突可视化实体。
        /// 自动将 VisualType 设为 "Dend"。
        /// </summary>
        public DendVisual(Point3D start, Point3D end, double radius, Color color)
            : base(start, end, radius, color, "Dend")
        {
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