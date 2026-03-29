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
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Media3D;
using HelixToolkit.Wpf;
using NeuronCAD.Visuals.Tabs.Modeling;
using NeuronCAD.Visuals.Tabs.Modeling.Visuals;
using NeuronCAD.Visuals.Tabs.Simulation;

namespace NeuronCAD.Visuals.Tabs.Shared
{
    /// <summary>
    /// Singleton scene state shared across modes; holds core data such as the entity list, connection controller, and device list.
    /// Modeling and Simulation interaction controllers share the same 3D scene data by referencing this object.
    /// Created by MainWindow.InitializeControllers and injected into controllers.
    /// </summary>
    public class SharedSceneState
    {
        /// <summary>Reference to the HelixToolkit 3D viewport control, used to add/remove Visual3D children.</summary>
        public HelixViewport3D HelixViewport { get; }

        /// <summary>Viewport controller responsible for environment initialization (grid, lighting), gesture configuration, and raycasting computations.</summary>
        public ViewportController ViewportController { get; }

        /// <summary>Connection controller that manages creation, deletion, and visualization updates of connections between entities.</summary>
        public ConnectionController ConnectionController { get; }

        /// <summary>Simulation registry responsible for global registration of modeling components and compartmentalization.</summary>
        public SimulationRegistry SimulationRegistry { get; }

        /// <summary>
        /// List of modeling entities (Soma, Axon, Dend). Created and modified by the InteractionController in Modeling mode; read-only in Simulation mode.
        /// </summary>
        public List<IVisualEntity> Entities { get; } = new();

        /// <summary>
        /// List of attached devices (Stimulation, Probe). Created and modified by SimulationInteractionController in Simulation mode; read-only in Modeling mode.
        /// </summary>
        public List<IAttachedDevice> Devices { get; } = new();

        /// <summary>
        /// Simulation data from the last completed run (includes compartmentalization results and probe/stimulation bindings).
        /// Written by MainWindow.OnBeginSimulationClick after a successful simulation; read by the Reporting panel for probe mapping.
        /// </summary>
        public SimulationData? LastSimulationData { get; set; }

        /// <summary>
        /// Constructor: creates viewport and connection controllers from the provided HelixViewport3D.
        /// Called by MainWindow.InitializeControllers.
        /// </summary>
        /// <param name="helixViewport">HelixViewport3D instance defined in XAML</param>
        public SharedSceneState(HelixViewport3D helixViewport)
        {
            HelixViewport = helixViewport;
            ViewportController = new ViewportController(helixViewport);
            ConnectionController = new ConnectionController(helixViewport);
            SimulationRegistry = new SimulationRegistry();
        }
    }

    /// <summary>
    /// WPF visual-tree helper providing Visual3D hierarchy query methods.
    /// Widely used by InteractionController and SimulationInteractionController hit-test logic to determine whether a raycast-hit Visual3D belongs to a particular entity or device.
    /// </summary>
    public static class VisualTreeUtils
    {
        /// <summary>
        /// Determine whether hitVisual is selfVisual or a child Visual3D of it.
        /// Implemented by traversing up the visual tree. Called by InteractionController.HitTestEntity, SimulationInteractionController.HitTestDevice, etc.
        /// </summary>
        /// <param name="hitVisual">The Visual3D hit by a raycast</param>
        /// <param name="selfVisual">The root Visual3D of the target entity</param>
        /// <returns>true if hitVisual belongs to the visual tree of selfVisual</returns>
        public static bool IsSelfOrChild(Visual3D hitVisual, Visual3D selfVisual)
        {
            if (hitVisual == selfVisual) return true;
            DependencyObject curr = hitVisual;
            while (curr != null)
            {
                if (curr == selfVisual) return true;
                curr = VisualTreeHelper.GetParent(curr);
            }
            return false;
        }
    }
}
