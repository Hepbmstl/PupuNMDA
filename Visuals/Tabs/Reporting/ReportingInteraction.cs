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
    /// 区室覆盖层数据，持有一个区室对应的 3D 可视化模型和元数据。
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
    /// Reporting 模式专属交互控制器。
    /// 职责：禁止一般操作；支持点击实体选中并展开面板条目；
    /// 鼠标靠近选中实体时按需显示区室覆盖层；点击覆盖层选择区室。
    /// </summary>
    public class ReportingInteractionController : IViewportInteractionHandler
    {
        private readonly SharedSceneState _scene;
        private readonly Action<Point, Point3D?> _updateCursorInfo;

        private readonly List<CompartmentOverlay> _overlays = new();
        private CompartmentOverlay? _hoveredOverlay;
        private bool _isActive;

        /// <summary>当前选中的实体（由面板展开或视口点击设置）。</summary>
        private IVisualEntity? _selectedEntity;

        /// <summary>覆盖层是否正在显示。</summary>
        private bool _overlaysVisible;

        /// <summary>鼠标按下位置，用于区分点击和视口拖拽。</summary>
        private Point _mouseDownPos;

        /// <summary>实体选中事件，面板订阅以展开对应条目。</summary>
        public event Action<IVisualEntity?>? OnEntitySelected;

        /// <summary>区室选中事件 (globalId, parentEntityId)，面板订阅以更新选中标签。</summary>
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

        /// <summary>外部（面板展开）设置选中实体。</summary>
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

            // 检查鼠标是否命中选中实体或其覆盖层
            var hits = Viewport3DHelper.FindHits(viewport.Viewport, mousePos);
            bool nearSelectedEntity = false;
            CompartmentOverlay? hitOverlay = null;

            foreach (var hit in hits)
            {
                if (hit.Visual == null) continue;

                // 检查覆盖层命中
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

                // 检查选中实体命中
                if (VisualTreeUtils.IsSelfOrChild(hit.Visual, _selectedEntity.Visual3D))
                {
                    nearSelectedEntity = true;
                    if (_overlaysVisible)
                        hitOverlay = ResolveOverlayFromEntityHit(_selectedEntity, hit.Position);
                    break;
                }
            }

            // 按需显示/隐藏覆盖层
            if (nearSelectedEntity && !_overlaysVisible)
                ShowOverlaysForEntity(_selectedEntity);
            else if (!nearSelectedEntity && _overlaysVisible)
                HideOverlays();

            // 更新高亮
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
            if ((pos - _mouseDownPos).Length > 5) return; // 视口拖拽，不处理

            var hits = Viewport3DHelper.FindHits(_scene.HelixViewport.Viewport, pos);

            foreach (var hit in hits)
            {
                if (hit.Visual == null) continue;

                // 优先检查覆盖层点击 → 选择区室
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

                // 检查实体点击 → 选中实体
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
            // 允许滚轮缩放
        }

        #endregion

        #region Overlay Management

        private void ShowOverlaysForEntity(IVisualEntity entity)
        {
            if (_overlaysVisible) HideOverlays();

            if (entity is SomaVisual soma)
                BuildSomaOverlays(soma);
            else if (entity is AxonVisual axon)
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

        private void BuildSomaOverlays(SomaVisual soma)
        {
            if (soma.CompartmentIds.Count == 0) return;

            var colorIdx = soma.CompartmentIds[0] % CompartmentColors.Length;
            var defaultColor = CompartmentColors[colorIdx];

            var mesh = CreateSphereMesh(soma.Radius + 0.08, 20, 20);
            var geoModel = new GeometryModel3D
            {
                Geometry = mesh,
                Material = new DiffuseMaterial(new SolidColorBrush(defaultColor)),
                BackMaterial = new DiffuseMaterial(new SolidColorBrush(defaultColor))
            };

            var visual = new ModelVisual3D { Content = geoModel };
            visual.Transform = soma.Visual3D.Transform;

            _overlays.Add(new CompartmentOverlay
            {
                GlobalId = soma.CompartmentIds[0],
                ParentEntityId = soma.Id,
                Index = 0,
                Visual3D = visual,
                GeometryModel = geoModel,
                DefaultMaterial = geoModel.Material,
                HighlightMaterial = new DiffuseMaterial(new SolidColorBrush(HighlightColor))
            });
            _scene.HelixViewport.Children.Add(visual);
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

            if (entity is SomaVisual)
                return _overlays.FirstOrDefault(o => o.ParentEntityId == entity.Id);

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

        private static MeshGeometry3D CreateSphereMesh(double radius, int stacks, int slices)
        {
            var mesh = new MeshGeometry3D();

            for (int stack = 0; stack <= stacks; stack++)
            {
                double phi = Math.PI * stack / stacks;
                double sinPhi = Math.Sin(phi);
                double cosPhi = Math.Cos(phi);

                for (int slice = 0; slice <= slices; slice++)
                {
                    double theta = 2 * Math.PI * slice / slices;
                    double x = radius * sinPhi * Math.Cos(theta);
                    double y = radius * sinPhi * Math.Sin(theta);
                    double z = radius * cosPhi;

                    mesh.Positions.Add(new Point3D(x, y, z));
                    mesh.Normals.Add(new Vector3D(x, y, z));
                }
            }

            for (int stack = 0; stack < stacks; stack++)
            {
                for (int slice = 0; slice < slices; slice++)
                {
                    int i0 = stack * (slices + 1) + slice;
                    int i1 = i0 + 1;
                    int i2 = i0 + slices + 1;
                    int i3 = i2 + 1;

                    mesh.TriangleIndices.Add(i0);
                    mesh.TriangleIndices.Add(i2);
                    mesh.TriangleIndices.Add(i1);

                    mesh.TriangleIndices.Add(i1);
                    mesh.TriangleIndices.Add(i2);
                    mesh.TriangleIndices.Add(i3);
                }
            }

            return mesh;
        }

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
