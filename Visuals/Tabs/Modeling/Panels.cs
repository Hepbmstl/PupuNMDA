using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using NeuronCAD.Visuals.Tabs.Modeling.Visuals;

namespace NeuronCAD.Visuals.Tabs.Modeling
{
    public class PropertiesPanelController
    {
        private readonly StackPanel _container;
        private readonly InteractionController _interaction;
        private readonly Popup _channelPopup;
        private readonly StackPanel _channelSelectorList;

        // UI 缓存字典：通过实体 ID 映射对应的 Expander 控件，实现 O(1) 的状态查找与销毁
        private readonly Dictionary<string, Expander> _uiNodes = new Dictionary<string, Expander>();

        // 状态缓存：记录当前正在执行“添加离子通道”操作的目标实体
        private IVisualEntity _currentOperatingEntity;

        public PropertiesPanelController(StackPanel container, Popup popup, StackPanel popupList, InteractionController interaction)
        {
            _container = container;
            _channelPopup = popup;
            _channelSelectorList = popupList;
            _interaction = interaction;

            // 挂载 Interaction 事件总线
            _interaction.OnEntityAdded += HandleEntityAdded;
            _interaction.OnEntityRemoved += HandleEntityRemoved;
            _interaction.OnSelectionChanged += HandleSelectionChanged;

            InitializeChannelSelector();
        }

        // 初始化全局弹窗菜单：读取 GlobalBiophysics 字典，生成待选按钮
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
                    Tag = kvp.Value // 将具体的 ChannelProperty 存入 Tag 传递给回调函数
                };

                btn.Click += (s, e) =>
                {
                    var ch = (ChannelProperty)((Button)s).Tag;
                    if (_currentOperatingEntity != null)
                    {
                        // 校验该实体是否已存在同名通道
                        if (!_currentOperatingEntity.Channels.ContainsKey(ch.Name))
                        {
                            _currentOperatingEntity.Channels.Add(ch.Name, ch);
                            _currentOperatingEntity.UpdateChannelVisuals(); // 触发 3D 视口重绘点云
                            RefreshChannelList(_currentOperatingEntity);    // 触发面板 UI 列表刷新
                        }
                    }
                    _channelPopup.IsOpen = false; // 关闭弹窗
                };
                _channelSelectorList.Children.Add(btn);
            }
        }

        private void HandleEntityAdded(IVisualEntity entity)
        {
            var expander = BuildEntityNode(entity);
            _uiNodes[entity.Id] = expander;
            _container.Children.Add(expander);
        }

        private void HandleEntityRemoved(IVisualEntity entity)
        {
            if (_uiNodes.TryGetValue(entity.Id, out var expander))
            {
                _container.Children.Remove(expander);
                _uiNodes.Remove(entity.Id);
            }
        }

        private void HandleSelectionChanged(IVisualEntity? selectedEntity)
        {
            // 遍历所有 UI 节点，比对 ID，同步折叠/展开与高亮状态
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

        // 核心装配：动态构建单个实体的控制面板
        private Expander BuildEntityNode(IVisualEntity entity)
        {
            string entityType = entity is AxonVisual ? "Axon" : "Soma";
            var expander = new Expander
            {
                Header = $"{entityType} [{entity.Id.Substring(0, 4)}]",
                Foreground = Brushes.LightGray,
                Margin = new Thickness(0, 0, 0, 5)
            };

            // 逆向同步：当用户手动展开面板时，强行让 3D 视口选中该物体
            expander.Expanded += (s, e) => _interaction.ForceSelect(entity);

            var panel = new StackPanel { Margin = new Thickness(10, 5, 0, 5) };

            // 1. 颜色修改状态注入
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

            // 2. 尺寸修改状态注入 (模式匹配提取派生类特有属性)
            if (entity is AxonVisual axon)
            {
                panel.Children.Add(new TextBlock { Text = "Radius:", Foreground = Brushes.Gray, Margin = new Thickness(0, 5, 0, 0) });
                var tbRadius = new TextBox { Text = axon.Radius.ToString("F2"), Background = Brushes.DarkGray, Foreground = Brushes.White };
                tbRadius.LostFocus += (s, e) => { if (double.TryParse(tbRadius.Text, out double v)) axon.Radius = v; };
                panel.Children.Add(tbRadius);

                panel.Children.Add(new TextBlock { Text = "Length:", Foreground = Brushes.Gray, Margin = new Thickness(0, 5, 0, 0) });
                var tbLength = new TextBox { Text = axon.Length.ToString("F2"), Background = Brushes.DarkGray, Foreground = Brushes.White };
                tbLength.LostFocus += (s, e) => { if (double.TryParse(tbLength.Text, out double v)) axon.Length = v; };
                panel.Children.Add(tbLength);
            }
            else if (entity is SomaVisual soma)
            {
                panel.Children.Add(new TextBlock { Text = "Radius:", Foreground = Brushes.Gray, Margin = new Thickness(0, 5, 0, 0) });
                var tbRadius = new TextBox { Text = soma.Radius.ToString("F2"), Background = Brushes.DarkGray, Foreground = Brushes.White };
                tbRadius.LostFocus += (s, e) => { if (double.TryParse(tbRadius.Text, out double v)) soma.Radius = v; };
                panel.Children.Add(tbRadius);
            }

            // 3. 离子通道容器区状态构建
            var channelHeader = new TextBlock { Text = "Ion Channels:", Foreground = Brushes.Gray, Margin = new Thickness(0, 10, 0, 0) };
            panel.Children.Add(channelHeader);

            var channelListPanel = new StackPanel();
            panel.Children.Add(channelListPanel);

            var btnAddChannel = new Button { Content = "+ Add Channel", Margin = new Thickness(0, 5, 0, 0), Background = Brushes.DarkSlateGray, Foreground = Brushes.White };
            btnAddChannel.Click += (s, e) =>
            {
                _currentOperatingEntity = entity; // 记录当前目标
                _channelPopup.IsOpen = true;      // 呼出弹窗
            };
            panel.Children.Add(btnAddChannel);

            expander.Content = panel;

            // 将内部的 Channel StackPanel 存入 Expander 的 Tag，方便局部刷新时提取
            expander.Tag = channelListPanel;
            RefreshChannelList(entity, channelListPanel);

            return expander;
        }

        // 局部刷新某个实体的离子通道列表 UI
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
                    // 状态移除序列：清理内存 -> 重算 3D 渲染面 -> 销毁 UI 节点
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