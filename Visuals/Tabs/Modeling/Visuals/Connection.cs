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

using System.Windows.Media;
using System.Windows.Media.Media3D;
using HelixToolkit.Wpf;
using NeuronCAD.Visuals.Tabs.Modeling.Visuals;

namespace NeuronCAD.Visuals.Tabs.Modeling
{
    /// <summary>
    /// Visual representation of a connection, consisting of a line segment and two endpoint spheres.
    /// Each ConnectionVisual corresponds to a Connection data object.
    /// Created by ConnectionController.Add; endpoint spheres can be dragged by InteractionController.
    /// </summary>
    public sealed class ConnectionVisual
    {
        /// <summary>Root ModelVisual3D container holding the line and two endpoint spheres as child elements.</summary>
        public ModelVisual3D Visual3D { get; } = new ModelVisual3D();
        /// <summary>Visual for the connection line (DeepSkyBlue).</summary>
        public LinesVisual3D Line { get; } = new LinesVisual3D { Thickness = 1.5, Color = Colors.DeepSkyBlue };
        /// <summary>Sphere visual for endpoint A (white, draggable to change anchor position).</summary>
        public SphereVisual3D EndA { get; } = new SphereVisual3D { Radius = 0.4, Fill = System.Windows.Media.Brushes.White };
        /// <summary>Sphere visual for endpoint B (white, draggable to change anchor position).</summary>
        public SphereVisual3D EndB { get; } = new SphereVisual3D { Radius = 0.4, Fill = System.Windows.Media.Brushes.White };
        /// <summary>Associated Connection data object ID, used to index into ConnectionController dictionaries.</summary>
        public string ConnectionId { get; }

        /// <summary>
        /// Constructor: create the connection visual and add child elements to the container.
        /// Called by ConnectionController.Add.
        /// </summary>
        /// <param name="connectionId">Associated Connection ID</param>
        public ConnectionVisual(string connectionId)
        {
            ConnectionId = connectionId;
            Visual3D.Children.Add(Line);
            Visual3D.Children.Add(EndA);
            Visual3D.Children.Add(EndB);
        }

        /// <summary>
        /// Update the world coordinates of the connection endpoints.
        /// Called by ConnectionController.Update.
        /// </summary>
        /// <param name="pA">World coordinate of endpoint A</param>
        /// <param name="pB">World coordinate of endpoint B</param>
        public void Update(Point3D pA, Point3D pB)
        {
            Line.Points = new Point3DCollection { pA, pB };
            EndA.Center = pA;
            EndB.Center = pB;
        }
    }

    /// <summary>
    /// Controller that manages the lifecycle and visualization updates of Connections between entities.
    /// Holds the connection data dictionary and the corresponding visuals dictionary.
    /// Created during SharedSceneState construction; used by InteractionController (creating/dragging endpoints)
    /// and MainWindow (calls UpdateAll per frame).
    /// </summary>
    public sealed class ConnectionController
    {
        /// <summary>HelixToolkit viewport reference, used to add/remove connection Visual3D objects.</summary>
        private readonly HelixViewport3D _viewport;

        /// <summary>Dictionary of connection data, keyed by Connection.Id.</summary>
        public Dictionary<string, Connection> ConnectionsById { get; } = new();

        /// <summary>Dictionary of connection visuals, keyed by Connection.Id, corresponding to ConnectionsById.</summary>
        public Dictionary<string, ConnectionVisual> VisualsById { get; } = new();

        /// <summary>
        /// Constructor. Created by SharedSceneState.
        /// </summary>
        /// <param name="viewport">HelixToolkit viewport instance</param>
        public ConnectionController(HelixViewport3D viewport)
        {
            _viewport = viewport;
        }

        /// <summary>
        /// Add a new connection and create the corresponding visual object.
        /// Called by InteractionController.ConfirmAction (auto-connect on placement) and the right-click 'Connect' menu action.
        /// </summary>
        /// <param name="connection">Connection data object</param>
        public void Add(Connection connection)
        {
            ConnectionsById[connection.Id] = connection;

            var visual = new ConnectionVisual(connection.Id);
            VisualsById[connection.Id] = visual;
            _viewport.Children.Add(visual.Visual3D);

            Update(connection.Id);
        }

        /// <summary>
        /// Remove the specified connection and its visual object.
        /// Reserved for cleanup when deleting entities.
        /// </summary>
        /// <param name="id">Connection ID</param>
        public void Remove(string id)
        {
            if (VisualsById.TryGetValue(id, out var v))
            {
                _viewport.Children.Remove(v.Visual3D);
                VisualsById.Remove(id);
            }
            ConnectionsById.Remove(id);
        }

        /// <summary>
        /// Update the visual position for the specified connection. Uses IAnchoredEntity.TryAnchorToWorldPoint to compute endpoint world coordinates.
        /// Called by InteractionController.UpdateDraggingConnectionEndpoint (when dragging endpoints) and UpdateAll.
        /// </summary>
        /// <param name="id">Connection ID</param>
        public void Update(string id)
        {
            if (!ConnectionsById.TryGetValue(id, out var c)) return;
            if (!VisualsById.TryGetValue(id, out var v)) return;

            if (c.A is not IAnchoredEntity aEnt) return;
            if (c.B is not IAnchoredEntity bEnt) return;

            if (!aEnt.TryAnchorToWorldPoint(c.AnchorA, out Point3D pA)) return;
            if (!bEnt.TryAnchorToWorldPoint(c.AnchorB, out Point3D pB)) return;

            v.Update(pA, pB);
        }

        /// <summary>
        /// Update visual positions for all connections.
        /// Registered to CompositionTarget.Rendering by MainWindow.InitializeControllers and called each frame, and also invoked by InteractionController.UpdateObjectPosition when entities move.
        /// </summary>
        public void UpdateAll()
        {
            foreach (var id in ConnectionsById.Keys)
            {
                Update(id);
            }
        }
    }




}