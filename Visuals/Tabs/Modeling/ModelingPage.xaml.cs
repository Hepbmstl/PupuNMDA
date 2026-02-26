using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Media3D;
using NeuronCAD.Visuals.Tabs.Modeling.Visuals;
//using Popup = NeuronCAD.Visuals.Tabs.Modeling.Visuals.Popup;
using System.Windows.Controls.Primitives;

namespace NeuronCAD.Visuals.Tabs.Modeling
{
    public partial class ModelingPage : UserControl
    {
        private ViewportController _viewportController = null!;
        private InteractionController _interactionController = null!;

        // 声明左侧面板属性控制器，持有 UI 生命周期引用
        private PropertiesPanelController _propertiesPanelController = null!;

        // 快捷编辑弹窗追踪的实体状态
        private IVisualEntity? _editingEntity;

        public ModelingPage()
        {
            InitializeComponent();
            InitializeControllers();
        }

        private void InitializeControllers()
        {
            // 1. 初始化视口与交互控制器
            _viewportController = new ViewportController(MainViewport);
            _interactionController = new InteractionController(this, _viewportController, MainViewport);

            // 2. 挂载左侧面板及 Popup 弹窗状态
            // 将 XAML 中的容器组件实例传递给面板控制器，由其接管后续的 DOM 增删操作
            _propertiesPanelController = new PropertiesPanelController(
                PropertiesPanelContainer,
                ChannelSelectorPopup,
                ChannelSelectorList,
                _interactionController
            );
        }

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

            if (entity is AxonVisual axon)
            {
                PanelAxonLength.Visibility = Visibility.Visible;
                PanelRadius.Visibility = Visibility.Visible;
                TbLength.Text = axon.Length.ToString("F2");
                TbRadius.Text = axon.Radius.ToString("F2");
            }
            else if (entity is SomaVisual soma)
            {
                PanelAxonLength.Visibility = Visibility.Collapsed;
                PanelRadius.Visibility = Visibility.Visible;
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
                // 写入数据将自动触发 UpdateGeometry，并级联触发 UpdateChannelVisuals
                if (_editingEntity is AxonVisual axon)
                {
                    if (double.TryParse(TbLength.Text, out double l)) axon.Length = l;
                    if (double.TryParse(TbRadius.Text, out double r)) axon.Radius = r;
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

        #region Toolbar Actions

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

        #endregion
    }
}