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
 *
 * Scientific and Algorithmic Foundations:
 * This software's biophysical organization and core numerical methods are 
 * fundamentally informed by the following works:
 * * 1. Destexhe, A., Neubig, M., Ulrich, D., & Huguenard, J. (1998). 
 * Dendritic Low-Threshold Calcium Currents in Thalamic Relay Cells. 
 * The Journal of Neuroscience, 18(10), 3574-3588.
 * * 2. Hines, M. (1984). Efficient computation of branched nerve equations. 
 * International Journal of Bio-Medical Computing, 15(1), 69-76.
 *
 */

using System;
using System.Collections.Generic;
using System.Windows.Media;
using System.Windows.Media.Media3D;
using HelixToolkit.Wpf;

namespace NeuronCAD.Visuals.Tabs.Modeling.Visuals
{
    /// <summary>
    /// Enumeration of simulation device types, distinguishing stimulation, probes, and voltage clamps.
    /// Used by SimulationInteractionController.StartPlacingDevice and IAttachedDevice.Type.
    /// </summary>
    public enum DeviceType
    {
        /// <summary>Stimulation device (yellow arrow).</summary>
        Stimulation,
        /// <summary>Probe device (cyan arrow).</summary>
        Probe,
        /// <summary>Voltage clamp device (red arrow).</summary>
        VoltageClamp
    }

    /// <summary>
    /// Interface definition for attached devices; all simulation devices must implement this.
    /// Instances are held in the SharedSceneState.Devices list and manipulated by SimulationInteractionController / SimulationPanelController.
    /// </summary>
    public interface IAttachedDevice
    {
        /// <summary>Unique device identifier (GUID).</summary>
        string Id { get; }

        /// <summary>Device type (Stimulation/Probe/VoltageClamp).</summary>
        DeviceType Type { get; }

        /// <summary>Target entity the device is attached to.</summary>
        IVisualEntity TargetEntity { get; }

        /// <summary>Anchor reference on the target entity surface.</summary>
        AnchorRef Anchor { get; set; }

        /// <summary>3D visual object for the device.</summary>
        ModelVisual3D Visual3D { get; }

        /// <summary>Update the device's position and orientation in 3D space. Called by SimulationInteractionController and InteractionController.UpdateObjectPosition.</summary>
        void UpdatePosition();
    }

    /// <summary>
    /// Abstract base class for attached devices. Handles arrow geometry creation and normal alignment logic.
    /// Inherited by StimulationDevice, ProbeDevice, and VoltageClampDevice.
    /// </summary>
    public abstract class AttachedDeviceBase : IAttachedDevice
    {
        /// <summary>Unique device identifier (GUID), generated on creation.</summary>
        public string Id { get; } = Guid.NewGuid().ToString();

        /// <summary>Device type, implemented by subclasses.</summary>
        public abstract DeviceType Type { get; }

        /// <summary>Target entity the device attaches to. Set by constructor.</summary>
        public IVisualEntity TargetEntity { get; }

        /// <summary>Anchor reference on the target entity surface; can be updated when dragging.</summary>
        public AnchorRef Anchor { get; set; }

        /// <summary>Container for the device's 3D visual.</summary>
        public ModelVisual3D Visual3D { get; } = new ModelVisual3D();

        /// <summary>Arrow visual representing the device direction.</summary>
        protected ArrowVisual3D Arrow { get; }

        /// <summary>
        /// Constructor. Initializes the arrow visual and calls UpdatePosition to set the initial position.
        /// Called by device subclass constructors.
        /// </summary>
        /// <param name="target">Target entity</param>
        /// <param name="anchor">Initial anchor</param>
        /// <param name="color">Arrow color</param>
        protected AttachedDeviceBase(IVisualEntity target, AnchorRef anchor, Color color)
        {
            TargetEntity = target;
            Anchor = anchor;

            // Initialize the arrow visual object
            Arrow = new ArrowVisual3D
            {
                Fill = new SolidColorBrush(color),
                HeadLength = 1.5,
                Diameter = 0.4
            };

            Visual3D.Children.Add(Arrow);
            UpdatePosition();
        }

        /// <summary>
        /// Update arrow position and orientation in 3D, computing tail and tip from anchor and normal.
        /// Called by SimulationInteractionController (placing/dragging) and InteractionController.UpdateObjectPosition (when entities move).
        /// </summary>
        public void UpdatePosition()
        {
            if (TargetEntity is not IAnchoredEntity anchoredTarget) return;

            // 1. Get surface contact point (world coordinates)
            if (anchoredTarget.TryAnchorToWorldPoint(Anchor, out Point3D tipPoint))
            {
                // 2. Compute surface normal at the contact point (world coordinates)
                Vector3D normal = CalculateWorldNormal();

                // 3. Set arrow length and orient arrow to point toward the surface (from outside inward)
                double arrowLength = 4.0;
                Point3D tailPoint = tipPoint + normal * arrowLength;

                Arrow.Point1 = tailPoint; // tail (away from surface)
                Arrow.Point2 = tipPoint;  // tip (on surface)
            }
        }

        /// <summary>
        /// Compute the surface normal at a specific Anchor on the target entity (world coordinates).
        /// For AxonVisual (including SomaVisual, DendVisual) use frustum geometry; other shapes use the vector from center to surface.
        /// Called internally by UpdatePosition.
        /// </summary>
        /// <returns>Unit normal vector in world coordinates</returns>
        private Vector3D CalculateWorldNormal()
        {
            Vector3D normal = new Vector3D(0, 0, 1);

            // If the target is a cylinder/frustum (Axon or Dend)
            if (TargetEntity is AxonVisual axon)
            {
                double cos = Math.Cos(Anchor.Angle);
                double sin = Math.Sin(Anchor.Angle);

                // Consider frustum slope
                double dz = axon.BaseRadius - axon.TopRadius;
                double slopeLen = Math.Sqrt(dz * dz + axon.Length * axon.Length);
                double nz = slopeLen > 0 ? dz / slopeLen : 0;
                double nxy = slopeLen > 0 ? axon.Length / slopeLen : 1;

                Vector3D localNormal;
                if (Anchor.Mode == AnchorMode.AxonCapStart)
                    localNormal = new Vector3D(0, 0, -1);
                else if (Anchor.Mode == AnchorMode.AxonCapEnd)
                    localNormal = new Vector3D(0, 0, 1);
                else
                    localNormal = new Vector3D(cos * nxy, sin * nxy, nz);

                // Transform the local normal to world normal using the visual's transform
                if (axon.Visual3D.Transform != null)
                {
                    normal = axon.Visual3D.Transform.Value.Transform(localNormal);
                    normal.Normalize();
                }
                else
                {
                    normal = localNormal;
                }
            }
            // If the target is a soma or other spherical object
            else
            {
                if (TargetEntity is IAnchoredEntity aEnt && aEnt.TryAnchorToWorldPoint(Anchor, out Point3D tip))
                {
                    // The normal on a sphere's surface is the vector from the center to the surface
                    normal = tip - TargetEntity.CenterPosition;
                    if (normal.LengthSquared > 1e-6)
                        normal.Normalize();
                    else
                        normal = new Vector3D(0, 1, 0);
                }
            }

            return normal;
        }
    }

    /// <summary>
    /// Stimulation device entity holding stimulation current, start time, and duration parameters; yellow arrow appearance.
    /// Corresponds to Hines_method.py: insert_stimulation(stimulation_id, segment_id, stimulation_uA, stim_start, stim_duration)
    /// Created by SimulationInteractionController.UpdatePlacingDevice and parameter card built by SimulationPanelController.BuildDeviceNode.
    /// </summary>
    public class StimulationDevice : AttachedDeviceBase
    {
        /// <summary>Device type: Stimulation.</summary>
        public override DeviceType Type => DeviceType.Stimulation;

        /// <summary>Stimulation current (µA), default 0.1. Edited via SimulationPanelController parameter card.</summary>
        public double Stimulation_uA { get; set; } = 0.1;

        /// <summary>Stimulation start time (ms), default 0.0. Edited via SimulationPanelController parameter card.</summary>
        public double StimStart { get; set; } = 0.0;

        /// <summary>Stimulation duration (ms), default 5.0. Edited via SimulationPanelController parameter card.</summary>
        public double StimDuration { get; set; } = 5.0;

        /// <summary>
        /// Constructor. Created by SimulationInteractionController.UpdatePlacingDevice.
        /// </summary>
        /// <param name="target">Target entity</param>
        /// <param name="anchor">Initial anchor</param>
        public StimulationDevice(IVisualEntity target, AnchorRef anchor)
            : base(target, anchor, Colors.Yellow)
        {
        }
    }

    /// <summary>
    /// Probe device entity holding sampling start time and duration (ms); cyan arrow appearance.
    /// Corresponds to Hines_method.py: insert_probe(probe_id, segment_id, probe_start_ms, probe_duration_ms)
    /// Created by SimulationInteractionController.UpdatePlacingDevice and parameter card built by SimulationPanelController.BuildDeviceNode.
    /// </summary>
    public class ProbeDevice : AttachedDeviceBase
    {
        /// <summary>Device type: Probe.</summary>
        public override DeviceType Type => DeviceType.Probe;

        /// <summary>Probe sampling start time (ms). Passed to insert_probe as probe_start_ms per Hines_method convention.</summary>
        public double StartMs { get; set; } = 0.0;

        /// <summary>Probe sampling duration (ms). Passed to insert_probe as probe_duration_ms per Hines_method convention.</summary>
        public double DurationMs { get; set; } = 1.0;

        /// <summary>
        /// Constructor. Created by SimulationInteractionController.UpdatePlacingDevice.
        /// </summary>
        /// <param name="target">Target entity</param>
        /// <param name="anchor">Initial anchor</param>
        public ProbeDevice(IVisualEntity target, AnchorRef anchor)
            : base(target, anchor, Colors.Cyan)
        {
        }
    }

    /// <summary>
    /// Voltage clamp protocol step, corresponding to NEURON SEClamp's dur[i] / amp[i].
    /// </summary>
    public class VCStep
    {
        /// <summary>Duration of this step (ms).</summary>
        public double Duration { get; set; }

        /// <summary>Clamp voltage for this step (mV).</summary>
        public double Amplitude { get; set; }
    }

    /// <summary>
    /// Voltage clamp device entity holding series resistance and protocol steps; red arrow appearance.
    /// Corresponds to Hines_method.py: insert_voltage_clamp(vc_id, segment_id, rs_MOhm, protocol)
    /// </summary>
    public class VoltageClampDevice : AttachedDeviceBase
    {
        /// <summary>Device type: Voltage clamp.</summary>
        public override DeviceType Type => DeviceType.VoltageClamp;

        /// <summary>Series resistance (MΩ), corresponds to SEClamp.rs, default 5.0.</summary>
        public double Rs { get; set; } = 5.0;

        /// <summary>
        /// List of voltage clamp protocol steps executed in order.
        /// Default 3 steps: [-115, 1000ms], [-65, 1000ms], [-65, 1000ms] (reproduces the tcD_vc.oc protocol).
        /// </summary>
        public List<VCStep> Protocol { get; set; } = new()
        {
            new VCStep { Duration = 1000, Amplitude = -115 },
            new VCStep { Duration = 1000, Amplitude = -65 },
            new VCStep { Duration = 1000, Amplitude = -65 }
        };

        /// <summary>
        /// Constructor.
        /// </summary>
        /// <param name="target">Target entity</param>
        /// <param name="anchor">Initial anchor</param>
        public VoltageClampDevice(IVisualEntity target, AnchorRef anchor)
            : base(target, anchor, Colors.Red)
        {
        }
    }
}