/*
 * Copyright 2026 [Hepbmstl Hepupu]
 *
 * Pupu NMDA / NeuronCAD
 * A Multi-Compartment Neuron Modeling and Dynamics Analysis Platform
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
using System.Windows;
using System.Windows.Input;
using System.Windows.Media.Media3D;
using HelixToolkit.Wpf;
using NeuronCAD.Visuals.Tabs.Shared;
using NeuronCAD.Visuals.Tabs.Modeling.Visuals;

namespace NeuronCAD.Visuals.Tabs.Simulation
{
    /// <summary>
    /// Simulation interaction state enum, tracking the current operation stage of SimulationInteractionController.
    /// </summary>
    public enum SimulationState
    {
        /// <summary>Idle state: devices can be clicked, dragged, or right-click deleted</summary>
        Idle,
        /// <summary>Placing a stimulation device (snaps to entity surface under mouse)</summary>
        PlacingStimulation,
        /// <summary>Placing a probe device (snaps to entity surface under mouse)</summary>
        PlacingProbe,
        /// <summary>Placing a voltage clamp device (snaps to entity surface under mouse)</summary>
        PlacingVoltageClamp,
        /// <summary>Dragging a placed device (slide along target entity surface)</summary>
        DraggingDevice
    }

    /// <summary>
    /// Interaction controller specific to Simulation mode, implementing IViewportInteractionHandler.
    /// Responsibilities: placing, dragging, and removing stimulation/probe devices.
    /// Created by MainWindow.InitializeControllers and injected with SharedSceneState and callbacks.
    /// </summary>
    public class SimulationInteractionController : IViewportInteractionHandler
    {
        /// <summary>Reference to the shared scene state. Injected via constructor.</summary>
        private readonly SharedSceneState _scene;

        /// <summary>Crosshair and coordinate HUD update callback, points to MainWindow.UpdateCursorInfo.</summary>
        private readonly Action<Point, Point3D?> _updateCursorInfo;

        /// <summary>Current simulation interaction state.</summary>
        private SimulationState _currentState = SimulationState.Idle;

        /// <summary>Reference to the device currently being placed (not yet committed).</summary>
        private IAttachedDevice? _placingDevice;

        /// <summary>Reference to the device currently being dragged.</summary>
        private IAttachedDevice? _dragDevice;

        /// <summary>Screen position when mouse was pressed, used to detect viewport dragging.</summary>
        private Point _mouseDownPos;

        /// <summary>Flag indicating whether the mouse is currently dragging the viewport.</summary>
        private bool _isDraggingViewport = false;

        /// <summary>Device added event, subscribed by SimulationPanelController.HandleDeviceAdded.</summary>
        public event Action<IAttachedDevice>? OnDeviceAdded;

        /// <summary>Device removed event, subscribed by SimulationPanelController.HandleDeviceRemoved.</summary>
        public event Action<IAttachedDevice>? OnDeviceRemoved;

        /// <summary>Device selection changed event, subscribed by SimulationPanelController.HandleDeviceSelectionChanged.</summary>
        public event Action<IAttachedDevice?>? OnDeviceSelectionChanged;

        /// <summary>Currently selected device.</summary>
        private IAttachedDevice? _selectedDevice;

        /// <summary>
        /// Constructor. Called by MainWindow.InitializeControllers.
        /// </summary>
        /// <param name="scene">Shared scene state</param>
        /// <param name="updateCursorInfo">Crosshair/HUD update callback</param>
        public SimulationInteractionController(
            SharedSceneState scene,
            Action<Point, Point3D?> updateCursorInfo)
        {
            _scene = scene;
            _updateCursorInfo = updateCursorInfo;
        }

        #region Public API

        /// <summary>
        /// Begin placing a device, entering PlacingStimulation or PlacingProbe state.
        /// Invoked by main window toolbar buttons "Add Stimulation"/"Add Probe".
        /// </summary>
        /// <param name="type">Device type (stimulation, probe, or voltage clamp)</param>
        public void StartPlacingDevice(DeviceType type)
        {
            if (_currentState != SimulationState.Idle) return;
            switch (type)
            {
                case DeviceType.Stimulation:
                    _currentState = SimulationState.PlacingStimulation;
                    break;
                case DeviceType.VoltageClamp:
                    _currentState = SimulationState.PlacingVoltageClamp;
                    break;
                default:
                    _currentState = SimulationState.PlacingProbe;
                    break;
            }
            _placingDevice = null;
        }

        /// <summary>
        /// Notify external listeners (panels etc.) that a device was loaded (restored from file) and trigger OnDeviceAdded.
        /// Called by SaveLoadManager.ApplyToScene.
        /// </summary>
        public void NotifyDeviceLoaded(IAttachedDevice device)
        {
            OnDeviceAdded?.Invoke(device);
        }

        /// <summary>
        /// Deactivate simulation interaction: cancel current operation and remove any uncommitted device visuals.
        /// Called by MainWindow.SwitchTab when switching tabs.
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
            SelectDevice(null);
        }

        /// <summary>
        /// Force-select the specified device (or deselect), triggering OnDeviceSelectionChanged.
        /// Called by panel expand and viewport clicks.
        /// </summary>
        public void SelectDevice(IAttachedDevice? device)
        {
            if (_selectedDevice == device) return;
            _selectedDevice = device;
            OnDeviceSelectionChanged?.Invoke(_selectedDevice);
        }

        #endregion

        #region Input Handlers (IViewportInteractionHandler)

        /// <summary>
        /// Mouse down event handler. Idle: left-drag device, right-delete device; Placing: left-confirm, right-cancel.
        /// Routed by MainWindow.OnViewportMouseDown.
        /// </summary>
        public void OnMouseDown(object sender, MouseButtonEventArgs e)
        {
            _mouseDownPos = e.GetPosition(_scene.HelixViewport);
            _isDraggingViewport = false;

            // ===== Idle mode: device click handling =====
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

            // ===== Placing mode: confirm/cancel =====
            if (_currentState == SimulationState.PlacingStimulation || _currentState == SimulationState.PlacingProbe || _currentState == SimulationState.PlacingVoltageClamp)
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
        /// Mouse move handler. Updates crosshair HUD; updates device position while dragging; updates snap point while placing.
        /// Routed by MainWindow.OnViewportMouseMove.
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

            if (_currentState == SimulationState.PlacingStimulation || _currentState == SimulationState.PlacingProbe || _currentState == SimulationState.PlacingVoltageClamp)
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
        /// Mouse up handler. Releases dragging in DraggingDevice state and returns to Idle.
        /// In Idle state, a left click (non-drag) selects a device.
        /// Routed by MainWindow.OnViewportMouseUp.
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

            if (_currentState == SimulationState.Idle && e.ChangedButton == MouseButton.Left && !_isDraggingViewport)
            {
                var mousePos = e.GetPosition(_scene.HelixViewport);
                var hitDevice = HitTestDevice(mousePos);
                SelectDevice(hitDevice);
            }
        }

        /// <summary>Mouse wheel handler (no custom logic currently).</summary>
        public void OnMouseWheel(object sender, MouseWheelEventArgs e) { }

        #endregion

        #region Hit Testing

        /// <summary>
        /// Hit test: find the simulation device under the mouse position.
        /// Called from OnMouseDown in Idle state to determine drag or delete targets.
        /// </summary>
        /// <param name="mousePos">Mouse screen coordinates</param>
        /// <returns>The hit device, or null</returns>
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
        /// Hit test: find the scene entity under the mouse position (used to determine snap targets when placing devices).
        /// Indirectly used by UpdatePlacingDevice and UpdateDraggingDevice.
        /// </summary>
        /// <param name="mousePos">Mouse screen coordinates</param>
        /// <returns>The hit entity, or null</returns>
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
        /// Perform a ray hit test on the given entity and return the world coordinate of the hit point.
        /// Called by UpdatePlacingDevice to obtain an accurate surface snap point.
        /// </summary>
        /// <param name="mousePos">Mouse screen coordinates</param>
        /// <param name="entity">Target entity</param>
        /// <returns>Hit point world coordinate, or null</returns>
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
        /// Update crosshair and HUD display. Called on every mouse move in OnMouseMove.
        /// </summary>
        private void UpdateCrosshair(Point mousePos)
        {
            var hits = _scene.HelixViewport.Viewport.FindHits(mousePos);
            Point3D? worldPos = hits?.FirstOrDefault()?.Position
                ?? _scene.ViewportController.UnProjectToZPlane(mousePos, 0);
            _updateCursorInfo(mousePos, worldPos);
        }

        /// <summary>
        /// Update the position of the device being placed: snap the device to the surface anchor under the mouse.
        /// If the target entity changes, a new device instance is created.
        /// Called from OnMouseMove when in PlacingStimulation/PlacingProbe/PlacingVoltageClamp state.
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
                        else if (_currentState == SimulationState.PlacingVoltageClamp)
                            _placingDevice = new VoltageClampDevice(targetEntity, anchor);
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
        /// Update the position of the device being dragged: slide the anchor point along the target entity surface.
        /// Called from OnMouseMove when in DraggingDevice state.
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
