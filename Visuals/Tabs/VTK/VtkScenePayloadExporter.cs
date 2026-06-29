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

using System.Globalization;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Windows.Media;
using System.Windows.Media.Media3D;
using NeuronCAD.Visuals.Tabs.Modeling;
using NeuronCAD.Visuals.Tabs.Modeling.Visuals;
using NeuronCAD.Visuals.Tabs.Shared;
using NeuronCAD.Visuals.Tabs.Simulation;

namespace NeuronCAD.Visuals.Tabs.VTK
{
    /// <summary>
    /// Exports the live Helix scene plus SimulationData compartment mapping into a small Python-friendly payload.
    /// </summary>
    public static class VtkScenePayloadExporter
    {
        public static string PayloadDirectory => Path.Combine(Path.GetTempPath(), "NeuronCAD", "VTK", "Payloads");

        private static readonly JsonSerializerOptions JsonOptions = new()
        {
            WriteIndented = false,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        };

        public static string ExportToTempFile(SharedSceneState scene)
        {
            string directory = PayloadDirectory;
            Directory.CreateDirectory(directory);

            string path = Path.Combine(directory, $"scene_{DateTime.Now:yyyyMMdd_HHmmss_fff}_{Guid.NewGuid():N}.json");
            File.WriteAllText(path, JsonSerializer.Serialize(BuildPayload(scene), JsonOptions));
            return path;
        }

        private static VtkScenePayload BuildPayload(SharedSceneState scene)
        {
            var payload = new VtkScenePayload
            {
                Source = "HelixScene",
                CreatedAtUtc = DateTime.UtcNow.ToString("O", CultureInfo.InvariantCulture),
            };

            foreach (var entity in scene.Entities)
            {
                if (entity is not AxonVisual axon)
                    continue;

                payload.Entities.Add(new VtkSceneEntity
                {
                    Id = entity.Id,
                    Type = axon.VisualType,
                    Length = axon.Length,
                    BaseRadius = axon.BaseRadius,
                    TopRadius = axon.TopRadius,
                    Transform = ToArray(entity.Visual3D.Transform?.Value ?? Matrix3D.Identity),
                    Color = ToArgb(entity.CurrentColor),
                    Channels = entity.Channels.ToDictionary(
                        pair => pair.Key,
                        pair => new VtkSceneChannel
                        {
                            G = pair.Value.G_ion_channel,
                            IsPermeability = pair.Value.IsPermeability
                        },
                        StringComparer.OrdinalIgnoreCase),
                    CompartmentIds = new List<int>(entity.CompartmentIds)
                });
            }

            if (scene.LastSimulationData != null)
            {
                var countsByEntity = scene.LastSimulationData.Compartments
                    .GroupBy(c => c.ParentEntityId)
                    .ToDictionary(g => g.Key, g => g.Count(), StringComparer.OrdinalIgnoreCase);

                foreach (var compartment in scene.LastSimulationData.Compartments)
                {
                    countsByEntity.TryGetValue(compartment.ParentEntityId, out int count);
                    count = Math.Max(1, count);

                    payload.Compartments.Add(new VtkSceneCompartment
                    {
                        GlobalId = compartment.GlobalId,
                        ParentEntityId = compartment.ParentEntityId,
                        ParentEntityType = compartment.ParentEntityType,
                        Index = compartment.Index,
                        AxialStart = Math.Clamp((double)compartment.Index / count, 0.0, 1.0),
                        AxialEnd = Math.Clamp((double)(compartment.Index + 1) / count, 0.0, 1.0)
                    });
                }
            }

            foreach (var connection in scene.ConnectionController.ConnectionsById.Values)
            {
                payload.Connections.Add(new VtkSceneConnection
                {
                    EntityAId = connection.A.Id,
                    EntityBId = connection.B.Id,
                    AnchorA = FromAnchor(connection.AnchorA),
                    AnchorB = FromAnchor(connection.AnchorB)
                });
            }

            foreach (var device in scene.Devices)
            {
                payload.Devices.Add(new VtkSceneDevice
                {
                    Type = device.Type.ToString(),
                    TargetEntityId = device.TargetEntity.Id,
                    Anchor = FromAnchor(device.Anchor)
                });
            }

            return payload;
        }

        private static VtkSceneAnchor FromAnchor(AnchorRef anchor)
        {
            return new VtkSceneAnchor
            {
                Mode = anchor.Mode.ToString(),
                AxialT = anchor.AxialT,
                Angle = anchor.Angle
            };
        }

        private static string ToArgb(Color color)
        {
            return $"#{color.A:X2}{color.R:X2}{color.G:X2}{color.B:X2}";
        }

        private static double[] ToArray(Matrix3D matrix)
        {
            return
            [
                matrix.M11, matrix.M12, matrix.M13, matrix.M14,
                matrix.M21, matrix.M22, matrix.M23, matrix.M24,
                matrix.M31, matrix.M32, matrix.M33, matrix.M34,
                matrix.OffsetX, matrix.OffsetY, matrix.OffsetZ, matrix.M44
            ];
        }

        private sealed class VtkScenePayload
        {
            public string Source { get; set; } = "";
            public string CreatedAtUtc { get; set; } = "";
            public List<VtkSceneEntity> Entities { get; set; } = new();
            public List<VtkSceneCompartment> Compartments { get; set; } = new();
            public List<VtkSceneConnection> Connections { get; set; } = new();
            public List<VtkSceneDevice> Devices { get; set; } = new();
        }

        private sealed class VtkSceneEntity
        {
            public string Id { get; set; } = "";
            public string Type { get; set; } = "";
            public double Length { get; set; }
            public double BaseRadius { get; set; }
            public double TopRadius { get; set; }
            public double[] Transform { get; set; } = [];
            public string Color { get; set; } = "";
            public Dictionary<string, VtkSceneChannel> Channels { get; set; } = new(StringComparer.OrdinalIgnoreCase);
            public List<int> CompartmentIds { get; set; } = new();
        }

        private sealed class VtkSceneChannel
        {
            public double G { get; set; }
            public bool IsPermeability { get; set; }
        }

        private sealed class VtkSceneCompartment
        {
            public int GlobalId { get; set; }
            public string ParentEntityId { get; set; } = "";
            public string ParentEntityType { get; set; } = "";
            public int Index { get; set; }
            public double AxialStart { get; set; }
            public double AxialEnd { get; set; }
        }

        private sealed class VtkSceneConnection
        {
            public string EntityAId { get; set; } = "";
            public string EntityBId { get; set; } = "";
            public VtkSceneAnchor AnchorA { get; set; } = new();
            public VtkSceneAnchor AnchorB { get; set; } = new();
        }

        private sealed class VtkSceneDevice
        {
            public string Type { get; set; } = "";
            public string TargetEntityId { get; set; } = "";
            public VtkSceneAnchor Anchor { get; set; } = new();
        }

        private sealed class VtkSceneAnchor
        {
            public string Mode { get; set; } = "";
            public double AxialT { get; set; }
            public double Angle { get; set; }
        }
    }
}
