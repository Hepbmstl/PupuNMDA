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
using System.Windows.Media;
using System.Windows.Media.Media3D;

namespace NeuronCAD.Visuals.Tabs.Modeling.Visuals
{
    /// <summary>
    /// Display modes for visual entities.
    /// Used by IVisualEntity.SetDisplayMode and VisualEntityBase.SetDisplayMode.
    /// </summary>
    public enum VisualDisplayMode
    {
        /// <summary>Normal rendering mode: show material and ion channel scatter points</summary>
        Normal,
        /// <summary>Wireframe mode used during selection/editing to show a transparent frame and the gimbal manipulator</summary>
        Wireframe
    }

    /// <summary>
    /// Interface for visual entities, defining the common behavior contract for 3D modeling objects (Soma, Axon, Dend).
    /// Implementations: VisualEntityBase (abstract) → SomaVisual, AxonVisual, DendVisual.
    /// Callers: InteractionController (selection/placement/movement), PropertiesPanelController (properties panel),
    /// MainWindow (edit dialogs), ConnectionController (connection position calculations), etc.
    /// </summary>
    public interface IVisualEntity
    {
        /// <summary>Unique entity identifier (GUID). Used for panel UI node indexing and connection mapping.</summary>
        string Id { get; }

        /// <summary>Root ModelVisual3D for HelixToolkit, containing the mesh model and ion-channel scatter children.</summary>
        ModelVisual3D Visual3D { get; }

        /// <summary>Whether the entity is selected. Set by InteractionController.ForceSelect.</summary>
        bool IsSelected { get; }

        /// <summary>The entity's center position in world coordinates. Used for connection anchor fallback and device normal calculations.</summary>
        Point3D CenterPosition { get; }
        /// <summary>
        /// Dictionary of ion channels bound to the entity. Key is channel name, Value is ChannelProperty.
        /// Added/removed by the channel selector popup in PropertiesPanelController.
        /// </summary>
        Dictionary<string, ChannelProperty> Channels { get; }

        /// <summary>Specific membrane capacitance (µF/cm²), standard 1.0. Used for compartmental simulation calculations.</summary>
        double Cm { get; set; }

        /// <summary>Axial resistivity (Ω·cm), typical values 35.4–100. Used for compartmental simulation calculations.</summary>
        double Ra { get; set; }

        /// <summary>Number of compartments this entity was split into after simulation. 0 if not simulated.</summary>
        int CompartmentCount { get; set; }

        /// <summary>List of global compartment IDs owned by this entity after simulation. Empty if not simulated.</summary>
        List<int> CompartmentIds { get; set; }

        /// <summary>Current entity color, read and displayed by the PropertiesPanelController panel.</summary>
        Color CurrentColor { get; }

        /// <summary>Set the entity's selected state, toggling material between selected and default colors. Called by InteractionController.ForceSelect.</summary>
        void SetSelected(bool isSelected);

        /// <summary>Set the entity color. Called by the color edit textbox LostFocus callback in PropertiesPanelController.</summary>
        void SetColor(Color color);

        /// <summary>Set entity opacity (0.0–1.0). Reserved for translucent display.</summary>
        void SetOpacity(double opacity);
        /// <summary>
        /// Align the entity to the specified world coordinate and normal direction.
        /// Called by InteractionController.UpdateObjectPosition during placement/movement to snap entities to hit surfaces.
        /// </summary>
        void AlignTo(Point3D position, Vector3D normal);

        /// <summary>Set whether the entity participates in hit testing. Disabled during placement/movement to avoid self-hits. Called by InteractionController.</summary>
        void SetHitTestVisible(bool isVisible);
        /// <summary>
        /// Switch display mode (Normal/Wireframe).
        /// Called by InteractionController.ShowGimbal (enter wireframe) and HideGimbal (restore normal mode).
        /// </summary>
        void SetDisplayMode(VisualDisplayMode mode);

        /// <summary>Get a string describing the entity dimensions. Reserved for status bar or tooltips.</summary>
        string GetDimensionInfo();
        /// <summary>
        /// Refresh ion channel scatter visualizations. Rebuilds the surface scatter distribution based on the Channels dictionary.
        /// Called after channels are added/removed via PropertiesPanelController.
        /// </summary>
        void UpdateChannelVisuals();
    }

    /// <summary>
    /// Interface for anchored entities, defining behavior that supports precise anchor positioning on entity surfaces.
    /// Implementations: SomaVisual, AxonVisual (and DendVisual subclasses).
    /// Callers: InteractionController (compute anchors when creating connections), ConnectionController (update connection endpoints),
    /// SimulationInteractionController (compute docking anchors when placing devices), AttachedDeviceBase (device position updates).
    /// </summary>
    public interface IAnchoredEntity
    {
        /// <summary>
        /// Convert a world-space point to an entity surface anchor reference.
        /// Called by InteractionController.ConfirmAction (when creating connections) and SimulationInteractionController.UpdatePlacingDevice.
        /// </summary>
        /// <param name="worldPoint">Point in world coordinates</param>
        /// <param name="anchor">Output anchor reference</param>
        /// <returns>True if conversion succeeded</returns>
        bool TryWorldPointToAnchor(Point3D worldPoint, out AnchorRef anchor);

        /// <summary>
        /// Convert an anchor reference back to a world-space point.
        /// Called by ConnectionController.Update (refresh connection endpoints) and AttachedDeviceBase.UpdatePosition (update device arrow position).
        /// </summary>
        /// <param name="anchor">Anchor reference</param>
        /// <param name="worldPoint">Output world coordinate</param>
        /// <returns>True if conversion succeeded</returns>
        bool TryAnchorToWorldPoint(AnchorRef anchor, out Point3D worldPoint);
    }
}