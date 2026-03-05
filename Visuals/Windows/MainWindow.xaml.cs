using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Media3D;
using NeuronCAD.Visuals.Tabs.Shared;
using NeuronCAD.Visuals.Tabs.Modeling;
using NeuronCAD.Visuals.Tabs.Modeling.Visuals;
using NeuronCAD.Visuals.Tabs.Simulation;

namespace NeuronCAD.Visuals.Windows
{
    public partial class MainWindow : Window
    {
        private SharedSceneState _scene = null!;

        private InteractionController _modelingInteraction = null!;
        private SimulationInteractionController _simulationInteraction = null!;

        private PropertiesPanelController _propertiesPanelController = null!;
        private SimulationPanelController _simulationPanelController = null!;

        private IViewportInteractionHandler _activeHandler = null!;

        private enum ActiveTab { Modeling, Simulating, Reporting }
        private ActiveTab _activeTab = ActiveTab.Modeling;

        private IVisualEntity? _editingEntity;

        public MainWindow()
        {
            InitializeComponent();
            Loaded += (s, e) => InitializeControllers();
        }

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
                _modelingInteraction);

            _simulationPanelController = new SimulationPanelController(
                SimulationPanelContainer,
                _simulationInteraction);

            _activeHandler = _modelingInteraction;

            CompositionTarget.Rendering += (s, e) => _scene.ConnectionController.UpdateAll();
        }

        #region Top-level Tab Switching

        private void OnTabModelingClick(object sender, RoutedEventArgs e)
        {
            if (_activeTab != ActiveTab.Modeling) SwitchTab(ActiveTab.Modeling);
            SyncTabButtons();
        }

        private void OnTabSimulationClick(object sender, RoutedEventArgs e)
        {
            if (_activeTab != ActiveTab.Simulating) SwitchTab(ActiveTab.Simulating);
            SyncTabButtons();
        }

        private void OnTabReportingClick(object sender, RoutedEventArgs e)
        {
            if (_activeTab != ActiveTab.Reporting) SwitchTab(ActiveTab.Reporting);
            SyncTabButtons();
        }

        private void SyncTabButtons()
        {
            TabModeling.IsChecked = _activeTab == ActiveTab.Modeling;
            TabSimulation.IsChecked = _activeTab == ActiveTab.Simulating;
            TabReporting.IsChecked = _activeTab == ActiveTab.Reporting;
        }

        private void SwitchTab(ActiveTab target)
        {
            // Deactivate current handler
            if (_activeTab == ActiveTab.Modeling)
                _modelingInteraction.Deactivate();
            else if (_activeTab == ActiveTab.Simulating)
                _simulationInteraction.Deactivate();

            _activeTab = target;

            // Hide all panels
            ModelingPanelScroll.Visibility = Visibility.Collapsed;
            SimulatingPanelRoot.Visibility = Visibility.Collapsed;
            ReportingPanelRoot.Visibility = Visibility.Collapsed;
            ModelingToolbar.Visibility = Visibility.Collapsed;
            SimulationToolbar.Visibility = Visibility.Collapsed;
            EditPopup.Visibility = Visibility.Collapsed;
            ChannelSelectorPopup.IsOpen = false;

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
                    _activeHandler = _modelingInteraction; // read-only fallback
                    break;
            }
        }

        #endregion

        #region Simulating Sub-tab Switching

        private void OnSubTabInsertClick(object sender, RoutedEventArgs e)
        {
            SubTabInsert.IsChecked = true;
            SubTabSimulate.IsChecked = false;
            InsertPanelScroll.Visibility = Visibility.Visible;
            SimulatePanelScroll.Visibility = Visibility.Collapsed;
        }

        private void OnSubTabSimulateClick(object sender, RoutedEventArgs e)
        {
            SubTabInsert.IsChecked = false;
            SubTabSimulate.IsChecked = true;
            InsertPanelScroll.Visibility = Visibility.Collapsed;
            SimulatePanelScroll.Visibility = Visibility.Visible;
        }

        #endregion

        #region Simulation Begin

        private void OnBeginSimulationClick(object sender, RoutedEventArgs e)
        {
            // Interface reserved for future implementation.
            // Parameters can be read from TbVInit, TbDt, TbSteps, TbENa, TbEK, TbELeak.
        }

        #endregion

        #region Viewport Mouse Event Routing

        private void OnViewportMouseDown(object sender, MouseButtonEventArgs e)
            => _activeHandler?.OnMouseDown(sender, e);

        private void OnViewportMouseMove(object sender, MouseEventArgs e)
            => _activeHandler?.OnMouseMove(sender, e);

        private void OnViewportMouseUp(object sender, MouseButtonEventArgs e)
            => _activeHandler?.OnMouseUp(sender, e);

        private void OnViewportMouseWheel(object sender, MouseWheelEventArgs e)
            => _activeHandler?.OnMouseWheel(sender, e);

        #endregion

        #region HUD and Overlay

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
            else if (entity is SomaVisual soma)
            {
                PanelAxonLength.Visibility = Visibility.Collapsed;
                PanelRadius.Visibility = Visibility.Visible;
                PanelTopRadius.Visibility = Visibility.Collapsed;
                TbRadius.Text = soma.Radius.ToString("F2");
            }

            EditPopup.Margin = new Thickness(mousePos.X, mousePos.Y, 0, 0);
            EditPopup.Visibility = Visibility.Visible;
        }

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
                else if (_editingEntity is SomaVisual soma)
                {
                    if (double.TryParse(TbRadius.Text, out double r)) soma.Radius = r;
                }
            }
            catch { }

            EditPopup.Visibility = Visibility.Collapsed;
            _editingEntity = null;
        }

        private void OnCancelEdit(object sender, RoutedEventArgs e)
        {
            EditPopup.Visibility = Visibility.Collapsed;
            _editingEntity = null;
        }

        #endregion

        #region Toolbar Actions (Modeling)

        private void OnAddSomaClick(object sender, RoutedEventArgs e)
        {
            var newSoma = new SomaVisual(new Point3D(0, 0, 0), 2.0, Colors.DodgerBlue);
            _modelingInteraction.StartPlacing(newSoma);
        }

        private void OnAddAxonClick(object sender, RoutedEventArgs e)
        {
            var start = new Point3D(0, 0, 0);
            var end = new Point3D(0, 0, 5);
            var newAxon = new AxonVisual(start, end, 0.5, Colors.LimeGreen);
            _modelingInteraction.StartPlacing(newAxon);
        }

        private void OnAddDendClick(object sender, RoutedEventArgs e)
        {
            var start = new Point3D(0, 0, 0);
            var end = new Point3D(0, 0, 5);
            var newDend = new DendVisual(start, end, 0.5, Colors.MediumPurple);
            _modelingInteraction.StartPlacing(newDend);
        }

        #endregion

        #region Toolbar Actions (Simulation)

        private void OnAddStimulationClick(object sender, RoutedEventArgs e)
        {
            _simulationInteraction.StartPlacingDevice(DeviceType.Stimulation);
        }

        private void OnAddProbeClick(object sender, RoutedEventArgs e)
        {
            _simulationInteraction.StartPlacingDevice(DeviceType.Probe);
        }

        #endregion
    }
}