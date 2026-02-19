using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Media3D;
using NeuronCAD.Visuals.Tabs.Modeling.Visuals;

namespace NeuronCAD.Visuals.Tabs.Modeling
{
    public partial class ModelingPage : UserControl
    {
        private ViewportController _viewportController = null!;
        private InteractionController _interactionController = null!;

        // 当前正在编辑的实体引用
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
        }

        #region HUD & Overlay API (供 InteractionController 调用)

        /// <summary>
        /// 更新十字准星和坐标显示
        /// </summary>
        public void UpdateCursorInfo(Point mousePos, Point3D? worldPos)
        {
            // 1. 移动十字准星
            Canvas.SetLeft(CrosshairPath, mousePos.X);
            Canvas.SetTop(CrosshairPath, mousePos.Y);

            // 2. 更新坐标文本
            if (worldPos.HasValue)
            {
                var p = worldPos.Value;
                CoordText.Text = $"X:{p.X:F2} Y:{p.Y:F2} Z:{p.Z:F2}";
                CoordHud.Visibility = Visibility.Visible;
                
                // HUD 放在鼠标右下角一点
                Canvas.SetLeft(CoordHud, mousePos.X + 15);
                Canvas.SetTop(CoordHud, mousePos.Y + 15);
            }
            else
            {
                CoordHud.Visibility = Visibility.Collapsed;
            }
        }

        /// <summary>
        /// 显示编辑弹窗
        /// </summary>
        public void ShowEditPopup(IVisualEntity entity, Point mousePos)
        {
            _editingEntity = entity;
            
            // 根据实体类型显示不同的输入框
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

            // 移动弹窗到鼠标附近 (防止超出边界的逻辑可后续添加)
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

        // ... 以下为事件转发，保持不变 ...
        private void OnViewportMouseDown(object sender, MouseButtonEventArgs e) => _interactionController.OnMouseDown(sender, e);
        private void OnViewportMouseMove(object sender, MouseEventArgs e) => _interactionController.OnMouseMove(sender, e);
        private void OnViewportMouseUp(object sender, MouseButtonEventArgs e) => _interactionController.OnMouseUp(sender, e);
        private void OnViewportMouseWheel(object sender, MouseWheelEventArgs e) => _interactionController.OnMouseWheel(sender, e);

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
    }
}