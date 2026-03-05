using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using NeuronCAD.Visuals.Tabs.Modeling.Visuals;

namespace NeuronCAD.Visuals.Tabs.Simulation
{
    /// <summary>
    /// 仿真模式下的属性面板控制器
    /// 职责：管理左侧面板中 Stimulation/Probe 设备的参数配置卡片
    /// </summary>
    public class SimulationPanelController
    {
        private readonly StackPanel _container;
        private readonly SimulationInteractionController _interaction;

        private readonly Dictionary<string, Expander> _uiNodes = new Dictionary<string, Expander>();

        public SimulationPanelController(StackPanel container, SimulationInteractionController interaction)
        {
            _container = container;
            _interaction = interaction;

            _interaction.OnDeviceAdded += HandleDeviceAdded;
            _interaction.OnDeviceRemoved += HandleDeviceRemoved;
        }

        private void HandleDeviceAdded(IAttachedDevice device)
        {
            var expander = BuildDeviceNode(device);
            _uiNodes[device.Id] = expander;
            _container.Children.Add(expander);
        }

        private void HandleDeviceRemoved(IAttachedDevice device)
        {
            if (_uiNodes.TryGetValue(device.Id, out var expander))
            {
                _container.Children.Remove(expander);
                _uiNodes.Remove(device.Id);
            }
        }

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
