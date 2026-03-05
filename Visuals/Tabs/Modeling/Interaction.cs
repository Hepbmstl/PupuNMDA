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
using NeuronCAD.Visuals.Tabs.Shared;

namespace NeuronCAD.Visuals.Tabs.Modeling
{
    /// <summary>
    /// 建模交互状态枚举，追踪 InteractionController 当前所处的操作阶段。
    /// 由 InteractionController._currentState 持有。
    /// </summary>
    public enum InteractionState
    {
        /// <summary>空闲状态，可进行选中/右键操作</summary>
        Idle,
        /// <summary>放置新实体中（跟随鼠标移动，左键确认，右键取消）</summary>
        Placing,
        /// <summary>移动已有实体中（跟随鼠标移动，左键确认，右键取消）</summary>
        Moving,
        /// <summary>拖拽连接线端点中（修改锚点位置）</summary>
        DraggingConnectionEndpoint,
        /// <summary>选择连接目标中（右键菜单 Connect 后，等待左键点击目标实体）</summary>
        SelectingConnectionTarget
    }

    /// <summary>
    /// 建模模式专属交互控制器，实现 IViewportInteractionHandler 接口。
    /// 职责：实体放置、选中、移动、删除、连接创建/拖拽、右键上下文菜单、万向轮操纵器管理。
    /// 由 MainWindow.InitializeControllers 创建，注入 SharedSceneState 和回调委托。
    /// </summary>
    public class InteractionController : IViewportInteractionHandler
    {
        /// <summary>共享场景状态引用，持有实体列表、连接控制器和视口。由构造函数注入。</summary>
        private readonly SharedSceneState _scene;

        /// <summary>准星和坐标 HUD 更新回调，指向 MainWindow.UpdateCursorInfo。由 OnMouseMove 调用。</summary>
        private readonly Action<Point, Point3D?> _updateCursorInfo;

        /// <summary>尺寸编辑请求回调（可选），指向 MainWindow.ShowEditPopup。由右键菜单 "Resize..." 触发。</summary>
        private readonly Action<IVisualEntity>? _onResizeRequested;

        /// <summary>当前交互状态。由各事件处理方法修改。</summary>
        private InteractionState _currentState = InteractionState.Idle;

        /// <summary>当前活跃的实体（正在放置/移动/选中的实体）。</summary>
        private IVisualEntity? _activeEntity;

        /// <summary>鼠标按下时的屏幕位置，用于判断是否发生了拖拽（区分点击和视口旋转）。</summary>
        private Point _mouseDownPos;

        /// <summary>标记鼠标是否正在拖拽视口（旋转/平移视角），若为 true 则 MouseUp 时不执行命中测试。</summary>
        private bool _isDraggingViewport = false;

        /// <summary>标记下一次命中测试是否应被跳过。在 ConfirmAction 后设为 true，防止确认点击同时触发选中。</summary>
        private bool _suppressNextHitTest = false;

        /// <summary>HelixToolkit 万向轮操纵器引用，用于在选中实体上显示平移/旋转手柄。由 ShowGimbal/HideGimbal 管理。</summary>
        private CombinedManipulator _gimbal;

        /// <summary>放置模式下鼠标悬停命中的目标实体（用于自动建立连接）。由 UpdateObjectPosition 更新。</summary>
        private IVisualEntity? _placingTargetEntity;

        /// <summary>放置模式下鼠标悬停命中的目标表面点（用于计算连接锚点）。由 UpdateObjectPosition 更新。</summary>
        private Point3D? _placingTargetPoint;

        /// <summary>正在拖拽的连接线 ID。在 DraggingConnectionEndpoint 状态使用。由 TryBeginDragConnectionEndpoint 设置。</summary>
        private string? _dragConnId;

        /// <summary>标记正在拖拽的是端点 A (true) 还是端点 B (false)。由 TryBeginDragConnectionEndpoint 设置。</summary>
        private bool _dragEndIsA;

        /// <summary>右键菜单 "Connect" 操作的源实体引用。在 SelectingConnectionTarget 状态使用。</summary>
        private IVisualEntity? _connectSourceEntity;

        /// <summary>正在被拖拽的连接端点球体引用，用于临时变色为橙红色。由 TryBeginDragConnectionEndpoint 设置。</summary>
        private SphereVisual3D? _dragSphere;

        /// <summary>实体添加事件，在放置确认后触发。被 PropertiesPanelController.HandleEntityAdded 订阅，用于创建属性面板卡片。</summary>
        public event Action<IVisualEntity> OnEntityAdded;

        /// <summary>实体删除事件，在 DeleteSelected 后触发。被 PropertiesPanelController.HandleEntityRemoved 订阅，用于移除属性面板卡片。</summary>
        public event Action<IVisualEntity> OnEntityRemoved;

        /// <summary>选中实体变更事件，选中/取消选中时触发。被 PropertiesPanelController.HandleSelectionChanged 订阅，用于高亮/折叠面板卡片。</summary>
        public event Action<IVisualEntity?> OnSelectionChanged;

        /// <summary>
        /// 构造函数。由 MainWindow.InitializeControllers 调用，注入共享场景状态和回调委托。
        /// </summary>
        /// <param name="scene">共享场景状态</param>
        /// <param name="updateCursorInfo">准星/HUD 更新回调</param>
        /// <param name="onResizeRequested">尺寸编辑请求回调（可选）</param>
        public InteractionController(
            SharedSceneState scene,
            Action<Point, Point3D?> updateCursorInfo,
            Action<IVisualEntity>? onResizeRequested = null)
        {
            _scene = scene;
            _updateCursorInfo = updateCursorInfo;
            _onResizeRequested = onResizeRequested;
        }

        #region Public API

        /// <summary>
        /// 开始放置新实体：将实体添加到视口并进入 Placing 状态。
        /// 被 MainWindow.OnAddSomaClick/OnAddAxonClick/OnAddDendClick 调用。
        /// </summary>
        /// <param name="newEntity">要放置的新实体</param>
        public void StartPlacing(IVisualEntity newEntity)
        {
            if (_currentState != InteractionState.Idle) return;
            _activeEntity = newEntity;
            _activeEntity.SetHitTestVisible(false);
            _scene.HelixViewport.Children.Add(_activeEntity.Visual3D);
            _currentState = InteractionState.Placing;
            _activeEntity.SetSelected(true);
        }

        /// <summary>
        /// 删除当前选中的实体及其视觉对象，同时触发 OnEntityRemoved 和 OnSelectionChanged 事件。
        /// 被右键上下文菜单 "Delete" 选项调用。
        /// </summary>
        public void DeleteSelected()
        {
            if (_activeEntity != null && _scene.Entities.Contains(_activeEntity))
            {
                var target = _activeEntity;
                HideGimbal();
                _scene.HelixViewport.Children.Remove(target.Visual3D);
                _scene.Entities.Remove(target);
                _activeEntity = null;

                OnEntityRemoved?.Invoke(target);
                OnSelectionChanged?.Invoke(null);
            }
        }

        /// <summary>
        /// 将当前选中的实体进入移动模式（跟随鼠标重新定位），隐藏万向轮并关闭命中测试。
        /// 被右键上下文菜单 "Move" 选项调用。
        /// </summary>
        public void StartMovingSelected()
        {
            if (_activeEntity != null && _scene.Entities.Contains(_activeEntity))
            {
                HideGimbal();
                _currentState = InteractionState.Moving;
                _activeEntity.SetSelected(true);
                _activeEntity.SetHitTestVisible(false);
            }
        }

        /// <summary>
        /// 强制选中或取消选中实体（取消旧选中、设置新选中、显示万向轮、触发事件）。
        /// 被 PerformHitTest（点击命中后）和 PropertiesPanelController（面板展开时）调用。
        /// </summary>
        /// <param name="entity">要选中的实体，null 表示取消选中</param>
        public void ForceSelect(IVisualEntity? entity)
        {
            if (_activeEntity != null && _activeEntity != entity)
            {
                _activeEntity.SetSelected(false);
                HideGimbal();
            }

            _activeEntity = entity;

            if (_activeEntity != null)
            {
                _activeEntity.SetSelected(true);
                ShowGimbal(_activeEntity);
            }

            OnSelectionChanged?.Invoke(_activeEntity);
        }

        /// <summary>
        /// 停用建模交互：取消当前操作并清除选中状态。
        /// 被 MainWindow.SwitchTab 在切换标签页时调用。
        /// </summary>
        public void Deactivate()
        {
            CancelAction();
            ForceSelect(null);
        }
        #endregion

        #region Input Handlers (IViewportInteractionHandler)

        /// <summary>
        /// 鼠标按下事件处理。空闲状态下尝试开始拖拽连接端点；放置/移动状态下左键确认、右键取消。
        /// 由 MainWindow.OnViewportMouseDown 路由调用。
        /// </summary>
        public void OnMouseDown(object sender, MouseButtonEventArgs e)
        {
            _mouseDownPos = e.GetPosition(_scene.HelixViewport);
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

        /// <summary>
        /// 鼠标移动事件处理。更新准星 HUD；根据当前状态更新连接端点拖拽、实体位置或视口旋转判定。
        /// 由 MainWindow.OnViewportMouseMove 路由调用。
        /// </summary>
        public void OnMouseMove(object sender, MouseEventArgs e)
        {
            var mousePos = e.GetPosition(_scene.HelixViewport);
            UpdateCrosshair(mousePos);

            if (_currentState == InteractionState.DraggingConnectionEndpoint)
            {
                UpdateDraggingConnectionEndpoint(mousePos);
                return;
            }

            if (_currentState == InteractionState.Placing || _currentState == InteractionState.Moving)
            {
                UpdateObjectPosition(mousePos);
                _scene.ConnectionController.UpdateAll();
            }
            else if (_currentState == InteractionState.Idle)
            {
                if (e.LeftButton == MouseButtonState.Pressed || e.RightButton == MouseButtonState.Pressed)
                {
                    if ((mousePos - _mouseDownPos).Length > 2) _isDraggingViewport = true;
                }
            }
        }

        /// <summary>
        /// 鼠标释放事件处理。DraggingConnectionEndpoint 状态完成端点拖拽；SelectingConnectionTarget 状态选择连接目标或取消；
        /// Idle 状态左键执行命中测试选中实体，右键显示上下文菜单。
        /// 由 MainWindow.OnViewportMouseUp 路由调用。
        /// </summary>
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
                    var mousePos = e.GetPosition(_scene.HelixViewport);
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
                            _scene.ConnectionController.Add(conn);
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
                PerformHitTest(e.GetPosition(_scene.HelixViewport));
            }
            else if (e.ChangedButton == MouseButton.Right)
            {
                if (_activeEntity != null && _activeEntity.IsSelected) ShowContextMenu();
            }
        }

        /// <summary>鼠标滚轮事件处理（当前无自定义逻辑，由 HelixViewport3D 内置缩放处理）。</summary>
        public void OnMouseWheel(object sender, MouseWheelEventArgs e) { }
        #endregion

        #region Core Logic & Hit Testing

        /// <summary>
        /// 在场景实体列表中执行射线命中测试，返回最近命中的实体。
        /// 被 OnMouseUp（SelectingConnectionTarget 状态）和 PerformHitTest 调用。
        /// </summary>
        /// <param name="mousePos">鼠标在视口中的屏幕坐标</param>
        /// <returns>命中的实体，或 null</returns>
        private IVisualEntity? HitTestEntity(Point mousePos)
        {
            var hits = _scene.HelixViewport.Viewport.FindHits(mousePos);
            if (hits == null || hits.Count == 0) return null;

            var nearest = hits.OrderBy(h => h.Distance).First();
            foreach (var entity in _scene.Entities)
            {
                if (VisualTreeUtils.IsSelfOrChild(nearest.Visual, entity.Visual3D))
                    return entity;
            }
            return null;
        }

        /// <summary>
        /// 在指定实体上执行射线命中测试，返回命中点的世界坐标。
        /// 被 OnMouseUp（创建连接时获取精确命中点）调用。
        /// </summary>
        /// <param name="mousePos">鼠标在视口中的屏幕坐标</param>
        /// <param name="entity">目标实体</param>
        /// <returns>命中点世界坐标，或 null</returns>
        private Point3D? HitTestPointOnEntity(Point mousePos, IVisualEntity entity)
        {
            var hits = _scene.HelixViewport.Viewport.FindHits(mousePos);
            if (hits == null || hits.Count == 0) return null;

            var hit = hits.OrderBy(h => h.Distance)
                          .FirstOrDefault(h => VisualTreeUtils.IsSelfOrChild(h.Visual, entity.Visual3D));

            return hit?.Position;
        }
        #endregion

        #region Position Updates

        /// <summary>
        /// 更新准星位置和坐标 HUD 显示。排除当前活跃实体以避免自命中。
        /// 在 OnMouseMove 中每次鼠标移动时调用。
        /// </summary>
        private void UpdateCrosshair(Point mousePos)
        {
            var hits = _scene.HelixViewport.Viewport.FindHits(mousePos);
            var validHit = hits?.FirstOrDefault(h => _activeEntity == null || !VisualTreeUtils.IsSelfOrChild(h.Visual, _activeEntity.Visual3D));
            Point3D? worldPos = validHit?.Position ?? _scene.ViewportController.UnProjectToZPlane(mousePos, 0);
            _updateCursorInfo(mousePos, worldPos);
        }

        /// <summary>
        /// 更新连接线端点拖拽位置，将鼠标命中点转换为新锚点并刷新连接可视化。
        /// 在 DraggingConnectionEndpoint 状态下由 OnMouseMove 调用。
        /// </summary>
        private void UpdateDraggingConnectionEndpoint(Point mousePos)
        {
            if (_dragConnId == null) return;
            if (!_scene.ConnectionController.ConnectionsById.TryGetValue(_dragConnId, out var conn)) return;

            var targetEntity = _dragEndIsA ? conn.A : conn.B;
            if (targetEntity is not IAnchoredEntity anchoredTarget) return;

            var hits = _scene.HelixViewport.Viewport.FindHits(mousePos);
            if (hits == null || hits.Count == 0) return;

            var hit = hits.OrderBy(h => h.Distance).FirstOrDefault(h => VisualTreeUtils.IsSelfOrChild(h.Visual, targetEntity.Visual3D));
            if (hit == null) return;

            if (!anchoredTarget.TryWorldPointToAnchor(hit.Position, out var newAnchor)) return;

            if (_dragEndIsA) conn.AnchorA = newAnchor;
            else conn.AnchorB = newAnchor;

            _scene.ConnectionController.Update(conn.Id);
        }

        /// <summary>
        /// 更新正在放置/移动的实体位置。优先吸附到其他实体表面，否则投影到 Z=0 平面。
        /// 同时更新连接线和附属设备位置，记录悬停目标用于自动创建连接。
        /// 在 Placing/Moving 状态下由 OnMouseMove 调用。
        /// </summary>
        private void UpdateObjectPosition(Point mousePos)
        {
            if (_activeEntity == null) return;

            var allHits = _scene.HelixViewport.Viewport.FindHits(mousePos);
            var validHit = allHits.FirstOrDefault(h => !VisualTreeUtils.IsSelfOrChild(h.Visual, _activeEntity.Visual3D));

            if (validHit != null)
            {
                _activeEntity.AlignTo(validHit.Position, validHit.Normal);
                _scene.ConnectionController.UpdateAll();

                foreach (var device in _scene.Devices.Where(d => d.TargetEntity == _activeEntity))
                {
                    device.UpdatePosition();
                }

                _placingTargetPoint = validHit.Position;
                _placingTargetEntity = _scene.Entities.FirstOrDefault(ent => VisualTreeUtils.IsSelfOrChild(validHit.Visual, ent.Visual3D));
            }
            else
            {
                _placingTargetEntity = null;
                _placingTargetPoint = null;
                Vector3D planeNormal = new Vector3D(0, 0, 1);
                var hitPoint = _scene.ViewportController.UnProjectToZPlane(mousePos, 0);

                if (hitPoint.HasValue)
                {
                    _activeEntity.AlignTo(hitPoint.Value, planeNormal);
                    _scene.ConnectionController.UpdateAll();

                    foreach (var device in _scene.Devices.Where(d => d.TargetEntity == _activeEntity))
                    {
                        device.UpdatePosition();
                    }
                }
            }
        }
        #endregion

        #region Validation & Utilities

        /// <summary>
        /// 确认放置/移动操作：将实体注册到场景列表、创建自动连接、重置状态。
        /// Placing 状态还会触发 OnEntityAdded 事件。由 OnMouseDown 左键点击时调用。
        /// </summary>
        private void ConfirmAction()
        {
            if (_activeEntity == null) return;

            if (_currentState == InteractionState.Placing)
            {
                _scene.Entities.Add(_activeEntity);
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
                            _scene.ConnectionController.Add(conn);
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
            OnSelectionChanged?.Invoke(null);
        }

        /// <summary>
        /// 取消当前操作：Placing 状态移除实体，Moving 状态恢复实体状态。
        /// 由 OnMouseDown 右键点击时调用。
        /// </summary>
        private void CancelAction()
        {
            if (_currentState == InteractionState.Placing && _activeEntity != null)
            {
                _scene.HelixViewport.Children.Remove(_activeEntity.Visual3D);
                _activeEntity = null;
            }
            else if (_currentState == InteractionState.Moving && _activeEntity != null)
            {
                _activeEntity.SetHitTestVisible(true);
                HideGimbal();
            }
            _currentState = InteractionState.Idle;
            OnSelectionChanged?.Invoke(_activeEntity);
        }

        /// <summary>
        /// 执行点击命中测试：检查是否点击了万向轮或场景实体，并更新选中状态。
        /// 在 Idle 状态由 OnMouseUp 左键调用。
        /// </summary>
        private void PerformHitTest(Point mousePos)
        {
            if (_gimbal != null)
            {
                var hits = _scene.HelixViewport.Viewport.FindHits(mousePos);
                var gimbalHit = hits?.FirstOrDefault(h => VisualTreeUtils.IsSelfOrChild(h.Visual, _gimbal));
                if (gimbalHit != null) return;
            }

            IVisualEntity? hitEntity = null;
            var hits2 = _scene.HelixViewport.Viewport.FindHits(mousePos);
            if (hits2 != null && hits2.Count > 0)
            {
                var nearest = hits2.OrderBy(h => h.Distance).First();
                foreach (var entity in _scene.Entities)
                {
                    if (VisualTreeUtils.IsSelfOrChild(nearest.Visual, entity.Visual3D))
                    {
                        hitEntity = entity;
                        break;
                    }
                }
            }

            ForceSelect(hitEntity);
        }

        /// <summary>
        /// 在选中实体上显示万向轮操纵器（CombinedManipulator），同时切换实体为线框显示模式。
        /// 被 ForceSelect 在选中实体时调用。
        /// </summary>
        /// <param name="entity">要显示万向轮的实体</param>
        private void ShowGimbal(IVisualEntity entity)
        {
            HideGimbal();
            entity.SetHitTestVisible(false);
            entity.SetDisplayMode(VisualDisplayMode.Wireframe);
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
            _scene.HelixViewport.Children.Add(_gimbal);
        }

        /// <summary>
        /// 隐藏并清理万向轮操纵器，恢复实体的正常显示模式和命中测试。
        /// 被 ForceSelect、DeleteSelected、StartMovingSelected、ConfirmAction 等调用。
        /// </summary>
        private void HideGimbal()
        {
            if (_gimbal != null)
            {
                _gimbal.UnBind();
                _scene.HelixViewport.Children.Remove(_gimbal);
                _gimbal = null;
            }
            if (_activeEntity != null)
            {
                _activeEntity.SetDisplayMode(VisualDisplayMode.Normal);
                _activeEntity.SetHitTestVisible(true);
            }
        }

        /// <summary>
        /// 显示右键上下文菜单，提供 Move/Resize/Connect/Delete 操作。
        /// 在 Idle 状态且有选中实体时由 OnMouseUp 右键调用。
        /// </summary>
        private void ShowContextMenu()
        {
            var contextMenu = new ContextMenu();

            var moveItem = new MenuItem { Header = "Move" };
            moveItem.Click += (s, e) => StartMovingSelected();

            var resizeItem = new MenuItem { Header = "Resize..." };
            resizeItem.Click += (s, e) =>
            {
                if (_activeEntity == null) return;
                _onResizeRequested?.Invoke(_activeEntity);
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

        /// <summary>
        /// 尝试开始拖拽连接线端点球体。遍历所有连接可视化端点进行命中测试。
        /// 命中后进入 DraggingConnectionEndpoint 状态，并将球体变为橙红色。
        /// 在 Idle 状态由 OnMouseDown 左键调用。
        /// </summary>
        /// <param name="mousePos">鼠标在视口中的屏幕坐标</param>
        /// <returns>是否成功开始拖拽</returns>
        private bool TryBeginDragConnectionEndpoint(Point mousePos)
        {
            var hits = _scene.HelixViewport.Viewport.FindHits(mousePos);
            if (hits == null || hits.Count == 0) return false;

            foreach (var h in hits.OrderBy(x => x.Distance))
            {
                foreach (var kv in _scene.ConnectionController.VisualsById)
                {
                    var id = kv.Key;
                    var vis = kv.Value;

                    if (VisualTreeUtils.IsSelfOrChild(h.Visual, vis.EndA))
                    {
                        _dragConnId = id;
                        _dragEndIsA = true;
                        _dragSphere = vis.EndA;
                        vis.EndA.Fill = System.Windows.Media.Brushes.OrangeRed;
                        _currentState = InteractionState.DraggingConnectionEndpoint;
                        return true;
                    }

                    if (VisualTreeUtils.IsSelfOrChild(h.Visual, vis.EndB))
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
