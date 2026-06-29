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
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Windows.Media;
using System.Windows.Media.Media3D;

namespace NeuronCAD.Backward
{
    /// <summary>
    /// Imports standard SWC morphology files as NeuronCAD project data.
    /// Each SWC parent-child edge becomes one frustum visual entity.
    /// </summary>
    public static class SwcImportManager
    {
        private const double DefaultCm = 1.0;
        private const double DefaultRa = 100.0;
        private const double ZeroLengthTolerance = 1e-9;

        public static ProjectData LoadAsProjectData(string filePath)
        {
            if (string.IsNullOrWhiteSpace(filePath))
                throw new ArgumentException("SWC file path is required.", nameof(filePath));
            if (!File.Exists(filePath))
                throw new FileNotFoundException("SWC file was not found.", filePath);

            string sourceName = Path.GetFileNameWithoutExtension(filePath);
            string safeSourceName = SanitizeIdPart(sourceName);
            var nodes = ParseNodes(filePath);
            var alignmentOrigin = ResolveAlignmentOrigin(nodes);
            var childrenByParent = nodes.Values
                .Where(node => node.ParentId != -1)
                .GroupBy(node => node.ParentId)
                .ToDictionary(group => group.Key, group => group.OrderBy(node => node.Id).ToList());

            var project = CreateDefaultProject(sourceName);
            var edgeByParentChild = new Dictionary<(int ParentId, int ChildId), EntityData>();

            foreach (var child in nodes.Values.Where(node => node.ParentId != -1).OrderBy(node => node.Id))
            {
                if (!nodes.TryGetValue(child.ParentId, out var parent))
                    throw new InvalidDataException($"SWC node {child.Id} references missing parent {child.ParentId}.");

                double length = Distance(parent, child);
                if (length <= ZeroLengthTolerance)
                    throw new InvalidDataException($"SWC edge {parent.Id}->{child.Id} has zero length.");

                var entity = new EntityData
                {
                    Id = $"swc_{safeSourceName}_{parent.Id}_{child.Id}",
                    Type = MapEntityType(child.Type),
                    BaseRadius = parent.Radius,
                    TopRadius = child.Radius,
                    Length = length,
                    Ra = DefaultRa,
                    Cm = DefaultCm,
                    Color = MapEntityColor(child.Type).ToString(CultureInfo.InvariantCulture),
                    Transform = BuildTransform(parent, child, alignmentOrigin),
                    Channels = new Dictionary<string, ChannelData>()
                };

                project.Entities.Add(entity);
                edgeByParentChild[(parent.Id, child.Id)] = entity;
            }

            if (project.Entities.Count == 0)
                throw new InvalidDataException("SWC file does not contain any drawable parent-child edges.");

            AddTopologyConnections(project, nodes, childrenByParent, edgeByParentChild, safeSourceName);
            return project;
        }

        private static Dictionary<int, SwcNode> ParseNodes(string filePath)
        {
            var nodes = new Dictionary<int, SwcNode>();
            int lineNumber = 0;

            foreach (string rawLine in File.ReadLines(filePath))
            {
                lineNumber++;
                string line = rawLine.Trim();
                if (line.Length == 0 || line.StartsWith("#", StringComparison.Ordinal))
                    continue;

                string[] parts = line.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length < 7)
                    throw new InvalidDataException($"SWC line {lineNumber} has {parts.Length} columns; expected 7.");

                var node = new SwcNode(
                    ParseInt(parts[0], lineNumber, "id"),
                    ParseInt(parts[1], lineNumber, "type"),
                    ParseDouble(parts[2], lineNumber, "x"),
                    ParseDouble(parts[3], lineNumber, "y"),
                    ParseDouble(parts[4], lineNumber, "z"),
                    ParseDouble(parts[5], lineNumber, "radius"),
                    ParseInt(parts[6], lineNumber, "parentId"));

                if (node.Radius <= 0.0)
                    throw new InvalidDataException($"SWC line {lineNumber} node {node.Id} has non-positive radius {node.Radius}.");
                if (!nodes.TryAdd(node.Id, node))
                    throw new InvalidDataException($"SWC line {lineNumber} duplicates node id {node.Id}.");
            }

            if (nodes.Count == 0)
                throw new InvalidDataException("SWC file does not contain any nodes.");

            foreach (var node in nodes.Values)
            {
                if (node.ParentId != -1 && !nodes.ContainsKey(node.ParentId))
                    throw new InvalidDataException($"SWC node {node.Id} references missing parent {node.ParentId}.");
            }

            return nodes;
        }

        private static SwcPoint ResolveAlignmentOrigin(IReadOnlyDictionary<int, SwcNode> nodes)
        {
            var soma = nodes.Values
                .Where(node => node.Type == 1)
                .OrderBy(node => node.Id)
                .FirstOrDefault();
            if (soma != null)
                return new SwcPoint(soma.X, soma.Y, soma.Z);

            var root = nodes.Values
                .Where(node => node.ParentId == -1)
                .OrderBy(node => node.Id)
                .FirstOrDefault();
            if (root != null)
                return new SwcPoint(root.X, root.Y, root.Z);

            throw new InvalidDataException("SWC file does not contain a soma or root node for alignment.");
        }

        private static void AddTopologyConnections(
            ProjectData project,
            IReadOnlyDictionary<int, SwcNode> nodes,
            IReadOnlyDictionary<int, List<SwcNode>> childrenByParent,
            IReadOnlyDictionary<(int ParentId, int ChildId), EntityData> edgeByParentChild,
            string safeSourceName)
        {
            foreach (var parent in nodes.Values.OrderBy(node => node.Id))
            {
                if (!childrenByParent.TryGetValue(parent.Id, out var children) || children.Count == 0)
                    continue;

                if (parent.ParentId != -1 &&
                    edgeByParentChild.TryGetValue((parent.ParentId, parent.Id), out var incomingEntity))
                {
                    foreach (var child in children)
                    {
                        if (!edgeByParentChild.TryGetValue((parent.Id, child.Id), out var outgoingEntity))
                            continue;

                        project.Connections.Add(CreateConnection(
                            $"swc_{safeSourceName}_conn_{parent.ParentId}_{parent.Id}_{child.Id}",
                            incomingEntity.Id,
                            outgoingEntity.Id,
                            "AxonCapEnd",
                            "AxonCapStart"));
                    }
                }
                else if (parent.ParentId == -1 && children.Count > 1)
                {
                    var rootHub = edgeByParentChild[(parent.Id, children[0].Id)];
                    foreach (var child in children.Skip(1))
                    {
                        var sibling = edgeByParentChild[(parent.Id, child.Id)];
                        project.Connections.Add(CreateConnection(
                            $"swc_{safeSourceName}_root_conn_{parent.Id}_{children[0].Id}_{child.Id}",
                            rootHub.Id,
                            sibling.Id,
                            "AxonCapStart",
                            "AxonCapStart"));
                    }
                }
            }
        }

        private static ConnectionData CreateConnection(
            string id,
            string entityAId,
            string entityBId,
            string anchorModeA,
            string anchorModeB)
        {
            return new ConnectionData
            {
                Id = id,
                EntityA_Id = entityAId,
                EntityB_Id = entityBId,
                AnchorA = new AnchorData { Mode = anchorModeA, AxialT = anchorModeA == "AxonCapEnd" ? 1.0 : 0.0 },
                AnchorB = new AnchorData { Mode = anchorModeB, AxialT = anchorModeB == "AxonCapEnd" ? 1.0 : 0.0 },
                Weight = 1.0
            };
        }

        private static ProjectData CreateDefaultProject(string sourceName)
        {
            return new ProjectData
            {
                ProjectId = Guid.NewGuid().ToString(),
                ProjectName = sourceName,
                GlobalEnvironment = new GlobalEnvironmentData(),
                E_TABLE =
                {
                    ["Na"] = new ETableEntry { E = 50.0 },
                    ["K"] = new ETableEntry { E = -77.0 },
                    ["L"] = new ETableEntry { E = -54.4 }
                },
                HH_PARAMS = CreateDefaultHHParams(),
                CA_PARAMS = CreateDefaultCaParams(),
                Segmentation = new SegmentationData()
            };
        }

        private static Dictionary<string, double> CreateDefaultHHParams()
        {
            return new Dictionary<string, double>
            {
                ["vtraub"] = -63.0,
                ["alpha_m_A"] = 0.32,
                ["alpha_m_V"] = 13.0,
                ["alpha_m_k"] = 4.0,
                ["beta_m_A"] = 0.28,
                ["beta_m_V"] = 40.0,
                ["beta_m_k"] = 5.0,
                ["alpha_h_A"] = 0.128,
                ["alpha_h_V"] = 17.0,
                ["alpha_h_k"] = 18.0,
                ["beta_h_A"] = 4.0,
                ["beta_h_V"] = 40.0,
                ["beta_h_k"] = 5.0,
                ["alpha_n_A"] = 0.032,
                ["alpha_n_V"] = 15.0,
                ["alpha_n_k"] = 5.0,
                ["beta_n_A"] = 0.5,
                ["beta_n_V"] = 10.0,
                ["beta_n_k"] = 40.0
            };
        }

        private static Dictionary<string, double> CreateDefaultCaParams()
        {
            return new Dictionary<string, double>
            {
                ["shift"] = -1.0,
                ["actshift"] = 0.0,
                ["inf_mT_Vh"] = 57.0,
                ["inf_mT_k"] = 6.2,
                ["inf_hT_Vh"] = 81.0,
                ["inf_hT_k"] = 4.0,
                ["tau_mT_base"] = 0.612,
                ["tau_mT_V1"] = 132.0,
                ["tau_mT_k1"] = 16.7,
                ["tau_mT_V2"] = 16.8,
                ["tau_mT_k2"] = 18.2,
                ["tau_mT_Q10"] = 2.5,
                ["tau_mT_Tref"] = 24.0,
                ["tau_hT_Vthresh"] = -80.0,
                ["tau_hT_V1"] = 467.0,
                ["tau_hT_k1"] = 66.6,
                ["tau_hT_base"] = 28.0,
                ["tau_hT_V2"] = 22.0,
                ["tau_hT_k2"] = 10.5,
                ["tau_hT_Q10"] = 2.5,
                ["tau_hT_Tref"] = 24.0
            };
        }

        private static double[] BuildTransform(SwcNode parent, SwcNode child, SwcPoint origin)
        {
            var direction = new Vector3D(child.X - parent.X, child.Y - parent.Y, child.Z - parent.Z);
            direction.Normalize();

            var localZ = new Vector3D(0, 0, 1);
            var matrix = Matrix3D.Identity;
            var axis = Vector3D.CrossProduct(localZ, direction);
            if (axis.LengthSquared > 1e-10)
            {
                axis.Normalize();
                double angle = Vector3D.AngleBetween(localZ, direction);
                matrix.Rotate(new Quaternion(axis, angle));
            }
            else if (Vector3D.DotProduct(localZ, direction) < 0)
            {
                matrix.Rotate(new Quaternion(new Vector3D(1, 0, 0), 180));
            }

            matrix.Translate(new Vector3D(parent.X - origin.X, parent.Y - origin.Y, parent.Z - origin.Z));
            return
            [
                matrix.M11, matrix.M12, matrix.M13, matrix.M14,
                matrix.M21, matrix.M22, matrix.M23, matrix.M24,
                matrix.M31, matrix.M32, matrix.M33, matrix.M34,
                matrix.OffsetX, matrix.OffsetY, matrix.OffsetZ, matrix.M44
            ];
        }

        private static double Distance(SwcNode a, SwcNode b)
        {
            double dx = b.X - a.X;
            double dy = b.Y - a.Y;
            double dz = b.Z - a.Z;
            return Math.Sqrt(dx * dx + dy * dy + dz * dz);
        }

        private static string MapEntityType(int swcType)
        {
            return swcType switch
            {
                1 => "Soma",
                2 => "Axon",
                _ => "Dend"
            };
        }

        private static Color MapEntityColor(int swcType)
        {
            return swcType switch
            {
                1 => Colors.DodgerBlue,
                2 => Colors.LimeGreen,
                _ => Colors.MediumPurple
            };
        }

        private static int ParseInt(string value, int lineNumber, string fieldName)
        {
            if (int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int result))
                return result;

            throw new InvalidDataException($"SWC line {lineNumber} has invalid {fieldName} value '{value}'.");
        }

        private static double ParseDouble(string value, int lineNumber, string fieldName)
        {
            if (double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out double result) &&
                double.IsFinite(result))
            {
                return result;
            }

            throw new InvalidDataException($"SWC line {lineNumber} has invalid {fieldName} value '{value}'.");
        }

        private static string SanitizeIdPart(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return "morphology";

            var builder = new StringBuilder(value.Length);
            foreach (char ch in value)
            {
                builder.Append(char.IsLetterOrDigit(ch) ? ch : '_');
            }

            string result = builder.ToString().Trim('_');
            return result.Length == 0 ? "morphology" : result;
        }

        private sealed record SwcNode(
            int Id,
            int Type,
            double X,
            double Y,
            double Z,
            double Radius,
            int ParentId);

        private sealed record SwcPoint(double X, double Y, double Z);
    }
}
