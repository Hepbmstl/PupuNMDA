using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Media3D;
using NeuronCAD.Visuals.Tabs.Modeling.Visuals;

namespace NeuronCAD.Visuals.Tabs.Modeling
{
    /// <summary>
    /// 建模模式下的属性面板控制器。
    /// 职责：管理左侧属性面板中实体卡片的创建/删除/选中高亮，以及离子通道选择弹窗。
    /// 由 MainWindow.InitializeControllers 创建，订阅 InteractionController 的事件总线。
    /// </summary>
    public class PropertiesPanelController
    {
        /// <summary>属性面板容器（左侧栏 StackPanel），由构造函数注入。</summary>
        private readonly StackPanel _container;

        /// <summary>建模交互控制器引用，用于订阅实体增删/选中事件及调用 ForceSelect。</summary>
        private readonly InteractionController _interaction;

        /// <summary>离子通道选择弹窗引用（MainWindow.xaml 中定义）。</summary>
        private readonly Popup _channelPopup;

        /// <summary>离子通道弹窗内部按钮列表容器。</summary>
        private readonly StackPanel _channelSelectorList;

        /// <summary>实体 ID 到属性卡片 Expander 的映射字典，用于快速查找和移除 UI 节点。</summary>
        private readonly Dictionary<string, Expander> _uiNodes = new Dictionary<string, Expander>();

        /// <summary>当前正在操作“添加通道”的目标实体，由 btnAddChannel.Click 设置。</summary>
        private IVisualEntity _currentOperatingEntity;

        /// <summary>相机跳转回调，由 MainWindow 注入。</summary>
        private readonly Action<Point3D>? _navigateToPoint;

        /// <summary>标记是否正在通过代码设置展开状态（防止递归触发选中）。</summary>
        private bool _suppressExpandEvent;

        /// <summary>
        /// 构造函数。由 MainWindow.InitializeControllers 调用，注入 UI 容器和交互控制器，并订阅事件。
        /// </summary>
        /// <param name="container">左侧属性面板 StackPanel</param>
        /// <param name="popup">离子通道选择弹窗</param>
        /// <param name="popupList">弹窗内按钮列表容器</param>
        /// <param name="interaction">建模交互控制器（用于订阅事件）</param>
        public PropertiesPanelController(StackPanel container, Popup popup, StackPanel popupList, InteractionController interaction, Action<Point3D>? navigateToPoint = null)
        {
            _container = container;
            _channelPopup = popup;
            _channelSelectorList = popupList;
            _interaction = interaction;
            _navigateToPoint = navigateToPoint;

            _interaction.OnEntityAdded += HandleEntityAdded;
            _interaction.OnEntityRemoved += HandleEntityRemoved;
            _interaction.OnSelectionChanged += HandleSelectionChanged;

            InitializeChannelSelector();
        }

        /// <summary>
        /// 初始化离子通道选择器：为 GlobalBiophysics.GlobalChannels 中每个通道创建按钮，
        /// 点击后为当前操作实体添加该通道并刷新可视化。
        /// 由构造函数调用。
        /// </summary>
        private void InitializeChannelSelector()
        {
            _channelSelectorList.Children.Clear();
            foreach (var kvp in GlobalBiophysics.GlobalChannels)
            {
                var btn = new Button
                {
                    Content = kvp.Value.Name,
                    Background = new SolidColorBrush(Color.FromArgb(255, 60, 60, 60)),
                    Foreground = Brushes.White,
                    Margin = new Thickness(0, 2, 0, 2),
                    Tag = kvp.Value
                };

                btn.Click += (s, e) =>
                {
                    var ch = (ChannelProperty)((Button)s).Tag;
                    if (_currentOperatingEntity != null)
                    {
                        if (!_currentOperatingEntity.Channels.ContainsKey(ch.Name))
                        {
                            _currentOperatingEntity.Channels.Add(ch.Name, ch);
                            _currentOperatingEntity.UpdateChannelVisuals();
                            RefreshChannelList(_currentOperatingEntity);
                        }
                    }
                    _channelPopup.IsOpen = false;
                };
                _channelSelectorList.Children.Add(btn);
            }
        }

        /// <summary>
        /// 处理实体添加事件：为新实体创建属性卡片并添加到面板。
        /// 被 InteractionController.OnEntityAdded 事件触发调用。
        /// </summary>
        private void HandleEntityAdded(IVisualEntity entity)
        {
            var expander = BuildEntityNode(entity);
            _uiNodes[entity.Id] = expander;
            _container.Children.Add(expander);
        }

        /// <summary>
        /// 处理实体删除事件：移除对应的属性卡片。
        /// 被 InteractionController.OnEntityRemoved 事件触发调用。
        /// </summary>
        private void HandleEntityRemoved(IVisualEntity entity)
        {
            if (_uiNodes.TryGetValue(entity.Id, out var expander))
            {
                _container.Children.Remove(expander);
                _uiNodes.Remove(entity.Id);
            }
        }

        /// <summary>
        /// 处理选中实体变更事件：展开对应卡片并橙色高亮边框，折叠其他卡片。
        /// 被 InteractionController.OnSelectionChanged 事件触发调用。
        /// </summary>
        private void HandleSelectionChanged(IVisualEntity? selectedEntity)
        {
            _suppressExpandEvent = true;
            try
            {
                foreach (var kvp in _uiNodes)
                {
                    var entId = kvp.Key;
                    var expander = kvp.Value;

                    if (selectedEntity != null && entId == selectedEntity.Id)
                    {
                        expander.IsExpanded = true;
                        expander.BorderBrush = Brushes.Orange;
                        expander.BorderThickness = new Thickness(1);
                    }
                    else
                    {
                        expander.IsExpanded = false;
                        expander.BorderThickness = new Thickness(0);
                    }
                }
            }
            finally
            {
                _suppressExpandEvent = false;
            }
        }

        /// <summary>
        /// 构建实体属性卡片 Expander，包含颜色、尺寸参数输入框和离子通道列表。
        /// 由 HandleEntityAdded 调用。展开时会调用 InteractionController.ForceSelect 同步选中。
        /// </summary>
        /// <param name="entity">目标实体</param>
        /// <returns>构建完成的 Expander 控件</returns>
        private Expander BuildEntityNode(IVisualEntity entity)
        {
            string entityType = "Unknown";
            if (entity is AxonVisual axon)
            {
                entityType = axon.VisualType;
            }
            else if (entity is SomaVisual)
            {
                entityType = "Soma";
            }

            var expander = new Expander
            {
                Header = $"{entityType} [{entity.Id.Substring(0, 4)}]",
                Foreground = Brushes.LightGray,
                Margin = new Thickness(0, 0, 0, 5)
            };

            expander.Expanded += (s, e) =>
            {
                if (_suppressExpandEvent) return;
                _interaction.ForceSelect(entity);
                _navigateToPoint?.Invoke(entity.CenterPosition);
            };

            var panel = new StackPanel { Margin = new Thickness(10, 5, 0, 5) };

            panel.Children.Add(new TextBlock { Text = "Color (Hex):", Foreground = Brushes.Gray });
            var tbColor = new TextBox { Text = entity.CurrentColor.ToString(), Background = Brushes.DarkGray, Foreground = Brushes.White };
            tbColor.LostFocus += (s, e) =>
            {
                try
                {
                    var c = (Color)ColorConverter.ConvertFromString(tbColor.Text);
                    entity.SetColor(c);
                }
                catch { tbColor.Text = entity.CurrentColor.ToString(); }
            };
            panel.Children.Add(tbColor);

            if (entity is AxonVisual axonEntity)
            {
                panel.Children.Add(new TextBlock { Text = "Base Radius (µm):", Foreground = Brushes.Gray, Margin = new Thickness(0, 5, 0, 0) });
                var tbBaseRadius = new TextBox { Text = axonEntity.BaseRadius.ToString("F2"), Background = Brushes.DarkGray, Foreground = Brushes.White };
                tbBaseRadius.LostFocus += (s, e) => { if (double.TryParse(tbBaseRadius.Text, out double v)) axonEntity.BaseRadius = v; };
                panel.Children.Add(tbBaseRadius);

                panel.Children.Add(new TextBlock { Text = "Top Radius (µm):", Foreground = Brushes.Gray, Margin = new Thickness(0, 5, 0, 0) });
                var tbTopRadius = new TextBox { Text = axonEntity.TopRadius.ToString("F2"), Background = Brushes.DarkGray, Foreground = Brushes.White };
                tbTopRadius.LostFocus += (s, e) => { if (double.TryParse(tbTopRadius.Text, out double v)) axonEntity.TopRadius = v; };
                panel.Children.Add(tbTopRadius);

                panel.Children.Add(new TextBlock { Text = "Length (µm):", Foreground = Brushes.Gray, Margin = new Thickness(0, 5, 0, 0) });
                var tbLength = new TextBox { Text = axonEntity.Length.ToString("F2"), Background = Brushes.DarkGray, Foreground = Brushes.White };
                tbLength.LostFocus += (s, e) => { if (double.TryParse(tbLength.Text, out double v)) axonEntity.Length = v; };
                panel.Children.Add(tbLength);
            }
            else if (entity is SomaVisual soma)
            {
                panel.Children.Add(new TextBlock { Text = "Radius (µm):", Foreground = Brushes.Gray, Margin = new Thickness(0, 5, 0, 0) });
                var tbRadius = new TextBox { Text = soma.Radius.ToString("F2"), Background = Brushes.DarkGray, Foreground = Brushes.White };
                tbRadius.LostFocus += (s, e) => { if (double.TryParse(tbRadius.Text, out double v)) soma.Radius = v; };
                panel.Children.Add(tbRadius);
            }

            // Cm (µF/cm²)
            panel.Children.Add(new TextBlock { Text = "Cm (µF/cm²):", Foreground = Brushes.Gray, Margin = new Thickness(0, 5, 0, 0) });
            var tbCm = new TextBox { Text = entity.Cm.ToString("F2"), Background = Brushes.DarkGray, Foreground = Brushes.White };
            tbCm.LostFocus += (s, e) => { if (double.TryParse(tbCm.Text, out double v) && v > 0) entity.Cm = v; else tbCm.Text = entity.Cm.ToString("F2"); };
            panel.Children.Add(tbCm);

            // Ra (Ω·cm)
            panel.Children.Add(new TextBlock { Text = "Ra (Ω·cm):", Foreground = Brushes.Gray, Margin = new Thickness(0, 5, 0, 0) });
            var tbRa = new TextBox { Text = entity.Ra.ToString("F2"), Background = Brushes.DarkGray, Foreground = Brushes.White };
            tbRa.LostFocus += (s, e) => { if (double.TryParse(tbRa.Text, out double v) && v > 0) entity.Ra = v; else tbRa.Text = entity.Ra.ToString("F2"); };
            panel.Children.Add(tbRa);

            var channelHeader = new TextBlock { Text = "Ion Channels:", Foreground = Brushes.Gray, Margin = new Thickness(0, 10, 0, 0) };
            panel.Children.Add(channelHeader);

            var channelListPanel = new StackPanel();
            panel.Children.Add(channelListPanel);

            var btnAddChannel = new Button { Content = "+ Add Channel", Margin = new Thickness(0, 5, 0, 0), Background = Brushes.DarkSlateGray, Foreground = Brushes.White };
            btnAddChannel.Click += (s, e) =>
            {
                _currentOperatingEntity = entity;
                _channelPopup.IsOpen = true;
            };
            panel.Children.Add(btnAddChannel);

            expander.Content = panel;

            expander.Tag = channelListPanel;
            RefreshChannelList(entity, channelListPanel);

            return expander;
        }

        /// <summary>
        /// 刷新实体卡片中的离子通道列表，显示当前已添加的通道名称和删除按钮。
        /// 由 BuildEntityNode（初始构建）和通道增删操作后调用。
        /// </summary>
        /// <param name="entity">目标实体</param>
        /// <param name="listPanel">通道列表容器（可选，null 时从 _uiNodes 查找）</param>
        private void RefreshChannelList(IVisualEntity entity, StackPanel listPanel = null)
        {
            if (listPanel == null && _uiNodes.TryGetValue(entity.Id, out var expander))
            {
                listPanel = expander.Tag as StackPanel;
            }

            if (listPanel == null) return;
            listPanel.Children.Clear();

            foreach (var kvp in entity.Channels)
            {
                var chName = kvp.Key;
                var row = new Grid { Margin = new Thickness(0, 2, 0, 2) };
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

                var txt = new TextBlock { Text = chName, Foreground = Brushes.LightGray, VerticalAlignment = VerticalAlignment.Center };
                Grid.SetColumn(txt, 0);

                var btnDel = new Button { Content = "-", Width = 20, Background = Brushes.Maroon, Foreground = Brushes.White };
                btnDel.Click += (s, e) =>
                {
                    entity.Channels.Remove(chName);
                    entity.UpdateChannelVisuals();
                    RefreshChannelList(entity, listPanel);
                };
                Grid.SetColumn(btnDel, 1);

                row.Children.Add(txt);
                row.Children.Add(btnDel);
                listPanel.Children.Add(row);
            }
        }
    }
}