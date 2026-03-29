using System;
using System.Collections.Generic;
using NeuronCAD.Visuals.Tabs.Modeling.Visuals;

namespace NeuronCAD.Visuals.Tabs.Simulation
{
    /// <summary>
    /// Enumeration for segmentation (compartmentalization) modes.
    /// </summary>
    public enum SegmentationMode
    {
        /// <summary>Segment by fixed number of compartments: split each component into NSeg compartments.</summary>
        NSeg,
        /// <summary>Segment by compartment length: split every LSeg µm into one compartment.</summary>
        LSeg
    }

    /// <summary>
    /// Simulation probe data class, corresponding to Hines_method.py's insert_probe(probe_id, segment_id, probe_start_ms, probe_duration_ms).
    /// Created automatically by SimulationRegistry.ResolveDevices based on ProbeDevice spatial positions when Begin is pressed.
    /// </summary>
    public class SimProbe
    {
        /// <summary>Automatically assigned global probe id (0-based).</summary>
        public int ProbeId { get; set; }

        /// <summary>Probe sampling start time (ms), corresponding to probe_start_ms in Hines_method.</summary>
        public double StartMs { get; set; }

        /// <summary>Probe sampling duration (ms), corresponding to probe_duration_ms in Hines_method.</summary>
        public double DurationMs { get; set; }

        /// <summary>Global compartment id bound to the probe (Compartment.GlobalId).</summary>
        public int SegmentId { get; set; }

        /// <summary>Associated visual device ID (GUID) for tracing back to the ProbeDevice.</summary>
        public string SourceDeviceId { get; set; } = string.Empty;
    }

    /// <summary>
    /// Simulation stimulation data class, corresponding to Hines_method.py's insert_stimulation(stimulation_id, segment_id, stimulation_uA, stim_start, stim_duration).
    /// Created automatically by SimulationRegistry.ResolveDevices based on StimulationDevice spatial positions when Begin is pressed.
    /// </summary>
    public class SimStimulation
    {
        /// <summary>Automatically assigned global stimulation id (0-based).</summary>
        public int StimulationId { get; set; }

        /// <summary>Global compartment id bound to the stimulation (Compartment.GlobalId).</summary>
        public int SegmentId { get; set; }

        /// <summary>Stimulation current amplitude (µA).</summary>
        public double Stimulation_uA { get; set; }

        /// <summary>Stimulation start time (ms).</summary>
        public double StimStart { get; set; }

        /// <summary>Stimulation duration (ms).</summary>
        public double StimDuration { get; set; }

        /// <summary>Associated visual device ID (GUID) for tracing back to the StimulationDevice.</summary>
        public string SourceDeviceId { get; set; } = string.Empty;
    }

    /// <summary>
    /// Voltage clamp protocol step data.
    /// </summary>
    public class SimVCStep
    {
        public double Duration { get; set; }
        public double Amplitude { get; set; }
    }

    /// <summary>
    /// Simulation voltage-clamp data class, corresponding to Hines_method.py's insert_voltage_clamp(vc_id, segment_id, rs_MOhm, protocol).
    /// </summary>
    public class SimVoltageClamp
    {
        /// <summary>Automatically assigned global voltage-clamp id (0-based).</summary>
        public int VCId { get; set; }

        /// <summary>Global compartment id bound to the voltage clamp (Compartment.GlobalId).</summary>
        public int SegmentId { get; set; }

        /// <summary>Series resistance (MΩ).</summary>
        public double Rs { get; set; }

        /// <summary>List of voltage-clamp protocol steps.</summary>
        public List<SimVCStep> Protocol { get; set; } = new();

        /// <summary>Associated visual device ID (GUID).</summary>
        public string SourceDeviceId { get; set; } = string.Empty;
    }

    /// <summary>
    /// Complete simulation payload containing compartmentalization results and device bindings.
    /// Generated once by SimulationRegistry.BuildSimulationData and can be passed directly to Hines_method.py.
    /// </summary>
    public class SimulationData
    {
        /// <summary>List of all compartments (ordered by GlobalId).</summary>
        public List<Compartment> Compartments { get; set; } = new();

        /// <summary>List of all probe bindings.</summary>
        public List<SimProbe> Probes { get; set; } = new();

        /// <summary>List of all current-clamp bindings.</summary>
        public List<SimStimulation> Stimulations { get; set; } = new();

        /// <summary>List of all voltage-clamp bindings.</summary>
        public List<SimVoltageClamp> VoltageClamps { get; set; } = new();
    }

    /// <summary>
    /// Compartment data class representing a discrete compartment after segmentation (corresponds to Segment in Hines_method.py).
    /// Physical units are standardized to micron-scale SI-derived units:
    ///   Length: µm, Diameter: µm, Specific membrane capacitance: µF/cm², Axial resistivity: Ω·cm, Voltage: mV, Time: ms
    /// </summary>
    public class Compartment
    {
        /// <summary>Global unique id (0-based) used for Hines matrix indexing.</summary>
        public int GlobalId { get; set; }

        /// <summary>Parent modeling entity ID (GUID).</summary>
        public string ParentEntityId { get; set; } = string.Empty;

        /// <summary>Parent entity type identifier ("Soma" / "Axon" / "Dend").</summary>
        public string ParentEntityType { get; set; } = string.Empty;

        /// <summary>Index within the parent entity (0-based).</summary>
        public int Index { get; set; }

        /// <summary>Compartment length (µm).</summary>
        public double Length_um { get; set; }

        /// <summary>Compartment diameter (µm), taken at the component center position.</summary>
        public double Diameter_um { get; set; }

        /// <summary>Specific membrane capacitance (µF/cm²).</summary>
        public double Cm { get; set; }

        /// <summary>Axial resistivity (Ω·cm).</summary>
        public double Ra { get; set; }

        /// <summary>Dictionary of ion channel properties; key is channel name, value is ChannelProperty reference.</summary>
        public Dictionary<string, ChannelProperty> Channels { get; set; } = new();

        /// <summary>List of global ids of connected compartments (used to build coupling in the Hines matrix).</summary>
        public List<int> ConnectedIds { get; set; } = new();
    }

    /// <summary>
    /// Simulation registry responsible for managing global registration of all modeling components and performing compartmentalization.
    /// Synchronized automatically via Register/Unregister when entities are created/removed in Modeling mode.
    /// When Begin is pressed in Simulation mode, Compartmentalize is called to perform segmentation.
    /// The segmentation results can be used directly with Hines_method.py's init_segment / add_connection interfaces.
    /// </summary>
    public class SimulationRegistry
    {
        /// <summary>Current segmentation mode (by count or by length).</summary>
        public SegmentationMode Mode { get; set; } = SegmentationMode.NSeg;

        /// <summary>Number of compartments per component (used in NSeg mode).</summary>
        public int NSeg { get; set; } = 5;

        /// <summary>Compartment spacing (µm) used in LSeg mode.</summary>
        public double LSeg { get; set; } = 20.0;

        /// <summary>
        /// Dictionary of registered modeling entities, keyed by entity ID.
        /// Updated by InteractionController.OnEntityAdded/OnEntityRemoved events.
        /// </summary>
        public Dictionary<string, IVisualEntity> RegisteredEntities { get; } = new();

        /// <summary>Register an entity to the global registry.</summary>
        public void Register(IVisualEntity entity)
        {
            RegisteredEntities[entity.Id] = entity;
        }

        /// <summary>Remove an entity from the global registry.</summary>
        public void Unregister(string entityId)
        {
            RegisteredEntities.Remove(entityId);
        }

        /// <summary>
        /// Perform compartmentalization for all registered entities.
        /// Each component is split into cylindrical compartments whose diameters are taken at the component center positions.
        /// Compartments within the same entity are linearly connected; inter-entity connections are mapped from anchors to the nearest compartments.
        /// </summary>
        /// <param name="connections">Dictionary of inter-entity connections (from ConnectionController.ConnectionsById).</param>
        /// <returns>List of all compartments ordered by GlobalId.</returns>
        public List<Compartment> Compartmentalize(Dictionary<string, Connection> connections)
        {
            var compartments = new List<Compartment>();
            var entityCompartmentMap = new Dictionary<string, List<int>>();
            int globalId = 0;

            foreach (var kvp in RegisteredEntities)
            {
                var entity = kvp.Value;
                var ids = new List<int>();

                if (entity is AxonVisual axon)
                {
                    double totalLength = axon.Length;
                    int n = ComputeSegmentCount(totalLength);
                    double segLen = totalLength / n;

                    for (int i = 0; i < n; i++)
                    {
                        // t: normalized axial position of the compartment center within the component [0, 1]
                        double t = (i + 0.5) / n;
                        // Linearly interpolate between base and top radius to obtain the center radius
                        double radius = axon.BaseRadius + (axon.TopRadius - axon.BaseRadius) * t;

                        var comp = new Compartment
                        {
                            GlobalId = globalId,
                            ParentEntityId = entity.Id,
                            ParentEntityType = axon.VisualType, // "Axon" or "Dend"
                            Index = i,
                            Length_um = segLen,
                            Diameter_um = radius * 2.0,
                            Cm = entity.Cm,
                            Ra = entity.Ra,
                            Channels = new Dictionary<string, ChannelProperty>(entity.Channels)
                        };
                        ids.Add(globalId);
                        compartments.Add(comp);
                        globalId++;
                    }

                    // Linear connections between adjacent compartments within the same entity
                    for (int i = 0; i < ids.Count - 1; i++)
                    {
                        compartments[ids[i]].ConnectedIds.Add(ids[i + 1]);
                        compartments[ids[i + 1]].ConnectedIds.Add(ids[i]);
                    }
                }

                entityCompartmentMap[entity.Id] = ids;
            }

            // Handle inter-entity connections: map Connection anchor positions to corresponding compartments
            foreach (var conn in connections.Values)
            {
                if (entityCompartmentMap.TryGetValue(conn.A.Id, out var compA) &&
                    entityCompartmentMap.TryGetValue(conn.B.Id, out var compB) &&
                    compA.Count > 0 && compB.Count > 0)
                {
                    int idA = ResolveCompartmentByAnchor(conn.AnchorA, compA);
                    int idB = ResolveCompartmentByAnchor(conn.AnchorB, compB);

                    if (!compartments[idA].ConnectedIds.Contains(idB))
                        compartments[idA].ConnectedIds.Add(idB);
                    if (!compartments[idB].ConnectedIds.Contains(idA))
                        compartments[idB].ConnectedIds.Add(idA);
                }
            }

            return compartments;
        }

        /// <summary>Compute the number of compartments for a given length according to the segmentation mode.</summary>
        private int ComputeSegmentCount(double totalLength)
        {
            if (Mode == SegmentationMode.NSeg)
                return Math.Max(1, NSeg);
            return Math.Max(1, (int)Math.Ceiling(totalLength / LSeg));
        }

        /// <summary>
        /// Map an anchor's axial position parameter (AxialT) to the nearest compartment global id.
        /// Used to locate inter-entity connection endpoints to specific compartments.
        /// </summary>
        private static int ResolveCompartmentByAnchor(AnchorRef anchor, List<int> compartmentIds)
        {
            if (compartmentIds.Count <= 1)
                return compartmentIds[0];

            double t = Math.Clamp(anchor.AxialT, 0.0, 1.0);
            int idx = (int)(t * compartmentIds.Count);
            return compartmentIds[Math.Clamp(idx, 0, compartmentIds.Count - 1)];
        }

        /// <summary>
        /// Build a complete simulation payload in one step: compartmentalization + device binding.
        /// Called by MainWindow.OnBeginSimulationClick when Begin is pressed.
        /// </summary>
        /// <param name="connections">Dictionary of inter-entity connections.</param>
        /// <param name="devices">List of all placed devices in the scene.</param>
        /// <returns>SimulationData containing compartments, probes, and stimulation data.</returns>
        public SimulationData BuildSimulationData(
            Dictionary<string, Connection> connections,
            List<IAttachedDevice> devices)
        {
            // 1. Compartmentalize (also produce entityCompartmentMap for device binding)
            var compartments = new List<Compartment>();
            var entityCompartmentMap = new Dictionary<string, List<int>>();
            int globalId = 0;

            foreach (var kvp in RegisteredEntities)
            {
                var entity = kvp.Value;
                var ids = new List<int>();

                if (entity is AxonVisual axon)
                {
                    double totalLength = axon.Length;
                    int n = ComputeSegmentCount(totalLength);
                    double segLen = totalLength / n;

                    for (int i = 0; i < n; i++)
                    {
                        double t = (i + 0.5) / n;
                        double radius = axon.BaseRadius + (axon.TopRadius - axon.BaseRadius) * t;

                        var comp = new Compartment
                        {
                            GlobalId = globalId,
                            ParentEntityId = entity.Id,
                            ParentEntityType = axon.VisualType,
                            Index = i,
                            Length_um = segLen,
                            Diameter_um = radius * 2.0,
                            Cm = entity.Cm,
                            Ra = entity.Ra,
                            Channels = new Dictionary<string, ChannelProperty>(entity.Channels)
                        };
                        ids.Add(globalId);
                        compartments.Add(comp);
                        globalId++;
                    }

                    for (int i = 0; i < ids.Count - 1; i++)
                    {
                        compartments[ids[i]].ConnectedIds.Add(ids[i + 1]);
                        compartments[ids[i + 1]].ConnectedIds.Add(ids[i]);
                    }
                }

                entityCompartmentMap[entity.Id] = ids;

                // Write compartment info back to entity
                entity.CompartmentCount = ids.Count;
                entity.CompartmentIds = new List<int>(ids);
            }

            // Inter-entity connections
            foreach (var conn in connections.Values)
            {
                if (entityCompartmentMap.TryGetValue(conn.A.Id, out var compA) &&
                    entityCompartmentMap.TryGetValue(conn.B.Id, out var compB) &&
                    compA.Count > 0 && compB.Count > 0)
                {
                    int idA = ResolveCompartmentByAnchor(conn.AnchorA, compA);
                    int idB = ResolveCompartmentByAnchor(conn.AnchorB, compB);

                    if (!compartments[idA].ConnectedIds.Contains(idB))
                        compartments[idA].ConnectedIds.Add(idB);
                    if (!compartments[idB].ConnectedIds.Contains(idA))
                        compartments[idB].ConnectedIds.Add(idA);
                }
            }

            // 2. Device binding: map Probe/Stimulation/VoltageClamp spatial positions to the nearest compartment
            var probes = new List<SimProbe>();
            var stimulations = new List<SimStimulation>();
            var voltageClamps = new List<SimVoltageClamp>();
            int probeId = 0;
            int stimId = 0;
            int vcId = 0;

            foreach (var device in devices)
            {
                // Find compartment list corresponding to the device's target entity
                if (!entityCompartmentMap.TryGetValue(device.TargetEntity.Id, out var compIds) || compIds.Count == 0)
                    continue;

                int segmentId = ResolveCompartmentByAnchor(device.Anchor, compIds);

                if (device is ProbeDevice probe)
                {
                    probes.Add(new SimProbe
                    {
                        ProbeId = probeId++,
                        StartMs = probe.StartMs,
                        DurationMs = probe.DurationMs,
                        SegmentId = segmentId,
                        SourceDeviceId = device.Id
                    });
                }
                else if (device is StimulationDevice stim)
                {
                    stimulations.Add(new SimStimulation
                    {
                        StimulationId = stimId++,
                        SegmentId = segmentId,
                        Stimulation_uA = stim.Stimulation_uA,
                        StimStart = stim.StimStart,
                        StimDuration = stim.StimDuration,
                        SourceDeviceId = device.Id
                    });
                }
                else if (device is VoltageClampDevice vc)
                {
                    var simVC = new SimVoltageClamp
                    {
                        VCId = vcId++,
                        SegmentId = segmentId,
                        Rs = vc.Rs,
                        SourceDeviceId = device.Id
                    };
                    foreach (var step in vc.Protocol)
                    {
                        simVC.Protocol.Add(new SimVCStep
                        {
                            Duration = step.Duration,
                            Amplitude = step.Amplitude
                        });
                    }
                    voltageClamps.Add(simVC);
                }
            }

            return new SimulationData
            {
                Compartments = compartments,
                Probes = probes,
                Stimulations = stimulations,
                VoltageClamps = voltageClamps
            };
        }
    }
}
