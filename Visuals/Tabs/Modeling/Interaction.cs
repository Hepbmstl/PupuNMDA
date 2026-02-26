using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Input;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Media3D;
using HelixToolkit.Wpf;
using NeuronCAD.Visuals.Tabs.Modeling.Visuals;

namespace NeuronCAD.Visuals.Tabs.Modeling
{
    public enum InteractionState
    {
        Idle, // 空闲
        Placing, // 放置中
        Moving, // 跟随鼠标中
        DraggingConnectionEndpoint,
        SelectingConnectionTarget
    }
    
    public enum VisualDisplayMode
    {
        Normal,
        Wireframe // 透明 框架 万向轮
    }

    public class InteractionController
    {
        private readonly ModelingPage _page;
        private readonly ViewportController _viewportController;
        private readonly HelixViewport3D _helixViewport;

        private InteractionState _currentState = InteractionState.Idle;
        private List<IVisualEntity> _entities = new List<IVisualEntity>();
        private IVisualEntity? _activeEntity;

        private Point _mouseDownPos;
        private bool _isDraggingViewport = false;
        private bool _suppressNextHitTest = false;

        private CombinedManipulator _gimbal;

        private readonly ConnectionController _connectionController;

        // placing 自动连线用：缓存“当前鼠标下命中的目标实体/接触点”
        private IVisualEntity? _placingTargetEntity;
        private Point3D? _placingTargetPoint;

        // 拖拽端点用
        private string? _dragConnId;
        private bool _dragEndIsA;

        // 右键 Connect 用
        private IVisualEntity? _connectSourceEntity;
        private SphereVisual3D? _dragSphere;

        // ====== 状态变更事件总线 (新增) ======
        public event Action<IVisualEntity> OnEntityAdded;
        public event Action<IVisualEntity> OnEntityRemoved;
        public event Action<IVisualEntity?> OnSelectionChanged;

        public InteractionController(ModelingPage page, ViewportController viewportController, HelixViewport3D helixViewport)
        {
            _page = page;
            _viewportController = viewportController;
            _helixViewport = helixViewport;

            _connectionController = new ConnectionController(_helixViewport);
            CompositionTarget.Rendering += (s, e) =>
            {
                if (_activeEntity != null)
                {
                    _connectionController.UpdateAll();
                }
            };
        }

        #region Public API
        public void StartPlacing(IVisualEntity newEntity)
        {
            if (_currentState != InteractionState.Idle) return;
            _activeEntity = newEntity;
            _activeEntity.SetHitTestVisible(false);
            _helixViewport.Children.Add(_activeEntity.Visual3D);
            _currentState = InteractionState.Placing;
            _activeEntity.SetSelected(true);
        }

        public void DeleteSelected()
        {
            if (_activeEntity != null && _entities.Contains(_activeEntity))
            {
                var target = _activeEntity;
                HideGimbal();
                _helixViewport.Children.Remove(target.Visual3D);
                _entities.Remove(target);
                _activeEntity = null;
                
                // 触发数据流事件：通知实体移除并清空当前选中项
                OnEntityRemoved?.Invoke(target);
                OnSelectionChanged?.Invoke(null);
            }
        }

        public void StartMovingSelected()
        {
            if (_activeEntity != null && _entities.Contains(_activeEntity))
            {
                HideGimbal();
                _currentState = InteractionState.Moving;
                _activeEntity.SetSelected(true);
                _activeEntity.SetHitTestVisible(false);
            }
        }

        // ====== 新增：供面板反向调用的强行选中 API ======
        public void ForceSelect(IVisualEntity? entity)
        {
            // 1. 清理旧状态
            if (_activeEntity != null && _activeEntity != entity)
            {
                _activeEntity.SetSelected(false);
                HideGimbal();
            }

            // 2. 挂载新状态
            _activeEntity = entity;

            if (_activeEntity != null)
            {
                _activeEntity.SetSelected(true);
                ShowGimbal(_activeEntity);
            }

            // 3. 广播状态更新
            OnSelectionChanged?.Invoke(_activeEntity);
        }
        #endregion

        #region Input Handlers
        public void OnMouseDown(object sender, MouseButtonEventArgs e)
        {
            _mouseDownPos = e.GetPosition(_helixViewport);
            _isDraggingViewport = false;

            if (_currentState == InteractionState.Idle && e.ChangedButton == MouseButton.Left)
            {
                if (TryBeginDragConnectionEndpoint(_mouseDownPos))
                {
                    e.Handled = true;
                    return;
                }
            }

            if (_currentState == InteractionState.Placing || _currentState == InteractionState.Moving)
            {
                e.Handled = true;
                if (e.ChangedButton == MouseButton.Left) ConfirmAction();
                else if (e.ChangedButton == MouseButton.Right) CancelAction();
            }
        }

        public void OnMouseMove(object sender, MouseEventArgs e)
        {
            var mousePos = e.GetPosition(_helixViewport);
            UpdateCrosshair(mousePos);

            if (_currentState == InteractionState.DraggingConnectionEndpoint)
            {
                UpdateDraggingConnectionEndpoint(mousePos);
                return;
            }

            if (_currentState == InteractionState.Placing || _currentState == InteractionState.Moving)
            {
                UpdateObjectPosition(mousePos);
                _connectionController.UpdateAll();
            }
            else if (_currentState == InteractionState.Idle)
            {
                if (e.LeftButton == MouseButtonState.Pressed || e.RightButton == MouseButtonState.Pressed)
                {
                    if ((mousePos - _mouseDownPos).Length > 2) _isDraggingViewport = true;
                }
            }
        }

        public void OnMouseUp(object sender, MouseButtonEventArgs e)
        {
            if (_currentState == InteractionState.DraggingConnectionEndpoint)
            {
                if (_dragSphere != null)
                    _dragSphere.Fill = System.Windows.Media.Brushes.White;

                _dragSphere = null;
                _dragConnId = null;
                _currentState = InteractionState.Idle;
                e.Handled = true;
                return;
            }

            if (_currentState == InteractionState.SelectingConnectionTarget)
            {
                if (e.ChangedButton == MouseButton.Right)
                {
                    _connectSourceEntity = null;
                    _currentState = InteractionState.Idle;
                    e.Handled = true;
                    return;
                }

                if (e.ChangedButton == MouseButton.Left)
                {
                    var mousePos = e.GetPosition(_helixViewport);
                    var target = HitTestEntity(mousePos);

                    if (target != null && _connectSourceEntity != null && target != _connectSourceEntity)
                    {
                        var p = HitTestPointOnEntity(mousePos, target) ?? target.CenterPosition;

                        if (_connectSourceEntity is IAnchoredEntity aEnt &&
                            target is IAnchoredEntity bEnt &&
                            aEnt.TryWorldPointToAnchor(p, out var anchorA) &&
                            bEnt.TryWorldPointToAnchor(p, out var anchorB))
                        {
                            var conn = new Connection(_connectSourceEntity, target, anchorA, anchorB, 1.0);
                            _connectionController.Add(conn);
                        }
                    }

                    _connectSourceEntity = null;
                    _currentState = InteractionState.Idle;
                    e.Handled = true;
                    return;
                }
            }

            if (_currentState != InteractionState.Idle) return;
            if (_isDraggingViewport) { _isDraggingViewport = false; return; }

            if (e.ChangedButton == MouseButton.Left)
            {
                if (_suppressNextHitTest) { _suppressNextHitTest = false; return; }
                PerformHitTest(e.GetPosition(_helixViewport));
            }
            else if (e.ChangedButton == MouseButton.Right)
            {
                if (_activeEntity != null && _activeEntity.IsSelected) ShowContextMenu();
            }
        }

        private IVisualEntity? HitTestEntity(Point mousePos)
        {
            var hits = _helixViewport.Viewport.FindHits(mousePos);
            if (hits == null || hits.Count == 0) return null;

            var nearest = hits.OrderBy(h => h.Distance).First();
            foreach (var entity in _entities)
            {
                if (IsSelfOrChild(nearest.Visual, entity.Visual3D))
                    return entity;
            }
            return null;
        }

        private Point3D? HitTestPointOnEntity(Point mousePos, IVisualEntity entity)
        {
            var hits = _helixViewport.Viewport.FindHits(mousePos);
            if (hits == null || hits.Count == 0) return null;

            var hit = hits.OrderBy(h => h.Distance)
                          .FirstOrDefault(h => IsSelfOrChild(h.Visual, entity.Visual3D));

            return hit?.Position;
        }

        public void OnMouseWheel(object sender, MouseWheelEventArgs e) { }
        #endregion

        #region Core Logic

        private void UpdateCrosshair(Point mousePos)
        {
            var hits = _helixViewport.Viewport.FindHits(mousePos);
            var validHit = hits?.FirstOrDefault(h => _activeEntity == null || !IsSelfOrChild(h.Visual, _activeEntity.Visual3D));
            Point3D? worldPos = validHit?.Position ?? _viewportController.UnProjectToZPlane(mousePos, 0);
            _page.UpdateCursorInfo(mousePos, worldPos);
        }

        private void UpdateDraggingConnectionEndpoint(Point mousePos)
        {
            if (_dragConnId == null) return;
            if (!_connectionController.ConnectionsById.TryGetValue(_dragConnId, out var conn)) return;

            var targetEntity = _dragEndIsA ? conn.A : conn.B;
            if (targetEntity is not Visuals.IAnchoredEntity anchoredTarget) return;

            var hits = _helixViewport.Viewport.FindHits(mousePos);
            if (hits == null || hits.Count == 0) return;

            var hit = hits.OrderBy(h => h.Distance).FirstOrDefault(h => IsSelfOrChild(h.Visual, targetEntity.Visual3D));
            if (hit == null) return;

            if (!anchoredTarget.TryWorldPointToAnchor(hit.Position, out var newAnchor)) return;

            if (_dragEndIsA) conn.AnchorA = newAnchor;
            else conn.AnchorB = newAnchor;

            _connectionController.Update(conn.Id);
        }

        private void UpdateObjectPosition(Point mousePos)
        {
            if (_activeEntity == null) return;

            var allHits = _helixViewport.Viewport.FindHits(mousePos);
            var validHit = allHits.FirstOrDefault(h => !IsSelfOrChild(h.Visual, _activeEntity.Visual3D));

            if (validHit != null)
            {
                _activeEntity.AlignTo(validHit.Position, validHit.Normal);
                _connectionController.UpdateAll();
                _placingTargetPoint = validHit.Position;
                _placingTargetEntity = _entities.FirstOrDefault(ent => IsSelfOrChild(validHit.Visual, ent.Visual3D));
            }
            else
            {
                _placingTargetEntity = null;
                _placingTargetPoint = null;
                Vector3D planeNormal = new Vector3D(0, 0, 1);
                var hitPoint = _viewportController.UnProjectToZPlane(mousePos, 0);

                if (hitPoint.HasValue)
                {
                    _activeEntity.AlignTo(hitPoint.Value, planeNormal);
                    _connectionController.UpdateAll();
                }
            }
        }

        private void ConfirmAction()
        {
            if (_activeEntity == null) return;

            if (_currentState == InteractionState.Placing)
            {
                _entities.Add(_activeEntity);
                
                // 触发数据流事件：通知面板新建节点
                OnEntityAdded?.Invoke(_activeEntity);

                if (_placingTargetEntity != null && _placingTargetPoint.HasValue)
                {
                    var a = _activeEntity;
                    var b = _placingTargetEntity;
                    var p = _placingTargetPoint.Value;

                    if (a is IAnchoredEntity aa && b is IAnchoredEntity bb)
                    {
                        if (aa.TryWorldPointToAnchor(p, out var anchorA) &&
                            bb.TryWorldPointToAnchor(p, out var anchorB))
                        {
                            var conn = new Connection(a, b, anchorA, anchorB, weight: 1.0);
                            _connectionController.Add(conn);
                        }
                    }
                }

                _placingTargetEntity = null;
                _placingTargetPoint = null;
            }

            _activeEntity.SetHitTestVisible(true);
            _activeEntity.SetSelected(false);
            HideGimbal();
            _activeEntity = null;

            _currentState = InteractionState.Idle;
            _suppressNextHitTest = true;
            
            // 放置完毕后，重置选中状态
            OnSelectionChanged?.Invoke(null);
        }

        private void CancelAction()
        {
            if (_currentState == InteractionState.Placing && _activeEntity != null)
            {
                _helixViewport.Children.Remove(_activeEntity.Visual3D);
                _activeEntity = null;
            }
            else if (_currentState == InteractionState.Moving && _activeEntity != null)
            {
                _activeEntity.SetHitTestVisible(true);
                HideGimbal();
            }
            _currentState = InteractionState.Idle;
            
            // 取消操作后可能恢复或清空选中
            OnSelectionChanged?.Invoke(_activeEntity);
        }

        private void PerformHitTest(Point mousePos)
        {
            if (_gimbal != null)
            {
                var hits = _helixViewport.Viewport.FindHits(mousePos);
                var gimbalHit = hits?.FirstOrDefault(h => IsSelfOrChild(h.Visual, _gimbal));
                if (gimbalHit != null) return;
            }

            IVisualEntity? hitEntity = null;
            var hits2 = _helixViewport.Viewport.FindHits(mousePos);
            if (hits2 != null && hits2.Count > 0)
            {
                var nearest = hits2.OrderBy(h => h.Distance).First();
                foreach (var entity in _entities)
                {
                    if (IsSelfOrChild(nearest.Visual, entity.Visual3D))
                    {
                        hitEntity = entity;
                        break;
                    }
                }
            }

            // 修改原逻辑：使用 ForceSelect 统一接管渲染状态变换与事件抛出
            ForceSelect(hitEntity);
        }

        private bool IsSelfOrChild(Visual3D hitVisual, Visual3D selfVisual)
        {
            if (hitVisual == selfVisual) return true;
            DependencyObject curr = hitVisual;
            while (curr != null)
            {
                if (curr == selfVisual) return true;
                curr = VisualTreeHelper.GetParent(curr);
            }
            return false;
        }

        private void ShowGimbal(IVisualEntity entity)
        {
            HideGimbal();
            entity.SetHitTestVisible(false);
            entity.SetDisplayMode(VisualDisplayMode.Wireframe);
            _gimbal = new CombinedManipulator
            {
                Diameter = 5,
                CanTranslateX = true, CanTranslateY = true, CanTranslateZ = true,
                CanRotateX = true, CanRotateY = true, CanRotateZ = true
            };
            _gimbal.Bind(entity.Visual3D);
            _helixViewport.Children.Add(_gimbal);
        }

        private void HideGimbal()
        {
            if (_gimbal != null)
            {
                _gimbal.UnBind();
                _helixViewport.Children.Remove(_gimbal);
                _gimbal = null;
            }
            if (_activeEntity != null)
            {
                _activeEntity.SetDisplayMode(VisualDisplayMode.Normal);
                _activeEntity.SetHitTestVisible(true);
            }
        }

        private void ShowContextMenu()
        {
            var contextMenu = new ContextMenu();

            var moveItem = new MenuItem { Header = "Move" };
            moveItem.Click += (s, e) => StartMovingSelected();

            var resizeItem = new MenuItem { Header = "Resize..." };
            resizeItem.Click += (s, e) =>
            {
                if (_activeEntity == null) return;
                var mousePos = Mouse.GetPosition(_page);
                _page.ShowEditPopup(_activeEntity, mousePos);
            };

            var connectItem = new MenuItem { Header = "Connect" };
            connectItem.Click += (s, e) =>
            {
                if (_activeEntity == null) return;
                _connectSourceEntity = _activeEntity;
                _currentState = InteractionState.SelectingConnectionTarget;
            };

            var deleteItem = new MenuItem { Header = "Delete" };
            deleteItem.Click += (s, e) => DeleteSelected();

            contextMenu.Items.Add(moveItem);
            contextMenu.Items.Add(resizeItem);
            contextMenu.Items.Add(connectItem);
            contextMenu.Items.Add(new Separator());
            contextMenu.Items.Add(deleteItem);

            contextMenu.IsOpen = true;
        }
        #endregion

        private bool TryBeginDragConnectionEndpoint(Point mousePos)
        {
            var hits = _helixViewport.Viewport.FindHits(mousePos);
            if (hits == null || hits.Count == 0) return false;

            foreach (var h in hits.OrderBy(x => x.Distance))
            {
                foreach (var kv in _connectionController.VisualsById)
                {
                    var id = kv.Key;
                    var vis = kv.Value;

                    if (IsSelfOrChild(h.Visual, vis.EndA))
                    {
                        _dragConnId = id;
                        _dragEndIsA = true;
                        _dragSphere = vis.EndA;
                        vis.EndA.Fill = System.Windows.Media.Brushes.OrangeRed;
                        _currentState = InteractionState.DraggingConnectionEndpoint;
                        return true;
                    }

                    if (IsSelfOrChild(h.Visual, vis.EndB))
                    {
                        _dragConnId = id;
                        _dragEndIsA = false;
                        _dragSphere = vis.EndB;
                        vis.EndB.Fill = System.Windows.Media.Brushes.OrangeRed;
                        _currentState = InteractionState.DraggingConnectionEndpoint;
                        return true;
                    }
                }
            }

            return false;
        }
    }
}