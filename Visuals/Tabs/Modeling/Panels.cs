/*
 * Copyright 2026 [Hepbmstl Hepupu]
 *
 * Pupu NMDA / NeuronCAD
 * A Multi-Compartment Neuron Physiological Simulation and Dynamics Analysis Platform
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

using System;
using System.Collections.Generic;
using System.Globalization;
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
    /// Properties panel controller for Modeling mode.
    /// Responsibilities: manage creation/removal/highlight of entity cards in the left properties panel, and the ion channel selector popup.
    /// Created by MainWindow.InitializeControllers and subscribes to the InteractionController event bus.
    /// </summary>
    public class PropertiesPanelController
    {
        /// <summary>Properties panel container (left sidebar StackPanel), injected via constructor.</summary>
        private readonly StackPanel _container;

        /// <summary>Reference to the modeling interaction controller, used to subscribe to entity add/remove/selection events and call ForceSelect.</summary>
        private readonly InteractionController _interaction;

        /// <summary>Reference to the ion channel selector popup (defined in MainWindow.xaml).</summary>
        private readonly Popup _channelPopup;

        /// <summary>Container for the list of buttons inside the ion channel popup.</summary>
        private readonly StackPanel _channelSelectorList;

        /// <summary>Mapping from entity ID to property card Expander for quick UI node lookup and removal.</summary>
        private readonly Dictionary<string, Expander> _uiNodes = new Dictionary<string, Expander>();

        /// <summary>The entity currently targeted for adding a channel, set by btnAddChannel.Click.</summary>
        private IVisualEntity _currentOperatingEntity;

        /// <summary>Camera navigation callback, injected by MainWindow.</summary>
        private readonly Action<Point3D>? _navigateToPoint;

        /// <summary>Flag indicating whether expansion state is being set programmatically (prevents recursive selection triggers).</summary>
        private bool _suppressExpandEvent;

        /// <summary>
        /// Constructor. Called by MainWindow.InitializeControllers; injects UI container and interaction controller, and subscribes to events.
        /// </summary>
        /// <param name="container">Left properties panel StackPanel</param>
        /// <param name="popup">Ion channel selector popup</param>
        /// <param name="popupList">Container for buttons inside the popup</param>
        /// <param name="interaction">Modeling interaction controller (used to subscribe to events)</param>
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
        /// Initialize the ion channel selector: create a button for each channel in GlobalBiophysics.GlobalChannels.
        /// Clicking a button adds the channel to the current operating entity and refreshes visuals.
        /// Called by the constructor.
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
                            var clone = ch.Clone();
                            _currentOperatingEntity.Channels.Add(clone.Name, clone);
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
        /// Handle entity added event: create a property card for the new entity and add it to the panel.
        /// Triggered by InteractionController.OnEntityAdded.
        /// </summary>
        private void HandleEntityAdded(IVisualEntity entity)
        {
            var expander = BuildEntityNode(entity);
            _uiNodes[entity.Id] = expander;
            _container.Children.Add(expander);
        }

        /// <summary>
        /// Handle entity removed event: remove the corresponding property card.
        /// Triggered by InteractionController.OnEntityRemoved.
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
        /// Handle selection change events: expand the corresponding card and highlight the border orange; collapse others.
        /// Triggered by InteractionController.OnSelectionChanged.
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
        /// Build the entity property card Expander, including color and size input fields and the ion channel list.
        /// Called by HandleEntityAdded. On expand, calls InteractionController.ForceSelect to synchronize selection.
        /// </summary>
        /// <param name="entity">The target entity</param>
        /// <returns>The constructed Expander control</returns>
        private Expander BuildEntityNode(IVisualEntity entity)
        {
            string entityType = "Unknown";
            if (entity is AxonVisual axon)
            {
                entityType = axon.VisualType;
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

            panel.Children.Add(new TextBlock { Text = "颜色 (Hex)：", Foreground = Brushes.Gray });
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
                panel.Children.Add(new TextBlock { Text = "基底半径 (µm)：", Foreground = Brushes.Gray, Margin = new Thickness(0, 5, 0, 0) });
                var tbBaseRadius = new TextBox { Text = axonEntity.BaseRadius.ToString("F2"), Background = Brushes.DarkGray, Foreground = Brushes.White };
                tbBaseRadius.LostFocus += (s, e) => { if (double.TryParse(tbBaseRadius.Text, out double v)) axonEntity.BaseRadius = v; };
                panel.Children.Add(tbBaseRadius);

                panel.Children.Add(new TextBlock { Text = "顶端半径 (µm)：", Foreground = Brushes.Gray, Margin = new Thickness(0, 5, 0, 0) });
                var tbTopRadius = new TextBox { Text = axonEntity.TopRadius.ToString("F2"), Background = Brushes.DarkGray, Foreground = Brushes.White };
                tbTopRadius.LostFocus += (s, e) => { if (double.TryParse(tbTopRadius.Text, out double v)) axonEntity.TopRadius = v; };
                panel.Children.Add(tbTopRadius);

                panel.Children.Add(new TextBlock { Text = "长度 (µm)：", Foreground = Brushes.Gray, Margin = new Thickness(0, 5, 0, 0) });
                var tbLength = new TextBox { Text = axonEntity.Length.ToString("F2"), Background = Brushes.DarkGray, Foreground = Brushes.White };
                tbLength.LostFocus += (s, e) => { if (double.TryParse(tbLength.Text, out double v)) axonEntity.Length = v; };
                panel.Children.Add(tbLength);
            }

            // Cm (µF/cm²)
            panel.Children.Add(new TextBlock { Text = "膜电容 (Cm, µF/cm²)：", Foreground = Brushes.Gray, Margin = new Thickness(0, 5, 0, 0) });
            var tbCm = new TextBox { Text = entity.Cm.ToString("F2"), Background = Brushes.DarkGray, Foreground = Brushes.White };
            tbCm.LostFocus += (s, e) => { if (double.TryParse(tbCm.Text, out double v) && v > 0) entity.Cm = v; else tbCm.Text = entity.Cm.ToString("F2"); };
            panel.Children.Add(tbCm);

            // Ra (Ω·cm)
            panel.Children.Add(new TextBlock { Text = "电阻 (Ra, Ω·cm)：", Foreground = Brushes.Gray, Margin = new Thickness(0, 5, 0, 0) });
            var tbRa = new TextBox { Text = entity.Ra.ToString("F2"), Background = Brushes.DarkGray, Foreground = Brushes.White };
            tbRa.LostFocus += (s, e) => { if (double.TryParse(tbRa.Text, out double v) && v > 0) entity.Ra = v; else tbRa.Text = entity.Ra.ToString("F2"); };
            panel.Children.Add(tbRa);

            var channelHeader = new TextBlock { Text = "离子通道：", Foreground = Brushes.Gray, Margin = new Thickness(0, 10, 0, 0) };
            panel.Children.Add(channelHeader);

            var channelListPanel = new StackPanel();
            panel.Children.Add(channelListPanel);

            var btnAddChannel = new Button { Content = "+ 添加通道", Margin = new Thickness(0, 5, 0, 0), Background = Brushes.DarkSlateGray, Foreground = Brushes.White };
            btnAddChannel.Click += (s, e) =>
            {
                _currentOperatingEntity = entity;
                InitializeChannelSelector();
                _channelPopup.IsOpen = true;
            };
            panel.Children.Add(btnAddChannel);

            expander.Content = panel;

            expander.Tag = channelListPanel;
            RefreshChannelList(entity, channelListPanel);

            return expander;
        }

        /// <summary>
        /// Refresh the ion channel list in the entity card, showing currently added channel names and delete buttons.
        /// Called by BuildEntityNode (initial construction) and after channel add/remove operations.
        /// </summary>
        /// <param name="entity">The target entity</param>
        /// <param name="listPanel">Channel list container (optional, if null, looks up from _uiNodes)</param>
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
                var chProp = kvp.Value;
                var row = new Grid { Margin = new Thickness(0, 2, 0, 2) };
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

                string unitSuffix = chProp.IsPermeability ? " cm/s" : " mS/cm\u00b2";
                var txt = new TextBlock
                {
                    Text = $"{chName} ({chProp.G_ion_channel.ToString(CultureInfo.InvariantCulture)}{unitSuffix})",
                    Foreground = Brushes.LightGray,
                    VerticalAlignment = VerticalAlignment.Center
                };
                Grid.SetColumn(txt, 0);

                var capturedName = chName;
                var btnGear = new Button
                {
                    Content = "\u2699",
                    Width = 20,
                    Background = new SolidColorBrush(Color.FromRgb(0x00, 0x7A, 0xCC)),
                    Foreground = Brushes.White,
                    Margin = new Thickness(2, 0, 2, 0),
                    ToolTip = "编辑该实体的通道数值"
                };
                btnGear.Click += (s, e) =>
                {
                    ShowChannelEditPopup(entity, capturedName, listPanel);
                };
                Grid.SetColumn(btnGear, 1);

                var btnDel = new Button { Content = "-", Width = 20, Background = Brushes.Maroon, Foreground = Brushes.White };
                btnDel.Click += (s, e) =>
                {
                    entity.Channels.Remove(chName);
                    entity.UpdateChannelVisuals();
                    RefreshChannelList(entity, listPanel);
                };
                Grid.SetColumn(btnDel, 2);

                row.Children.Add(txt);
                row.Children.Add(btnGear);
                row.Children.Add(btnDel);
                listPanel.Children.Add(row);
            }
        }

        private void ShowChannelEditPopup(IVisualEntity entity, string channelName, StackPanel listPanel)
        {
            if (!entity.Channels.TryGetValue(channelName, out var ch)) return;

            var win = new Window
            {
                Title = $"编辑通道： {channelName}",
                Width = 340,
                Height = 180,
                WindowStartupLocation = WindowStartupLocation.CenterScreen,
                Background = new SolidColorBrush(Color.FromRgb(0x1E, 0x1E, 0x1E)),
                ResizeMode = ResizeMode.NoResize
            };

            var panel = new StackPanel { Margin = new Thickness(15) };

            string unitText = ch.IsPermeability
                ? "渗透率 P (cm/s) — GHK，非电导率"
                : "电导 g (mS/cm\u00b2)";
            panel.Children.Add(new TextBlock
            {
                Text = unitText,
                Foreground = Brushes.LightGray,
                FontSize = 12,
                Margin = new Thickness(0, 0, 0, 8)
            });

            var tbVal = new TextBox
            {
                Text = ch.G_ion_channel.ToString(CultureInfo.InvariantCulture),
                Background = new SolidColorBrush(Color.FromRgb(0x2A, 0x2A, 0x2A)),
                Foreground = Brushes.White,
                BorderBrush = new SolidColorBrush(Color.FromRgb(0x55, 0x55, 0x55)),
                Padding = new Thickness(6, 4, 6, 4),
                FontSize = 14
            };
            panel.Children.Add(tbVal);

            var btnPanel = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Right,
                Margin = new Thickness(0, 12, 0, 0)
            };

            var btnOK = new Button
            {
                Content = "确定",
                Width = 60,
                Padding = new Thickness(0, 4, 0, 4),
                Margin = new Thickness(0, 0, 8, 0),
                Background = new SolidColorBrush(Color.FromRgb(0x00, 0x7A, 0xCC)),
                Foreground = Brushes.White,
                BorderThickness = new Thickness(0),
                Cursor = Cursors.Hand
            };
            btnOK.Click += (s2, e2) =>
            {
                if (float.TryParse(tbVal.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out float val))
                {
                    ch.G_ion_channel = val;
                    RefreshChannelList(entity, listPanel);
                    win.DialogResult = true;
                    win.Close();
                }
                else
                {
                    MessageBox.Show("数值无效。", "错误", MessageBoxButton.OK, MessageBoxImage.Warning);
                }
            };

            var btnCancel = new Button
            {
                Content = "取消",
                Width = 60,
                Padding = new Thickness(0, 4, 0, 4),
                Background = new SolidColorBrush(Color.FromRgb(0x44, 0x44, 0x44)),
                Foreground = Brushes.White,
                BorderThickness = new Thickness(0),
                Cursor = Cursors.Hand
            };
            btnCancel.Click += (s2, e2) => { win.DialogResult = false; win.Close(); };

            btnPanel.Children.Add(btnOK);
            btnPanel.Children.Add(btnCancel);
            panel.Children.Add(btnPanel);

            win.Content = panel;
            win.ShowDialog();
        }
    }
}