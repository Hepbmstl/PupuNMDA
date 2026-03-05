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
    /// <summary>
    /// 应用程序主窗口，承载所有 UI 组件的顶层容器。
    /// 职责：顶部标签页切换 (Modeling/Simulating/Reporting)、视口鼠标事件路由、
    /// HUD 叠加层管理、工具栏按钮响应、编辑弹窗控制。
    /// 由 App.xaml 的 StartupUri 启动。
    /// </summary>
    public partial class MainWindow : Window
    {
        /// <summary>跨模式共享的场景状态，持有实体列表、连接控制器和设备列表。由 InitializeControllers 创建。</summary>
        private SharedSceneState _scene = null!;

        /// <summary>建模模式交互控制器，处理实体的放置、选择、移动、连接等操作。由 InitializeControllers 创建。</summary>
        private InteractionController _modelingInteraction = null!;
        /// <summary>仿真模式交互控制器，处理刺激/探针设备的放置与拖拽。由 InitializeControllers 创建。</summary>
        private SimulationInteractionController _simulationInteraction = null!;

        /// <summary>建模模式左侧属性面板控制器，管理实体属性编辑卡片与离子通道选择器。由 InitializeControllers 创建。</summary>
        private PropertiesPanelController _propertiesPanelController = null!;
        /// <summary>仿真模式左侧面板控制器，管理刺激/探针设备的参数配置卡片。由 InitializeControllers 创建。</summary>
        private SimulationPanelController _simulationPanelController = null!;

        /// <summary>当前活跃的视口交互处理器，根据标签页切换在 _modelingInteraction 和 _simulationInteraction 之间切换。</summary>
        private IViewportInteractionHandler _activeHandler = null!;

        /// <summary>顶层标签页枚举，用于追踪当前激活的功能模式。</summary>
        private enum ActiveTab { Modeling, Simulating, Reporting }
        /// <summary>当前激活的标签页，默认为建模模式。</summary>
        private ActiveTab _activeTab = ActiveTab.Modeling;

        /// <summary>当前正在编辑尺寸的实体引用，用于 EditPopup 弹窗。由 ShowEditPopup 设置，OnApplyEdit/OnCancelEdit 清除。</summary>
        private IVisualEntity? _editingEntity;

        /// <summary>
        /// 构造函数，初始化 XAML 组件并在窗口 Loaded 后初始化控制器。
        /// </summary>
        public MainWindow()
        {
            InitializeComponent();
            Loaded += (s, e) => InitializeControllers();
        }

        /// <summary>
        /// 初始化所有控制器实例和事件关联。在窗口 Loaded 事件中调用。
        /// 创建 SharedSceneState、双模式交互控制器、双模式面板控制器，
        /// 并注册 CompositionTarget.Rendering 事件以实时更新连接线。
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
                _modelingInteraction);

            _simulationPanelController = new SimulationPanelController(
                SimulationPanelContainer,
                _simulationInteraction);

            _activeHandler = _modelingInteraction;

            // 每帧更新所有连接线位置，确保实体移动后连接线跟随
            CompositionTarget.Rendering += (s, e) => _scene.ConnectionController.UpdateAll();
        }

        #region Top-level Tab Switching

        /// <summary>点击 "Modeling" 标签页按钮时触发，切换至建模模式。由 XAML 按钮 Click 绑定。</summary>
        private void OnTabModelingClick(object sender, RoutedEventArgs e)
        {
            if (_activeTab != ActiveTab.Modeling) SwitchTab(ActiveTab.Modeling);
            SyncTabButtons();
        }

        /// <summary>点击 "Simulating" 标签页按钮时触发，切换至仿真模式。由 XAML 按钮 Click 绑定。</summary>
        private void OnTabSimulationClick(object sender, RoutedEventArgs e)
        {
            if (_activeTab != ActiveTab.Simulating) SwitchTab(ActiveTab.Simulating);
            SyncTabButtons();
        }

        /// <summary>点击 "Reporting" 标签页按钮时触发，切换至报告模式。由 XAML 按钮 Click 绑定。</summary>
        private void OnTabReportingClick(object sender, RoutedEventArgs e)
        {
            if (_activeTab != ActiveTab.Reporting) SwitchTab(ActiveTab.Reporting);
            SyncTabButtons();
        }

        /// <summary>
        /// 同步三个顶层标签页 ToggleButton 的 IsChecked 状态，使之与 _activeTab 一致。
        /// 被所有 OnTab*Click 方法调用。
        /// </summary>
        private void SyncTabButtons()
        {
            TabModeling.IsChecked = _activeTab == ActiveTab.Modeling;
            TabSimulation.IsChecked = _activeTab == ActiveTab.Simulating;
            TabReporting.IsChecked = _activeTab == ActiveTab.Reporting;
        }

        /// <summary>
        /// 执行标签页切换的核心逻辑：停用旧模式处理器、隐藏所有面板/工具栏、
        /// 显示目标模式的面板并切换 _activeHandler。
        /// 被 OnTab*Click 方法调用。
        /// </summary>
        /// <param name="target">目标标签页</param>
        private void SwitchTab(ActiveTab target)
        {
            // 停用当前模式的交互处理器
            if (_activeTab == ActiveTab.Modeling)
                _modelingInteraction.Deactivate();
            else if (_activeTab == ActiveTab.Simulating)
                _simulationInteraction.Deactivate();

            _activeTab = target;

            // 隐藏所有面板和工具栏
            ModelingPanelScroll.Visibility = Visibility.Collapsed;
            SimulatingPanelRoot.Visibility = Visibility.Collapsed;
            ReportingPanelRoot.Visibility = Visibility.Collapsed;
            ModelingToolbar.Visibility = Visibility.Collapsed;
            SimulationToolbar.Visibility = Visibility.Collapsed;
            EditPopup.Visibility = Visibility.Collapsed;
            ChannelSelectorPopup.IsOpen = false;

            // 根据目标模式显示对应面板并切换交互处理器
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
                    _activeHandler = _modelingInteraction; // 报告模式下使用建模交互作为只读回退
                    break;
            }
        }

        #endregion

        #region Simulating Sub-tab Switching

        /// <summary>
        /// 点击仿真模式下 "Insert" 子标签时触发，显示设备插入面板。
        /// 由 XAML 按钮 Click 绑定。
        /// </summary>
        private void OnSubTabInsertClick(object sender, RoutedEventArgs e)
        {
            SubTabInsert.IsChecked = true;
            SubTabSimulate.IsChecked = false;
            InsertPanelScroll.Visibility = Visibility.Visible;
            SimulatePanelScroll.Visibility = Visibility.Collapsed;
        }

        /// <summary>
        /// 点击仿真模式下 "Simulate" 子标签时触发，显示仿真参数配置面板。
        /// 由 XAML 按钮 Click 绑定。
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
        /// 点击 "Begin" 按钮时触发仿真运行（预留接口，尚未实现）。
        /// 可从 TbVInit, TbDt, TbSteps, TbENa, TbEK, TbELeak 读取参数。
        /// 由 XAML 按钮 Click 绑定。
        /// </summary>
        private void OnBeginSimulationClick(object sender, RoutedEventArgs e)
        {
            // Interface reserved for future implementation.
            // Parameters can be read from TbVInit, TbDt, TbSteps, TbENa, TbEK, TbELeak.
        }

        #endregion

        #region Viewport Mouse Event Routing

        /// <summary>将视口 MouseDown 路由到当前活跃交互处理器。由 XAML 的 HelixViewport3D.MouseDown 绑定。</summary>
        private void OnViewportMouseDown(object sender, MouseButtonEventArgs e)
            => _activeHandler?.OnMouseDown(sender, e);

        /// <summary>将视口 MouseMove 路由到当前活跃交互处理器。由 XAML 的 HelixViewport3D.MouseMove 绑定。</summary>
        private void OnViewportMouseMove(object sender, MouseEventArgs e)
            => _activeHandler?.OnMouseMove(sender, e);

        /// <summary>将视口 MouseUp 路由到当前活跃交互处理器。由 XAML 的 HelixViewport3D.MouseUp 绑定。</summary>
        private void OnViewportMouseUp(object sender, MouseButtonEventArgs e)
            => _activeHandler?.OnMouseUp(sender, e);

        /// <summary>将视口 MouseWheel 路由到当前活跃交互处理器。由 XAML 的 HelixViewport3D.MouseWheel 绑定。</summary>
        private void OnViewportMouseWheel(object sender, MouseWheelEventArgs e)
            => _activeHandler?.OnMouseWheel(sender, e);

        #endregion

        #region HUD and Overlay

        /// <summary>
        /// 更新准星位置和世界坐标 HUD 显示。
        /// 被 InteractionController 和 SimulationInteractionController 通过委托回调调用，
        /// 在每次鼠标移动时触发。
        /// </summary>
        /// <param name="mousePos">鼠标在视口控件中的屏幕坐标</param>
        /// <param name="worldPos">鼠标对应的三维世界坐标，若无有效命中则为 null</param>
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
        /// 显示实体尺寸编辑弹窗（EditPopup），根据实体类型显示不同的编辑字段。
        /// 被 InteractionController 的 _onResizeRequested 委托在用户右键选择 "Resize..." 时调用。
        /// </summary>
        /// <param name="entity">要编辑的可视化实体</param>
        /// <param name="mousePos">弹窗显示位置（鼠标在 ViewportGrid 中的坐标）</param>
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

        /// <summary>
        /// 应用编辑弹窗中的尺寸修改。将文本框值解析后写入实体属性。
        /// 由 EditPopup 中 "Apply" 按钮的 Click 事件绑定。
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
                else if (_editingEntity is SomaVisual soma)
                {
                    if (double.TryParse(TbRadius.Text, out double r)) soma.Radius = r;
                }
            }
            catch { }

            EditPopup.Visibility = Visibility.Collapsed;
            _editingEntity = null;
        }

        /// <summary>
        /// 取消编辑弹窗，不应用任何修改。
        /// 由 EditPopup 中 "Cancel" 按钮的 Click 事件绑定。
        /// </summary>
        private void OnCancelEdit(object sender, RoutedEventArgs e)
        {
            EditPopup.Visibility = Visibility.Collapsed;
            _editingEntity = null;
        }

        #endregion

        #region Toolbar Actions (Modeling)

        /// <summary>
        /// 点击 "Add Soma" 按钮，创建细胞体实体并进入放置模式。
        /// 由建模模式底部工具栏按钮 Click 绑定。
        /// </summary>
        private void OnAddSomaClick(object sender, RoutedEventArgs e)
        {
            var newSoma = new SomaVisual(new Point3D(0, 0, 0), 2.0, Colors.DodgerBlue);
            _modelingInteraction.StartPlacing(newSoma);
        }

        /// <summary>
        /// 点击 "Add Axon" 按钮，创建轴突实体并进入放置模式。
        /// 由建模模式底部工具栏按钮 Click 绑定。
        /// </summary>
        private void OnAddAxonClick(object sender, RoutedEventArgs e)
        {
            var start = new Point3D(0, 0, 0);
            var end = new Point3D(0, 0, 5);
            var newAxon = new AxonVisual(start, end, 0.5, Colors.LimeGreen);
            _modelingInteraction.StartPlacing(newAxon);
        }

        /// <summary>
        /// 点击 "Add Dend" 按钮，创建树突实体并进入放置模式。
        /// 由建模模式底部工具栏按钮 Click 绑定。
        /// </summary>
        private void OnAddDendClick(object sender, RoutedEventArgs e)
        {
            var start = new Point3D(0, 0, 0);
            var end = new Point3D(0, 0, 5);
            var newDend = new DendVisual(start, end, 0.5, Colors.MediumPurple);
            _modelingInteraction.StartPlacing(newDend);
        }

        #endregion

        #region Toolbar Actions (Simulation)

        /// <summary>
        /// 点击 "Add Stimulation" 按钮，在仿真模式下启动刺激设备放置流程。
        /// 由仿真模式底部工具栏按钮 Click 绑定。
        /// </summary>
        private void OnAddStimulationClick(object sender, RoutedEventArgs e)
        {
            _simulationInteraction.StartPlacingDevice(DeviceType.Stimulation);
        }

        /// <summary>
        /// 点击 "Add Probe" 按钮，在仿真模式下启动探针设备放置流程。
        /// 由仿真模式底部工具栏按钮 Click 绑定。
        /// </summary>
        private void OnAddProbeClick(object sender, RoutedEventArgs e)
        {
            _simulationInteraction.StartPlacingDevice(DeviceType.Probe);
        }

        #endregion
    }
}