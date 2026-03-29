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
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Media3D;
using NeuronCAD.Backward;
using NeuronCAD.Visuals.Tabs.Modeling.Visuals;
using NeuronCAD.Visuals.Tabs.Shared;
using NeuronCAD.Visuals.Tabs.Simulation;

namespace NeuronCAD.Visuals.Tabs.Reporting
{
    /// <summary>
    /// Controller for the left-side panel in Reporting mode.
    /// Provides two sub-tabs: Components and Probes:
    ///   Components — lists modeling entities and their properties; supports plotting variable time series for a selected compartment.
    ///   Probes — lists simulation probes; supports plotting phase portraits with two selected variables.
    /// </summary>
    public class ReportingPanelController
    {
        private readonly StackPanel _componentsContainer;
        private readonly StackPanel _probesContainer;
        private readonly ReportingInteractionController _interaction;
        private readonly SharedSceneState _scene;
        private readonly Action<Point3D>? _navigateToPoint;

        // Entity tracking
        private readonly Dictionary<string, Expander> _entityExpanders = new();
        private readonly Dictionary<string, TextBlock> _entityCompartmentLabels = new();
        private readonly Dictionary<string, int> _entitySelectedCompartment = new();
        private readonly Dictionary<string, ComboBox> _entityVarCombos = new();
        private readonly Dictionary<string, TextBox> _entityStartTimeBoxes = new();
        private readonly Dictionary<string, TextBox> _entityEndTimeBoxes = new();

        // Probe tracking
        private readonly Dictionary<string, Expander> _probeExpanders = new();

        private bool _suppressExpandEvent;

        private static readonly SolidColorBrush ItemBg = new(Color.FromRgb(0x2A, 0x2A, 0x2A));
        private static readonly SolidColorBrush SelectedBorder = new(Colors.Orange);
        private static readonly SolidColorBrush DefaultBorder = new(Color.FromRgb(0x3F, 0x3F, 0x46));

        public ReportingPanelController(
            StackPanel componentsContainer,
            StackPanel probesContainer,
            ReportingInteractionController interaction,
            SharedSceneState scene,
            Action<Point3D>? navigateToPoint = null)
        {
            _componentsContainer = componentsContainer;
            _probesContainer = probesContainer;
            _interaction = interaction;
            _scene = scene;
            _navigateToPoint = navigateToPoint;

            _interaction.OnEntitySelected += HandleEntitySelected;
            _interaction.OnCompartmentSelected += HandleCompartmentSelected;
        }

        /// <summary>
        /// Rebuilds the contents of both sub-tab panels.
        /// </summary>
        public void Rebuild()
        {
            RebuildComponents();
            RebuildProbes();
        }

        #region Components Tab

        private void RebuildComponents()
        {
            _componentsContainer.Children.Clear();
            _entityExpanders.Clear();
            _entityCompartmentLabels.Clear();
            _entitySelectedCompartment.Clear();
            _entityVarCombos.Clear();
            _entityStartTimeBoxes.Clear();
            _entityEndTimeBoxes.Clear();

            foreach (var entity in _scene.Entities)
            {
                var expander = BuildEntityExpander(entity);
                _entityExpanders[entity.Id] = expander;
                _componentsContainer.Children.Add(expander);
            }

            if (_componentsContainer.Children.Count == 0)
            {
                _componentsContainer.Children.Add(new TextBlock
                {
                    Text = "No modeling components.\nAdd entities in Modeling mode.",
                    Foreground = new SolidColorBrush(Color.FromRgb(0x88, 0x88, 0x88)),
                    FontSize = 13,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    Margin = new Thickness(0, 30, 0, 0),
                    TextAlignment = TextAlignment.Center
                });
            }
        }

        private Expander BuildEntityExpander(IVisualEntity entity)
        {
            string typeName = entity switch
            {
                DendVisual => "Dend",
                AxonVisual a => a.VisualType,
                _ => "Entity"
            };

            var expander = new Expander
            {
                Header = $"{typeName}  [{entity.Id[..8]}]",
                Foreground = Brushes.White,
                Background = ItemBg,
                BorderBrush = DefaultBorder,
                BorderThickness = new Thickness(1),
                Margin = new Thickness(0, 2, 0, 2),
                Padding = new Thickness(6)
            };

            var stack = new StackPanel { Margin = new Thickness(4) };

            // --- Properties ---
            AddLabel(stack, $"Type: {typeName}");
            AddLabel(stack, $"Cm: {entity.Cm} µF/cm²");
            AddLabel(stack, $"Ra: {entity.Ra} Ω·cm");

            if (entity is AxonVisual axon)
            {
                AddLabel(stack, $"Length: {axon.Length:F2} µm");
                AddLabel(stack, $"Base R: {axon.BaseRadius:F2} µm");
                AddLabel(stack, $"Top R: {axon.TopRadius:F2} µm");
            }

            if (entity.Channels.Count > 0)
            {
                AddLabel(stack, "Channels:");
                foreach (var ch in entity.Channels)
                    AddLabel(stack, $"  {ch.Key}: g={ch.Value.G_ion_channel} µS/cm²");
            }

            int compCount = entity.CompartmentCount;
            AddLabel(stack, $"Compartments: {compCount}", FontWeights.Bold);

            // --- Variable selection ---
            AddLabel(stack, "Variable:", topMargin: 8);
            var varCombo = new ComboBox
            {
                Background = new SolidColorBrush(Color.FromRgb(0x2A, 0x2A, 0x2A)),
                Foreground = Brushes.White,
                Margin = new Thickness(0, 2, 0, 4),
                FontSize = 11
            };
            foreach (var v in new[] { "V", "m", "h", "n", "Ca", "mT", "hT" }) varCombo.Items.Add(v);
            varCombo.SelectedIndex = 0;
            stack.Children.Add(varCombo);
            _entityVarCombos[entity.Id] = varCombo;

            // --- Time range ---
            AddLabel(stack, "Start (ms):");
            var tbStart = MakeTextBox("0");
            stack.Children.Add(tbStart);
            _entityStartTimeBoxes[entity.Id] = tbStart;

            AddLabel(stack, "End (ms):");
            var tbEnd = MakeTextBox("20");
            stack.Children.Add(tbEnd);
            _entityEndTimeBoxes[entity.Id] = tbEnd;

            // --- Selected compartment display ---
            var compartmentLabel = new TextBlock
            {
                Text = compCount > 0
                    ? "Selected Compartment: (click in viewport)"
                    : "No compartments (run simulation first)",
                Foreground = new SolidColorBrush(Color.FromRgb(0xFF, 0xD7, 0x00)),
                FontSize = 11,
                Margin = new Thickness(0, 4, 0, 4),
                TextWrapping = TextWrapping.Wrap
            };
            stack.Children.Add(compartmentLabel);
            _entityCompartmentLabels[entity.Id] = compartmentLabel;

            // --- Plot button ---
            var btnPlot = MakeButton("Plot Variable", Color.FromRgb(0x00, 0x7A, 0xCC), compCount > 0);
            string eid = entity.Id;
            btnPlot.Click += async (s, e) => await OnPlotVariableClick(eid, btnPlot);
            stack.Children.Add(btnPlot);

            expander.Content = stack;

            expander.Expanded += (s, e) =>
            {
                if (_suppressExpandEvent) return;
                _navigateToPoint?.Invoke(entity.CenterPosition);
                _interaction.SelectEntity(entity);
            };

            return expander;
        }

        private async System.Threading.Tasks.Task OnPlotVariableClick(string entityId, Button btn)
        {
            if (!_entitySelectedCompartment.TryGetValue(entityId, out int segId))
            {
                MessageBox.Show("Please click to select a compartment in the viewport first.", "Reporting",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
            string varLabel = _entityVarCombos[entityId].SelectedItem?.ToString() ?? "V";
            if (!double.TryParse(_entityStartTimeBoxes[entityId].Text, out double startMs)) startMs = 0;
            if (!double.TryParse(_entityEndTimeBoxes[entityId].Text, out double endMs)) endMs = 20;

            try
            {
                btn.IsEnabled = false;
                await SimulationRunner.CallPlotVariableOverTime(segId, varLabel, startMs, endMs);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Plot failed:\n{ex.Message}", "Plot Error",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                btn.IsEnabled = true;
            }
        }

        #endregion

        #region Probes Tab

        private void RebuildProbes()
        {
            _probesContainer.Children.Clear();
            _probeExpanders.Clear();

            var simData = _scene.LastSimulationData;
            if (simData == null || simData.Probes.Count == 0)
            {
                _probesContainer.Children.Add(new TextBlock
                {
                    Text = "No probe data.\nRun simulation with probes first.",
                    Foreground = new SolidColorBrush(Color.FromRgb(0x88, 0x88, 0x88)),
                    FontSize = 13,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    Margin = new Thickness(0, 30, 0, 0),
                    TextAlignment = TextAlignment.Center
                });
                return;
            }

            var deviceMap = _scene.Devices
                .Where(d => d.Type == DeviceType.Probe)
                .ToDictionary(d => d.Id, d => d);

            foreach (var simProbe in simData.Probes)
            {
                deviceMap.TryGetValue(simProbe.SourceDeviceId, out var device);
                var expander = BuildProbeExpander(simProbe, device);
                _probeExpanders[simProbe.SourceDeviceId] = expander;
                _probesContainer.Children.Add(expander);
            }
        }

        private Expander BuildProbeExpander(SimProbe simProbe, IAttachedDevice? device)
        {
            string targetInfo = device != null
                ? $"Target: {(device.TargetEntity is DendVisual ? "Dend" : device.TargetEntity is SomaVisual ? "Soma" : "Axon")}"
                : "Target: unknown";

            var expander = new Expander
            {
                Header = $"Probe #{simProbe.ProbeId}  [Seg {simProbe.SegmentId}]",
                Foreground = Brushes.White,
                Background = ItemBg,
                BorderBrush = DefaultBorder,
                BorderThickness = new Thickness(1),
                Margin = new Thickness(0, 2, 0, 2),
                Padding = new Thickness(6)
            };

            var stack = new StackPanel { Margin = new Thickness(4) };

            AddLabel(stack, targetInfo);
            AddLabel(stack, $"Segment ID: {simProbe.SegmentId}");
            AddLabel(stack, $"Start: {simProbe.StartMs} ms");
            AddLabel(stack, $"Duration: {simProbe.DurationMs} ms");

            // X-axis variable
            AddLabel(stack, "X-axis variable:", topMargin: 8);
            var xCombo = new ComboBox
            {
                Background = new SolidColorBrush(Color.FromRgb(0x2A, 0x2A, 0x2A)),
                Foreground = Brushes.White,
                Margin = new Thickness(0, 2, 0, 4),
                FontSize = 11
            };
            foreach (var v in new[] { "V", "m", "h", "n", "Ca", "mT", "hT" }) xCombo.Items.Add(v);
            xCombo.SelectedIndex = 0;
            stack.Children.Add(xCombo);

            // Y-axis variable
            AddLabel(stack, "Y-axis variable:");
            var yCombo = new ComboBox
            {
                Background = new SolidColorBrush(Color.FromRgb(0x2A, 0x2A, 0x2A)),
                Foreground = Brushes.White,
                Margin = new Thickness(0, 2, 0, 4),
                FontSize = 11
            };
            foreach (var v in new[] { "V", "m", "h", "n", "Ca", "mT", "hT" }) yCombo.Items.Add(v);
            yCombo.SelectedIndex = 3;
            stack.Children.Add(yCombo);

            // Plot button
            var btnPlot = MakeButton("Plot Phase Portrait", Color.FromRgb(0x00, 0x80, 0x80), true);
            int probeId = simProbe.ProbeId;
            btnPlot.Click += async (s, e) =>
            {
                string xVar = xCombo.SelectedItem?.ToString() ?? "V";
                string yVar = yCombo.SelectedItem?.ToString() ?? "n";
                if (xVar == yVar)
                {
                    MessageBox.Show("X-axis and Y-axis variables cannot be the same.", "Reporting",
                        MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }
                try
                {
                    btnPlot.IsEnabled = false;
                    await SimulationRunner.CallShowPhasePortrait(probeId, xVar, yVar);
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"Plot failed:\n{ex.Message}", "Plot Error",
                        MessageBoxButton.OK, MessageBoxImage.Error);
                }
                finally
                {
                    btnPlot.IsEnabled = true;
                }
            };
            stack.Children.Add(btnPlot);

            expander.Content = stack;

            expander.Expanded += (s, e) =>
            {
                if (_suppressExpandEvent || device == null) return;
                _navigateToPoint?.Invoke(device.TargetEntity.CenterPosition);
            };

            return expander;
        }

        #endregion

        #region Event Handlers

        private void HandleEntitySelected(IVisualEntity? entity)
        {
            _suppressExpandEvent = true;
            foreach (var kvp in _entityExpanders)
            {
                kvp.Value.IsExpanded = false;
                kvp.Value.BorderBrush = DefaultBorder;
            }
            if (entity != null && _entityExpanders.TryGetValue(entity.Id, out var exp))
            {
                exp.IsExpanded = true;
                exp.BorderBrush = SelectedBorder;
                exp.BringIntoView();
            }
            _suppressExpandEvent = false;
        }

        private void HandleCompartmentSelected(int globalId, string parentEntityId)
        {
            _entitySelectedCompartment[parentEntityId] = globalId;
            if (_entityCompartmentLabels.TryGetValue(parentEntityId, out var lbl))
                lbl.Text = $"Selected Compartment: ID {globalId}";
        }

        #endregion

        #region Helpers

        private static TextBox MakeTextBox(string text)
        {
            return new TextBox
            {
                Text = text,
                Background = new SolidColorBrush(Color.FromRgb(0x2A, 0x2A, 0x2A)),
                Foreground = Brushes.White,
                BorderBrush = new SolidColorBrush(Color.FromRgb(0x55, 0x55, 0x55)),
                Padding = new Thickness(4),
                Margin = new Thickness(0, 2, 0, 4),
                FontSize = 11
            };
        }

        private static Button MakeButton(string content, Color bg, bool enabled)
        {
            return new Button
            {
                Content = content,
                Background = new SolidColorBrush(bg),
                Foreground = Brushes.White,
                FontWeight = FontWeights.Bold,
                Padding = new Thickness(8, 5, 8, 5),
                Margin = new Thickness(0, 6, 0, 0),
                HorizontalAlignment = HorizontalAlignment.Stretch,
                Cursor = Cursors.Hand,
                IsEnabled = enabled
            };
        }

        private static void AddLabel(StackPanel parent, string text, FontWeight? weight = null, double topMargin = 0)
        {
            parent.Children.Add(new TextBlock
            {
                Text = text,
                Foreground = new SolidColorBrush(Color.FromRgb(0xCC, 0xCC, 0xCC)),
                FontSize = 11,
                Margin = new Thickness(0, topMargin, 0, 1),
                FontWeight = weight ?? FontWeights.Normal,
                TextWrapping = TextWrapping.Wrap
            });
        }

        #endregion
    }
}
