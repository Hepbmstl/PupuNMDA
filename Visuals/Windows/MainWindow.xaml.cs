using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Media3D;
using System.Windows.Threading;
using Microsoft.Win32;
using NeuronCAD.Backward;
using NeuronCAD.Visuals.Tabs.Shared;
using NeuronCAD.Visuals.Tabs.Modeling;
using NeuronCAD.Visuals.Tabs.Modeling.Visuals;
using NeuronCAD.Visuals.Tabs.Simulation;
using NeuronCAD.Visuals.Tabs.Reporting;

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

        /// <summary>Reporting 模式交互控制器，处理区室分块显示与悬停高亮。由 InitializeControllers 创建。</summary>
        private ReportingInteractionController _reportingInteraction = null!;
        /// <summary>Reporting 模式左侧面板控制器，按实体分组展示区室列表。由 InitializeControllers 创建。</summary>
        private ReportingPanelController _reportingPanelController = null!;

        /// <summary>当前活跃的视口交互处理器，根据标签页切换在 _modelingInteraction 和 _simulationInteraction 之间切换。</summary>
        private IViewportInteractionHandler _activeHandler = null!;

        /// <summary>顶层标签页枚举，用于追踪当前激活的功能模式。</summary>
        private enum ActiveTab { Modeling, Simulating, Reporting }
        /// <summary>当前激活的标签页，默认为建模模式。</summary>
        private ActiveTab _activeTab = ActiveTab.Modeling;

        /// <summary>当前正在编辑尺寸的实体引用，用于 EditPopup 弹窗。由 ShowEditPopup 设置，OnApplyEdit/OnCancelEdit 清除。</summary>
        private IVisualEntity? _editingEntity;

        /// <summary>仿真运行器实例，管理 Python 互操作和仿真执行。</summary>
        private SimulationRunner? _simulationRunner;

        /// <summary>仿真进度轮询定时器，每 100ms 从 SimulationRunner 读取当前步数并更新 UI。</summary>
        private DispatcherTimer? _simProgressTimer;

        /// <summary>是否正在仿真中。为 true 时禁止所有建模/仿真交互操作。</summary>
        private bool _isSimulating;

        /// <summary>当前项目文件路径。Save 时直接覆盖，为 null 时走 Save As 流程。</summary>
        private string? _currentProjectPath;

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

            // 将建模实体的创建/删除同步登记到仿真注册表
            _modelingInteraction.OnEntityAdded += entity => _scene.SimulationRegistry.Register(entity);
            _modelingInteraction.OnEntityRemoved += entity => _scene.SimulationRegistry.Unregister(entity.Id);

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
            else if (_activeTab == ActiveTab.Reporting)
                _reportingInteraction.Deactivate();

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
                    _activeHandler = _reportingInteraction;
                    _reportingInteraction.Activate();
                    _reportingPanelController.Rebuild();
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
        /// 点击 "Begin" 按钮时触发仿真运行。
        /// 读取 UI 参数 → 构建仿真数据 → 通过 SimulationRunner 调用 Hines_method.py。
        /// 仿真期间禁止所有修改操作，显示实时进度。
        /// 由 XAML 按钮 Click 绑定。
        /// </summary>
        private async void OnBeginSimulationClick(object sender, RoutedEventArgs e)
        {
            if (_isSimulating) return;

            var registry = _scene.SimulationRegistry;

            // 读取区室化模式和参数
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

            // 一次性构建完整仿真数据：区室化 + 设备绑定
            var simData = registry.BuildSimulationData(
                _scene.ConnectionController.ConnectionsById,
                _scene.Devices);

            if (simData.Compartments.Count == 0)
            {
                MessageBox.Show("没有可仿真的区室，请先添加建模实体。", "Simulation",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            // 读取仿真参数
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

            // ── 进入仿真状态：禁用交互，显示进度面板 ──
            _isSimulating = true;
            SetSimulationLockUI(true);

            SimProgressPanel.Visibility = Visibility.Visible;
            SimStatusText.Text = "Simulating...";
            SimStepText.Text = $"Step: 0 / {steps}";
            SimProgressBar.Maximum = steps;
            SimProgressBar.Value = 0;

            // 创建运行器
            _simulationRunner = new SimulationRunner();

            // 启动进度轮询定时器
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

                // ── 仿真完成 ──
                SimStatusText.Text = "Simulation Complete";
                SimStatusText.Foreground = new SolidColorBrush(Color.FromRgb(0x00, 0xFF, 0x88));
                SimStepText.Text = $"Step: {steps} / {steps}";
                SimProgressBar.Value = steps;

                // 存储仿真数据供 Reporting 面板使用
                _scene.LastSimulationData = simData;

                MessageBox.Show(
                    $"仿真完成：\n" +
                    $"  区室数: {simData.Compartments.Count}\n" +
                    $"  电流钳数: {simData.Stimulations.Count}\n" +
                    $"  电压钳数: {simData.VoltageClamps.Count}\n" +
                    $"  探针数: {simData.Probes.Count}\n" +
                    $"  总步数: {steps}",
                    "Simulation",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                // pythonnet 将回调中抛出的 OperationCanceledException 包装为 PythonException，
                // 所以直接检查 WasAborted 标志更可靠。
                bool aborted = _simulationRunner?.WasAborted == true
                            || ex.InnerException is OperationCanceledException
                            || ex is OperationCanceledException;
                if (aborted)
                {
                    SimStatusText.Text = "Simulation Aborted";
                    SimStatusText.Foreground = new SolidColorBrush(Colors.OrangeRed);
                    MessageBox.Show("仿真已被用户终止。", "Simulation Aborted",
                        MessageBoxButton.OK, MessageBoxImage.Warning);
                }
                else
                {
                    SimStatusText.Text = "Simulation Failed";
                    SimStatusText.Foreground = new SolidColorBrush(Colors.Red);
                    MessageBox.Show($"仿真失败：\n{ex.Message}", "Simulation Error",
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
        /// 切换仿真锁定状态：禁用/启用工具栏、标签页切换和 Begin 按钮。
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
        /// 点击 "Abort" 按钮时强制终止仿真。
        /// 通过 SimulationRunner.Abort() 在下一次 Python 回调时中断执行。
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

        /// <summary>将视口 MouseDown 路由到当前活跃交互处理器。由 XAML 的 HelixViewport3D.MouseDown 绑定。</summary>
        private void OnViewportMouseDown(object sender, MouseButtonEventArgs e)
        {
            if (_isSimulating) return;
            _activeHandler?.OnMouseDown(sender, e);
        }

        /// <summary>将视口 MouseMove 路由到当前活跃交互处理器。由 XAML 的 HelixViewport3D.MouseMove 绑定。</summary>
        private void OnViewportMouseMove(object sender, MouseEventArgs e)
        {
            if (_isSimulating) return;
            _activeHandler?.OnMouseMove(sender, e);
        }

        /// <summary>将视口 MouseUp 路由到当前活跃交互处理器。由 XAML 的 HelixViewport3D.MouseUp 绑定。</summary>
        private void OnViewportMouseUp(object sender, MouseButtonEventArgs e)
        {
            if (_isSimulating) return;
            _activeHandler?.OnMouseUp(sender, e);
        }

        /// <summary>将视口 MouseWheel 路由到当前活跃交互处理器。由 XAML 的 HelixViewport3D.MouseWheel 绑定。</summary>
        private void OnViewportMouseWheel(object sender, MouseWheelEventArgs e)
        {
            if (_isSimulating) return;
            _activeHandler?.OnMouseWheel(sender, e);
        }

        #endregion

        #region HUD and Overlay

        /// <summary>
        /// 平滑将相机视角对准指定世界坐标点（500ms 动画过渡）。
        /// 被 PropertiesPanelController 和 SimulationPanelController 的面板展开事件调用。
        /// </summary>
        private void NavigateToPoint(Point3D target)
        {
            MainViewport.LookAt(target, 500);
        }

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
            var start = new Point3D(0, 0, 0);
            var end = new Point3D(0, 0, 10);
            var newSoma = new SomaVisual(start, end, 5.0, Colors.DodgerBlue);
            _modelingInteraction.StartPlacing(newSoma);
        }

        /// <summary>
        /// 点击 "Add Axon" 按钮，创建轴突实体并进入放置模式。
        /// 由建模模式底部工具栏按钮 Click 绑定。
        /// </summary>
        private void OnAddAxonClick(object sender, RoutedEventArgs e)
        {
            var start = new Point3D(0, 0, 0);
            var end = new Point3D(0, 0, 50);
            var newAxon = new AxonVisual(start, end, 2.5, Colors.LimeGreen);
            _modelingInteraction.StartPlacing(newAxon);
        }

        /// <summary>
        /// 点击 "Add Dend" 按钮，创建树突实体并进入放置模式。
        /// 由建模模式底部工具栏按钮 Click 绑定。
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

        /// <summary>
        /// 点击 "Add VoltageClamp" 按钮，在仿真模式下启动电压钳设备放置流程。
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

        #region File Menu

        private void OnNewProjectClick(object sender, RoutedEventArgs e)
        {
            if (_isSimulating) return;
            var result = MessageBox.Show("是否创建新项目？当前未保存的更改将丢失。",
                "New Project", MessageBoxButton.OKCancel, MessageBoxImage.Question);
            if (result != MessageBoxResult.OK) return;

            ClearScene();
            _currentProjectPath = null;
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

                _currentProjectPath = dlg.FileName;
                Title = $"NeuronCAD 2026 — {System.IO.Path.GetFileName(dlg.FileName)}";

                // 显示加载参数概要
                var summary = SaveLoadManager.GetLoadedParamsSummary(project);
                MessageBox.Show(
                    $"项目加载成功：{System.IO.Path.GetFileName(dlg.FileName)}\n\n{summary}",
                    "Project Loaded", MessageBoxButton.OK, MessageBoxImage.Information);

                // 切换到建模模式查看加载结果
                if (_activeTab != ActiveTab.Modeling)
                {
                    SwitchTab(ActiveTab.Modeling);
                    SyncTabButtons();
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"加载失败：\n{ex.Message}", "Error",
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

            SaveToFile(dlg.FileName);
            _currentProjectPath = dlg.FileName;
            Title = $"NeuronCAD 2026 — {System.IO.Path.GetFileName(dlg.FileName)}";
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
                    segMode, nSeg, lSeg);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"保存失败：\n{ex.Message}", "Error",
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

            IonChannelParams.ResetToDefault();
        }

        #endregion
    }
}