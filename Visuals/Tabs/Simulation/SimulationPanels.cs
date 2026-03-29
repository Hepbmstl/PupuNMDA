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
    /// Properties panel controller for Simulation mode.
    /// Responsibilities: manage parameter cards for Stimulation/Probe devices in the left panel.
    /// Created by MainWindow.InitializeControllers and subscribes to SimulationInteractionController events.
    /// </summary>
    public class SimulationPanelController
    {
        /// <summary>Container for simulation property panels (left-side StackPanel).</summary>
        private readonly StackPanel _container;

        /// <summary>Reference to the simulation interaction controller, used to subscribe to device add/remove events.</summary>
        private readonly SimulationInteractionController _interaction;

        /// <summary>Mapping from device ID to its property card Expander.</summary>
        private readonly Dictionary<string, Expander> _uiNodes = new Dictionary<string, Expander>();

        /// <summary>Mapping from device ID to IAttachedDevice, used for navigation on panel click.</summary>
        private readonly Dictionary<string, IAttachedDevice> _deviceMap = new Dictionary<string, IAttachedDevice>();

        /// <summary>Camera navigation callback, injected by MainWindow.</summary>
        private readonly Action<Point3D>? _navigateToPoint;

        /// <summary>Flag indicating whether expand state is being set programmatically (prevents recursive selection).</summary>
        private bool _suppressExpandEvent;

        /// <summary>
        /// Constructor. Called by MainWindow.InitializeControllers; injects the UI container and subscribes to events.
        /// </summary>
        /// <param name="container">Simulation property panel StackPanel</param>
        /// <param name="interaction">Simulation interaction controller</param>
        /// <param name="navigateToPoint">Camera navigation callback (optional)</param>
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
        /// Handle device-added event: create a parameter card for the new device and add it to the panel.
        /// Invoked by SimulationInteractionController.OnDeviceAdded event.
        /// </summary>
        private void HandleDeviceAdded(IAttachedDevice device)
        {
            var expander = BuildDeviceNode(device);
            _uiNodes[device.Id] = expander;
            _deviceMap[device.Id] = device;
            _container.Children.Add(expander);
        }

        /// <summary>
        /// Handle device-removed event: remove the corresponding parameter card.
        /// Invoked by SimulationInteractionController.OnDeviceRemoved event.
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
        /// Handle device selection changed event: expand the corresponding card and highlight its border.
        /// Invoked by SimulationInteractionController.OnDeviceSelectionChanged event.
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
        /// Build a device parameter card Expander containing target entity information and type-specific input fields.
        /// StimulationDevice: Current (µA), StartTime, Duration; ProbeDevice: Threshold.
        /// Called by HandleDeviceAdded.
        /// </summary>
        /// <param name="device">Target device</param>
        /// <returns>Constructed Expander control</returns>
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
