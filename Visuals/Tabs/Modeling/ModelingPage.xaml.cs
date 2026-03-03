using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Media3D;
using NeuronCAD.Visuals.Tabs.Modeling.Visuals;
using System.Windows.Controls.Primitives;

namespace NeuronCAD.Visuals.Tabs.Modeling
{
    public partial class ModelingPage : UserControl
    {
        private ViewportController _viewportController = null!;
        private InteractionController _interactionController = null!;
        
        // 双控制器分离管理左右侧面板状态
        private PropertiesPanelController _propertiesPanelController = null!;
        private SimulationPanelController _simulationPanelController = null!;
        
        private IVisualEntity? _editingEntity;

        public ModelingPage()
        {
            InitializeComponent();
            InitializeControllers();
        }

        private void InitializeControllers()
        {
            _viewportController = new ViewportController(MainViewport);
            _interactionController = new InteractionController(this, _viewportController, MainViewport);

            // 初始化建模属性面板控制器
            _propertiesPanelController = new PropertiesPanelController(
                PropertiesPanelContainer,
                ChannelSelectorPopup,
                ChannelSelectorList,
                _interactionController
            );

            // 初始化仿真属性面板控制器
            _simulationPanelController = new SimulationPanelController(
                SimulationPanelContainer,
                _interactionController
            );
        }

        #region Mode Switching API (核心数据流控)

        /// <summary>
        /// 供外部 (如 MainWindow 标签页切换时) 调用的模式切换接口
        /// </summary>
        public void SwitchMode(bool isSimulation)
        {
            // 1. 切换交互控制器的状态机分支
            _interactionController.SetSimulationMode(isSimulation);

            // 2. 切换左侧面板的可见性
            PropertiesPanelContainer.Visibility = isSimulation ? Visibility.Collapsed : Visibility.Visible;
            SimulationPanelContainer.Visibility = isSimulation ? Visibility.Visible : Visibility.Collapsed;

            // 3. 切换底部操作工具栏的可见性
            ModelingToolbar.Visibility = isSimulation ? Visibility.Collapsed : Visibility.Visible;
            SimulationToolbar.Visibility = isSimulation ? Visibility.Visible : Visibility.Collapsed;

            // 4. 关闭可能遗留的浮窗
            EditPopup.Visibility = Visibility.Collapsed;
            ChannelSelectorPopup.IsOpen = false;
        }

        // 仅供测试调试用的 UI 按钮回调
        private void OnModeToggleClick(object sender, RoutedEventArgs e)
        {
            bool isSimulation = ModeToggleButton.IsChecked == true;
            ModeToggleButton.Content = isSimulation ? "Switch to Modeling Mode" : "Switch to Simulation Mode";
            SwitchMode(isSimulation);
        }

        #endregion

        #region HUD & Overlay API

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

            var panelTopRadius = this.FindName("PanelTopRadius") as FrameworkElement;
            var tbTopRadius = this.FindName("TbTopRadius") as TextBox;

            if (entity is AxonVisual axon)
            {
                PanelAxonLength.Visibility = Visibility.Visible;
                PanelRadius.Visibility = Visibility.Visible;
                if (panelTopRadius != null) panelTopRadius.Visibility = Visibility.Visible;

                TbLength.Text = axon.Length.ToString("F2");
                TbRadius.Text = axon.BaseRadius.ToString("F2"); 
                if (tbTopRadius != null) tbTopRadius.Text = axon.TopRadius.ToString("F2");
            }
            else if (entity is SomaVisual soma)
            {
                PanelAxonLength.Visibility = Visibility.Collapsed;
                PanelRadius.Visibility = Visibility.Visible;
                if (panelTopRadius != null) panelTopRadius.Visibility = Visibility.Collapsed;

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
                var tbTopRadius = this.FindName("TbTopRadius") as TextBox;

                if (_editingEntity is AxonVisual axon)
                {
                    if (double.TryParse(TbLength.Text, out double l)) axon.Length = l;
                    if (double.TryParse(TbRadius.Text, out double br)) axon.BaseRadius = br;
                    
                    if (tbTopRadius != null && double.TryParse(tbTopRadius.Text, out double tr)) 
                        axon.TopRadius = tr;
                }
                else if (_editingEntity is SomaVisual soma)
                {
                    if (double.TryParse(TbRadius.Text, out double r)) soma.Radius = r;
                }
            }
            catch { /* Ignore parse errors */ }

            EditPopup.Visibility = Visibility.Collapsed;
            _editingEntity = null;
        }

        private void OnCancelEdit(object sender, RoutedEventArgs e)
        {
            EditPopup.Visibility = Visibility.Collapsed;
            _editingEntity = null;
        }

        #endregion

        #region Viewport Input Routing

        private void OnViewportMouseDown(object sender, MouseButtonEventArgs e) => _interactionController.OnMouseDown(sender, e);
        private void OnViewportMouseMove(object sender, MouseEventArgs e) => _interactionController.OnMouseMove(sender, e);
        private void OnViewportMouseUp(object sender, MouseButtonEventArgs e) => _interactionController.OnMouseUp(sender, e);
        private void OnViewportMouseWheel(object sender, MouseWheelEventArgs e) => _interactionController.OnMouseWheel(sender, e);

        #endregion

        #region Toolbar Actions (Modeling)

        private void OnAddSomaClick(object sender, RoutedEventArgs e)
        {
            var newSoma = new SomaVisual(new Point3D(0, 0, 0), 2.0, Colors.DodgerBlue);
            _interactionController.StartPlacing(newSoma);
        }

        private void OnAddAxonClick(object sender, RoutedEventArgs e)
        {
            var start = new Point3D(0, 0, 0);
            var end = new Point3D(0, 0, 5);
            var newAxon = new AxonVisual(start, end, 0.5, Colors.LimeGreen); 
            _interactionController.StartPlacing(newAxon);
        }

        private void OnAddDendClick(object sender, RoutedEventArgs e)
        {
            var start = new Point3D(0, 0, 0);
            var end = new Point3D(0, 0, 5);
            var newDend = new DendVisual(start, end, 0.5, Colors.MediumPurple); 
            _interactionController.StartPlacing(newDend);
        }

        #endregion

        #region Toolbar Actions (Simulation)

        private void OnAddStimulationClick(object sender, RoutedEventArgs e)
        {
            // 向交互控制器下发“准备放置刺激设备”的指令
            _interactionController.StartPlacingDevice(DeviceType.Stimulation);
        }

        private void OnAddProbeClick(object sender, RoutedEventArgs e)
        {
            // 向交互控制器下发“准备放置探针设备”的指令
            _interactionController.StartPlacingDevice(DeviceType.Probe);
        }

        #endregion
    }
}