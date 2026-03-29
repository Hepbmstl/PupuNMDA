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
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Media3D;
using HelixToolkit.Wpf;
using NeuronCAD.Visuals.Tabs.Modeling.Visuals;
using NeuronCAD.Visuals.Tabs.Shared;

namespace NeuronCAD.Visuals.Tabs.Modeling
{
    /// <summary>
    /// Interaction states for modeling interactions, tracking the current operation stage of InteractionController.
    /// Held by InteractionController._currentState.
    /// </summary>
    public enum InteractionState
    {
        /// <summary>Idle state: selection and right-click operations available</summary>
        Idle,
        /// <summary>Placing a new entity (follows mouse; left-click to confirm; right-click to cancel)</summary>
        Placing,
        /// <summary>Moving an existing entity (follows mouse; left-click to confirm; right-click to cancel)</summary>
        Moving,
        /// <summary>Dragging a connection endpoint (modifying anchor position)</summary>
        DraggingConnectionEndpoint,
        /// <summary>Selecting connection target (after right-click menu 'Connect', waiting for left-click on target entity)</summary>
        SelectingConnectionTarget
    }

    /// <summary>
    /// Interaction controller specific to Modeling mode, implementing IViewportInteractionHandler.
    /// Responsibilities: entity placement, selection, movement, deletion, connection creation/dragging,
    /// right-click context menus, and gimbal manipulator management.
    /// Created by MainWindow.InitializeControllers; injects SharedSceneState and callback delegates.
    /// </summary>
    public class InteractionController : IViewportInteractionHandler
    {
        /// <summary>Reference to the shared scene state, holding the entity list, connection controller and viewport. Injected via constructor.</summary>
        private readonly SharedSceneState _scene;

        /// <summary>Crosshair and coordinate HUD update callback, points to MainWindow.UpdateCursorInfo. Called by OnMouseMove.</summary>
        private readonly Action<Point, Point3D?> _updateCursorInfo;

        /// <summary>Resize request callback (optional), points to MainWindow.ShowEditPopup. Triggered by right-click menu 'Resize...'.</summary>
        private readonly Action<IVisualEntity>? _onResizeRequested;

        /// <summary>Current interaction state. Modified by event handlers.</summary>
        private InteractionState _currentState = InteractionState.Idle;

        /// <summary>The currently active entity (being placed/moved/selected).</summary>
        private IVisualEntity? _activeEntity;

        /// <summary>Screen position when mouse was pressed; used to detect dragging (distinguish click from viewport rotate).</summary>
        private Point _mouseDownPos;

        /// <summary>Flag indicating whether the mouse is dragging the viewport (rotating/panning); if true, hit tests are skipped on MouseUp.</summary>
        private bool _isDraggingViewport = false;

        /// <summary>Flag to skip the next hit test. Set to true after ConfirmAction to prevent confirmation click from also selecting.</summary>
        private bool _suppressNextHitTest = false;

        /// <summary>Reference to the HelixToolkit gimbal manipulator, used to show translation/rotation handles on selected entities. Managed by ShowGimbal/HideGimbal.</summary>
        private CombinedManipulator _gimbal;

        /// <summary>Target entity hit by mouse hover during placing mode (used for auto-connecting). Updated by UpdateObjectPosition.</summary>
        private IVisualEntity? _placingTargetEntity;

        /// <summary>Target surface point hit by mouse hover during placing mode (used to compute connection anchors). Updated by UpdateObjectPosition.</summary>
        private Point3D? _placingTargetPoint;

        /// <summary>ID of the connection currently being dragged. Used in DraggingConnectionEndpoint state. Set by TryBeginDragConnectionEndpoint.</summary>
        private string? _dragConnId;

        /// <summary>Flag indicating whether the dragged end is endpoint A (true) or endpoint B (false). Set by TryBeginDragConnectionEndpoint.</summary>
        private bool _dragEndIsA;

        /// <summary>Reference to the source entity for the right-click 'Connect' action. Used in SelectingConnectionTarget state.</summary>
        private IVisualEntity? _connectSourceEntity;

        /// <summary>Reference to the connection endpoint sphere being dragged, used to temporarily change color to orange-red. Set by TryBeginDragConnectionEndpoint.</summary>
        private SphereVisual3D? _dragSphere;

        /// <summary>Entity added event, fired after placement confirmation. Subscribed by PropertiesPanelController.HandleEntityAdded to create property panel cards.</summary>
        public event Action<IVisualEntity> OnEntityAdded;

        /// <summary>Entity removed event, fired after DeleteSelected. Subscribed by PropertiesPanelController.HandleEntityRemoved to remove property panel cards.</summary>
        public event Action<IVisualEntity> OnEntityRemoved;

        /// <summary>Selection changed event, fired on select/deselect. Subscribed by PropertiesPanelController.HandleSelectionChanged to highlight/collapse panel cards.</summary>
        public event Action<IVisualEntity?> OnSelectionChanged;

        /// <summary>
        /// Constructor. Called by MainWindow.InitializeControllers; injects shared scene state and callback delegates.
        /// </summary>
        /// <param name="scene">Shared scene state</param>
        /// <param name="updateCursorInfo">Crosshair/HUD update callback</param>
        /// <param name="onResizeRequested">Resize request callback (optional)</param>
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
        /// Begin placing a new entity: add the entity to the viewport and enter the Placing state.
        /// Called by MainWindow.OnAddSomaClick/OnAddAxonClick/OnAddDendClick.
        /// </summary>
        /// <param name="newEntity">The new entity to place</param>
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
        /// Delete the currently selected entity and its visual object, and trigger OnEntityRemoved and OnSelectionChanged.
        /// Invoked by the right-click context menu 'Delete'.
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
        /// Enter moving mode for the currently selected entity (follow mouse), hide the gimbal and disable hit testing.
        /// Invoked by the right-click context menu 'Move'.
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
        /// Force select or deselect an entity (clear previous selection, set new selection, show gimbal, fire events).
        /// Called by PerformHitTest (on click hit) and PropertiesPanelController (on panel expand).
        /// </summary>
        /// <param name="entity">Entity to select, or null to deselect</param>
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
        /// Deactivate modeling interactions: cancel the current action and clear selection.
        /// Called by MainWindow.SwitchTab when switching tabs.
        /// </summary>
        public void Deactivate()
        {
            CancelAction();
            ForceSelect(null);
        }

        /// <summary>
        /// Notify external callers (properties panel, etc.) that an entity was loaded (restored from file), triggering OnEntityAdded.
        /// Called by SaveLoadManager.ApplyToScene.
        /// </summary>
        public void NotifyEntityLoaded(IVisualEntity entity)
        {
            OnEntityAdded?.Invoke(entity);
        }

        /// <summary>
        /// Notify external callers that an entity was removed, triggering OnEntityRemoved.
        /// Called by MainWindow.ClearScene.
        /// </summary>
        public void NotifyEntityRemoved(IVisualEntity entity)
        {
            OnEntityRemoved?.Invoke(entity);
        }
        #endregion

        #region Input Handlers (IViewportInteractionHandler)

        /// <summary>
        /// Mouse down event handler. In Idle state attempts to begin dragging a connection endpoint; in Placing/Moving states left-click confirms and right-click cancels.
        /// Routed from MainWindow.OnViewportMouseDown.
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
        /// Mouse move event handler. Updates crosshair HUD; depending on current state updates dragging endpoint, entity position, or viewport rotation detection.
        /// Routed from MainWindow.OnViewportMouseMove.
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
        /// Mouse up event handler. In DraggingConnectionEndpoint state completes endpoint drag; in SelectingConnectionTarget state selects a connection target or cancels; in Idle state left-click performs hit test selection and right-click shows context menu.
        /// Routed from MainWindow.OnViewportMouseUp.
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

        /// <summary>Mouse wheel event handler (no custom logic currently; handled by HelixViewport3D built-in zoom).</summary>
        public void OnMouseWheel(object sender, MouseWheelEventArgs e) { }
        #endregion

        #region Core Logic & Hit Testing

        /// <summary>
        /// Perform a ray hit test across the scene entity list and return the nearest hit entity.
        /// Used by OnMouseUp (SelectingConnectionTarget state) and PerformHitTest.
        /// </summary>
        /// <param name="mousePos">Mouse screen coordinates in the viewport</param>
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
        /// Perform a ray hit test on the specified entity and return the hit point in world coordinates.
        /// Used by OnMouseUp to obtain a precise hit point when creating a connection.
        /// </summary>
        /// <param name="mousePos">Mouse screen coordinates in the viewport</param>
        /// <param name="entity">Target entity</param>
        /// <returns>Hit point in world coordinates, or null</returns>
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
        /// Update crosshair position and coordinate HUD display, excluding the currently active entity to avoid self-hits.
        /// Called on every mouse move in OnMouseMove.
        /// </summary>
        private void UpdateCrosshair(Point mousePos)
        {
            var hits = _scene.HelixViewport.Viewport.FindHits(mousePos);
            var validHit = hits?.FirstOrDefault(h => _activeEntity == null || !VisualTreeUtils.IsSelfOrChild(h.Visual, _activeEntity.Visual3D));
            Point3D? worldPos = validHit?.Position ?? _scene.ViewportController.UnProjectToZPlane(mousePos, 0);
            _updateCursorInfo(mousePos, worldPos);
        }

        /// <summary>
        /// Update the dragging connection endpoint position by converting mouse hits to a new anchor and refreshing connection visuals.
        /// Called during OnMouseMove while in DraggingConnectionEndpoint state.
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
        /// Update the position of the entity being placed/moved. Prefer snapping to other entity surfaces; otherwise project to the Z=0 plane.
        /// Also update connections and attached devices, and record hover targets for auto-creating connections.
        /// Called during OnMouseMove while in Placing/Moving states.
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
        /// Confirm placement/move action: register the entity to the scene list, create auto-connections, and reset state.
        /// Placing state also triggers the OnEntityAdded event. Called on left-click in OnMouseDown.
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
        /// Cancel the current action: remove the entity if in Placing state, restore entity state if in Moving state.
        /// Called on right-click in OnMouseDown.
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
        /// Perform a click hit test: check whether the gimbal or a scene entity was clicked, and update selection accordingly.
        /// Called on left mouse up in Idle state.
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
        /// Show the gimbal manipulator (CombinedManipulator) on the selected entity and switch the entity to wireframe display mode.
        /// Called by ForceSelect when an entity is selected.
        /// </summary>
        /// <param name="entity">Entity to show the gimbal for</param>
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
        /// Hide and clean up the gimbal manipulator, restoring the entity's normal display mode and hit testing.
        /// Called by ForceSelect, DeleteSelected, StartMovingSelected, ConfirmAction, etc.
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
        /// Show the right-click context menu offering Move/Resize/Connect/Delete operations.
        /// Called on right mouse up in Idle state when an entity is selected.
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
        /// Try to begin dragging a connection endpoint sphere. Iterate over all connection visual endpoints to perform hit tests.
        /// On hit, enter the DraggingConnectionEndpoint state and set the sphere color to orange-red.
        /// Called on left mouse down in Idle state.
        /// </summary>
        /// <param name="mousePos">Mouse screen coordinates in the viewport</param>
        /// <returns>Whether the drag was successfully started</returns>
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
