using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media.Media3D;
using HelixToolkit.Wpf;
using NeuronCAD.Visuals.Tabs.Shared;
using NeuronCAD.Visuals.Tabs.Modeling.Visuals;

namespace NeuronCAD.Visuals.Tabs.Simulation
{
    /// <summary>
    /// 仿真交互状态枚举，追踪 SimulationInteractionController 当前所处的操作阶段。
    /// </summary>
    public enum SimulationState
    {
        /// <summary>空闲状态，可点击拖拽或右键删除设备</summary>
        Idle,
        /// <summary>放置刺激设备中（跟随鼠标吸附到实体表面）</summary>
        PlacingStimulation,
        /// <summary>放置探针设备中（跟随鼠标吸附到实体表面）</summary>
        PlacingProbe,
        /// <summary>拖拽已放置设备中（沿目标实体表面滑动）</summary>
        DraggingDevice
    }

    /// <summary>
    /// 仿真模式专属交互控制器，实现 IViewportInteractionHandler 接口。
    /// 职责：刺激/探针设备的放置、拖拽、删除。
    /// 由 MainWindow.InitializeControllers 创建，注入 SharedSceneState 和回调。
    /// </summary>
    public class SimulationInteractionController : IViewportInteractionHandler
    {
        /// <summary>共享场景状态引用。由构造函数注入。</summary>
        private readonly SharedSceneState _scene;

        /// <summary>准星和坐标 HUD 更新回调，指向 MainWindow.UpdateCursorInfo。</summary>
        private readonly Action<Point, Point3D?> _updateCursorInfo;

        /// <summary>当前仿真交互状态。</summary>
        private SimulationState _currentState = SimulationState.Idle;

        /// <summary>正在放置的设备引用（尚未确认提交）。</summary>
        private IAttachedDevice? _placingDevice;

        /// <summary>正在拖拽的设备引用。</summary>
        private IAttachedDevice? _dragDevice;

        /// <summary>鼠标按下时的屏幕位置，用于判断视口拖拽。</summary>
        private Point _mouseDownPos;

        /// <summary>标记鼠标是否正在拖拽视口。</summary>
        private bool _isDraggingViewport = false;

        /// <summary>设备添加事件，被 SimulationPanelController.HandleDeviceAdded 订阅。</summary>
        public event Action<IAttachedDevice>? OnDeviceAdded;

        /// <summary>设备删除事件，被 SimulationPanelController.HandleDeviceRemoved 订阅。</summary>
        public event Action<IAttachedDevice>? OnDeviceRemoved;

        /// <summary>
        /// 构造函数。由 MainWindow.InitializeControllers 调用。
        /// </summary>
        /// <param name="scene">共享场景状态</param>
        /// <param name="updateCursorInfo">准星/HUD 更新回调</param>
        public SimulationInteractionController(
            SharedSceneState scene,
            Action<Point, Point3D?> updateCursorInfo)
        {
            _scene = scene;
            _updateCursorInfo = updateCursorInfo;
        }

        #region Public API

        /// <summary>
        /// 启动放置设备操作，进入 PlacingStimulation 或 PlacingProbe 状态。
        /// 被 MainWindow 工具栏按钮 "Add Stimulation"/"Add Probe" 调用。
        /// </summary>
        /// <param name="type">设备类型（刺激或探针）</param>
        public void StartPlacingDevice(DeviceType type)
        {
            if (_currentState != SimulationState.Idle) return;
            _currentState = type == DeviceType.Stimulation
                ? SimulationState.PlacingStimulation
                : SimulationState.PlacingProbe;
            _placingDevice = null;
        }

        /// <summary>
        /// 停用仿真交互：取消当前操作，移除未提交的设备可视化对象。
        /// 被 MainWindow.SwitchTab 在切换标签页时调用。
        /// </summary>
        public void Deactivate()
        {
            if (_placingDevice != null)
            {
                _scene.HelixViewport.Children.Remove(_placingDevice.Visual3D);
                _placingDevice = null;
            }
            _dragDevice = null;
            _currentState = SimulationState.Idle;
        }

        #endregion

        #region Input Handlers (IViewportInteractionHandler)

        /// <summary>
        /// 鼠标按下事件处理。空闲状态：左键拖拽设备，右键删除设备；放置状态：左键确认，右键取消。
        /// 由 MainWindow.OnViewportMouseDown 路由调用。
        /// </summary>
        public void OnMouseDown(object sender, MouseButtonEventArgs e)
        {
            _mouseDownPos = e.GetPosition(_scene.HelixViewport);
            _isDraggingViewport = false;

            // ===== 空闲模式：点击设备 =====
            if (_currentState == SimulationState.Idle)
            {
                var hitDevice = HitTestDevice(_mouseDownPos);

                if (e.ChangedButton == MouseButton.Left && hitDevice != null)
                {
                    _dragDevice = hitDevice;
                    _currentState = SimulationState.DraggingDevice;
                    e.Handled = true;
                }
                else if (e.ChangedButton == MouseButton.Right && hitDevice != null)
                {
                    _scene.Devices.Remove(hitDevice);
                    _scene.HelixViewport.Children.Remove(hitDevice.Visual3D);
                    OnDeviceRemoved?.Invoke(hitDevice);
                    e.Handled = true;
                }
                return;
            }

            // ===== 放置模式：确认/取消 =====
            if (_currentState == SimulationState.PlacingStimulation || _currentState == SimulationState.PlacingProbe)
            {
                e.Handled = true;
                if (e.ChangedButton == MouseButton.Left)
                {
                    if (_placingDevice != null)
                    {
                        _scene.Devices.Add(_placingDevice);
                        OnDeviceAdded?.Invoke(_placingDevice);
                        _placingDevice = null;
                    }
                    _currentState = SimulationState.Idle;
                }
                else if (e.ChangedButton == MouseButton.Right)
                {
                    if (_placingDevice != null)
                    {
                        _scene.HelixViewport.Children.Remove(_placingDevice.Visual3D);
                        _placingDevice = null;
                    }
                    _currentState = SimulationState.Idle;
                }
                return;
            }
        }

        /// <summary>
        /// 鼠标移动事件处理。更新准星 HUD；拖拽状态更新设备位置；放置状态更新待放置设备的吸附点。
        /// 由 MainWindow.OnViewportMouseMove 路由调用。
        /// </summary>
        public void OnMouseMove(object sender, MouseEventArgs e)
        {
            var mousePos = e.GetPosition(_scene.HelixViewport);
            UpdateCrosshair(mousePos);

            if (_currentState == SimulationState.DraggingDevice && _dragDevice != null)
            {
                UpdateDraggingDevice(mousePos);
                return;
            }

            if (_currentState == SimulationState.PlacingStimulation || _currentState == SimulationState.PlacingProbe)
            {
                UpdatePlacingDevice(mousePos);
                return;
            }

            if (_currentState == SimulationState.Idle)
            {
                if (e.LeftButton == MouseButtonState.Pressed || e.RightButton == MouseButtonState.Pressed)
                {
                    if ((mousePos - _mouseDownPos).Length > 2) _isDraggingViewport = true;
                }
            }
        }

        /// <summary>
        /// 鼠标释放事件处理。DraggingDevice 状态下释放拖拽，回到 Idle。
        /// 由 MainWindow.OnViewportMouseUp 路由调用。
        /// </summary>
        public void OnMouseUp(object sender, MouseButtonEventArgs e)
        {
            if (_currentState == SimulationState.DraggingDevice)
            {
                _dragDevice = null;
                _currentState = SimulationState.Idle;
                e.Handled = true;
                return;
            }
        }

        /// <summary>鼠标滚轮事件处理（当前无自定义逻辑）。</summary>
        public void OnMouseWheel(object sender, MouseWheelEventArgs e) { }

        #endregion

        #region Hit Testing

        /// <summary>
        /// 命中测试：查找鼠标位置下的仿真设备。
        /// 被 OnMouseDown 在 Idle 状态调用，用于判断拖拽或删除目标。
        /// </summary>
        /// <param name="mousePos">鼠标屏幕坐标</param>
        /// <returns>命中的设备，或 null</returns>
        private IAttachedDevice? HitTestDevice(Point mousePos)
        {
            var hits = _scene.HelixViewport.Viewport.FindHits(mousePos);
            if (hits == null || hits.Count == 0) return null;

            var nearest = hits.OrderBy(h => h.Distance).FirstOrDefault();
            if (nearest != null)
            {
                foreach (var device in _scene.Devices)
                {
                    if (VisualTreeUtils.IsSelfOrChild(nearest.Visual, device.Visual3D))
                        return device;
                }
            }
            return null;
        }

        /// <summary>
        /// 命中测试：查找鼠标位置下的场景实体（用于设备放置时确定吸附目标）。
        /// 被 UpdatePlacingDevice 和 UpdateDraggingDevice 间接使用。
        /// </summary>
        /// <param name="mousePos">鼠标屏幕坐标</param>
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
        /// 被 UpdatePlacingDevice 调用，用于获取精确的表面吸附点。
        /// </summary>
        /// <param name="mousePos">鼠标屏幕坐标</param>
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
        /// 更新准星和 HUD 显示。在 OnMouseMove 中每次鼠标移动时调用。
        /// </summary>
        private void UpdateCrosshair(Point mousePos)
        {
            var hits = _scene.HelixViewport.Viewport.FindHits(mousePos);
            Point3D? worldPos = hits?.FirstOrDefault()?.Position
                ?? _scene.ViewportController.UnProjectToZPlane(mousePos, 0);
            _updateCursorInfo(mousePos, worldPos);
        }

        /// <summary>
        /// 更新放置中设备的位置：将设备吸附到鼠标悬停的实体表面锚点。
        /// 若目标实体变更，会重新创建设备实例。
        /// 在 PlacingStimulation/PlacingProbe 状态下由 OnMouseMove 调用。
        /// </summary>
        private void UpdatePlacingDevice(Point mousePos)
        {
            var targetEntity = HitTestEntity(mousePos);
            if (targetEntity is IAnchoredEntity anchoredTarget)
            {
                var hitPoint = HitTestPointOnEntity(mousePos, targetEntity) ?? targetEntity.CenterPosition;

                if (anchoredTarget.TryWorldPointToAnchor(hitPoint, out var anchor))
                {
                    if (_placingDevice == null || _placingDevice.TargetEntity != targetEntity)
                    {
                        if (_placingDevice != null)
                            _scene.HelixViewport.Children.Remove(_placingDevice.Visual3D);

                        if (_currentState == SimulationState.PlacingStimulation)
                            _placingDevice = new StimulationDevice(targetEntity, anchor);
                        else
                            _placingDevice = new ProbeDevice(targetEntity, anchor);

                        _scene.HelixViewport.Children.Add(_placingDevice.Visual3D);
                    }
                    else
                    {
                        _placingDevice.Anchor = anchor;
                        _placingDevice.UpdatePosition();
                    }
                }
            }
        }

        /// <summary>
        /// 更新拖拽中设备的位置：沿目标实体表面滑动锚点。
        /// 在 DraggingDevice 状态下由 OnMouseMove 调用。
        /// </summary>
        private void UpdateDraggingDevice(Point mousePos)
        {
            if (_dragDevice == null) return;

            var hits = _scene.HelixViewport.Viewport.FindHits(mousePos);
            var hit = hits?.OrderBy(h => h.Distance)
                .FirstOrDefault(h => VisualTreeUtils.IsSelfOrChild(h.Visual, _dragDevice.TargetEntity.Visual3D));

            if (hit != null && _dragDevice.TargetEntity is IAnchoredEntity anchored)
            {
                if (anchored.TryWorldPointToAnchor(hit.Position, out var anchor))
                {
                    _dragDevice.Anchor = anchor;
                    _dragDevice.UpdatePosition();
                }
            }
        }

        #endregion
    }
}
