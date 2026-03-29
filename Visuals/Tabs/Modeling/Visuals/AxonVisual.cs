using System.Windows.Media;
using System.Windows.Media.Media3D;
using HelixToolkit.Wpf;
using System;

namespace NeuronCAD.Visuals.Tabs.Modeling.Visuals
{
    /// <summary>
    /// Axon visual entity rendered as a frustum/cylinder.
    /// Inherits VisualEntityBase and implements IAnchoredEntity.
    /// Local coordinate system: base at Z=0, top at Z=Length, axial direction along local Z axis.
    /// Created by MainWindow.OnAddAxonClick and reused by DendVisual subclasses.
    /// </summary>
    public class AxonVisual : VisualEntityBase, IAnchoredEntity
    {
        /// <summary>
        /// Visual type identifier string ("Axon" or "Dend"), used to distinguish display names in the properties panel.
        /// Injected in the constructor and read by PropertiesPanelController.BuildEntityNode.
        /// </summary>
        public string VisualType { get; private set; }

        /// <summary>Frustum length (local Z axis). Changing this triggers a geometry rebuild.</summary>
        private double _length;
        /// <summary>Base radius. Changing this triggers a geometry rebuild.</summary>
        private double _baseRadius;
        /// <summary>Top radius. Changing this triggers a geometry rebuild. When equal to _baseRadius the shape is a cylinder.</summary>
        private double _topRadius;

        /// <summary>Cached last valid anchor angle. Used to avoid angle jump when hit points are too close to the axis.</summary>
        private double _lastAnchorAngle = 0.0;

        /// <summary>
        /// Length property for the frustum. Setting it automatically calls UpdateGeometry to rebuild the mesh.
        /// Modified by MainWindow.OnApplyEdit and the PropertiesPanelController panel.
        /// </summary>
        public double Length
        {
            get => _length;
            set { _length = value; UpdateGeometry(); }
        }

        /// <summary>
        /// Base radius property. Setting it automatically calls UpdateGeometry to rebuild the mesh.
        /// Modified by MainWindow.OnApplyEdit and the PropertiesPanelController panel.
        /// </summary>
        public double BaseRadius
        {
            get => _baseRadius;
            set { _baseRadius = value; UpdateGeometry(); }
        }

        /// <summary>
        /// Top radius property. Setting it automatically calls UpdateGeometry to rebuild the mesh.
        /// Modified by MainWindow.OnApplyEdit and the PropertiesPanelController panel.
        /// </summary>
        public double TopRadius
        {
            get => _topRadius;
            set { _topRadius = value; UpdateGeometry(); }
        }

        /// <summary>
        /// Center position in world coordinates, which is the local center point (0,0,Length/2) transformed by Visual3D.Transform.
        /// Referenced by Connection endpoint fallback and device normal calculations.
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
        /// Constructor: create a frustum/cylinder visual from start/end points, radius and color.
        /// Length is determined by the distance from start to end, and direction by the start->end vector.
        /// </summary>
        /// <param name="start">World coordinate of the frustum base</param>
        /// <param name="end">World coordinate of the frustum top</param>
        /// <param name="radius">Initial radius (both base and top)</param>
        /// <param name="color">Entity color</param>
        /// <param name="visualType">Visual type identifier, default "Axon"; DendVisual passes "Dend"</param>
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
        /// Align the frustum to a given position and direction. Rotate via quaternion so the local Z axis aligns with the normal,
        /// then translate to the specified position.
        /// Called by InteractionController.UpdateObjectPosition during placement/movement.
        /// </summary>
        public override void AlignTo(Point3D position, Vector3D normal)
        {
            normal.Normalize();
            var localZ = new Vector3D(0, 0, 1);

            var matrix = Matrix3D.Identity;
            // Compute rotation from local Z axis to the target normal
            var axis = Vector3D.CrossProduct(localZ, normal);
            if (axis.LengthSquared > 1e-10)
            {
                axis.Normalize();
                double angle = Vector3D.AngleBetween(localZ, normal);
                matrix.Rotate(new Quaternion(axis, angle));
            }
            else if (Vector3D.DotProduct(localZ, normal) < 0)
            {
                // If local Z and target normal are opposite, rotate 180° around the X axis
                matrix.Rotate(new Quaternion(new Vector3D(1, 0, 0), 180));
            }

            matrix.Translate(new Vector3D(position.X, position.Y, position.Z));
            Visual3D.Transform = new MatrixTransform3D(matrix);
        }

        /// <summary>Return a frustum dimension info string. Reserved for status bar or tooltips.</summary>
        public override string GetDimensionInfo()
        {
            return $"L: {_length:F2}, BaseR: {_baseRadius:F2}, TopR: {_topRadius:F2}";
        }

        /// <summary>
        /// Rebuild the frustum/cylinder 3D mesh including side surface and two end caps.
        /// Local coordinates: base at Z=0, top at Z=_length.
        /// Called from the Length/BaseRadius/TopRadius property setters and the constructor.
        /// </summary>
        protected override void UpdateGeometry()
        {
            var mesh = new MeshGeometry3D();
            bool hasNormals = true;
            int segments = 18;

            // ---- Side vertices and normals ----
            for (int i = 0; i < segments; i++)
            {
                double angle = 2 * Math.PI * i / segments;
                double cos = Math.Cos(angle);
                double sin = Math.Sin(angle);

                // Base and top vertices are interleaved
                mesh.Positions.Add(new Point3D(_baseRadius * cos, _baseRadius * sin, 0));
                mesh.Positions.Add(new Point3D(_topRadius * cos, _topRadius * sin, _length));

                // Compute side normals for the frustum (account for slope caused by radius change)
                double dz = _baseRadius - _topRadius;
                double slopeLen = Math.Sqrt(dz * dz + _length * _length);
                double nz = slopeLen > 0 ? dz / slopeLen : 0;
                double nxy = slopeLen > 0 ? _length / slopeLen : 1;

                mesh.Normals.Add(new Vector3D(cos * nxy, sin * nxy, nz));
                mesh.Normals.Add(new Vector3D(cos * nxy, sin * nxy, nz));
            }

            // ---- Side triangle indices ----
            for (int i = 0; i < segments; i++)
            {
                int next_i = (i + 1) % segments;
                int b0 = i * 2;       // current base vertex
                int t0 = i * 2 + 1;   // current top vertex
                int b1 = next_i * 2;   // next base vertex
                int t1 = next_i * 2 + 1; // next top vertex

                mesh.TriangleIndices.Add(b0);
                mesh.TriangleIndices.Add(b1);
                mesh.TriangleIndices.Add(t0);

                mesh.TriangleIndices.Add(t0);
                mesh.TriangleIndices.Add(b1);
                mesh.TriangleIndices.Add(t1);
            }

            // ---- End cap centers and rim vertices ----
            int startIndex = mesh.Positions.Count;

            // Bottom cap center point
            mesh.Positions.Add(new Point3D(0, 0, 0));
            if (hasNormals) mesh.Normals.Add(new Vector3D(0, 0, -1));

            // Top cap center point
            mesh.Positions.Add(new Point3D(0, 0, _length));
            if (hasNormals) mesh.Normals.Add(new Vector3D(0, 0, 1));

            // Bottom cap rim vertices
            int capVertsStart = startIndex + 2;
            for (int i = 0; i < segments; i++)
            {
                double angle = 2 * Math.PI * i / segments;
                double x = _baseRadius * Math.Cos(angle);
                double y = _baseRadius * Math.Sin(angle);
                mesh.Positions.Add(new Point3D(x, y, 0));
                if (hasNormals) mesh.Normals.Add(new Vector3D(0, 0, -1));
            }

            // Top cap rim vertices
            int capVertsEnd = mesh.Positions.Count;
            for (int i = 0; i < segments; i++)
            {
                double angle = 2 * Math.PI * i / segments;
                double x = _topRadius * Math.Cos(angle);
                double y = _topRadius * Math.Sin(angle);
                mesh.Positions.Add(new Point3D(x, y, _length));
                if (hasNormals) mesh.Normals.Add(new Vector3D(0, 0, 1));
            }

            int centerStartIdx = startIndex;      // bottom cap center index
            int centerEndIdx = startIndex + 1;     // top cap center index

            // ---- Cap triangle indices ----
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

            // Duplicate cap faces (ensure double-sided rendering)
            for (int i = 0; i < segments; i++)
            {
                int next = (i + 1) % segments;
                // Bottom cap (Z=0)
                mesh.TriangleIndices.Add(centerStartIdx);
                mesh.TriangleIndices.Add(capVertsStart + next);
                mesh.TriangleIndices.Add(capVertsStart + i);

                // Top cap (Z=Length)
                mesh.TriangleIndices.Add(centerEndIdx);
                mesh.TriangleIndices.Add(capVertsEnd + i);
                mesh.TriangleIndices.Add(capVertsEnd + next);
            }

            MainModel.Geometry = mesh;
            NotifyGeometryChanged();
        }

        /// <summary>
        /// Convert a world-space point to a frustum surface anchor reference.
        /// Transforms the world point into local space via the inverse transform, then determines cap vs side by the Z value.
        /// Side anchors are parameterized by (AxialT, Angle).
        /// Called by InteractionController.ConfirmAction (when creating connections) and SimulationInteractionController.
        /// </summary>
        public bool TryWorldPointToAnchor(Point3D worldPoint, out AnchorRef anchor)
        {
            anchor = new AnchorRef { Mode = AnchorMode.AxonCylinder, AxialT = 0.5, Angle = 0.0 };

            if (Visual3D.Transform == null) return false;
            var inv = Visual3D.Transform.Value;
            if (!inv.HasInverse) return false;
            inv.Invert();

            var local = inv.Transform(worldPoint);

            // Bottom cap test
            if (local.Z <= 1e-3)
            {
                anchor.Mode = AnchorMode.AxonCapStart;
                anchor.AxialT = 0.0;
                anchor.Angle = 0.0;
                return true;
            }
            // Top cap test
            if (local.Z >= _length - 1e-3)
            {
                anchor.Mode = AnchorMode.AxonCapEnd;
                anchor.AxialT = 1.0;
                anchor.Angle = 0.0;
                return true;
            }

            // Side: compute axial fraction and polar angle
            var t = _length <= 1e-9 ? 0.5 : local.Z / _length;
            t = Math.Clamp(t, 0.0, 1.0);

            double angle;
            double r2 = local.X * local.X + local.Y * local.Y;

            if (r2 < 1e-6)
            {
                // Hit point too close to the axis; use cached angle to avoid discontinuity
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
        /// Convert an anchor reference back to world coordinates. Computes the point in local space based on the anchor mode,
        /// then maps it to world coordinates via the transform matrix.
        /// Called by ConnectionController.Update (refresh connection visuals) and AttachedDeviceBase.UpdatePosition.
        /// </summary>
        public bool TryAnchorToWorldPoint(AnchorRef anchor, out Point3D worldPoint)
        {
            worldPoint = new Point3D();
            if (Visual3D.Transform == null) return false;

            Point3D local;

            if (anchor.Mode == AnchorMode.AxonCapStart)
            {
                // bottom cap center
                local = new Point3D(0, 0, 0);
            }
            else if (anchor.Mode == AnchorMode.AxonCapEnd)
            {
                // top cap center
                local = new Point3D(0, 0, _length);
            }
            else
            {
                // Side: use AxialT and Angle parameterization to locate the point
                double t = Math.Clamp(anchor.AxialT, 0.0, 1.0);
                double z = t * _length;
                double r = _baseRadius + (_topRadius - _baseRadius) * t; // linearly interpolate radius along axial direction

                double x = r * Math.Cos(anchor.Angle);
                double y = r * Math.Sin(anchor.Angle);
                local = new Point3D(x, y, z);
            }

            worldPoint = Visual3D.Transform.Transform(local);
            return true;
        }
    }

    /// <summary>
    /// Dend visual entity that directly inherits from AxonVisual and injects "Dend" as visual type.
    /// Geometry and behavior are identical to AxonVisual; displayed under a different name in the panel.
    /// Created by MainWindow.OnAddDendClick.
    /// </summary>
    public class DendVisual : AxonVisual
    {
        /// <summary>
        /// Constructor: create a dend visual entity.
        /// Automatically sets VisualType to "Dend".
        /// </summary>
        public DendVisual(Point3D start, Point3D end, double radius, Color color)
            : base(start, end, radius, color, "Dend")
        {
        }
    }
}