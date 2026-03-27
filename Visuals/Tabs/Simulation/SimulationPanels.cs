using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Media3D;
using NeuronCAD.Visuals.Tabs.Modeling.Visuals;

namespace NeuronCAD.Visuals.Tabs.Simulation
{
    /// <summary>
    /// 仿真模式下的属性面板控制器。
    /// 职责：管理左侧面板中 Stimulation/Probe 设备的参数配置卡片。
    /// 由 MainWindow.InitializeControllers 创建，订阅 SimulationInteractionController 的事件总线。
    /// </summary>
    public class SimulationPanelController
    {
        /// <summary>仿真属性面板容器（左侧栏 StackPanel）。</summary>
        private readonly StackPanel _container;

        /// <summary>仿真交互控制器引用，用于订阅设备增删事件。</summary>
        private readonly SimulationInteractionController _interaction;

        /// <summary>设备 ID 到属性卡片 Expander 的映射字典。</summary>
        private readonly Dictionary<string, Expander> _uiNodes = new Dictionary<string, Expander>();

        /// <summary>设备 ID 到 IAttachedDevice 的映射字典，用于面板点击时导航。</summary>
        private readonly Dictionary<string, IAttachedDevice> _deviceMap = new Dictionary<string, IAttachedDevice>();

        /// <summary>相机跳转回调，由 MainWindow 注入。</summary>
        private readonly Action<Point3D>? _navigateToPoint;

        /// <summary>标记是否正在通过代码设置展开状态（防止递归触发选中）。</summary>
        private bool _suppressExpandEvent;

        /// <summary>
        /// 构造函数。由 MainWindow.InitializeControllers 调用，注入 UI 容器并订阅事件。
        /// </summary>
        /// <param name="container">仿真属性面板 StackPanel</param>
        /// <param name="interaction">仿真交互控制器</param>
        /// <param name="navigateToPoint">相机导航回调（可选）</param>
        public SimulationPanelController(StackPanel container, SimulationInteractionController interaction, Action<Point3D>? navigateToPoint = null)
        {
            _container = container;
            _interaction = interaction;
            _navigateToPoint = navigateToPoint;

            _interaction.OnDeviceAdded += HandleDeviceAdded;
            _interaction.OnDeviceRemoved += HandleDeviceRemoved;
            _interaction.OnDeviceSelectionChanged += HandleDeviceSelectionChanged;
        }

        /// <summary>
        /// 处理设备添加事件：为新设备创建参数卡片并添加到面板。
        /// 被 SimulationInteractionController.OnDeviceAdded 事件触发调用。
        /// </summary>
        private void HandleDeviceAdded(IAttachedDevice device)
        {
            var expander = BuildDeviceNode(device);
            _uiNodes[device.Id] = expander;
            _deviceMap[device.Id] = device;
            _container.Children.Add(expander);
        }

        /// <summary>
        /// 处理设备删除事件：移除对应的参数卡片。
        /// 被 SimulationInteractionController.OnDeviceRemoved 事件触发调用。
        /// </summary>
        private void HandleDeviceRemoved(IAttachedDevice device)
        {
            if (_uiNodes.TryGetValue(device.Id, out var expander))
            {
                _container.Children.Remove(expander);
                _uiNodes.Remove(device.Id);
                _deviceMap.Remove(device.Id);
            }
        }

        /// <summary>
        /// 处理设备选中变更事件：展开对应卡片并高亮边框。
        /// 被 SimulationInteractionController.OnDeviceSelectionChanged 事件触发调用。
        /// </summary>
        private void HandleDeviceSelectionChanged(IAttachedDevice? selectedDevice)
        {
            _suppressExpandEvent = true;
            try
            {
                foreach (var kvp in _uiNodes)
                {
                    var devId = kvp.Key;
                    var expander = kvp.Value;

                    if (selectedDevice != null && devId == selectedDevice.Id)
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
        /// 构建设备参数卡片 Expander，包含目标实体信息和类型特定参数输入框。
        /// StimulationDevice：Voltage、StartTime、Duration；ProbeDevice：Threshold。
        /// 由 HandleDeviceAdded 调用。
        /// </summary>
        /// <param name="device">目标设备</param>
        /// <returns>构建完成的 Expander 控件</returns>
        private Expander BuildDeviceNode(IAttachedDevice device)
        {
            string devType;
            switch (device.Type)
            {
                case DeviceType.Stimulation: devType = "CurrentClamp"; break;
                case DeviceType.VoltageClamp: devType = "VoltageClamp"; break;
                default: devType = "Probe"; break;
            }

            var expander = new Expander
            {
                Header = $"{devType} [{device.Id.Substring(0, 4)}]",
                Foreground = Brushes.LightGray,
                Margin = new Thickness(0, 0, 0, 5),
                IsExpanded = true
            };

            expander.Expanded += (s, e) =>
            {
                if (_suppressExpandEvent) return;
                _interaction.SelectDevice(device);
                _navigateToPoint?.Invoke(device.TargetEntity.CenterPosition);
            };

            var panel = new StackPanel { Margin = new Thickness(10, 5, 0, 5) };

            panel.Children.Add(new TextBlock
            {
                Text = $"Target: {device.TargetEntity.Id.Substring(0, 4)}",
                Foreground = Brushes.DarkGray,
                Margin = new Thickness(0, 0, 0, 5)
            });

            if (device is StimulationDevice stim)
            {
                // Stimulation_uA
                panel.Children.Add(new TextBlock { Text = "Current (µA):", Foreground = Brushes.Gray, Margin = new Thickness(0, 5, 0, 0) });
                var tbCurrent = new TextBox { Text = stim.Stimulation_uA.ToString("F4"), Background = Brushes.DarkGray, Foreground = Brushes.White };
                tbCurrent.LostFocus += (s, e) =>
                {
                    if (double.TryParse(tbCurrent.Text, out double v)) stim.Stimulation_uA = v;
                    else tbCurrent.Text = stim.Stimulation_uA.ToString("F4");
                };
                panel.Children.Add(tbCurrent);

                // StimStart
                panel.Children.Add(new TextBlock { Text = "Start Time (ms):", Foreground = Brushes.Gray, Margin = new Thickness(0, 5, 0, 0) });
                var tbStart = new TextBox { Text = stim.StimStart.ToString("F2"), Background = Brushes.DarkGray, Foreground = Brushes.White };
                tbStart.LostFocus += (s, e) =>
                {
                    if (double.TryParse(tbStart.Text, out double v)) stim.StimStart = v;
                    else tbStart.Text = stim.StimStart.ToString("F2");
                };
                panel.Children.Add(tbStart);

                // StimDuration
                panel.Children.Add(new TextBlock { Text = "Duration (ms):", Foreground = Brushes.Gray, Margin = new Thickness(0, 5, 0, 0) });
                var tbDuration = new TextBox { Text = stim.StimDuration.ToString("F2"), Background = Brushes.DarkGray, Foreground = Brushes.White };
                tbDuration.LostFocus += (s, e) =>
                {
                    if (double.TryParse(tbDuration.Text, out double v)) stim.StimDuration = v;
                    else tbDuration.Text = stim.StimDuration.ToString("F2");
                };
                panel.Children.Add(tbDuration);
            }
            else if (device is ProbeDevice probe)
            {
                // Start time (ms)
                panel.Children.Add(new TextBlock { Text = "Start Time (ms):", Foreground = Brushes.Gray, Margin = new Thickness(0, 5, 0, 0) });
                var tbStart = new TextBox { Text = probe.StartMs.ToString("F2"), Background = Brushes.DarkGray, Foreground = Brushes.White };
                tbStart.LostFocus += (s, e) =>
                {
                    if (double.TryParse(tbStart.Text, out double v) && v >= 0) probe.StartMs = v;
                    else tbStart.Text = probe.StartMs.ToString("F2");
                };
                panel.Children.Add(tbStart);

                // Duration (ms)
                panel.Children.Add(new TextBlock { Text = "Duration (ms):", Foreground = Brushes.Gray, Margin = new Thickness(0, 5, 0, 0) });
                var tbDur = new TextBox { Text = probe.DurationMs.ToString("F2"), Background = Brushes.DarkGray, Foreground = Brushes.White };
                tbDur.LostFocus += (s, e) =>
                {
                    if (double.TryParse(tbDur.Text, out double v) && v >= 0) probe.DurationMs = v;
                    else tbDur.Text = probe.DurationMs.ToString("F2");
                };
                panel.Children.Add(tbDur);
            }
            else if (device is VoltageClampDevice vc)
            {
                // Rs (MΩ)
                panel.Children.Add(new TextBlock { Text = "Rs (MΩ):", Foreground = Brushes.Gray, Margin = new Thickness(0, 5, 0, 0) });
                var tbRs = new TextBox { Text = vc.Rs.ToString("F2"), Background = Brushes.DarkGray, Foreground = Brushes.White };
                tbRs.LostFocus += (s, e) =>
                {
                    if (double.TryParse(tbRs.Text, out double v) && v > 0) vc.Rs = v;
                    else tbRs.Text = vc.Rs.ToString("F2");
                };
                panel.Children.Add(tbRs);

                // Protocol steps
                panel.Children.Add(new TextBlock { Text = "Protocol Steps:", Foreground = Brushes.Gray, Margin = new Thickness(0, 10, 0, 0) });
                for (int si = 0; si < vc.Protocol.Count; si++)
                {
                    var step = vc.Protocol[si];
                    var stepPanel = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 2, 0, 0) };
                    stepPanel.Children.Add(new TextBlock { Text = $"Step {si + 1}: ", Foreground = Brushes.Gray, VerticalAlignment = System.Windows.VerticalAlignment.Center, Width = 50 });

                    var tbAmp = new TextBox { Text = step.Amplitude.ToString("F1"), Background = Brushes.DarkGray, Foreground = Brushes.White, Width = 60 };
                    stepPanel.Children.Add(new TextBlock { Text = "mV ", Foreground = Brushes.Gray, VerticalAlignment = System.Windows.VerticalAlignment.Center });
                    int capturedIdx = si;
                    tbAmp.LostFocus += (s, e) =>
                    {
                        if (double.TryParse(tbAmp.Text, out double v)) vc.Protocol[capturedIdx].Amplitude = v;
                        else tbAmp.Text = vc.Protocol[capturedIdx].Amplitude.ToString("F1");
                    };
                    stepPanel.Children.Add(tbAmp);

                    stepPanel.Children.Add(new TextBlock { Text = " ", Foreground = Brushes.Gray, VerticalAlignment = System.Windows.VerticalAlignment.Center });

                    var tbDur = new TextBox { Text = step.Duration.ToString("F1"), Background = Brushes.DarkGray, Foreground = Brushes.White, Width = 60 };
                    tbDur.LostFocus += (s, e) =>
                    {
                        if (double.TryParse(tbDur.Text, out double v) && v > 0) vc.Protocol[capturedIdx].Duration = v;
                        else tbDur.Text = vc.Protocol[capturedIdx].Duration.ToString("F1");
                    };
                    stepPanel.Children.Add(tbDur);
                    stepPanel.Children.Add(new TextBlock { Text = "ms", Foreground = Brushes.Gray, VerticalAlignment = System.Windows.VerticalAlignment.Center });

                    panel.Children.Add(stepPanel);
                }
            }

            expander.Content = panel;
            return expander;
        }
    }
}
