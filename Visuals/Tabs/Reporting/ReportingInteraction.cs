using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Media3D;
using HelixToolkit.Wpf;
using NeuronCAD.Visuals.Tabs.Shared;
using NeuronCAD.Visuals.Tabs.Modeling.Visuals;

namespace NeuronCAD.Visuals.Tabs.Reporting
{
    /// <summary>
    /// Compartment overlay data, holding a 3D visual model and metadata for a compartment.
    /// </summary>
    public class CompartmentOverlay
    {
        public int GlobalId { get; set; }
        public string ParentEntityId { get; set; } = string.Empty;
        public int Index { get; set; }
        public ModelVisual3D Visual3D { get; set; } = new ModelVisual3D();
        public GeometryModel3D GeometryModel { get; set; } = null!;
        public Material DefaultMaterial { get; set; } = null!;
        public Material HighlightMaterial { get; set; } = null!;
    }

    /// <summary>
    /// Interaction controller specific to Reporting mode.
    /// Responsibilities: disable general editing operations; support clicking entities to select and expand panel entries;
    /// show compartment overlays on demand when the mouse is near a selected entity; select compartments by clicking an overlay.
    /// </summary>
    public class ReportingInteractionController : IViewportInteractionHandler
    {
        private readonly SharedSceneState _scene;
        private readonly Action<Point, Point3D?> _updateCursorInfo;

        private readonly List<CompartmentOverlay> _overlays = new();
        private CompartmentOverlay? _hoveredOverlay;
        private bool _isActive;

        /// <summary>Externally (panel expansion) set the selected entity.</summary>
        private IVisualEntity? _selectedEntity;

        /// <summary>Whether overlays are currently visible.</summary>
        private bool _overlaysVisible;

        /// <summary>Mouse down position used to distinguish clicks from viewport dragging.</summary>
        private Point _mouseDownPos;

        /// <summary>Entity selected event; panel subscribes to expand the corresponding entry.</summary>
        public event Action<IVisualEntity?>? OnEntitySelected;

        /// <summary>Compartment selected event (globalId, parentEntityId); panel subscribes to update selection labels.</summary>
        public event Action<int, string>? OnCompartmentSelected;

        private static readonly Color[] CompartmentColors = new[]
        {
            Color.FromArgb(60, 0x00, 0xBF, 0xFF),
            Color.FromArgb(60, 0x00, 0xFF, 0x7F),
            Color.FromArgb(60, 0xFF, 0xA5, 0x00),
            Color.FromArgb(60, 0xDA, 0x70, 0xD6),
            Color.FromArgb(60, 0xFF, 0xFF, 0x00),
            Color.FromArgb(60, 0x00, 0xFF, 0xFF),
        };

        private static readonly Color HighlightColor = Color.FromArgb(140, 0xFF, 0xFF, 0x00);

        public ReportingInteractionController(SharedSceneState scene, Action<Point, Point3D?> updateCursorInfo)
        {
            _scene = scene;
            _updateCursorInfo = updateCursorInfo;
        }

        /// <summary>Set selected entity externally (e.g., from panel expansion).</summary>
        public void SelectEntity(IVisualEntity? entity)
        {
            if (_selectedEntity == entity) return;
            _selectedEntity = entity;
            HideOverlays();
            OnEntitySelected?.Invoke(entity);
        }

        public void Activate()
        {
            if (_isActive) return;
            _isActive = true;
        }

        public void Deactivate()
        {
            if (!_isActive) return;
            _isActive = false;
            HideOverlays();
            _hoveredOverlay = null;
            _selectedEntity = null;
        }

        public void Refresh()
        {
            if (!_isActive) return;
            HideOverlays();
        }

        #region IViewportInteractionHandler

        public void OnMouseDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ChangedButton == MouseButton.Left)
                _mouseDownPos = e.GetPosition(_scene.HelixViewport);
        }

        public void OnMouseMove(object sender, MouseEventArgs e)
        {
            var viewport = _scene.HelixViewport;
            var mousePos = e.GetPosition(viewport);

            var worldPos = _scene.ViewportController.UnProjectToZPlane(mousePos, 0);
            _updateCursorInfo(mousePos, worldPos);

            if (_selectedEntity == null || _selectedEntity.CompartmentCount <= 0)
            {
                if (_overlaysVisible) HideOverlays();
                return;
            }

            // Check whether the mouse hits the selected entity or its overlays
            var hits = Viewport3DHelper.FindHits(viewport.Viewport, mousePos);
            bool nearSelectedEntity = false;
            CompartmentOverlay? hitOverlay = null;

            foreach (var hit in hits)
            {
                if (hit.Visual == null) continue;

                // Check overlay hit
                if (_overlaysVisible)
                {
                    foreach (var overlay in _overlays)
                    {
                        if (VisualTreeUtils.IsSelfOrChild(hit.Visual, overlay.Visual3D))
                        {
                            hitOverlay = overlay;
                            nearSelectedEntity = true;
                            break;
                        }
                    }
                    if (hitOverlay != null) break;
                }

                // Check hit on selected entity
                if (VisualTreeUtils.IsSelfOrChild(hit.Visual, _selectedEntity.Visual3D))
                {
                    nearSelectedEntity = true;
                    if (_overlaysVisible)
                        hitOverlay = ResolveOverlayFromEntityHit(_selectedEntity, hit.Position);
                    break;
                }
            }

            // Show/hide overlays as needed
            if (nearSelectedEntity && !_overlaysVisible)
                ShowOverlaysForEntity(_selectedEntity);
            else if (!nearSelectedEntity && _overlaysVisible)
                HideOverlays();

            // Update highlight
            if (hitOverlay != _hoveredOverlay)
            {
                if (_hoveredOverlay != null)
                {
                    _hoveredOverlay.GeometryModel.Material = _hoveredOverlay.DefaultMaterial;
                    _hoveredOverlay.GeometryModel.BackMaterial = _hoveredOverlay.DefaultMaterial;
                }
                _hoveredOverlay = hitOverlay;
                if (_hoveredOverlay != null)
                {
                    _hoveredOverlay.GeometryModel.Material = _hoveredOverlay.HighlightMaterial;
                    _hoveredOverlay.GeometryModel.BackMaterial = _hoveredOverlay.HighlightMaterial;
                }
            }
        }

        public void OnMouseUp(object sender, MouseButtonEventArgs e)
        {
            if (e.ChangedButton != MouseButton.Left) return;
            var pos = e.GetPosition(_scene.HelixViewport);
            if ((pos - _mouseDownPos).Length > 5) return; // viewport drag, ignore

            var hits = Viewport3DHelper.FindHits(_scene.HelixViewport.Viewport, pos);

            foreach (var hit in hits)
            {
                if (hit.Visual == null) continue;

                // Check overlay clicks first → select compartment
                if (_overlaysVisible)
                {
                    foreach (var overlay in _overlays)
                    {
                        if (VisualTreeUtils.IsSelfOrChild(hit.Visual, overlay.Visual3D))
                        {
                            OnCompartmentSelected?.Invoke(overlay.GlobalId, overlay.ParentEntityId);
                            return;
                        }
                    }
                }

                // Check entity clicks → select entity
                foreach (var entity in _scene.Entities)
                {
                    if (VisualTreeUtils.IsSelfOrChild(hit.Visual, entity.Visual3D))
                    {
                        SelectEntity(entity);
                        return;
                    }
                }
            }
        }

        public void OnMouseWheel(object sender, MouseWheelEventArgs e)
        {
            // Allow wheel zoom
        }

        #endregion

        #region Overlay Management

        private void ShowOverlaysForEntity(IVisualEntity entity)
        {
            if (_overlaysVisible) HideOverlays();

            if (entity is AxonVisual axon)
                BuildAxonOverlays(axon);

            _overlaysVisible = true;
        }

        private void HideOverlays()
        {
            foreach (var overlay in _overlays)
                _scene.HelixViewport.Children.Remove(overlay.Visual3D);
            _overlays.Clear();
            _hoveredOverlay = null;
            _overlaysVisible = false;
        }

        private void BuildAxonOverlays(AxonVisual axon)
        {
            int n = axon.CompartmentCount;
            if (n <= 0) return;

            double segLen = axon.Length / n;

            for (int i = 0; i < n; i++)
            {
                double zStart = i * segLen;
                double tStart = axon.Length > 0 ? zStart / axon.Length : 0;
                double tEnd = axon.Length > 0 ? (i + 1) * segLen / axon.Length : 1;
                double rStart = axon.BaseRadius + (axon.TopRadius - axon.BaseRadius) * tStart;
                double rEnd = axon.BaseRadius + (axon.TopRadius - axon.BaseRadius) * tEnd;

                double offset = 0.06;
                var mesh = CreateTruncatedConeMesh(rStart + offset, rEnd + offset, segLen, 18);

                int globalId = i < axon.CompartmentIds.Count ? axon.CompartmentIds[i] : -1;
                var colorIdx = Math.Abs(globalId) % CompartmentColors.Length;
                var defaultColor = CompartmentColors[colorIdx];

                var geoModel = new GeometryModel3D
                {
                    Geometry = mesh,
                    Material = new DiffuseMaterial(new SolidColorBrush(defaultColor)),
                    BackMaterial = new DiffuseMaterial(new SolidColorBrush(defaultColor))
                };

                var visual = new ModelVisual3D { Content = geoModel };
                var localTranslate = new TranslateTransform3D(0, 0, zStart);
                var group = new Transform3DGroup();
                group.Children.Add(localTranslate);
                if (axon.Visual3D.Transform != null)
                    group.Children.Add(axon.Visual3D.Transform);
                visual.Transform = group;

                _overlays.Add(new CompartmentOverlay
                {
                    GlobalId = globalId,
                    ParentEntityId = axon.Id,
                    Index = i,
                    Visual3D = visual,
                    GeometryModel = geoModel,
                    DefaultMaterial = geoModel.Material,
                    HighlightMaterial = new DiffuseMaterial(new SolidColorBrush(HighlightColor))
                });
                _scene.HelixViewport.Children.Add(visual);
            }
        }

        private CompartmentOverlay? ResolveOverlayFromEntityHit(IVisualEntity entity, Point3D worldHitPoint)
        {
            if (entity.CompartmentCount <= 0) return null;

            if (entity is AxonVisual axon)
            {
                if (axon.Visual3D.Transform == null) return null;
                var inv = axon.Visual3D.Transform.Value;
                if (!inv.HasInverse) return null;
                inv.Invert();

                var local = inv.Transform(worldHitPoint);
                double t = axon.Length > 0 ? local.Z / axon.Length : 0;
                t = Math.Clamp(t, 0.0, 1.0);

                int idx = (int)(t * axon.CompartmentCount);
                idx = Math.Clamp(idx, 0, axon.CompartmentCount - 1);

                return _overlays.FirstOrDefault(o => o.ParentEntityId == entity.Id && o.Index == idx);
            }

            return null;
        }

        #endregion

        #region Mesh Generators

        private static MeshGeometry3D CreateTruncatedConeMesh(double baseRadius, double topRadius, double length, int segments)
        {
            var mesh = new MeshGeometry3D();

            for (int i = 0; i <= segments; i++)
            {
                double angle = 2 * Math.PI * i / segments;
                double cos = Math.Cos(angle);
                double sin = Math.Sin(angle);

                mesh.Positions.Add(new Point3D(baseRadius * cos, baseRadius * sin, 0));
                mesh.Positions.Add(new Point3D(topRadius * cos, topRadius * sin, length));
            }

            for (int i = 0; i < segments; i++)
            {
                int b0 = i * 2;
                int t0 = i * 2 + 1;
                int b1 = (i + 1) * 2;
                int t1 = (i + 1) * 2 + 1;

                mesh.TriangleIndices.Add(b0);
                mesh.TriangleIndices.Add(b1);
                mesh.TriangleIndices.Add(t0);

                mesh.TriangleIndices.Add(t0);
                mesh.TriangleIndices.Add(b1);
                mesh.TriangleIndices.Add(t1);
            }

            int baseCenterIdx = mesh.Positions.Count;
            mesh.Positions.Add(new Point3D(0, 0, 0));
            int topCenterIdx = mesh.Positions.Count;
            mesh.Positions.Add(new Point3D(0, 0, length));

            int capStart = mesh.Positions.Count;
            for (int i = 0; i <= segments; i++)
            {
                double angle = 2 * Math.PI * i / segments;
                mesh.Positions.Add(new Point3D(baseRadius * Math.Cos(angle), baseRadius * Math.Sin(angle), 0));
            }

            int topCapStart = mesh.Positions.Count;
            for (int i = 0; i <= segments; i++)
            {
                double angle = 2 * Math.PI * i / segments;
                mesh.Positions.Add(new Point3D(topRadius * Math.Cos(angle), topRadius * Math.Sin(angle), length));
            }

            for (int i = 0; i < segments; i++)
            {
                mesh.TriangleIndices.Add(baseCenterIdx);
                mesh.TriangleIndices.Add(capStart + i + 1);
                mesh.TriangleIndices.Add(capStart + i);

                mesh.TriangleIndices.Add(topCenterIdx);
                mesh.TriangleIndices.Add(topCapStart + i);
                mesh.TriangleIndices.Add(topCapStart + i + 1);
            }

            return mesh;
        }

        #endregion
    }
}
