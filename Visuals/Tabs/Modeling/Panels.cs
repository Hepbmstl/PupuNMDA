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
    // ==========================================
    // 建模模式下的属性面板控制器 (原逻辑保留)
    // ==========================================
    public class PropertiesPanelController
    {
        private readonly StackPanel _container;
        private readonly InteractionController _interaction;
        private readonly Popup _channelPopup;
        private readonly StackPanel _channelSelectorList;

        private readonly Dictionary<string, Expander> _uiNodes = new Dictionary<string, Expander>();
        private IVisualEntity _currentOperatingEntity;

        public PropertiesPanelController(StackPanel container, Popup popup, StackPanel popupList, InteractionController interaction)
        {
            _container = container;
            _channelPopup = popup;
            _channelSelectorList = popupList;
            _interaction = interaction;

            _interaction.OnEntityAdded += HandleEntityAdded;
            _interaction.OnEntityRemoved += HandleEntityRemoved;
            _interaction.OnSelectionChanged += HandleSelectionChanged;

            InitializeChannelSelector();
        }

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

            expander.Expanded += (s, e) => _interaction.ForceSelect(entity);

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
                panel.Children.Add(new TextBlock { Text = "Base Radius:", Foreground = Brushes.Gray, Margin = new Thickness(0, 5, 0, 0) });
                var tbBaseRadius = new TextBox { Text = axonEntity.BaseRadius.ToString("F2"), Background = Brushes.DarkGray, Foreground = Brushes.White };
                tbBaseRadius.LostFocus += (s, e) => { if (double.TryParse(tbBaseRadius.Text, out double v)) axonEntity.BaseRadius = v; };
                panel.Children.Add(tbBaseRadius);

                panel.Children.Add(new TextBlock { Text = "Top Radius:", Foreground = Brushes.Gray, Margin = new Thickness(0, 5, 0, 0) });
                var tbTopRadius = new TextBox { Text = axonEntity.TopRadius.ToString("F2"), Background = Brushes.DarkGray, Foreground = Brushes.White };
                tbTopRadius.LostFocus += (s, e) => { if (double.TryParse(tbTopRadius.Text, out double v)) axonEntity.TopRadius = v; };
                panel.Children.Add(tbTopRadius);

                panel.Children.Add(new TextBlock { Text = "Length:", Foreground = Brushes.Gray, Margin = new Thickness(0, 5, 0, 0) });
                var tbLength = new TextBox { Text = axonEntity.Length.ToString("F2"), Background = Brushes.DarkGray, Foreground = Brushes.White };
                tbLength.LostFocus += (s, e) => { if (double.TryParse(tbLength.Text, out double v)) axonEntity.Length = v; };
                panel.Children.Add(tbLength);
            }
            else if (entity is SomaVisual soma)
            {
                panel.Children.Add(new TextBlock { Text = "Radius:", Foreground = Brushes.Gray, Margin = new Thickness(0, 5, 0, 0) });
                var tbRadius = new TextBox { Text = soma.Radius.ToString("F2"), Background = Brushes.DarkGray, Foreground = Brushes.White };
                tbRadius.LostFocus += (s, e) => { if (double.TryParse(tbRadius.Text, out double v)) soma.Radius = v; };
                panel.Children.Add(tbRadius);
            }

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