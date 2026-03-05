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
    public enum SimulationState
    {
        Idle,
        PlacingStimulation,
        PlacingProbe,
        DraggingDevice
    }

    /// <summary>
    /// 仿真模式专属交互控制器
    /// 职责：刺激/探针设备的放置、滑动、删除
    /// </summary>
    public class SimulationInteractionController : IViewportInteractionHandler
    {
        private readonly SharedSceneState _scene;
        private readonly Action<Point, Point3D?> _updateCursorInfo;

        private SimulationState _currentState = SimulationState.Idle;
        private IAttachedDevice? _placingDevice;
        private IAttachedDevice? _dragDevice;

        private Point _mouseDownPos;
        private bool _isDraggingViewport = false;

        // ====== 状态变更事件总线 ======
        public event Action<IAttachedDevice>? OnDeviceAdded;
        public event Action<IAttachedDevice>? OnDeviceRemoved;

        public SimulationInteractionController(
            SharedSceneState scene,
            Action<Point, Point3D?> updateCursorInfo)
        {
            _scene = scene;
            _updateCursorInfo = updateCursorInfo;
        }

        #region Public API

        /// <summary>
        /// 启动添加探针或刺激的操作
        /// </summary>
        public void StartPlacingDevice(DeviceType type)
        {
            if (_currentState != SimulationState.Idle) return;
            _currentState = type == DeviceType.Stimulation
                ? SimulationState.PlacingStimulation
                : SimulationState.PlacingProbe;
            _placingDevice = null;
        }

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

        public void OnMouseWheel(object sender, MouseWheelEventArgs e) { }

        #endregion

        #region Hit Testing

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

        private void UpdateCrosshair(Point mousePos)
        {
            var hits = _scene.HelixViewport.Viewport.FindHits(mousePos);
            Point3D? worldPos = hits?.FirstOrDefault()?.Position
                ?? _scene.ViewportController.UnProjectToZPlane(mousePos, 0);
            _updateCursorInfo(mousePos, worldPos);
        }

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
