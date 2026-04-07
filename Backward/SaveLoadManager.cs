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
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Windows.Media;
using System.Windows.Media.Media3D;
using NeuronCAD.Visuals.Tabs.Modeling;
using NeuronCAD.Visuals.Tabs.Modeling.Visuals;
using NeuronCAD.Visuals.Tabs.Shared;
using NeuronCAD.Visuals.Tabs.Simulation;
using NeuronCAD.Visuals.Windows;

namespace NeuronCAD.Backward
{
    #region JSON Data Models

    /// <summary>
    /// Root node of the project file. Contains all data required to drive a full simulation in Hines_method.py.
    /// </summary>
    public class ProjectData
    {
        public string ProjectId { get; set; } = "";
        public string ProjectName { get; set; } = "Untitled";
        public GlobalEnvironmentData GlobalEnvironment { get; set; } = new();
        public Dictionary<string, ETableEntry> E_TABLE { get; set; } = new();
        public Dictionary<string, double> HH_PARAMS { get; set; } = new();
        public Dictionary<string, double> CA_PARAMS { get; set; } = new();
        public SegmentationData Segmentation { get; set; } = new();
        public List<EntityData> Entities { get; set; } = new();
        public List<ConnectionData> Connections { get; set; } = new();
        public List<DeviceData> Devices { get; set; } = new();
    }

    /// <summary>
    /// Data model for exported simulation results. Contains the project identifier for validation
    /// and the raw probe JSON data returned by Hines_method.py.
    /// </summary>
    public class SimulationResultData
    {
        public string ProjectId { get; set; } = "";
        public string ProjectName { get; set; } = "";
        public string FullSimulationJson { get; set; } = "";
    }

    public class GlobalEnvironmentData
    {
        public double V_init { get; set; } = -70.0;
        public double dt { get; set; } = 0.1;
        public int STEPS { get; set; } = 10000;
        public double celsius { get; set; } = 24.0;
        public double CA_OUT { get; set; } = 2.0;
        public double CA_INF { get; set; } = 2.4e-4;
        public double TAU_CA { get; set; } = 5.0;
    }

    public class ETableEntry
    {
        public double E { get; set; }
    }

    public class SegmentationData
    {
        public string Mode { get; set; } = "NSeg";
        public int NSeg { get; set; } = 5;
        public double LSeg { get; set; } = 20.0;
    }

    public class EntityData
    {
        public string Id { get; set; } = "";
        public string Type { get; set; } = "";
        public double BaseRadius { get; set; }
        public double TopRadius { get; set; }
        public double Length { get; set; }
        public double Ra { get; set; }
        public double Cm { get; set; }
        public string Color { get; set; } = "";
        public double[] Transform { get; set; } = Array.Empty<double>();
        public Dictionary<string, ChannelData> Channels { get; set; } = new();
    }

    public class ChannelData
    {
        public double G { get; set; }
        public bool IsPermeability { get; set; }
    }

    public class AnchorData
    {
        public string Mode { get; set; } = "AxonCylinder";
        public double AxialT { get; set; }
        public double Angle { get; set; }
    }

    public class ConnectionData
    {
        public string Id { get; set; } = "";
        public string EntityA_Id { get; set; } = "";
        public string EntityB_Id { get; set; } = "";
        public AnchorData AnchorA { get; set; } = new();
        public AnchorData AnchorB { get; set; } = new();
        public double Weight { get; set; } = 1.0;
    }

    public class DeviceData
    {
        public string Id { get; set; } = "";
        public string Type { get; set; } = "";
        public string TargetEntityId { get; set; } = "";
        public AnchorData Anchor { get; set; } = new();
        // Stimulation (current clamp) params
        public double? Stimulation_uA { get; set; }
        public double? StimStart { get; set; }
        public double? StimDuration { get; set; }
        // Probe params
        public double? StartMs { get; set; }
        public double? DurationMs { get; set; }
        // Voltage clamp params
        public double? Rs { get; set; }
        public List<VCStepData>? Protocol { get; set; }
    }

    public class VCStepData
    {
        public double Duration { get; set; }
        public double Amplitude { get; set; }
    }

    #endregion

    /// <summary>
    /// Project save/load manager.
    /// Serialization: build ProjectData from SharedSceneState + UI parameters → JSON.
    /// Deserialization: JSON → ProjectData → rebuild scene entities, connections, devices, and global parameters.
    /// </summary>
    public static class SaveLoadManager
    {
        private static readonly JsonSerializerOptions JsonOpts = new()
        {
            WriteIndented = true,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
            PropertyNamingPolicy = null, // keep original property names
        };

        #region Save

        /// <summary>
        /// Serialize the current scene state into ProjectData and write to a JSON file.
        /// </summary>
        public static void Save(
            string filePath,
            SharedSceneState scene,
            double vInit, double dt, int steps,
            double eNa, double eK, double eLeak,
            double celsius, double caOut, double caInf, double tauCa,
            string segMode, int nSeg, double lSeg,
            string? projectId = null, string? projectName = null)
        {
            var project = new ProjectData();
            project.ProjectId = projectId ?? Guid.NewGuid().ToString();
            if (projectName != null) project.ProjectName = projectName;

            // ── GlobalEnvironment ──
            project.GlobalEnvironment = new GlobalEnvironmentData
            {
                V_init = vInit,
                dt = dt,
                STEPS = steps,
                celsius = celsius,
                CA_OUT = caOut,
                CA_INF = caInf,
                TAU_CA = tauCa
            };

            // ── E_TABLE ──
            project.E_TABLE["Na"] = new ETableEntry { E = eNa };
            project.E_TABLE["K"] = new ETableEntry { E = eK };
            project.E_TABLE["L"] = new ETableEntry { E = eLeak };

            // ── HH_PARAMS (Traub-modified hh2.mod) ──
            project.HH_PARAMS = new Dictionary<string, double>
            {
                ["vtraub"] = IonChannelParams.Vtraub,
                ["alpha_m_A"] = IonChannelParams.AlphaM_A,
                ["alpha_m_V"] = IonChannelParams.AlphaM_V,
                ["alpha_m_k"] = IonChannelParams.AlphaM_k,
                ["beta_m_A"] = IonChannelParams.BetaM_A,
                ["beta_m_V"] = IonChannelParams.BetaM_V,
                ["beta_m_k"] = IonChannelParams.BetaM_k,
                ["alpha_h_A"] = IonChannelParams.AlphaH_A,
                ["alpha_h_V"] = IonChannelParams.AlphaH_V,
                ["alpha_h_k"] = IonChannelParams.AlphaH_k,
                ["beta_h_A"] = IonChannelParams.BetaH_A,
                ["beta_h_V"] = IonChannelParams.BetaH_V,
                ["beta_h_k"] = IonChannelParams.BetaH_k,
                ["alpha_n_A"] = IonChannelParams.AlphaN_A,
                ["alpha_n_V"] = IonChannelParams.AlphaN_V,
                ["alpha_n_k"] = IonChannelParams.AlphaN_k,
                ["beta_n_A"] = IonChannelParams.BetaN_A,
                ["beta_n_V"] = IonChannelParams.BetaN_V,
                ["beta_n_k"] = IonChannelParams.BetaN_k,
            };

            // ── CA_PARAMS (ITGHK.mod + tcD_vc.oc overrides) ──
            project.CA_PARAMS = new Dictionary<string, double>
            {
                ["shift"] = IonChannelParams.Shift,
                ["actshift"] = IonChannelParams.ActShift,
                ["inf_mT_Vh"] = IonChannelParams.InfMT_Vh,
                ["inf_mT_k"] = IonChannelParams.InfMT_k,
                ["inf_hT_Vh"] = IonChannelParams.InfHT_Vh,
                ["inf_hT_k"] = IonChannelParams.InfHT_k,
                ["tau_mT_base"] = IonChannelParams.TauMT_base,
                ["tau_mT_V1"] = IonChannelParams.TauMT_V1,
                ["tau_mT_k1"] = IonChannelParams.TauMT_k1,
                ["tau_mT_V2"] = IonChannelParams.TauMT_V2,
                ["tau_mT_k2"] = IonChannelParams.TauMT_k2,
                ["tau_mT_Q10"] = IonChannelParams.TauMT_Q10,
                ["tau_mT_Tref"] = IonChannelParams.TauMT_Tref,
                ["tau_hT_Vthresh"] = IonChannelParams.TauHT_Vthresh,
                ["tau_hT_V1"] = IonChannelParams.TauHT_V1,
                ["tau_hT_k1"] = IonChannelParams.TauHT_k1,
                ["tau_hT_base"] = IonChannelParams.TauHT_base,
                ["tau_hT_V2"] = IonChannelParams.TauHT_V2,
                ["tau_hT_k2"] = IonChannelParams.TauHT_k2,
                ["tau_hT_Q10"] = IonChannelParams.TauHT_Q10,
                ["tau_hT_Tref"] = IonChannelParams.TauHT_Tref,
            };

            // ── Segmentation ──
            project.Segmentation = new SegmentationData
            {
                Mode = segMode,
                NSeg = nSeg,
                LSeg = lSeg
            };

            // ── Entities ──
            foreach (var entity in scene.Entities)
            {
                if (entity is not AxonVisual axon) continue;

                var ed = new EntityData
                {
                    Id = entity.Id,
                    Type = axon.VisualType,
                    BaseRadius = axon.BaseRadius,
                    TopRadius = axon.TopRadius,
                    Length = axon.Length,
                    Ra = entity.Ra,
                    Cm = entity.Cm,
                    Color = entity.CurrentColor.ToString(),
                    Transform = Matrix3DToArray(entity.Visual3D.Transform?.Value ?? Matrix3D.Identity),
                };

                foreach (var ch in entity.Channels)
                {
                    ed.Channels[ch.Key] = new ChannelData
                    {
                        G = ch.Value.G_ion_channel,
                        IsPermeability = ch.Value.IsPermeability
                    };
                }

                project.Entities.Add(ed);
            }

            // ── Connections ──
            foreach (var conn in scene.ConnectionController.ConnectionsById.Values)
            {
                project.Connections.Add(new ConnectionData
                {
                    Id = conn.Id,
                    EntityA_Id = conn.A.Id,
                    EntityB_Id = conn.B.Id,
                    AnchorA = AnchorRefToData(conn.AnchorA),
                    AnchorB = AnchorRefToData(conn.AnchorB),
                    Weight = conn.Weight
                });
            }

            // ── Devices ──
            foreach (var device in scene.Devices)
            {
                var dd = new DeviceData
                {
                    Id = device.Id,
                    Type = device.Type.ToString(),
                    TargetEntityId = device.TargetEntity.Id,
                    Anchor = AnchorRefToData(device.Anchor),
                };

                if (device is StimulationDevice stim)
                {
                    dd.Stimulation_uA = stim.Stimulation_uA;
                    dd.StimStart = stim.StimStart;
                    dd.StimDuration = stim.StimDuration;
                }
                else if (device is ProbeDevice probe)
                {
                    dd.StartMs = probe.StartMs;
                    dd.DurationMs = probe.DurationMs;
                }
                else if (device is VoltageClampDevice vc)
                {
                    dd.Rs = vc.Rs;
                    dd.Protocol = vc.Protocol.Select(s => new VCStepData
                    {
                        Duration = s.Duration,
                        Amplitude = s.Amplitude
                    }).ToList();
                }

                project.Devices.Add(dd);
            }

            string json = JsonSerializer.Serialize(project, JsonOpts);
            File.WriteAllText(filePath, json);
        }

        #endregion

        #region Load

        /// <summary>
        /// Load ProjectData from a JSON file.
        /// </summary>
        public static ProjectData Load(string filePath)
        {
            string json = File.ReadAllText(filePath);
            return JsonSerializer.Deserialize<ProjectData>(json, JsonOpts)
                ?? throw new InvalidOperationException("Failed to parse project file.");
        }

        /// <summary>
        /// Apply ProjectData to the scene: clear current state → rebuild entities → rebuild connections → rebuild devices → restore global parameters.
        /// </summary>
        public static void ApplyToScene(
            ProjectData project,
            SharedSceneState scene,
            InteractionController modelingInteraction,
            SimulationInteractionController simulationInteraction,
            Action<string> setTbVInit,
            Action<string> setTbDt,
            Action<string> setTbSteps,
            Action<string> setTbENa,
            Action<string> setTbEK,
            Action<string> setTbELeak,
            Action<string> setTbCelsius,
            Action<string> setTbCaOut,
            Action<string> setTbCaInf,
            Action<string> setTbTauCa,
            Action<string> setTbNSeg,
            Action<string> setTbLSeg,
            Action<bool> setRbNSeg)
        {
            // ── 1. Clear current scene ──
            modelingInteraction.Deactivate();
            simulationInteraction.Deactivate();

            // Remove all devices
            foreach (var device in scene.Devices.ToList())
            {
                scene.HelixViewport.Children.Remove(device.Visual3D);
            }
            scene.Devices.Clear();

            // Remove all connections
            foreach (var connId in scene.ConnectionController.ConnectionsById.Keys.ToList())
            {
                scene.ConnectionController.Remove(connId);
            }

            // Remove all entities
            foreach (var entity in scene.Entities.ToList())
            {
                scene.HelixViewport.Children.Remove(entity.Visual3D);
                scene.SimulationRegistry.Unregister(entity.Id);
            }
            scene.Entities.Clear();

            // ── 2. Restore global environment parameters to UI ──
            var env = project.GlobalEnvironment;
            setTbVInit(env.V_init.ToString(CultureInfo.InvariantCulture));
            setTbDt(env.dt.ToString(CultureInfo.InvariantCulture));
            setTbSteps(env.STEPS.ToString(CultureInfo.InvariantCulture));

            if (project.E_TABLE.TryGetValue("Na", out var eNa))
                setTbENa(eNa.E.ToString(CultureInfo.InvariantCulture));
            if (project.E_TABLE.TryGetValue("K", out var eK))
                setTbEK(eK.E.ToString(CultureInfo.InvariantCulture));
            if (project.E_TABLE.TryGetValue("L", out var eL))
                setTbELeak(eL.E.ToString(CultureInfo.InvariantCulture));

            setTbCelsius(env.celsius.ToString(CultureInfo.InvariantCulture));
            setTbCaOut(env.CA_OUT.ToString(CultureInfo.InvariantCulture));
            setTbCaInf(env.CA_INF.ToString(CultureInfo.InvariantCulture));
            setTbTauCa(env.TAU_CA.ToString(CultureInfo.InvariantCulture));

            // ── 3. Restore segmentation parameters ──
            var seg = project.Segmentation;
            bool isNSeg = seg.Mode == "NSeg";
            setRbNSeg(isNSeg);
            setTbNSeg(seg.NSeg.ToString(CultureInfo.InvariantCulture));
            setTbLSeg(seg.LSeg.ToString(CultureInfo.InvariantCulture));

            scene.SimulationRegistry.Mode = isNSeg
                ? Visuals.Tabs.Simulation.SegmentationMode.NSeg
                : Visuals.Tabs.Simulation.SegmentationMode.LSeg;
            scene.SimulationRegistry.NSeg = seg.NSeg;
            scene.SimulationRegistry.LSeg = seg.LSeg;

            // ── 4. Restore HH / CA parameters ──
            ApplyHHParams(project.HH_PARAMS);
            ApplyCaParams(project.CA_PARAMS);

            // ── 4b. Diagnostic output: display all loaded global parameters ──
            TraceLoadedParams(project);

            // ── 5. Rebuild entities ──
            var entityMap = new Dictionary<string, IVisualEntity>();

            foreach (var ed in project.Entities)
            {
                AxonVisual entity;
                var defaultStart = new Point3D(0, 0, 0);
                var defaultEnd = new Point3D(0, 0, 1);
                Color color;
                try { color = (Color)ColorConverter.ConvertFromString(ed.Color); }
                catch { color = Colors.Gray; }

                switch (ed.Type)
                {
                    case "Soma":
                        entity = new SomaVisual(defaultStart, defaultEnd, ed.BaseRadius, color);
                        break;
                    case "Dend":
                        entity = new DendVisual(defaultStart, defaultEnd, ed.BaseRadius, color);
                        break;
                    default: // "Axon"
                        entity = new AxonVisual(defaultStart, defaultEnd, ed.BaseRadius, color);
                        break;
                }

                // Set Id via reflection (preserve saved value)
                SetEntityId(entity, ed.Id);

                // Set geometry parameters (triggers UpdateGeometry)
                entity.BaseRadius = ed.BaseRadius;
                entity.TopRadius = ed.TopRadius;
                entity.Length = ed.Length;

                // Set biophysical parameters
                entity.Ra = ed.Ra;
                entity.Cm = ed.Cm;

                // Restore transformation matrix (includes position and rotation)
                if (ed.Transform.Length == 16)
                {
                    var m = ArrayToMatrix3D(ed.Transform);
                    entity.Visual3D.Transform = new MatrixTransform3D(m);
                }

                // Restore ion channels
                entity.Channels.Clear();
                foreach (var chEntry in ed.Channels)
                {
                    // Retrieve base color from GlobalBiophysics; use default gray if missing
                    Color chColor = Colors.Gray;
                    if (GlobalBiophysics.GlobalChannels.TryGetValue(chEntry.Key, out var globalCh))
                        chColor = globalCh.Color;

                    entity.Channels[chEntry.Key] = new ChannelProperty(
                        chEntry.Key, chColor, (float)chEntry.Value.G, chEntry.Value.IsPermeability);
                }
                entity.UpdateChannelVisuals();

                // Add to scene
                scene.HelixViewport.Children.Add(entity.Visual3D);
                scene.Entities.Add(entity);
                scene.SimulationRegistry.Register(entity);

                // Trigger panel update
                modelingInteraction.NotifyEntityLoaded(entity);

                entityMap[ed.Id] = entity;
            }

            // ── 6. Rebuild connections ──
            foreach (var cd in project.Connections)
            {
                if (!entityMap.TryGetValue(cd.EntityA_Id, out var entityA)) continue;
                if (!entityMap.TryGetValue(cd.EntityB_Id, out var entityB)) continue;

                var anchorA = DataToAnchorRef(cd.AnchorA);
                var anchorB = DataToAnchorRef(cd.AnchorB);
                var conn = new Connection(entityA, entityB, anchorA, anchorB, cd.Weight);
                SetConnectionId(conn, cd.Id);
                scene.ConnectionController.Add(conn);
            }

            // ── 7. Rebuild devices ──
            foreach (var dd in project.Devices)
            {
                if (!entityMap.TryGetValue(dd.TargetEntityId, out var targetEntity)) continue;
                var anchor = DataToAnchorRef(dd.Anchor);

                IAttachedDevice device;
                if (dd.Type == "Stimulation")
                {
                    var stim = new StimulationDevice(targetEntity, anchor);
                    stim.Stimulation_uA = dd.Stimulation_uA ?? 0.1;
                    stim.StimStart = dd.StimStart ?? 0.0;
                    stim.StimDuration = dd.StimDuration ?? 5.0;
                    device = stim;
                }
                else if (dd.Type == "VoltageClamp")
                {
                    var vc = new VoltageClampDevice(targetEntity, anchor);
                    vc.Rs = dd.Rs ?? 5.0;
                    if (dd.Protocol != null && dd.Protocol.Count > 0)
                    {
                        vc.Protocol.Clear();
                        foreach (var stepData in dd.Protocol)
                        {
                            vc.Protocol.Add(new VCStep
                            {
                                Duration = stepData.Duration,
                                Amplitude = stepData.Amplitude
                            });
                        }
                    }
                    device = vc;
                }
                else
                {
                    var probe = new ProbeDevice(targetEntity, anchor);
                    probe.StartMs = dd.StartMs ?? 0.0;
                    probe.DurationMs = dd.DurationMs ?? 1.0;
                    device = probe;
                }

                scene.HelixViewport.Children.Add(device.Visual3D);
                scene.Devices.Add(device);
                simulationInteraction.NotifyDeviceLoaded(device);
            }
        }

        #endregion

        #region Helpers

        private static double[] Matrix3DToArray(Matrix3D m)
        {
            return new[]
            {
                m.M11, m.M12, m.M13, m.M14,
                m.M21, m.M22, m.M23, m.M24,
                m.M31, m.M32, m.M33, m.M34,
                m.OffsetX, m.OffsetY, m.OffsetZ, m.M44
            };
        }

        private static Matrix3D ArrayToMatrix3D(double[] a)
        {
            return new Matrix3D(
                a[0], a[1], a[2], a[3],
                a[4], a[5], a[6], a[7],
                a[8], a[9], a[10], a[11],
                a[12], a[13], a[14], a[15]);
        }

        private static AnchorData AnchorRefToData(AnchorRef anchor)
        {
            return new AnchorData
            {
                Mode = anchor.Mode.ToString(),
                AxialT = anchor.AxialT,
                Angle = anchor.Angle
            };
        }

        private static AnchorRef DataToAnchorRef(AnchorData data)
        {
            return new AnchorRef
            {
                Mode = Enum.TryParse<AnchorMode>(data.Mode, out var mode)
                    ? mode : AnchorMode.AxonCylinder,
                AxialT = data.AxialT,
                Angle = data.Angle
            };
        }

        private static void SetEntityId(VisualEntityBase entity, string id)
        {
            var prop = typeof(VisualEntityBase).GetProperty("Id");
            prop?.SetValue(entity, id);
        }

        private static void SetConnectionId(Connection conn, string id)
        {
            // Connection.Id has only a get accessor; set the private backing field via reflection
            var field = typeof(Connection).GetField("<Id>k__BackingField",
                System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
            field?.SetValue(conn, id);
        }

        private static void ApplyHHParams(Dictionary<string, double> p)
        {
            if (p == null || p.Count == 0) return;
            if (p.TryGetValue("vtraub", out var v)) IonChannelParams.Vtraub = v;
            if (p.TryGetValue("alpha_m_A", out v)) IonChannelParams.AlphaM_A = v;
            if (p.TryGetValue("alpha_m_V", out v)) IonChannelParams.AlphaM_V = v;
            if (p.TryGetValue("alpha_m_k", out v)) IonChannelParams.AlphaM_k = v;
            if (p.TryGetValue("beta_m_A", out v)) IonChannelParams.BetaM_A = v;
            if (p.TryGetValue("beta_m_V", out v)) IonChannelParams.BetaM_V = v;
            if (p.TryGetValue("beta_m_k", out v)) IonChannelParams.BetaM_k = v;
            if (p.TryGetValue("alpha_h_A", out v)) IonChannelParams.AlphaH_A = v;
            if (p.TryGetValue("alpha_h_V", out v)) IonChannelParams.AlphaH_V = v;
            if (p.TryGetValue("alpha_h_k", out v)) IonChannelParams.AlphaH_k = v;
            if (p.TryGetValue("beta_h_A", out v)) IonChannelParams.BetaH_A = v;
            if (p.TryGetValue("beta_h_V", out v)) IonChannelParams.BetaH_V = v;
            if (p.TryGetValue("beta_h_k", out v)) IonChannelParams.BetaH_k = v;
            if (p.TryGetValue("alpha_n_A", out v)) IonChannelParams.AlphaN_A = v;
            if (p.TryGetValue("alpha_n_V", out v)) IonChannelParams.AlphaN_V = v;
            if (p.TryGetValue("alpha_n_k", out v)) IonChannelParams.AlphaN_k = v;
            if (p.TryGetValue("beta_n_A", out v)) IonChannelParams.BetaN_A = v;
            if (p.TryGetValue("beta_n_V", out v)) IonChannelParams.BetaN_V = v;
            if (p.TryGetValue("beta_n_k", out v)) IonChannelParams.BetaN_k = v;
        }

        private static void ApplyCaParams(Dictionary<string, double> p)
        {
            if (p == null || p.Count == 0) return;
            if (p.TryGetValue("shift", out var v)) IonChannelParams.Shift = v;
            if (p.TryGetValue("actshift", out v)) IonChannelParams.ActShift = v;
            if (p.TryGetValue("inf_mT_Vh", out v)) IonChannelParams.InfMT_Vh = v;
            if (p.TryGetValue("inf_mT_k", out v)) IonChannelParams.InfMT_k = v;
            if (p.TryGetValue("inf_hT_Vh", out v)) IonChannelParams.InfHT_Vh = v;
            if (p.TryGetValue("inf_hT_k", out v)) IonChannelParams.InfHT_k = v;
            if (p.TryGetValue("tau_mT_base", out v)) IonChannelParams.TauMT_base = v;
            if (p.TryGetValue("tau_mT_V1", out v)) IonChannelParams.TauMT_V1 = v;
            if (p.TryGetValue("tau_mT_k1", out v)) IonChannelParams.TauMT_k1 = v;
            if (p.TryGetValue("tau_mT_V2", out v)) IonChannelParams.TauMT_V2 = v;
            if (p.TryGetValue("tau_mT_k2", out v)) IonChannelParams.TauMT_k2 = v;
            if (p.TryGetValue("tau_mT_Q10", out v)) IonChannelParams.TauMT_Q10 = v;
            if (p.TryGetValue("tau_mT_Tref", out v)) IonChannelParams.TauMT_Tref = v;
            if (p.TryGetValue("tau_hT_Vthresh", out v)) IonChannelParams.TauHT_Vthresh = v;
            if (p.TryGetValue("tau_hT_V1", out v)) IonChannelParams.TauHT_V1 = v;
            if (p.TryGetValue("tau_hT_k1", out v)) IonChannelParams.TauHT_k1 = v;
            if (p.TryGetValue("tau_hT_base", out v)) IonChannelParams.TauHT_base = v;
            if (p.TryGetValue("tau_hT_V2", out v)) IonChannelParams.TauHT_V2 = v;
            if (p.TryGetValue("tau_hT_k2", out v)) IonChannelParams.TauHT_k2 = v;
            if (p.TryGetValue("tau_hT_Q10", out v)) IonChannelParams.TauHT_Q10 = v;
            if (p.TryGetValue("tau_hT_Tref", out v)) IonChannelParams.TauHT_Tref = v;
        }

        /// <summary>
        /// Diagnostic output: print loaded global project parameters to the Debug Output window,
        /// allowing developers to verify that JSON → IonChannelParams overrides were applied correctly.
        /// </summary>
        private static void TraceLoadedParams(ProjectData project)
        {
            var ci = CultureInfo.InvariantCulture;
            var env = project.GlobalEnvironment;

            Debug.WriteLine("╔══════════════════════════════════════════════════════════╗");
            Debug.WriteLine("║         NeuronCAD: Loaded Project Parameters             ║");
            Debug.WriteLine("╠══════════════════════════════════════════════════════════╣");

            Debug.WriteLine("║ [GlobalEnvironment]");
            Debug.WriteLine($"║   V_init   = {env.V_init.ToString(ci)} mV");
            Debug.WriteLine($"║   dt       = {env.dt.ToString(ci)} ms");
            Debug.WriteLine($"║   STEPS    = {env.STEPS}");
            Debug.WriteLine($"║   celsius  = {env.celsius.ToString(ci)} °C");
            Debug.WriteLine($"║   CA_OUT   = {env.CA_OUT.ToString(ci)} mM");
            Debug.WriteLine($"║   CA_INF   = {env.CA_INF.ToString(ci)} mM");
            Debug.WriteLine($"║   TAU_CA   = {env.TAU_CA.ToString(ci)} ms");

            Debug.WriteLine("║ [E_TABLE]");
            foreach (var kvp in project.E_TABLE)
                Debug.WriteLine($"║   E_{kvp.Key} = {kvp.Value.E.ToString(ci)} mV");

            Debug.WriteLine("║ [HH_PARAMS] (IonChannelParams after overwrite)");
            Debug.WriteLine($"║   vtraub   = {IonChannelParams.Vtraub.ToString(ci)}");
            Debug.WriteLine($"║   αm: A={IonChannelParams.AlphaM_A.ToString(ci)}, V={IonChannelParams.AlphaM_V.ToString(ci)}, k={IonChannelParams.AlphaM_k.ToString(ci)}");
            Debug.WriteLine($"║   βm: A={IonChannelParams.BetaM_A.ToString(ci)}, V={IonChannelParams.BetaM_V.ToString(ci)}, k={IonChannelParams.BetaM_k.ToString(ci)}");
            Debug.WriteLine($"║   αh: A={IonChannelParams.AlphaH_A.ToString(ci)}, V={IonChannelParams.AlphaH_V.ToString(ci)}, k={IonChannelParams.AlphaH_k.ToString(ci)}");
            Debug.WriteLine($"║   βh: A={IonChannelParams.BetaH_A.ToString(ci)}, V={IonChannelParams.BetaH_V.ToString(ci)}, k={IonChannelParams.BetaH_k.ToString(ci)}");
            Debug.WriteLine($"║   αn: A={IonChannelParams.AlphaN_A.ToString(ci)}, V={IonChannelParams.AlphaN_V.ToString(ci)}, k={IonChannelParams.AlphaN_k.ToString(ci)}");
            Debug.WriteLine($"║   βn: A={IonChannelParams.BetaN_A.ToString(ci)}, V={IonChannelParams.BetaN_V.ToString(ci)}, k={IonChannelParams.BetaN_k.ToString(ci)}");

            Debug.WriteLine("║ [CA_PARAMS] (IonChannelParams after overwrite)");
            Debug.WriteLine($"║   shift={IonChannelParams.Shift.ToString(ci)}, actshift={IonChannelParams.ActShift.ToString(ci)}");
            Debug.WriteLine($"║   m∞: Vh={IonChannelParams.InfMT_Vh.ToString(ci)}, k={IonChannelParams.InfMT_k.ToString(ci)}");
            Debug.WriteLine($"║   h∞: Vh={IonChannelParams.InfHT_Vh.ToString(ci)}, k={IonChannelParams.InfHT_k.ToString(ci)}");
            Debug.WriteLine($"║   τm: base={IonChannelParams.TauMT_base.ToString(ci)}, V1={IonChannelParams.TauMT_V1.ToString(ci)}, k1={IonChannelParams.TauMT_k1.ToString(ci)}, V2={IonChannelParams.TauMT_V2.ToString(ci)}, k2={IonChannelParams.TauMT_k2.ToString(ci)}, Q10={IonChannelParams.TauMT_Q10.ToString(ci)}, Tref={IonChannelParams.TauMT_Tref.ToString(ci)}");
            Debug.WriteLine($"║   τh: Vth={IonChannelParams.TauHT_Vthresh.ToString(ci)}, V1={IonChannelParams.TauHT_V1.ToString(ci)}, k1={IonChannelParams.TauHT_k1.ToString(ci)}, base={IonChannelParams.TauHT_base.ToString(ci)}, V2={IonChannelParams.TauHT_V2.ToString(ci)}, k2={IonChannelParams.TauHT_k2.ToString(ci)}, Q10={IonChannelParams.TauHT_Q10.ToString(ci)}, Tref={IonChannelParams.TauHT_Tref.ToString(ci)}");

            Debug.WriteLine("╚══════════════════════════════════════════════════════════╝");
        }

        /// <summary>
        /// For external UI calls: generate a human-readable summary string of loaded parameters.
        /// </summary>
        public static string GetLoadedParamsSummary(ProjectData project)
        {
            var ci = CultureInfo.InvariantCulture;
            var env = project.GlobalEnvironment;
            var lines = new List<string>
            {
                "── Global Environment ──",
                $"  V_init={env.V_init.ToString(ci)} mV, dt={env.dt.ToString(ci)} ms, STEPS={env.STEPS}",
                $"  celsius={env.celsius.ToString(ci)}°C, CA_OUT={env.CA_OUT.ToString(ci)} mM, CA_INF={env.CA_INF.ToString(ci)} mM, TAU_CA={env.TAU_CA.ToString(ci)} ms",
                "── E_TABLE ──",
            };
            foreach (var kvp in project.E_TABLE)
                lines.Add($"  E_{kvp.Key} = {kvp.Value.E.ToString(ci)} mV");

            lines.Add("── HH_PARAMS ──");
            lines.Add($"  vtraub = {IonChannelParams.Vtraub.ToString(ci)}");
            foreach (var kvp in project.HH_PARAMS)
                lines.Add($"  {kvp.Key} = {kvp.Value.ToString(ci)}");

            lines.Add("── CA_PARAMS ──");
            foreach (var kvp in project.CA_PARAMS)
                lines.Add($"  {kvp.Key} = {kvp.Value.ToString(ci)}");

            lines.Add($"── Entities: {project.Entities.Count}, Connections: {project.Connections.Count}, Devices: {project.Devices.Count} ──");
            return string.Join("\n", lines);
        }

        #endregion
    }
}
