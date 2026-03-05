using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
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

        /// <summary>
        /// 构造函数。由 MainWindow.InitializeControllers 调用，注入 UI 容器并订阅事件。
        /// </summary>
        /// <param name="container">仿真属性面板 StackPanel</param>
        /// <param name="interaction">仿真交互控制器</param>
        public SimulationPanelController(StackPanel container, SimulationInteractionController interaction)
        {
            _container = container;
            _interaction = interaction;

            _interaction.OnDeviceAdded += HandleDeviceAdded;
            _interaction.OnDeviceRemoved += HandleDeviceRemoved;
        }

        /// <summary>
        /// 处理设备添加事件：为新设备创建参数卡片并添加到面板。
        /// 被 SimulationInteractionController.OnDeviceAdded 事件触发调用。
        /// </summary>
        private void HandleDeviceAdded(IAttachedDevice device)
        {
            var expander = BuildDeviceNode(device);
            _uiNodes[device.Id] = expander;
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
            string devType = device.Type == DeviceType.Stimulation ? "Stimulation" : "Probe";

            var expander = new Expander
            {
                Header = $"{devType} [{device.Id.Substring(0, 4)}]",
                Foreground = Brushes.LightGray,
                Margin = new Thickness(0, 0, 0, 5),
                IsExpanded = true
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
                // Voltage
                panel.Children.Add(new TextBlock { Text = "Voltage (mV):", Foreground = Brushes.Gray, Margin = new Thickness(0, 5, 0, 0) });
                var tbVoltage = new TextBox { Text = stim.Voltage.ToString("F2"), Background = Brushes.DarkGray, Foreground = Brushes.White };
                tbVoltage.LostFocus += (s, e) =>
                {
                    if (double.TryParse(tbVoltage.Text, out double v)) stim.Voltage = v;
                    else tbVoltage.Text = stim.Voltage.ToString("F2");
                };
                panel.Children.Add(tbVoltage);

                // StartTime
                panel.Children.Add(new TextBlock { Text = "Start Time (ms):", Foreground = Brushes.Gray, Margin = new Thickness(0, 5, 0, 0) });
                var tbStart = new TextBox { Text = stim.StartTime.ToString("F2"), Background = Brushes.DarkGray, Foreground = Brushes.White };
                tbStart.LostFocus += (s, e) =>
                {
                    if (double.TryParse(tbStart.Text, out double v)) stim.StartTime = v;
                    else tbStart.Text = stim.StartTime.ToString("F2");
                };
                panel.Children.Add(tbStart);

                // Duration
                panel.Children.Add(new TextBlock { Text = "Duration (ms):", Foreground = Brushes.Gray, Margin = new Thickness(0, 5, 0, 0) });
                var tbDuration = new TextBox { Text = stim.Duration.ToString("F2"), Background = Brushes.DarkGray, Foreground = Brushes.White };
                tbDuration.LostFocus += (s, e) =>
                {
                    if (double.TryParse(tbDuration.Text, out double v)) stim.Duration = v;
                    else tbDuration.Text = stim.Duration.ToString("F2");
                };
                panel.Children.Add(tbDuration);
            }
            else if (device is ProbeDevice probe)
            {
                // Threshold
                panel.Children.Add(new TextBlock { Text = "Threshold (mV):", Foreground = Brushes.Gray, Margin = new Thickness(0, 5, 0, 0) });
                var tbThreshold = new TextBox { Text = probe.Threshold.ToString("F2"), Background = Brushes.DarkGray, Foreground = Brushes.White };
                tbThreshold.LostFocus += (s, e) =>
                {
                    if (double.TryParse(tbThreshold.Text, out double v)) probe.Threshold = v;
                    else tbThreshold.Text = probe.Threshold.ToString("F2");
                };
                panel.Children.Add(tbThreshold);
            }

            expander.Content = panel;
            return expander;
        }
    }
}
