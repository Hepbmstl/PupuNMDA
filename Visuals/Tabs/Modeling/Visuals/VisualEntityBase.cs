/*
 * Copyright 2026 [Hepbmstl Hepupu]
 *
 * Pupu NMDA / NeuronCAD
 * A Multi-Compartment Neuron Physiological Simulation and Dynamics Analysis Platform
 *
 * Licensed under the Apache License, Version 2.0 (the "License");
 * you may not use this file except in compliance with the License.
 * You may obtain a copy of the License at
 *
 * http://www.apache.org/licenses/LICENSE-2.0
 *
 * Unless required by applicable law or agreed to in writing, software
 * distributed under the License is distributed on an "AS IS" BASIS,
 * WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
 * See the License for the specific language governing permissions and
 * limitations under the License.
 */

using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows.Media;
using System.Windows.Media.Media3D;
using HelixToolkit.Wpf;

namespace NeuronCAD.Visuals.Tabs.Modeling.Visuals
{
    /// <summary>
    /// Abstract base class for visual entities, implementing common IVisualEntity logic.
    /// Holds a HelixToolkit 3D model (GeometryModel3D), material management, wireframe mode toggle,
    /// and visualization for ion channel surface point clouds.
    /// Derived classes: SomaVisual (frustum, inherits AxonVisual), AxonVisual (frustum/cylinder), DendVisual (wrapper around AxonVisual).
    /// Callers: InteractionController (placing/selecting/moving/display mode toggles),
    /// PropertiesPanelController (property editing and channel management), MainWindow (edit dialogs).
    /// </summary>
    public abstract class VisualEntityBase : IVisualEntity
    {
        /// <summary>Unique identifier (GUID) for the entity, generated in the constructor. Used for panel node indices and connection dictionary keys.</summary>
        public string Id { get; private set; }

        /// <summary>Root ModelVisual3D for HelixToolkit 3D visuals, containing the main mesh model and child point/wireframe visuals.</summary>
        public ModelVisual3D Visual3D { get; private set; }

        /// <summary>Whether the entity is selected. Managed by SetSelected method.</summary>
        public bool IsSelected { get; private set; }

        /// <summary>Whether the entity participates in hit testing. Disabled by InteractionController during placing/moving to avoid self-intersection.</summary>
        public bool IsHitTestVisible { get; private set; } = true;

        /// <summary>The entity's center position in world coordinates (abstract; computed by derived classes from the transform).</summary>
        public abstract Point3D CenterPosition { get; }

        /// <summary>Main geometry model holding MeshGeometry3D and materials. Updated by derived classes' UpdateGeometry method.</summary>
        protected GeometryModel3D MainModel;

        /// <summary>Default material (used when not selected); color set by SetColor.</summary>
        protected Material _defaultMaterial;

        /// <summary>Material used when selected (orange highlight); applied when SetSelected(true) is called.</summary>
        protected Material _selectedMaterial;

        /// <summary>Current entity color, updated by SetColor and exposed to panels via CurrentColor property.</summary>
        protected Color _current_color = Colors.Gray;

        /// <summary>Wireframe visual used in Wireframe mode. Edges are extracted from mesh triangles when in wireframe.</summary>
        private LinesVisual3D? _wireframe;

        /// <summary>Current display mode (Normal/Wireframe). Managed by SetDisplayMode.</summary>
        private VisualDisplayMode _displayMode = VisualDisplayMode.Normal;

        /// <summary>Public read-only property for current color, used by PropertiesPanelController.</summary>
        public Color CurrentColor => _current_color;

        /// <summary>
        /// Dictionary of ion channels bound to this entity.
        /// Channels are added/removed by the channel selector dialog in PropertiesPanelController.
        /// </summary>
        public Dictionary<string, ChannelProperty> Channels { get; set; } = new Dictionary<string, ChannelProperty>();

        /// <summary>Dictionary of channel visualization layers. Key: channel name. Value: ModelVisual3D (merged mesh). Managed by UpdateChannelVisuals.</summary>
        private Dictionary<string, ModelVisual3D> _channelVisuals = new Dictionary<string, ModelVisual3D>();

        /// <summary>Shared random generator used for Monte Carlo sampling in UpdateChannelVisuals.</summary>
        private static readonly Random Rnd = new Random();

        /// <summary>Membrane capacitance (µF/cm²), default 1.0.</summary>
        public double Cm { get; set; } = 1.0;

        /// <summary>Axial resistivity (Ω·cm), default 100.0.</summary>
        public double Ra { get; set; } = 100.0;

        /// <summary>Number of compartments this entity is split into after simulation. 0 when not simulated.</summary>
        public int CompartmentCount { get; set; } = 0;

        /// <summary>List of global compartment IDs owned by this entity after simulation. Empty when not simulated.</summary>
        public List<int> CompartmentIds { get; set; } = new List<int>();

        /// <summary>
        /// Base class constructor. Initializes GUID, Visual3D container, main geometry model and default materials.
        /// Called by derived class constructors via base().
        /// </summary>
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

            // Initialize transform to identity to ensure CombinedManipulator.Bind has a valid target
            Visual3D.Transform = new System.Windows.Media.Media3D.MatrixTransform3D(Matrix3D.Identity);
        }

        /// <summary>
        /// Set selection state for the entity, toggling materials to selected highlight or default.
        /// Also updates wireframe appearance.
        /// Called by InteractionController.ForceSelect and actions like StartPlacing/ConfirmAction.
        /// </summary>
        public void SetSelected(bool isSelected)
        {
            IsSelected = isSelected;

            if (_displayMode == VisualDisplayMode.Normal)
            {
                MainModel.Material = isSelected ? _selectedMaterial : _defaultMaterial;
                MainModel.BackMaterial = isSelected ? _selectedMaterial : _defaultMaterial;
            }

            UpdateWireframeAppearance();
        }

        /// <summary>
        /// Set the entity color and update the default material.
        /// Called from PropertiesPanelController color edit textbox LostFocus callback.
        /// </summary>
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

        /// <summary>
        /// Set entity opacity (0.0~1.0) by adjusting alpha channel and rebuilding materials.
        /// Reserved for semi-transparent visualization effects.
        /// </summary>
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

        /// <summary>
        /// Set whether the entity participates in hit testing.
        /// Disabled during placing/moving to avoid self-selection.
        /// Called by InteractionController.StartPlacing/ConfirmAction/ShowGimbal/HideGimbal.
        /// </summary>
        public void SetHitTestVisible(bool isVisible)
        {
            IsHitTestVisible = isVisible;
        }

        /// <summary>
        /// Toggle display mode. Normal shows materials and point clouds; Wireframe shows edges and hides materials/points.
        /// Called by InteractionController.ShowGimbal (switch to Wireframe) and HideGimbal (switch to Normal).
        /// </summary>
        public void SetDisplayMode(VisualDisplayMode mode)
        {
            if (_displayMode == mode) return;
            _displayMode = mode;

            if (_displayMode == VisualDisplayMode.Normal)
            {
                // Restore normal materials
                MainModel.Material = IsSelected ? _selectedMaterial : _defaultMaterial;
                MainModel.BackMaterial = IsSelected ? _selectedMaterial : _defaultMaterial;

                // Remove wireframe
                if (_wireframe != null)
                {
                    Visual3D.Children.Remove(_wireframe);
                }

                // Restore channel point visuals
                foreach (var visual in _channelVisuals.Values)
                {
                    if (!Visual3D.Children.Contains(visual))
                        Visual3D.Children.Add(visual);
                }
            }
            else // Wireframe mode
            {
                // Clear materials to make the model transparent
                MainModel.Material = null;
                MainModel.BackMaterial = null;

                // Build and show wireframe
                EnsureWireframe();
                RebuildWireframeFromCurrentMesh();

                if (_wireframe != null && !Visual3D.Children.Contains(_wireframe))
                {
                    Visual3D.Children.Add(_wireframe);
                }

                // Hide channel point visuals to keep wireframe unobstructed
                foreach (var visual in _channelVisuals.Values)
                {
                    Visual3D.Children.Remove(visual);
                }
            }
        }

        /// <summary>
        /// Refresh ion channel surface visualization.
        /// Use low-density 3D spheres instead of screen-space points; spheres are merged into a single mesh to reduce draw calls.
        /// Spheres have a fixed 3D size and do not change with camera distance.
        /// Each channel has up to MaxChannelParticles particles; density scales with conductance.
        /// </summary>
        public void UpdateChannelVisuals()
        {
            const int MaxChannelParticles = 80;
            const double DensityFactor = 0.03;
            const double ParticleRadius = 0.15;
            const int ParticleSeg = 4;

            // 1. Clear current layers
            foreach (var vis in _channelVisuals.Values)
            {
                Visual3D.Children.Remove(vis);
            }
            _channelVisuals.Clear();

            // 2. Guard against empty data
            if (MainModel.Geometry is not MeshGeometry3D mesh ||
                mesh.Positions == null || mesh.TriangleIndices == null || mesh.Positions.Count == 0)
                return;

            var positions = mesh.Positions;
            var indices = mesh.TriangleIndices;
            int triangleCount = indices.Count / 3;

            // 3. Precompute cumulative triangle areas (area-weighted sampling)
            double[] cumulativeAreas = new double[triangleCount];
            double totalArea = 0;

            for (int i = 0; i < triangleCount; i++)
            {
                Point3D p0 = positions[indices[i * 3]];
                Point3D p1 = positions[indices[i * 3 + 1]];
                Point3D p2 = positions[indices[i * 3 + 2]];

                Vector3D v1 = p1 - p0;
                Vector3D v2 = p2 - p0;
                double area = Vector3D.CrossProduct(v1, v2).Length * 0.5;

                totalArea += area;
                cumulativeAreas[i] = totalArea;
            }

            if (totalArea <= 0) return;

            // 4. Generate fixed-size particle mesh for each channel
            foreach (var kvp in Channels)
            {
                var channel = kvp.Value;
                int pointCount = Math.Clamp(
                    (int)(totalArea * channel.G_ion_channel * DensityFactor),
                    3, MaxChannelParticles);

                var builder = new MeshBuilder();

                for (int p = 0; p < pointCount; p++)
                {
                    double randomArea = Rnd.NextDouble() * totalArea;
                    int triIndex = Array.BinarySearch(cumulativeAreas, randomArea);
                    if (triIndex < 0) triIndex = ~triIndex;
                    if (triIndex >= triangleCount) triIndex = triangleCount - 1;

                    Point3D p0 = positions[indices[triIndex * 3]];
                    Point3D p1 = positions[indices[triIndex * 3 + 1]];
                    Point3D p2 = positions[indices[triIndex * 3 + 2]];

                    double r1 = Rnd.NextDouble();
                    double r2 = Rnd.NextDouble();
                    double sqrtR1 = Math.Sqrt(r1);

                    double u = 1 - sqrtR1;
                    double v = sqrtR1 * (1 - r2);
                    double w = sqrtR1 * r2;

                    double px = u * p0.X + v * p1.X + w * p2.X;
                    double py = u * p0.Y + v * p1.Y + w * p2.Y;
                    double pz = u * p0.Z + v * p1.Z + w * p2.Z;

                    // Offset along normal to avoid Z-fighting
                    Vector3D normal = Vector3D.CrossProduct(p1 - p0, p2 - p0);
                    if (normal.LengthSquared > 1e-10)
                    {
                        normal.Normalize();
                        px += normal.X * ParticleRadius;
                        py += normal.Y * ParticleRadius;
                        pz += normal.Z * ParticleRadius;
                    }

                    builder.AddSphere(new Point3D(px, py, pz), ParticleRadius, ParticleSeg, ParticleSeg);
                }

                var meshGeo = builder.ToMesh();
                if (meshGeo == null) continue;

                var material = MaterialHelper.CreateMaterial(channel.Color);
                var model = new GeometryModel3D(meshGeo, material) { BackMaterial = material };
                var visual = new ModelVisual3D { Content = model };

                _channelVisuals[kvp.Key] = visual;

                if (_displayMode == VisualDisplayMode.Normal)
                {
                    Visual3D.Children.Add(visual);
                }
            }
        }

        /// <summary>
        /// Lazy-initialize the wireframe object. Called on first switch to Wireframe mode.
        /// </summary>
        private void EnsureWireframe()
        {
            if (_wireframe != null) return;

            _wireframe = new LinesVisual3D
            {
                Thickness = 1.0
            };

            UpdateWireframeAppearance();
        }

        /// <summary>
        /// Update wireframe color: orange when selected, otherwise use the entity's current color.
        /// Called by SetSelected, SetColor, SetOpacity.
        /// </summary>
        private void UpdateWireframeAppearance()
        {
            if (_wireframe == null) return;
            var baseColor = IsSelected ? Colors.Orange : _current_color;
            _wireframe.Color = Color.FromArgb(255, baseColor.R, baseColor.G, baseColor.B);
        }

        /// <summary>
        /// Notify that geometry has changed. Call after derived classes rebuild meshes due to radius/length changes.
        /// Rebuilds wireframe in Wireframe mode and forces a refresh of channel point clouds.
        /// Typically called at the end of derived UpdateGeometry implementations.
        /// </summary>
        protected void NotifyGeometryChanged()
        {
            if (_displayMode == VisualDisplayMode.Wireframe)
            {
                EnsureWireframe();
                RebuildWireframeFromCurrentMesh();
            }

            // When dimensions (radius, length) change and mesh is rebuilt, force sync of point cloud data
            UpdateChannelVisuals();
        }

        /// <summary>
        /// Extract all unique edges from the current MeshGeometry3D triangle data and rebuild the wireframe vertex collection.
        /// Called by NotifyGeometryChanged and SetDisplayMode when in Wireframe mode.
        /// </summary>
        private void RebuildWireframeFromCurrentMesh()
        {
            if (_wireframe == null) return;
            if (MainModel.Geometry is not MeshGeometry3D mesh) return;
            if (mesh.Positions == null || mesh.TriangleIndices == null) return;

            var positions = mesh.Positions;
            var indices = mesh.TriangleIndices;

            // Use a HashSet to deduplicate edges (undirected edges: always store smaller index first)
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

            // Convert deduplicated edges to segment vertex pairs
            var pts = new Point3DCollection(edges.Count * 2);
            foreach (var (a, b) in edges)
            {
                pts.Add(positions[a]);
                pts.Add(positions[b]);
            }

            _wireframe.Points = pts;
        }

        // ====== Abstract contract for derived classes ======

        /// <summary>Get a string describing the entity's dimensions (implemented by derived classes).</summary>
        public abstract string GetDimensionInfo();

        /// <summary>Align the entity to the specified world position and normal (implemented by derived classes based on geometry).</summary>
        public abstract void AlignTo(Point3D position, Vector3D normal);

        /// <summary>Update the geometry mesh (called by derived classes when parameters change; should call NotifyGeometryChanged at the end).</summary>
        protected abstract void UpdateGeometry();
    }

    /// <summary>
    /// Anchor mode enum describing how an anchor is positioned on the entity surface.
    /// Used by AnchorRef.Mode; chosen by AxonVisual.TryWorldPointToAnchor and
    /// AttachedDeviceBase.CalculateWorldNormal based on hit location.
    /// </summary>
    public enum AnchorMode
    {
        /// <summary>Anchor located on the cylindrical/frustum side (Axon/Dend), positioned using AxialT and Angle.</summary>
        AxonCylinder,
        /// <summary>Anchor on Soma frustum surface (reserved; current SomaVisual uses AxonCylinder).</summary>
        SomaCylinder,
        /// <summary>Anchor uniformly distributed on Soma surface (deprecated; Soma now uses frustum logic).</summary>
        SomaUniform,
        /// <summary>Anchor on the start cap of Axon/Dend (Z=0).</summary>
        AxonCapStart,
        /// <summary>Anchor on the end cap of Axon/Dend (Z=Length).</summary>
        AxonCapEnd
    }

    /// <summary>
    /// Anchor reference data class describing a precise location on an entity surface.
    /// Uniquely determined by the tuple (Mode, AxialT, Angle).
    /// Held by Connection endpoints and IAttachedDevice attachment points.
    /// </summary>
    public sealed class AnchorRef
    {
        /// <summary>Anchor positioning mode (side/cap/sphere etc.).</summary>
        public AnchorMode Mode { get; set; }

        /// <summary>
        /// Axial parameter (0.0~1.0), where 0.0 indicates the base (Z=0) and 1.0 the tip (Z=Length).
        /// Not meaningful for Soma types.
        /// </summary>
        public double AxialT { get; set; }

        /// <summary>
        /// Circumferential angle (radians) indicating rotation around the cross-section.
        /// Not meaningful for cap anchors and Soma types.
        /// </summary>
        public double Angle { get; set; }
    }

    /// <summary>
    /// Connection data class between two entities, holding references to endpoints and anchor information.
    /// Created by InteractionController.ConfirmAction or the "Connect" operation in the context menu.
    /// Managed by ConnectionController for lifecycle and visualization updates.
    /// </summary>
    public class Connection
    {
        /// <summary>Unique identifier (GUID) for the connection, used as dictionary key in ConnectionController.</summary>
        public string Id { get; } = Guid.NewGuid().ToString();

        /// <summary>Reference to the entity at endpoint A.</summary>
        public IVisualEntity A { get; }

        /// <summary>Reference to the entity at endpoint B.</summary>
        public IVisualEntity B { get; }

        /// <summary>Anchor position for endpoint A on the entity surface. Can be dragged to modify.</summary>
        public AnchorRef AnchorA { get; set; }

        /// <summary>Anchor position for endpoint B on the entity surface. Can be dragged to modify.</summary>
        public AnchorRef AnchorB { get; set; }

        /// <summary>Connection weight, reserved for parameters like synaptic strength in simulation calculations.</summary>
        public double Weight { get; set; } = 1.0;

        /// <summary>
        /// Constructor creating a Connection instance between two entities.
        /// Called by InteractionController.ConfirmAction and the context menu Connect operation.
        /// </summary>
        public Connection(IVisualEntity a, IVisualEntity b, AnchorRef anchorA, AnchorRef anchorB, double weight = 1.0)
        {
            A = a; B = b;
            AnchorA = anchorA; AnchorB = anchorB;
            Weight = weight;
        }
    }
}