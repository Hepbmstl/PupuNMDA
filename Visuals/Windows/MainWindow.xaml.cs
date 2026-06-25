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
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Media3D;
using System.Windows.Threading;
using Microsoft.Win32;
using Python.Runtime;
using NeuronCAD.Backward;
using NeuronCAD.Visuals.Tabs.Shared;
using NeuronCAD.Visuals.Tabs.Modeling;
using NeuronCAD.Visuals.Tabs.Modeling.Visuals;
using NeuronCAD.Visuals.Tabs.Simulation;
using NeuronCAD.Visuals.Tabs.Reporting;

namespace NeuronCAD.Visuals.Windows
{
    /// <summary>
    /// Application main window serving as the top-level container for all UI components.
    /// Responsibilities: top-level tab switching (Modeling/Simulating/Reporting),
    /// viewport mouse event routing, HUD overlay management, toolbar handling, and
    /// edit popup control.
    /// Launched by App.xaml's StartupUri.
    /// </summary>
    public partial class MainWindow : Window
    {
        /// <summary>Scene state shared across modes, holding entity lists, connection controller and device lists. Created by InitializeControllers.</summary>
        private SharedSceneState _scene = null!;

        /// <summary>Modeling-mode interaction controller handling placement, selection, movement, and connection of entities. Created by InitializeControllers.</summary>
        private InteractionController _modelingInteraction = null!;
        /// <summary>Simulation-mode interaction controller handling insertion and dragging of stimulation/probe devices. Created by InitializeControllers.</summary>
        private SimulationInteractionController _simulationInteraction = null!;

        /// <summary>Left-side properties panel controller for modeling mode; manages entity property edit cards and ion channel selector. Created by InitializeControllers.</summary>
        private PropertiesPanelController _propertiesPanelController = null!;
        /// <summary>Left-side panel controller for simulation mode; manages parameter configuration cards for stimulation/probe devices. Created by InitializeControllers.</summary>
        private SimulationPanelController _simulationPanelController = null!;

        /// <summary>Reporting-mode interaction controller handling compartment chunk display and hover highlighting. Created by InitializeControllers.</summary>
        private ReportingInteractionController _reportingInteraction = null!;
        /// <summary>Reporting-mode left-side panel controller; displays compartment lists grouped by entity. Created by InitializeControllers.</summary>
        private ReportingPanelController _reportingPanelController = null!;

        /// <summary>Currently active viewport interaction handler, switches between _modelingInteraction and _simulationInteraction based on the active tab.</summary>
        private IViewportInteractionHandler _activeHandler = null!;

        /// <summary>Top-level tab enum used to track the current active functional mode.</summary>
        private enum ActiveTab { Modeling, Simulating, Reporting }
        /// <summary>Currently active tab, defaulting to Modeling mode.</summary>
        private ActiveTab _activeTab = ActiveTab.Modeling;

        /// <summary>Reference to the entity currently editing dimensions for the EditPopup. Set by ShowEditPopup and cleared by OnApplyEdit/OnCancelEdit.</summary>
        private IVisualEntity? _editingEntity;

        /// <summary>Simulation runner instance managing Python interop and simulation execution.</summary>
        private SimulationRunner? _simulationRunner;

        /// <summary>Simulation progress polling timer; reads the current step from SimulationRunner every 100ms and updates the UI.</summary>
        private DispatcherTimer? _simProgressTimer;

        /// <summary>Indicates whether a simulation is in progress. When true, all modeling/simulation interactions are disabled.</summary>
        private bool _isSimulating;

        /// <summary>Current project file path. Overwritten on Save; when null triggers Save As flow.</summary>
        private string? _currentProjectPath;

        /// <summary>Current project unique identifier. Set on Load/New, persisted in Save.</summary>
        private string _currentProjectId = Guid.NewGuid().ToString();

        /// <summary>Current project display name. Shown in the simulation panel header.</summary>
        private string _currentProjectName = "Untitled";

        /// <summary>Last full simulation JSON from simulation, used for export/import.</summary>
        private string? _lastFullSimulationJson;

        /// <summary>
        /// Constructor: initializes XAML components and initializes controllers after the window is loaded.
        /// </summary>
        public MainWindow()
        {
            InitializeComponent();
            Loaded += (s, e) => InitializeControllers();
        }

        /// <summary>
        /// Initialize all controller instances and event bindings. Called on the window's Loaded event.
        /// Creates SharedSceneState, interaction controllers and panel controllers for both modes,
        /// and registers CompositionTarget.Rendering to update connection lines in real time.
        /// </summary>
        private void InitializeControllers()
        {
            _scene = new SharedSceneState(MainViewport);

            _modelingInteraction = new InteractionController(
                _scene,
                UpdateCursorInfo,
                onResizeRequested: entity =>
                {
                    var mousePos = Mouse.GetPosition(ViewportGrid);
                    ShowEditPopup(entity, mousePos);
                });

            _simulationInteraction = new SimulationInteractionController(
                _scene,
                UpdateCursorInfo);

            _propertiesPanelController = new PropertiesPanelController(
                ModelingPanelContainer,
                ChannelSelectorPopup,
                ChannelSelectorList,
                _modelingInteraction,
                NavigateToPoint);

            _simulationPanelController = new SimulationPanelController(
                SimulationPanelContainer,
                _simulationInteraction,
                NavigateToPoint);

            _reportingInteraction = new ReportingInteractionController(
                _scene,
                UpdateCursorInfo);

            _reportingPanelController = new ReportingPanelController(
                ReportComponentsContainer,
                ReportProbesContainer,
                _reportingInteraction,
                _scene,
                NavigateToPoint);

            // Register modeling entity creation/removal with the simulation registry
            _modelingInteraction.OnEntityAdded += entity => _scene.SimulationRegistry.Register(entity);
            _modelingInteraction.OnEntityRemoved += entity => _scene.SimulationRegistry.Unregister(entity.Id);

            _activeHandler = _modelingInteraction;

            // Update all connection line positions each frame to ensure connections follow entities after movement
            CompositionTarget.Rendering += (s, e) => _scene.ConnectionController.UpdateAll();
        }

        #region Top-level Tab Switching

        /// <summary>Triggered when the "Modeling" tab button is clicked; switch to Modeling mode. Bound to the XAML button Click event.</summary>
        private void OnTabModelingClick(object sender, RoutedEventArgs e)
        {
            if (_activeTab != ActiveTab.Modeling) SwitchTab(ActiveTab.Modeling);
            SyncTabButtons();
        }

        /// <summary>Triggered when the "Simulating" tab button is clicked; switch to Simulation mode. Bound to the XAML button Click event.</summary>
        private void OnTabSimulationClick(object sender, RoutedEventArgs e)
        {
            if (_activeTab != ActiveTab.Simulating) SwitchTab(ActiveTab.Simulating);
            SyncTabButtons();
        }

        /// <summary>Triggered when the "Reporting" tab button is clicked; switch to Reporting mode. Bound to the XAML button Click event.</summary>
        private void OnTabReportingClick(object sender, RoutedEventArgs e)
        {
            if (_activeTab != ActiveTab.Reporting) SwitchTab(ActiveTab.Reporting);
            SyncTabButtons();
        }

        /// <summary>
        /// Synchronize the IsChecked state of the three top-level tab ToggleButtons to match _activeTab.
        /// Called by all OnTab*Click handlers.
        /// </summary>
        private void SyncTabButtons()
        {
            TabModeling.IsChecked = _activeTab == ActiveTab.Modeling;
            TabSimulation.IsChecked = _activeTab == ActiveTab.Simulating;
            TabReporting.IsChecked = _activeTab == ActiveTab.Reporting;
        }

        /// <summary>
        /// Perform the core tab-switching logic: deactivate the previous mode handler, hide all panels/toolbars,
        /// show the target mode's panels, and switch _activeHandler.
        /// Called by the OnTab*Click handlers.
        /// </summary>
        /// <param name="target">Target tab</param>
        private void SwitchTab(ActiveTab target)
        {
            // Deactivate the interaction handler for the current mode
            if (_activeTab == ActiveTab.Modeling)
                _modelingInteraction.Deactivate();
            else if (_activeTab == ActiveTab.Simulating)
                _simulationInteraction.Deactivate();
            else if (_activeTab == ActiveTab.Reporting)
                _reportingInteraction.Deactivate();

            _activeTab = target;

            // Hide all panels and toolbars
            ModelingPanelScroll.Visibility = Visibility.Collapsed;
            SimulatingPanelRoot.Visibility = Visibility.Collapsed;
            ReportingPanelRoot.Visibility = Visibility.Collapsed;
            ModelingToolbar.Visibility = Visibility.Collapsed;
            SimulationToolbar.Visibility = Visibility.Collapsed;
            EditPopup.Visibility = Visibility.Collapsed;
            ChannelSelectorPopup.IsOpen = false;

            // Display panels for the target mode and switch the interaction handler
            switch (target)
            {
                case ActiveTab.Modeling:
                    ModelingPanelScroll.Visibility = Visibility.Visible;
                    ModelingToolbar.Visibility = Visibility.Visible;
                    _activeHandler = _modelingInteraction;
                    break;
                case ActiveTab.Simulating:
                    SimulatingPanelRoot.Visibility = Visibility.Visible;
                    SimulationToolbar.Visibility = Visibility.Visible;
                    _activeHandler = _simulationInteraction;
                    break;
                case ActiveTab.Reporting:
                    ReportingPanelRoot.Visibility = Visibility.Visible;
                    _activeHandler = _reportingInteraction;
                    _reportingInteraction.Activate();
                    _reportingPanelController.Rebuild();
                    break;
            }
        }

        #endregion

        #region Simulating Sub-tab Switching

        /// <summary>
        /// <summary>
        /// Triggered when the "Insert" sub-tab under Simulation is selected; show the device insertion panel.
        /// Called by the XAML button Click event.
        /// </summary>
        private void OnSubTabInsertClick(object sender, RoutedEventArgs e)
        {
            SubTabInsert.IsChecked = true;
            SubTabSimulate.IsChecked = false;
            InsertPanelScroll.Visibility = Visibility.Visible;
            SimulatePanelScroll.Visibility = Visibility.Collapsed;
        }

        /// <summary>
        /// Triggered when the "Simulate" sub-tab under Simulation is selected; show the simulation parameter panel.
        /// Called by the XAML button Click event.
        /// </summary>
        private void OnSubTabSimulateClick(object sender, RoutedEventArgs e)
        {
            SubTabInsert.IsChecked = false;
            SubTabSimulate.IsChecked = true;
            InsertPanelScroll.Visibility = Visibility.Collapsed;
            SimulatePanelScroll.Visibility = Visibility.Visible;
        }

        #endregion

        #region Simulation Begin

        /// <summary>
        /// Triggered by the "Begin" button to start the simulation.
        /// Reads UI parameters → builds simulation data → invokes Hines_method.py via SimulationRunner.
        /// Disables modification during simulation and displays live progress.
        /// Bound to the XAML button Click event.
        /// </summary>
        private async void OnBeginSimulationClick(object sender, RoutedEventArgs e)
        {
            if (_isSimulating) return;

            var registry = _scene.SimulationRegistry;

            // Read compartmentalization mode and parameters
            if (RbNSeg.IsChecked == true)
            {
                registry.Mode = SegmentationMode.NSeg;
                if (int.TryParse(TbNSeg.Text, out int n) && n > 0)
                    registry.NSeg = n;
            }
            else
            {
                registry.Mode = SegmentationMode.LSeg;
                if (double.TryParse(TbLSeg.Text, out double l) && l > 0)
                    registry.LSeg = l;
            }

            // Build full simulation data once: compartmentalization + device bindings
            var simData = registry.BuildSimulationData(
                _scene.ConnectionController.ConnectionsById,
                _scene.Devices);

            if (simData.Compartments.Count == 0)
            {
                MessageBox.Show("No compartments to simulate. Please add modeling entities first.", "Simulation",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            // Read simulation parameters
            if (!double.TryParse(TbVInit.Text, out double vInit)) vInit = -70.0;
            if (!double.TryParse(TbDt.Text, out double dt) || dt <= 0) dt = 0.1;
            if (!int.TryParse(TbSteps.Text, out int steps) || steps <= 0) steps = 10000;
            if (!double.TryParse(TbENa.Text, out double eNa)) eNa = 50.0;
            if (!double.TryParse(TbEK.Text, out double eK)) eK = -90.0;
            if (!double.TryParse(TbELeak.Text, out double eLeak)) eLeak = -76.5;
            if (!double.TryParse(TbCelsius.Text, out double celsius)) celsius = 24.0;
            if (!double.TryParse(TbCaOut.Text, out double caOut)) caOut = 2.0;
            if (!double.TryParse(TbCaInf.Text, out double caInf)) caInf = 2.4e-4;
            if (!double.TryParse(TbTauCa.Text, out double tauCa)) tauCa = 5.0;

            // ── Enter simulation state: disable interactions, show progress panel ──
            _isSimulating = true;
            SetSimulationLockUI(true);

            SimProgressPanel.Visibility = Visibility.Visible;
            SimStatusText.Text = "Simulating...";
            SimStepText.Text = $"Step: 0 / {steps}";
            SimProgressBar.Maximum = steps;
            SimProgressBar.Value = 0;

            // Create the runner
            _simulationRunner = new SimulationRunner();

            // Start progress polling timer
            _simProgressTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(100) };
            _simProgressTimer.Tick += (s2, e2) =>
            {
                if (_simulationRunner == null) return;
                int cur = _simulationRunner.CurrentStep;
                int total = _simulationRunner.TotalSteps;
                SimStepText.Text = $"Step: {cur} / {total}";
                SimProgressBar.Value = cur;
            };
            _simProgressTimer.Start();

            try
            {
                await _simulationRunner.RunAsync(simData, vInit, dt, steps, eNa, eK, eLeak, celsius, caOut, caInf, tauCa);

                // ── Simulation complete ──
                SimStatusText.Text = "Simulation Complete";
                SimStatusText.Foreground = new SolidColorBrush(Color.FromRgb(0x00, 0xFF, 0x88));
                SimStepText.Text = $"Step: {steps} / {steps}";
                SimProgressBar.Value = steps;

                // Store simulation data for use by the Reporting panel
                _scene.LastSimulationData = simData;
                _scene.HasCompletedSimulation = true;

                // Store probe result JSON for export
                _lastFullSimulationJson = _simulationRunner?.FullSimulationJson;

                MessageBox.Show(
                    $"Simulation complete:\n" +
                    $"  Compartments: {simData.Compartments.Count}\n" +
                    $"  Current clamps: {simData.Stimulations.Count}\n" +
                    $"  Voltage clamps: {simData.VoltageClamps.Count}\n" +
                    $"  Probes: {simData.Probes.Count}\n" +
                    $"  Total steps: {steps}",
                    "Simulation",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                // pythonnet wraps OperationCanceledException thrown in callbacks as PythonException,
                // so checking the WasAborted flag is more reliable.
                bool aborted = _simulationRunner?.WasAborted == true
                            || ex.InnerException is OperationCanceledException
                            || ex is OperationCanceledException;
                if (aborted)
                {
                    SimStatusText.Text = "Simulation Aborted";
                    SimStatusText.Foreground = new SolidColorBrush(Colors.OrangeRed);
                    MessageBox.Show("Simulation aborted by user.", "Simulation Aborted",
                        MessageBoxButton.OK, MessageBoxImage.Warning);
                }
                else
                {
                    SimStatusText.Text = "Simulation Failed";
                    SimStatusText.Foreground = new SolidColorBrush(Colors.Red);
                    MessageBox.Show($"Simulation failed:\n{ex.Message}", "Simulation Error",
                        MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
            finally
            {
                _simProgressTimer?.Stop();
                _simProgressTimer = null;
                _simulationRunner = null;
                _isSimulating = false;
                SetSimulationLockUI(false);
                BtnAbortSimulation.IsEnabled = true;
            }
        }

        /// <summary>
        /// Toggle simulation lock UI: disable/enable toolbars, tab switching and the Begin button.
        /// </summary>
        private void SetSimulationLockUI(bool locked)
        {
            BtnBeginSimulation.IsEnabled = !locked;
            TabModeling.IsEnabled = !locked;
            TabSimulation.IsEnabled = !locked;
            TabReporting.IsEnabled = !locked;
            ModelingToolbar.IsEnabled = !locked;
            SimulationToolbar.IsEnabled = !locked;
        }

        /// <summary>
        /// Forcefully abort the simulation when the "Abort" button is clicked.
        /// Calls SimulationRunner.Abort() to interrupt execution on the next Python callback.
        /// </summary>
        private void OnAbortSimulationClick(object sender, RoutedEventArgs e)
        {
            if (_simulationRunner != null && _simulationRunner.IsRunning)
            {
                _simulationRunner.Abort();
                BtnAbortSimulation.IsEnabled = false;
                SimStatusText.Text = "Aborting...";
                SimStatusText.Foreground = new SolidColorBrush(Colors.OrangeRed);
            }
        }

        #endregion

        #region Viewport Mouse Event Routing
        /// <summary>Route the viewport MouseDown event to the currently active interaction handler. Bound to HelixViewport3D.MouseDown in XAML.</summary>
        private void OnViewportMouseDown(object sender, MouseButtonEventArgs e)
        {
            if (_isSimulating) return;
            _activeHandler?.OnMouseDown(sender, e);
        }
        /// <summary>Route the viewport MouseMove event to the currently active interaction handler. Bound to HelixViewport3D.MouseMove in XAML.</summary>
        private void OnViewportMouseMove(object sender, MouseEventArgs e)
        {
            if (_isSimulating) return;
            _activeHandler?.OnMouseMove(sender, e);
        }
        /// <summary>Route the viewport MouseUp event to the currently active interaction handler. Bound to HelixViewport3D.MouseUp in XAML.</summary>
        private void OnViewportMouseUp(object sender, MouseButtonEventArgs e)
        {
            if (_isSimulating) return;
            _activeHandler?.OnMouseUp(sender, e);
        }
        /// <summary>Route the viewport MouseWheel event to the currently active interaction handler. Bound to HelixViewport3D.MouseWheel in XAML.</summary>
        private void OnViewportMouseWheel(object sender, MouseWheelEventArgs e)
        {
            if (_isSimulating) return;
            _activeHandler?.OnMouseWheel(sender, e);
        }

        #endregion

        #region HUD and Overlay
        /// <summary>
        /// Smoothly align the camera view to a specified world coordinate point (500ms animated transition).
        /// Called by panel expand events of PropertiesPanelController and SimulationPanelController.
        /// </summary>
        private void NavigateToPoint(Point3D target)
        {
            MainViewport.LookAt(target, 500);
        }
        /// <summary>
        /// Update the crosshair position and world-coordinate HUD display.
        /// Invoked via delegate callbacks from InteractionController and SimulationInteractionController on every mouse move.
        /// </summary>
        /// <param name="mousePos">Mouse position in the viewport control's screen coordinates</param>
        /// <param name="worldPos">The corresponding 3D world coordinate for the mouse; null if no valid hit</param>
        public void UpdateCursorInfo(Point mousePos, Point3D? worldPos)
        {
            Canvas.SetLeft(CrosshairPath, mousePos.X);
            Canvas.SetTop(CrosshairPath, mousePos.Y);

            if (worldPos.HasValue)
            {
                var p = worldPos.Value;
                CoordText.Text = $"X:{p.X:F2} Y:{p.Y:F2} Z:{p.Z:F2}";
                CoordHud.Visibility = Visibility.Visible;
                Canvas.SetLeft(CoordHud, mousePos.X + 15);
                Canvas.SetTop(CoordHud, mousePos.Y + 15);
            }
            else
            {
                CoordHud.Visibility = Visibility.Collapsed;
            }
        }

        /// <summary>
        /// Display the entity size edit popup (EditPopup), showing different edit fields depending on the entity type.
        /// Called by InteractionController's _onResizeRequested delegate when the user selects "Resize..." from the context menu.
        /// </summary>
        /// <param name="entity">The visual entity to edit</param>
        /// <param name="mousePos">Popup display position (mouse coordinates within the ViewportGrid)</param>
        public void ShowEditPopup(IVisualEntity entity, Point mousePos)
        {
            _editingEntity = entity;

            if (entity is AxonVisual axon)
            {
                PanelAxonLength.Visibility = Visibility.Visible;
                PanelRadius.Visibility = Visibility.Visible;
                PanelTopRadius.Visibility = Visibility.Visible;
                TbLength.Text = axon.Length.ToString("F2");
                TbRadius.Text = axon.BaseRadius.ToString("F2");
                TbTopRadius.Text = axon.TopRadius.ToString("F2");
            }


            EditPopup.Margin = new Thickness(mousePos.X, mousePos.Y, 0, 0);
            EditPopup.Visibility = Visibility.Visible;
        }

        /// <summary>
        /// Apply size changes from the edit popup. Parse textbox values and write them to the entity's properties.
        /// Bound to the 'Apply' button's Click event in the EditPopup.
        /// </summary>
        private void OnApplyEdit(object sender, RoutedEventArgs e)
        {
            if (_editingEntity == null) return;

            try
            {
                if (_editingEntity is AxonVisual axon)
                {
                    if (double.TryParse(TbLength.Text, out double l)) axon.Length = l;
                    if (double.TryParse(TbRadius.Text, out double br)) axon.BaseRadius = br;
                    if (double.TryParse(TbTopRadius.Text, out double tr)) axon.TopRadius = tr;
                }

            }
            catch { }

            EditPopup.Visibility = Visibility.Collapsed;
            _editingEntity = null;
        }

        /// <summary>
        /// Cancel the edit popup without applying changes.
        /// Bound to the 'Cancel' button's Click event in the EditPopup.
        /// </summary>
        private void OnCancelEdit(object sender, RoutedEventArgs e)
        {
            EditPopup.Visibility = Visibility.Collapsed;
            _editingEntity = null;
        }

        #endregion

        #region Toolbar Actions (Modeling)
        /// <summary>
        /// Handle the 'Add Soma' button click: create a soma entity and enter placement mode.
        /// Bound to the modeling toolbar button's Click event.
        /// </summary>
        private void OnAddSomaClick(object sender, RoutedEventArgs e)
        {
            var start = new Point3D(0, 0, 0);
            var end = new Point3D(0, 0, 10);
            var newSoma = new SomaVisual(start, end, 5.0, Colors.DodgerBlue);
            _modelingInteraction.StartPlacing(newSoma);
        }

        /// <summary>
        /// Handle the 'Add Axon' button click: create an axon entity and enter placement mode.
        /// Bound to the modeling toolbar button's Click event.
        /// </summary>
        private void OnAddAxonClick(object sender, RoutedEventArgs e)
        {
            var start = new Point3D(0, 0, 0);
            var end = new Point3D(0, 0, 50);
            var newAxon = new AxonVisual(start, end, 2.5, Colors.LimeGreen);
            _modelingInteraction.StartPlacing(newAxon);
        }

        /// <summary>
        /// Handle the 'Add Dend' button click: create a dendrite entity and enter placement mode.
        /// Bound to the modeling toolbar button's Click event.
        /// </summary>
        private void OnAddDendClick(object sender, RoutedEventArgs e)
        {
            var start = new Point3D(0, 0, 0);
            var end = new Point3D(0, 0, 50);
            var newDend = new DendVisual(start, end, 1.0, Colors.MediumPurple);
            _modelingInteraction.StartPlacing(newDend);
        }

        #endregion

        #region Toolbar Actions (Simulation)
        /// <summary>
        /// Handle the 'Add Stimulation' button click: start placing a stimulation device in simulation mode.
        /// Bound to the simulation toolbar button's Click event.
        /// </summary>
        private void OnAddStimulationClick(object sender, RoutedEventArgs e)
        {
            _simulationInteraction.StartPlacingDevice(DeviceType.Stimulation);
        }

        /// <summary>
        /// Handle the 'Add Probe' button click: start placing a probe device in simulation mode.
        /// Bound to the simulation toolbar button's Click event.
        /// </summary>
        private void OnAddProbeClick(object sender, RoutedEventArgs e)
        {
            _simulationInteraction.StartPlacingDevice(DeviceType.Probe);
        }

        /// <summary>
        /// Handle the 'Add VoltageClamp' button click: start placing a voltage clamp device in simulation mode.
        /// </summary>
        private void OnAddVoltageClampClick(object sender, RoutedEventArgs e)
        {
            _simulationInteraction.StartPlacingDevice(DeviceType.VoltageClamp);
        }

        #endregion

        #region Reporting Sub-tab Switching

        private void OnReportSubTabComponentsClick(object sender, RoutedEventArgs e)
        {
            ReportSubTabComponents.IsChecked = true;
            ReportSubTabProbes.IsChecked = false;
            ReportComponentsScroll.Visibility = Visibility.Visible;
            ReportProbesScroll.Visibility = Visibility.Collapsed;
        }

        private void OnReportSubTabProbesClick(object sender, RoutedEventArgs e)
        {
            ReportSubTabComponents.IsChecked = false;
            ReportSubTabProbes.IsChecked = true;
            ReportComponentsScroll.Visibility = Visibility.Collapsed;
            ReportProbesScroll.Visibility = Visibility.Visible;
        }

        #endregion

        #region Edit Menu

        private void OnIonChannelSettingClick(object sender, RoutedEventArgs e)
        {
            var win = new IonChannelSettingWindow { Owner = this };
            win.ShowDialog();
        }

        #endregion

        #region Data Menu

        private void OnExportSimulationDataClick(object sender, RoutedEventArgs e)
        {
            if (_isSimulating) return;

            if (_scene.LastSimulationData == null || !_scene.HasCompletedSimulation)
            {
                MessageBox.Show("No simulation results to export. Please run a simulation first.",
                    "Export Simulation Data", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            var dlg = new SaveFileDialog
            {
                Filter = "Simulation Data (*.npz)|*.npz|All Files (*.*)|*.*",
                Title = "Export Simulation Data",
                FileName = $"{_currentProjectName}_sim.npz",
                DefaultExt = ".npz"
            };
            if (dlg.ShowDialog() != true) return;

            try
            {
                SaveSimulationDataNpzAsync(dlg.FileName).GetAwaiter().GetResult();

                MessageBox.Show($"Simulation data exported to:\n{dlg.FileName}",
                    "Export Complete", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Export failed:\n{ex.Message}", "Error",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private async void OnImportSimulationDataClick(object sender, RoutedEventArgs e)
        {
            if (_isSimulating) return;

            if (_scene.Entities.Count == 0)
            {
                MessageBox.Show("Please load a project model first before importing simulation data.",
                    "Import Simulation Data", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            var dlg = new OpenFileDialog
            {
                Filter = "Simulation Data (*.npz;*.simjson)|*.npz;*.simjson|NPZ Files (*.npz)|*.npz|Legacy Simulation Data (*.simjson)|*.simjson|All Files (*.*)|*.*",
                Title = "Import Simulation Data"
            };
            if (dlg.ShowDialog() != true) return;

            try
            {
                string extension = Path.GetExtension(dlg.FileName).ToLowerInvariant();
                string resultProjectId;
                string resultProjectName;

                if (extension == ".npz")
                {
                    var manifest = ReadSimulationManifestFromNpz(dlg.FileName);
                    resultProjectId = manifest.ProjectId;
                    resultProjectName = manifest.ProjectName;
                }
                else
                {
                    string json = File.ReadAllText(dlg.FileName);
                    var resultData = JsonSerializer.Deserialize<SimulationResultData>(json)
                        ?? throw new InvalidOperationException("Failed to parse simulation data file.");
                    resultProjectId = resultData.ProjectId;
                    resultProjectName = resultData.ProjectName;
                }

                // Validate project identifier
                if (resultProjectId != _currentProjectId)
                {
                    MessageBox.Show(
                        $"Simulation data mismatch!\n\n" +
                        $"Current model: {_currentProjectName} ({_currentProjectId[..8]}...)\n" +
                        $"Simulation data: {resultProjectName} ({resultProjectId[..8]}...)\n\n" +
                        $"The simulation results do not belong to the currently loaded model.",
                        "Import Error", MessageBoxButton.OK, MessageBoxImage.Error);
                    return;
                }

                // Build simulation data (compartmentalization) so Reporting has the structure
                var registry = _scene.SimulationRegistry;
                if (RbNSeg.IsChecked == true)
                {
                    registry.Mode = SegmentationMode.NSeg;
                    if (int.TryParse(TbNSeg.Text, out int n) && n > 0) registry.NSeg = n;
                }
                else
                {
                    registry.Mode = SegmentationMode.LSeg;
                    if (double.TryParse(TbLSeg.Text, out double l) && l > 0) registry.LSeg = l;
                }

                var simData = registry.BuildSimulationData(
                    _scene.ConnectionController.ConnectionsById,
                    _scene.Devices);

                _scene.LastSimulationData = simData;
                if (extension == ".npz")
                    await LoadSimulationDataNpzAsync(dlg.FileName);
                else
                    await InjectLegacySimulationJsonAsync(dlg.FileName);
                _scene.HasCompletedSimulation = true;

                MessageBox.Show(
                    $"Simulation data loaded successfully!\n\n" +
                    $"Project: {resultProjectName}\n" +
                    $"You can now use the Reporting tab to analyze the data.",
                    "Data Loaded", MessageBoxButton.OK, MessageBoxImage.Information);

                // Switch to Reporting tab
                SwitchTab(ActiveTab.Reporting);
                SyncTabButtons();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Import failed:\n{ex.Message}", "Error",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private sealed class SimulationArchiveManifest
        {
            public string ProjectId { get; set; } = "";
            public string ProjectName { get; set; } = "";
        }

        private SimulationArchiveManifest ReadSimulationManifestFromNpz(string path)
        {
            using var stream = File.OpenRead(path);
            using var archive = new ZipArchive(stream, ZipArchiveMode.Read, leaveOpen: false);
            var entry = archive.GetEntry("manifest.json")
                ?? throw new InvalidOperationException("NPZ file is missing manifest.json.");
            using var reader = new StreamReader(entry.Open());
            string manifestJson = reader.ReadToEnd();
            using var doc = JsonDocument.Parse(manifestJson);

            if (!doc.RootElement.TryGetProperty("project_id", out var projectIdElement))
                throw new InvalidOperationException("NPZ manifest is missing project_id.");
            if (!doc.RootElement.TryGetProperty("project_name", out var projectNameElement))
                throw new InvalidOperationException("NPZ manifest is missing project_name.");

            return new SimulationArchiveManifest
            {
                ProjectId = projectIdElement.GetString() ?? "",
                ProjectName = projectNameElement.GetString() ?? ""
            };
        }

        private async Task SaveSimulationDataNpzAsync(string path)
        {
            await SimulationRunner.CallSaveSimulationDataNpz(path, _currentProjectId, _currentProjectName);
        }

        private async Task LoadSimulationDataNpzAsync(string path)
        {
            await SimulationRunner.CallLoadSimulationDataNpz(path);
        }

        private async Task InjectLegacySimulationJsonAsync(string simJsonPath)
        {
            string json = File.ReadAllText(simJsonPath);
            var resultData = JsonSerializer.Deserialize<SimulationResultData>(json)
                ?? throw new InvalidOperationException("Failed to parse simulation data file.");

            try
            {
                await PythonWorker.EnsureStartedAsync();
                await PythonWorker.RunAsync(() =>
                {
                    dynamic sim = Py.Import("Hines_method");
                    sim.import_full_simulation_json(resultData.FullSimulationJson);
                });
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"InjectLegacySimulationJsonAsync error: {ex.Message}");
                throw;
            }
        }

        #endregion

        #region File Menu

        private void OnNewProjectClick(object sender, RoutedEventArgs e)
        {
            if (_isSimulating) return;
            var result = MessageBox.Show("Create a new project? Unsaved changes will be lost.",
                "New Project", MessageBoxButton.OKCancel, MessageBoxImage.Question);
            if (result != MessageBoxResult.OK) return;

            ClearScene();
            _currentProjectPath = null;
            _currentProjectId = Guid.NewGuid().ToString();
            _currentProjectName = "Untitled";
            ProjectNameLabel.Text = "Project: (none)";
            Title = "NeuronCAD 2026";
        }

        private void OnOpenProjectClick(object sender, RoutedEventArgs e)
        {
            if (_isSimulating) return;

            var dlg = new OpenFileDialog
            {
                Filter = "NeuronCAD Project (*.json)|*.json|All Files (*.*)|*.*",
                Title = "Open Project"
            };
            if (dlg.ShowDialog() != true) return;

            try
            {
                var project = SaveLoadManager.Load(dlg.FileName);
                SaveLoadManager.ApplyToScene(
                    project,
                    _scene,
                    _modelingInteraction,
                    _simulationInteraction,
                    v => TbVInit.Text = v,
                    v => TbDt.Text = v,
                    v => TbSteps.Text = v,
                    v => TbENa.Text = v,
                    v => TbEK.Text = v,
                    v => TbELeak.Text = v,
                    v => TbCelsius.Text = v,
                    v => TbCaOut.Text = v,
                    v => TbCaInf.Text = v,
                    v => TbTauCa.Text = v,
                    v => TbNSeg.Text = v,
                    v => TbLSeg.Text = v,
                    isNSeg => { RbNSeg.IsChecked = isNSeg; RbLSeg.IsChecked = !isNSeg; });

                _scene.LastSimulationData = _scene.SimulationRegistry.BuildSimulationData(
                    _scene.ConnectionController.ConnectionsById,
                    _scene.Devices);
                _scene.HasCompletedSimulation = false;

                _currentProjectPath = dlg.FileName;
                // Handle legacy files without ProjectId
                bool migratedLegacyProject = false;
                if (string.IsNullOrEmpty(project.ProjectId))
                {
                    project.ProjectId = Guid.NewGuid().ToString();
                    migratedLegacyProject = true;
                }
                if (string.IsNullOrEmpty(project.ProjectName))
                {
                    project.ProjectName = System.IO.Path.GetFileNameWithoutExtension(dlg.FileName);
                    migratedLegacyProject = true;
                }

                _currentProjectId = project.ProjectId;
                _currentProjectName = project.ProjectName;
                if (migratedLegacyProject)
                    SaveLoadManager.SaveProjectData(dlg.FileName, project);
                ProjectNameLabel.Text = $"Project: {_currentProjectName}";
                Title = $"NeuronCAD 2026 — {_currentProjectName}";

                // Show loaded parameters summary
                var summary = SaveLoadManager.GetLoadedParamsSummary(project);
                MessageBox.Show(
                    $"Project loaded: {System.IO.Path.GetFileName(dlg.FileName)}\n\n{summary}",
                    "Project Loaded", MessageBoxButton.OK, MessageBoxImage.Information);
                // Switch to Modeling tab to view the loaded results
                if (_activeTab != ActiveTab.Modeling)
                {
                    SwitchTab(ActiveTab.Modeling);
                    SyncTabButtons();
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Load failed:\n{ex.Message}", "Error",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void OnSaveProjectClick(object sender, RoutedEventArgs e)
        {
            if (_isSimulating) return;
            if (_currentProjectPath == null)
            {
                OnSaveAsProjectClick(sender, e);
                return;
            }
            SaveToFile(_currentProjectPath);
        }

        private void OnSaveAsProjectClick(object sender, RoutedEventArgs e)
        {
            if (_isSimulating) return;

            var dlg = new SaveFileDialog
            {
                Filter = "NeuronCAD Project (*.json)|*.json|All Files (*.*)|*.*",
                Title = "Save Project As"
            };
            if (dlg.ShowDialog() != true) return;

            // Derive project name from file name (without extension)
            _currentProjectName = System.IO.Path.GetFileNameWithoutExtension(dlg.FileName);

            SaveToFile(dlg.FileName);
            _currentProjectPath = dlg.FileName;
            ProjectNameLabel.Text = $"Project: {_currentProjectName}";
            Title = $"NeuronCAD 2026 — {_currentProjectName}";
        }

        private void OnExitClick(object sender, RoutedEventArgs e)
        {
            Close();
        }

        private void SaveToFile(string filePath)
        {
            try
            {
                if (!double.TryParse(TbVInit.Text, out double vInit)) vInit = -70.0;
                if (!double.TryParse(TbDt.Text, out double dt) || dt <= 0) dt = 0.1;
                if (!int.TryParse(TbSteps.Text, out int steps) || steps <= 0) steps = 10000;
                if (!double.TryParse(TbENa.Text, out double eNa)) eNa = 50.0;
                if (!double.TryParse(TbEK.Text, out double eK)) eK = -90.0;
                if (!double.TryParse(TbELeak.Text, out double eLeak)) eLeak = -76.5;
                if (!double.TryParse(TbCelsius.Text, out double celsius)) celsius = 24.0;
                if (!double.TryParse(TbCaOut.Text, out double caOut)) caOut = 2.0;
                if (!double.TryParse(TbCaInf.Text, out double caInf)) caInf = 2.4e-4;
                if (!double.TryParse(TbTauCa.Text, out double tauCa)) tauCa = 5.0;

                string segMode = RbNSeg.IsChecked == true ? "NSeg" : "LSeg";
                if (!int.TryParse(TbNSeg.Text, out int nSeg) || nSeg <= 0) nSeg = 5;
                if (!double.TryParse(TbLSeg.Text, out double lSeg) || lSeg <= 0) lSeg = 20.0;

                SaveLoadManager.Save(filePath, _scene,
                    vInit, dt, steps, eNa, eK, eLeak,
                    celsius, caOut, caInf, tauCa,
                    segMode, nSeg, lSeg,
                    _currentProjectId, _currentProjectName);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Save failed:\n{ex.Message}", "Error",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void ClearScene()
        {
            _modelingInteraction.Deactivate();
            _simulationInteraction.Deactivate();

            foreach (var device in _scene.Devices.ToList())
                _scene.HelixViewport.Children.Remove(device.Visual3D);
            _scene.Devices.Clear();

            foreach (var connId in _scene.ConnectionController.ConnectionsById.Keys.ToList())
                _scene.ConnectionController.Remove(connId);

            foreach (var entity in _scene.Entities.ToList())
            {
                _scene.HelixViewport.Children.Remove(entity.Visual3D);
                _scene.SimulationRegistry.Unregister(entity.Id);
                _modelingInteraction.NotifyEntityRemoved(entity);
            }
            _scene.Entities.Clear();

            _scene.LastSimulationData = null;
            _scene.HasCompletedSimulation = false;
            _lastFullSimulationJson = null;

            IonChannelParams.ResetToDefault();
        }

        #endregion
    }
}
