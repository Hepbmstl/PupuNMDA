using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Input;
using System.Windows.Controls;
using System.Windows.Media; // <--- 添加此行
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
        Wireframe //透明 框架 万向轮

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

        public InteractionController(ModelingPage page, ViewportController viewportController, HelixViewport3D helixViewport)
        {
            _page = page;
            _viewportController = viewportController;
            _helixViewport = helixViewport;

            _connectionController = new ConnectionController(_helixViewport);
        }

        // a

        #region Public API
        public void StartPlacing(IVisualEntity newEntity)
        {
            if (_currentState != InteractionState.Idle) return;
            _activeEntity = newEntity;
            // 放置时关闭自身HitTest，防止射线检测到自己
            _activeEntity.SetHitTestVisible(false);
            _helixViewport.Children.Add(_activeEntity.Visual3D);
            // children加入场景
            _currentState = InteractionState.Placing;
            _activeEntity.SetSelected(true);
        }

        public void DeleteSelected()
        {
            if (_activeEntity != null && _entities.Contains(_activeEntity))
            {
                HideGimbal();
                _helixViewport.Children.Remove(_activeEntity.Visual3D);
                _entities.Remove(_activeEntity);
                _activeEntity = null;
            }
        }

        public void StartMovingSelected()
        {
            if (_activeEntity != null && _entities.Contains(_activeEntity))
            {
                HideGimbal();
                _currentState = InteractionState.Moving;
                _activeEntity.SetSelected(true);
                // 移动时也要暂时忽略自身的 HitTest
                _activeEntity.SetHitTestVisible(false);
            }
        }
        #endregion

        #region Input Handlers
        public void OnMouseDown(object sender, MouseButtonEventArgs e)
        {
            _mouseDownPos = e.GetPosition(_helixViewport);
            _isDraggingViewport = false;

            // Idle + 左键：优先看是否点到了连接端点球
            if (_currentState == InteractionState.Idle && e.ChangedButton == MouseButton.Left)
            {
                if (TryBeginDragConnectionEndpoint(_mouseDownPos))
                {
                    e.Handled = true;
                    return;
                }
            }

            // 你原来的 placing/moving confirm/cancel 逻辑...
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
                _connectionController.UpdateAll(); // 实体在动，连接跟着更新（先用粗暴版）
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
            // 1) 拖拽端点：MouseUp 结束拖拽
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

            // 2) 选择连接目标：左键选择目标，右键取消
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

                    // 无论成功与否都退出，避免卡住
                    _connectSourceEntity = null;
                    _currentState = InteractionState.Idle;

                    e.Handled = true;
                    return;
                }
            }

            // 3) 其它状态：保持你原有逻辑
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
            // CTRL-based Z offset adjustment removed; use the gimbal for component translations
        }
        #endregion

        #region Core Logic

        //更新十字准星和 HUD
        private void UpdateCrosshair(Point mousePos)
        {
            // 发射射线寻找最近的物体表面
            var hits = _helixViewport.Viewport.FindHits(mousePos);

            // 排除正在放置的物体(如果有)
            var validHit = hits?.FirstOrDefault(h => _activeEntity == null || !IsSelfOrChild(h.Visual, _activeEntity.Visual3D));

            Point3D? worldPos = null;

            if (validHit != null)
            {
                // 如果击中物体，显示接触点
                worldPos = validHit.Position;
            }
            else
            {
                // 如果没击中，显示网格平面上的点 (Z=0)
                // 注意：这里用 Z=0 而不是 _gridZOffset，因为准星是测量工具，通常测量绝对坐标
                worldPos = _viewportController.UnProjectToZPlane(mousePos, 0);
            }

            _page.UpdateCursorInfo(mousePos, worldPos);
        }

        private void UpdateDraggingConnectionEndpoint(Point mousePos)
        {
            if (_dragConnId == null) return;
            if (!_connectionController.ConnectionsById.TryGetValue(_dragConnId, out var conn)) return;

            // 端点 A 只能落在 A 实体上；端点 B 只能落在 B 实体上
            var targetEntity = _dragEndIsA ? conn.A : conn.B;
            if (targetEntity is not Visuals.IAnchoredEntity anchoredTarget)
                return;

            var hits = _helixViewport.Viewport.FindHits(mousePos);
            if (hits == null || hits.Count == 0) return;

            // 找到命中 targetEntity.Visual3D 的最近 hit
            var hit = hits
                .OrderBy(h => h.Distance)
                .FirstOrDefault(h => IsSelfOrChild(h.Visual, targetEntity.Visual3D));

            if (hit == null) return;

            if (!anchoredTarget.TryWorldPointToAnchor(hit.Position, out var newAnchor))
                return;

            if (_dragEndIsA) conn.AnchorA = newAnchor;
            else conn.AnchorB = newAnchor;

            _connectionController.Update(conn.Id);
        }

        private void UpdateObjectPosition(Point mousePos)
        {//吸附 移动到命中点
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
                Vector3D planeNormal = new Vector3D(0, 0, 1);
                var hitPoint = _viewportController.UnProjectToZPlane(mousePos, 0);

                if (hitPoint.HasValue)
                {
                    _activeEntity.AlignTo(hitPoint.Value, planeNormal);
                    _connectionController.UpdateAll();
                }
            }
        }

        // 逻辑更新1：放置后自动放弃选中
        private void ConfirmAction()
        {
            if (_activeEntity == null) return;

            if (_currentState == InteractionState.Placing)
            {
                _entities.Add(_activeEntity);

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

            // 恢复 HitTest 可见性
            _activeEntity.SetHitTestVisible(true);

            // 自动放弃选中 (需求1)
            _activeEntity.SetSelected(false);
            HideGimbal();
            _activeEntity = null; // 清空当前指针

            _currentState = InteractionState.Idle;
            // Fix double-click: the second click of the double-click must not re-select the just-placed entity
            _suppressNextHitTest = true;
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
                 // 移动取消时，也要恢复 HitTest
                 _activeEntity.SetHitTestVisible(true);
                 HideGimbal();
                 // 暂时回到 Idle，位置可能没复原(需 Memento)
            }
            _currentState = InteractionState.Idle;
        }
        private void PerformHitTest(Point mousePos)
        {
            // 先清空旧的选中并隐藏 gimbal
            if (_activeEntity != null) { _activeEntity.SetSelected(false); HideGimbal(); _activeEntity = null; }

            // 原逻辑：清空旧选中并隐藏 gimbal
            if (_activeEntity != null) { _activeEntity.SetSelected(false); HideGimbal(); _activeEntity = null; }

            var hits2 = _helixViewport.Viewport.FindHits(mousePos);
            if (hits2 != null && hits2.Count > 0)
            {
                var nearest = hits2.OrderBy(h => h.Distance).First();
                foreach (var entity in _entities)
                {
                    if (IsSelfOrChild(nearest.Visual, entity.Visual3D))
                    {
                        _activeEntity = entity;
                        _activeEntity.SetSelected(true);
                        ShowGimbal(_activeEntity);
                        break; // 选中一个即可
                    }
                }
            }
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
            _gimbal = new CombinedManipulator
            {
                Diameter = 5,
                CanTranslateX = true,
                CanTranslateY = true,
                CanTranslateZ = true,
                CanRotateX = true,
                CanRotateY = true,
                CanRotateZ = true
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
        }

        // 逻辑更新3：右键菜单增加 Resize 选项
        private void ShowContextMenu()
        {
            var contextMenu = new ContextMenu();

            // 移动
            var moveItem = new MenuItem { Header = "Move" };
            moveItem.Click += (s, e) => StartMovingSelected();

            // 调整尺寸 (Resize)
            var resizeItem = new MenuItem { Header = "Resize..." };
            resizeItem.Click += (s, e) =>
            {
                if (_activeEntity == null) return;
                // 获取鼠标位置以显示弹窗
                var mousePos = Mouse.GetPosition(_page);
                _page.ShowEditPopup(_activeEntity, mousePos);
            };

            // 删除
            var deleteItem = new MenuItem { Header = "Delete" };
            deleteItem.Click += (s, e) => DeleteSelected();

            var connectItem = new MenuItem { Header = "Connect" };
            connectItem.Click += (s, e) =>
            {
                if (_activeEntity == null) return;
                _connectSourceEntity = _activeEntity;
                _currentState = InteractionState.SelectingConnectionTarget;
            };

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

            // 从近到远检查命中的是不是某个端点球
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
                        vis.EndA.Fill = System.Windows.Media.Brushes.OrangeRed; // 高亮色
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