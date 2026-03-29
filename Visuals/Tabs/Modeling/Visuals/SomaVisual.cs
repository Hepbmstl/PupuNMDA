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

using System.Windows.Media;
using System.Windows.Media.Media3D;

namespace NeuronCAD.Visuals.Tabs.Modeling.Visuals
{
    /// <summary>
    /// Visual entity for the cell body (Soma), rendered as a frustum (reuses AxonVisual logic).
    /// Inherits AxonVisual and injects the "Soma" type identifier in the constructor, following the same pattern as DendVisual.
    /// Created by MainWindow.OnAddSomaClick and enters the placement flow via InteractionController.StartPlacing.
    /// </summary>
    public class SomaVisual : AxonVisual
    {
        /// <summary>
        /// Constructor that creates the Soma visual entity.
        /// Automatically sets VisualType to "Soma".
        /// </summary>
        public SomaVisual(Point3D start, Point3D end, double radius, Color color)
            : base(start, end, radius, color, "Soma")
        {
        }
    }
}