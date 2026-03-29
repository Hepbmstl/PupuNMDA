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
using System.Windows.Media;
using NeuronCAD.Visuals.Tabs.Modeling.Visuals;

namespace NeuronCAD.Visuals.Windows
{
    /// <summary>
    /// Global static container for ion channel parameters, persisting HH and
    /// Ca T-type channel parameters for the application lifetime.
    /// Values are modified by IonChannelSettingWindow and pushed to Python by
    /// SimulationRunner before each simulation.
    /// </summary>
    public static class IonChannelParams
    {
        // ── HH Gating (Traub-modified, hh2.mod) ──
        public static double Vtraub = -63.0;
        public static double AlphaM_A = 0.32, AlphaM_V = 13.0, AlphaM_k = 4.0;
        public static double BetaM_A = 0.28, BetaM_V = 40.0, BetaM_k = 5.0;
        public static double AlphaH_A = 0.128, AlphaH_V = 17.0, AlphaH_k = 18.0;
        public static double BetaH_A = 4.0, BetaH_V = 40.0, BetaH_k = 5.0;
        public static double AlphaN_A = 0.032, AlphaN_V = 15.0, AlphaN_k = 5.0;
        public static double BetaN_A = 0.5, BetaN_V = 10.0, BetaN_k = 40.0;

        // ── Ca²⁺ T-type (ITGHK.mod + tcD_vc.oc overrides) ──
        public static double Shift = -1.0, ActShift = 0.0;
        public static double InfMT_Vh = 57.0, InfMT_k = 6.2;
        public static double InfHT_Vh = 81.0, InfHT_k = 4.0;
        public static double TauMT_base = 0.612, TauMT_V1 = 132.0, TauMT_k1 = 16.7;
        public static double TauMT_V2 = 16.8, TauMT_k2 = 18.2, TauMT_Q10 = 2.5, TauMT_Tref = 24.0;
        public static double TauHT_Vthresh = -80.0;
        public static double TauHT_V1 = 467.0, TauHT_k1 = 66.6;
        public static double TauHT_base = 28.0, TauHT_V2 = 22.0, TauHT_k2 = 10.5;
        public static double TauHT_Q10 = 2.5, TauHT_Tref = 24.0;

        /// <summary>Generate HH parameters JSON string for passing to Python set_hh_params.</summary>
        public static string GetHHParamsJson()
        {
            return string.Format(CultureInfo.InvariantCulture,
                "{{" +
                "\"vtraub\":{0}," +
                "\"alpha_m_A\":{1},\"alpha_m_V\":{2},\"alpha_m_k\":{3}," +
                "\"beta_m_A\":{4},\"beta_m_V\":{5},\"beta_m_k\":{6}," +
                "\"alpha_h_A\":{7},\"alpha_h_V\":{8},\"alpha_h_k\":{9}," +
                "\"beta_h_A\":{10},\"beta_h_V\":{11},\"beta_h_k\":{12}," +
                "\"alpha_n_A\":{13},\"alpha_n_V\":{14},\"alpha_n_k\":{15}," +
                "\"beta_n_A\":{16},\"beta_n_V\":{17},\"beta_n_k\":{18}" +
                "}}",
                Vtraub,
                AlphaM_A, AlphaM_V, AlphaM_k,
                BetaM_A, BetaM_V, BetaM_k,
                AlphaH_A, AlphaH_V, AlphaH_k,
                BetaH_A, BetaH_V, BetaH_k,
                AlphaN_A, AlphaN_V, AlphaN_k,
                BetaN_A, BetaN_V, BetaN_k);
        }

        /// <summary>Generate Ca T-type parameters JSON string for passing to Python set_ca_params.</summary>
        public static string GetCaParamsJson()
        {
            return string.Format(CultureInfo.InvariantCulture,
                "{{" +
                "\"shift\":{0},\"actshift\":{1}," +
                "\"inf_mT_Vh\":{2},\"inf_mT_k\":{3}," +
                "\"inf_hT_Vh\":{4},\"inf_hT_k\":{5}," +
                "\"tau_mT_base\":{6},\"tau_mT_V1\":{7},\"tau_mT_k1\":{8}," +
                "\"tau_mT_V2\":{9},\"tau_mT_k2\":{10},\"tau_mT_Q10\":{11},\"tau_mT_Tref\":{12}," +
                "\"tau_hT_Vthresh\":{13}," +
                "\"tau_hT_V1\":{14},\"tau_hT_k1\":{15}," +
                "\"tau_hT_base\":{16},\"tau_hT_V2\":{17},\"tau_hT_k2\":{18}," +
                "\"tau_hT_Q10\":{19},\"tau_hT_Tref\":{20}" +
                "}}",
                Shift, ActShift,
                InfMT_Vh, InfMT_k,
                InfHT_Vh, InfHT_k,
                TauMT_base, TauMT_V1, TauMT_k1,
                TauMT_V2, TauMT_k2, TauMT_Q10, TauMT_Tref,
                TauHT_Vthresh,
                TauHT_V1, TauHT_k1,
                TauHT_base, TauHT_V2, TauHT_k2,
                TauHT_Q10, TauHT_Tref);
        }

        /// <summary>Reset all parameters to their default values.</summary>
        public static void ResetToDefault()
        {
            Vtraub = -63.0;
            AlphaM_A = 0.32; AlphaM_V = 13.0; AlphaM_k = 4.0;
            BetaM_A = 0.28; BetaM_V = 40.0; BetaM_k = 5.0;
            AlphaH_A = 0.128; AlphaH_V = 17.0; AlphaH_k = 18.0;
            BetaH_A = 4.0; BetaH_V = 40.0; BetaH_k = 5.0;
            AlphaN_A = 0.032; AlphaN_V = 15.0; AlphaN_k = 5.0;
            BetaN_A = 0.5; BetaN_V = 10.0; BetaN_k = 40.0;

            Shift = -1.0; ActShift = 0.0;
            InfMT_Vh = 57.0; InfMT_k = 6.2;
            InfHT_Vh = 81.0; InfHT_k = 4.0;
            TauMT_base = 0.612; TauMT_V1 = 132.0; TauMT_k1 = 16.7;
            TauMT_V2 = 16.8; TauMT_k2 = 18.2; TauMT_Q10 = 2.5; TauMT_Tref = 24.0;
            TauHT_Vthresh = -80.0;
            TauHT_V1 = 467.0; TauHT_k1 = 66.6;
            TauHT_base = 28.0; TauHT_V2 = 22.0; TauHT_k2 = 10.5;
            TauHT_Q10 = 2.5; TauHT_Tref = 24.0;
        }
    }

    /// <summary>
    /// Ion channel parameter settings window, containing tabs for HH gating
    /// parameters and Ca²⁺ T-type channel parameters.
    /// Renders classical HH equations and provides editable Boltzmann constants.
    /// Opened from the MainWindow Edit menu's "Ion Channel Setting" item.
    /// </summary>
    public partial class IonChannelSettingWindow : Window
    {
        private readonly Dictionary<string, TextBox> _tb = new();
        private readonly List<(string origKey, TextBox nameTb, TextBox valTb, bool isPermeability)> _channelRows = new();

        private static readonly SolidColorBrush BgInput = new(Color.FromRgb(0x2A, 0x2A, 0x2A));
        private static readonly SolidColorBrush FgWhite = new(Colors.White);
        private static readonly SolidColorBrush FgFormula = new(Color.FromRgb(0x88, 0xCC, 0xFF));
        private static readonly SolidColorBrush FgSection = new(Color.FromRgb(0xFF, 0xD7, 0x00));
        private static readonly SolidColorBrush BorderInput = new(Color.FromRgb(0x55, 0x55, 0x55));

        public IonChannelSettingWindow()
        {
            InitializeComponent();
            BuildHHPanel();
            BuildCaPanel();
            BuildChannelsPanel();
        }

        #region Build HH Tab

        private void BuildHHPanel()
        {
            // ── Traub shift ──
            AddSection(HHPanel, "Traub 电压偏移 (hh2.mod: v2 = V − vtraub)");

            AddEquation(HHPanel,
                "v₂ = V − vtraub",
                ("vtraub", "vtraub", IonChannelParams.Vtraub));

            // ── Na⁺ m gate ──
            AddSection(HHPanel, "Na⁺ 激活 — m 门");

            AddEquation(HHPanel,
                "αm(V) = A · (V₀ − v₂) / (exp((V₀ − v₂) / k) − 1)",
                ("A", "alpha_m_A", IonChannelParams.AlphaM_A),
                ("V₀", "alpha_m_V", IonChannelParams.AlphaM_V),
                ("k", "alpha_m_k", IonChannelParams.AlphaM_k));

            AddEquation(HHPanel,
                "βm(V) = A · (v₂ − V₀) / (exp((v₂ − V₀) / k) − 1)",
                ("A", "beta_m_A", IonChannelParams.BetaM_A),
                ("V₀", "beta_m_V", IonChannelParams.BetaM_V),
                ("k", "beta_m_k", IonChannelParams.BetaM_k));

            // ── Na⁺ h gate ──
            AddSection(HHPanel, "Na⁺ 失活 — h 门");

            AddEquation(HHPanel,
                "αh(V) = A · exp((V₀ − v₂) / k)",
                ("A", "alpha_h_A", IonChannelParams.AlphaH_A),
                ("V₀", "alpha_h_V", IonChannelParams.AlphaH_V),
                ("k", "alpha_h_k", IonChannelParams.AlphaH_k));

            AddEquation(HHPanel,
                "βh(V) = A / (1 + exp((V₀ − v₂) / k))",
                ("A", "beta_h_A", IonChannelParams.BetaH_A),
                ("V₀", "beta_h_V", IonChannelParams.BetaH_V),
                ("k", "beta_h_k", IonChannelParams.BetaH_k));

            // ── K⁺ n gate ──
            AddSection(HHPanel, "K⁺ 激活 — n 门");

            AddEquation(HHPanel,
                "αn(V) = A · (V₀ − v₂) / (exp((V₀ − v₂) / k) − 1)",
                ("A", "alpha_n_A", IonChannelParams.AlphaN_A),
                ("V₀", "alpha_n_V", IonChannelParams.AlphaN_V),
                ("k", "alpha_n_k", IonChannelParams.AlphaN_k));

            AddEquation(HHPanel,
                "βn(V) = A · exp((V₀ − v₂) / k)",
                ("A", "beta_n_A", IonChannelParams.BetaN_A),
                ("V₀", "beta_n_V", IonChannelParams.BetaN_V),
                ("k", "beta_n_k", IonChannelParams.BetaN_k));
        }

        #endregion

        #region Build Ca Tab

        private void BuildCaPanel()
        {
            // ── Shift parameters ──
            AddSection(CaPanel, "T 型 Ca²⁺ 电压偏移 (ITGHK.mod)");

            AddEquation(CaPanel,
                "shift: global voltage shift applied to all Ca kinetics\n" +
                "actshift: additional shift for activation (m∞, τm) only",
                ("shift", "shift", IonChannelParams.Shift),
                ("actshift", "actshift", IonChannelParams.ActShift));

            // ── Steady-state ──
            AddSection(CaPanel, "T 型 Ca²⁺ 稳态激活/失活");

            AddEquation(CaPanel,
                "m\u221E(V) = 1 / (1 + exp(\u2212(V + V\u00BD) / k))",
                ("V\u00BD", "inf_mT_Vh", IonChannelParams.InfMT_Vh),
                ("k", "inf_mT_k", IonChannelParams.InfMT_k));

            AddEquation(CaPanel,
                "h\u221E(V) = 1 / (1 + exp((V + V\u00BD) / k))",
                ("V\u00BD", "inf_hT_Vh", IonChannelParams.InfHT_Vh),
                ("k", "inf_hT_k", IonChannelParams.InfHT_k));

            // ── Time constants ──
            AddSection(CaPanel, "T 型 Ca²⁺ 时间常数");

            AddEquation(CaPanel,
                "\u03C4m(V) = (base + 1 / (exp(\u2212(V+V\u2081)/k\u2081) + exp((V+V\u2082)/k\u2082))) / \u03C6\n" +
                "    \u03C6 = Q\u2081\u2080 ^ ((T \u2212 T_ref) / 10)",
                ("base", "tau_mT_base", IonChannelParams.TauMT_base),
                ("V\u2081", "tau_mT_V1", IonChannelParams.TauMT_V1),
                ("k\u2081", "tau_mT_k1", IonChannelParams.TauMT_k1),
                ("V\u2082", "tau_mT_V2", IonChannelParams.TauMT_V2),
                ("k\u2082", "tau_mT_k2", IonChannelParams.TauMT_k2),
                ("Q\u2081\u2080", "tau_mT_Q10", IonChannelParams.TauMT_Q10),
                ("T_ref", "tau_mT_Tref", IonChannelParams.TauMT_Tref));

            AddEquation(CaPanel,
                "\u03C4h(V):\n" +
                "  V < V_th : exp((V + V\u2081) / k\u2081) / \u03C6\n" +
                "  V \u2265 V_th : (base + exp(\u2212(V + V\u2082) / k\u2082)) / \u03C6\n" +
                "    \u03C6 = Q\u2081\u2080 ^ ((T \u2212 T_ref) / 10)",
                ("V_th", "tau_hT_Vthresh", IonChannelParams.TauHT_Vthresh),
                ("V\u2081", "tau_hT_V1", IonChannelParams.TauHT_V1),
                ("k\u2081", "tau_hT_k1", IonChannelParams.TauHT_k1),
                ("base", "tau_hT_base", IonChannelParams.TauHT_base),
                ("V\u2082", "tau_hT_V2", IonChannelParams.TauHT_V2),
                ("k\u2082", "tau_hT_k2", IonChannelParams.TauHT_k2),
                ("Q\u2081\u2080", "tau_hT_Q10", IonChannelParams.TauHT_Q10),
                ("T_ref", "tau_hT_Tref", IonChannelParams.TauHT_Tref));
        }

        #endregion

        #region Build Channels Tab

        private void BuildChannelsPanel()
        {
            ChannelsPanel.Children.Clear();
            _channelRows.Clear();

            AddSection(ChannelsPanel, "全局离子通道默认值");

            foreach (var kvp in GlobalBiophysics.GlobalChannels)
            {
                var ch = kvp.Value;
                var block = new StackPanel { Margin = new Thickness(0, 0, 0, 10) };
                var row = new WrapPanel { Margin = new Thickness(8, 0, 0, 0) };

                // Name
                row.Children.Add(new TextBlock
                {
                    Text = "名称：",
                    Foreground = new SolidColorBrush(Color.FromRgb(0xCC, 0xCC, 0xCC)),
                    VerticalAlignment = VerticalAlignment.Center,
                    FontSize = 12,
                    Margin = new Thickness(0, 0, 4, 0)
                });
                var tbName = new TextBox
                {
                    Text = ch.Name,
                    Background = BgInput,
                    Foreground = FgWhite,
                    BorderBrush = BorderInput,
                    Padding = new Thickness(4, 2, 4, 2),
                    MinWidth = 60,
                    FontSize = 12
                };
                row.Children.Add(tbName);

                // Value
                string unitLabel = ch.IsPermeability ? "P（cm/s）：" : "g（mS/cm\u00b2）：";
                row.Children.Add(new TextBlock
                {
                    Text = unitLabel,
                    Foreground = new SolidColorBrush(Color.FromRgb(0xCC, 0xCC, 0xCC)),
                    VerticalAlignment = VerticalAlignment.Center,
                    FontSize = 12,
                    Margin = new Thickness(16, 0, 4, 0)
                });
                var tbVal = new TextBox
                {
                    Text = ch.G_ion_channel.ToString(CultureInfo.InvariantCulture),
                    Background = BgInput,
                    Foreground = FgWhite,
                    BorderBrush = BorderInput,
                    Padding = new Thickness(4, 2, 4, 2),
                    MinWidth = 80,
                    FontSize = 12
                };
                row.Children.Add(tbVal);

                // Permeability note for CaT-type channels
                if (ch.IsPermeability)
                {
                    row.Children.Add(new TextBlock
                    {
                        Text = "  \u26a0 这是渗透率（GHK），非电导",
                        Foreground = new SolidColorBrush(Color.FromRgb(0xFF, 0xA5, 0x00)),
                        VerticalAlignment = VerticalAlignment.Center,
                        FontSize = 11,
                        FontStyle = FontStyles.Italic
                    });
                }

                block.Children.Add(row);
                ChannelsPanel.Children.Add(block);
                _channelRows.Add((kvp.Key, tbName, tbVal, ch.IsPermeability));
            }
        }

        private bool TryApplyChannels()
        {
            var newChannels = new Dictionary<string, ChannelProperty>();
            foreach (var (origKey, nameTb, valTb, isPerm) in _channelRows)
            {
                string newName = nameTb.Text.Trim();
                if (string.IsNullOrEmpty(newName))
                {
                    MessageBox.Show($"通道名称不能为空（原始键：'{origKey}'）。",
                        "验证错误", MessageBoxButton.OK, MessageBoxImage.Warning);
                    nameTb.Focus();
                    return false;
                }
                if (!float.TryParse(valTb.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out float val))
                {
                    MessageBox.Show($"通道 '{newName}' 的值无效：{valTb.Text}",
                        "验证错误", MessageBoxButton.OK, MessageBoxImage.Warning);
                    valTb.Focus();
                    return false;
                }
                if (newChannels.ContainsKey(newName))
                {
                    MessageBox.Show($"重复的通道名称：'{newName}'。",
                        "验证错误", MessageBoxButton.OK, MessageBoxImage.Warning);
                    nameTb.Focus();
                    return false;
                }

                var orig = GlobalBiophysics.GlobalChannels[origKey];
                orig.Name = newName;
                orig.G_ion_channel = val;
                newChannels[newName] = orig;
            }

            GlobalBiophysics.GlobalChannels.Clear();
            foreach (var kvp in newChannels)
                GlobalBiophysics.GlobalChannels[kvp.Key] = kvp.Value;

            return true;
        }

        #endregion

        #region UI Builders

        private void AddSection(StackPanel parent, string title)
        {
            parent.Children.Add(new TextBlock
            {
                Text = title,
                Foreground = FgSection,
                FontWeight = FontWeights.Bold,
                FontSize = 14,
                Margin = new Thickness(0, parent.Children.Count > 0 ? 18 : 0, 0, 6)
            });

            parent.Children.Add(new Border
            {
                Height = 1,
                Background = new SolidColorBrush(Color.FromRgb(0x3F, 0x3F, 0x46)),
                Margin = new Thickness(0, 0, 0, 10)
            });
        }

        private void AddEquation(StackPanel parent, string formula,
            params (string label, string key, double value)[] parameters)
        {
            var block = new StackPanel { Margin = new Thickness(0, 0, 0, 16) };

            // Formula display
            block.Children.Add(new TextBlock
            {
                Text = formula,
                Foreground = FgFormula,
                FontFamily = new FontFamily("Consolas"),
                FontSize = 13,
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(8, 0, 0, 6)
            });

            // Parameter row(s)
            var wrap = new WrapPanel { Margin = new Thickness(12, 0, 0, 0) };
            foreach (var (label, key, value) in parameters)
            {
                var sp = new StackPanel
                {
                    Orientation = Orientation.Horizontal,
                    Margin = new Thickness(0, 0, 16, 4)
                };

                sp.Children.Add(new TextBlock
                {
                    Text = label + " :",
                    Foreground = new SolidColorBrush(Color.FromRgb(0xCC, 0xCC, 0xCC)),
                    VerticalAlignment = VerticalAlignment.Center,
                    FontSize = 12,
                    Margin = new Thickness(0, 0, 4, 0),
                    MinWidth = 36
                });

                var tb = new TextBox
                {
                    Text = value.ToString(CultureInfo.InvariantCulture),
                    Background = BgInput,
                    Foreground = FgWhite,
                    BorderBrush = BorderInput,
                    Padding = new Thickness(4, 2, 4, 2),
                    MinWidth = 64,
                    FontSize = 12,
                    Tag = key
                };
                sp.Children.Add(tb);
                _tb[key] = tb;

                wrap.Children.Add(sp);
            }
            block.Children.Add(wrap);

            parent.Children.Add(block);
        }

        #endregion

        #region Button Handlers

        private void OnApplyClick(object sender, RoutedEventArgs e)
        {
            if (!TryReadAll()) return;
            if (!TryApplyChannels()) return;
            DialogResult = true;
            Close();
        }

        private void OnCancelClick(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }

        private void OnResetClick(object sender, RoutedEventArgs e)
        {
            IonChannelParams.ResetToDefault();
            GlobalBiophysics.ResetToDefaults();
            RefreshAllFields();
            BuildChannelsPanel();
        }

        #endregion

        #region Read / Write

        private bool TryReadAll()
        {
            foreach (var kv in _tb)
            {
                if (!double.TryParse(kv.Value.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out double val))
                {
                    MessageBox.Show($"参数 '{kv.Key}' 的值无效：{kv.Value.Text}",
                        "验证错误", MessageBoxButton.OK, MessageBoxImage.Warning);
                    kv.Value.Focus();
                    return false;
                }
                WriteParam(kv.Key, val);
            }
            return true;
        }

        private void WriteParam(string key, double val)
        {
            switch (key)
            {
                case "vtraub": IonChannelParams.Vtraub = val; break;
                case "alpha_m_A": IonChannelParams.AlphaM_A = val; break;
                case "alpha_m_V": IonChannelParams.AlphaM_V = val; break;
                case "alpha_m_k": IonChannelParams.AlphaM_k = val; break;
                case "beta_m_A": IonChannelParams.BetaM_A = val; break;
                case "beta_m_V": IonChannelParams.BetaM_V = val; break;
                case "beta_m_k": IonChannelParams.BetaM_k = val; break;
                case "alpha_h_A": IonChannelParams.AlphaH_A = val; break;
                case "alpha_h_V": IonChannelParams.AlphaH_V = val; break;
                case "alpha_h_k": IonChannelParams.AlphaH_k = val; break;
                case "beta_h_A": IonChannelParams.BetaH_A = val; break;
                case "beta_h_V": IonChannelParams.BetaH_V = val; break;
                case "beta_h_k": IonChannelParams.BetaH_k = val; break;
                case "alpha_n_A": IonChannelParams.AlphaN_A = val; break;
                case "alpha_n_V": IonChannelParams.AlphaN_V = val; break;
                case "alpha_n_k": IonChannelParams.AlphaN_k = val; break;
                case "beta_n_A": IonChannelParams.BetaN_A = val; break;
                case "beta_n_V": IonChannelParams.BetaN_V = val; break;
                case "beta_n_k": IonChannelParams.BetaN_k = val; break;
                case "shift": IonChannelParams.Shift = val; break;
                case "actshift": IonChannelParams.ActShift = val; break;
                case "inf_mT_Vh": IonChannelParams.InfMT_Vh = val; break;
                case "inf_mT_k": IonChannelParams.InfMT_k = val; break;
                case "inf_hT_Vh": IonChannelParams.InfHT_Vh = val; break;
                case "inf_hT_k": IonChannelParams.InfHT_k = val; break;
                case "tau_mT_base": IonChannelParams.TauMT_base = val; break;
                case "tau_mT_V1": IonChannelParams.TauMT_V1 = val; break;
                case "tau_mT_k1": IonChannelParams.TauMT_k1 = val; break;
                case "tau_mT_V2": IonChannelParams.TauMT_V2 = val; break;
                case "tau_mT_k2": IonChannelParams.TauMT_k2 = val; break;
                case "tau_mT_Q10": IonChannelParams.TauMT_Q10 = val; break;
                case "tau_mT_Tref": IonChannelParams.TauMT_Tref = val; break;
                case "tau_hT_Vthresh": IonChannelParams.TauHT_Vthresh = val; break;
                case "tau_hT_V1": IonChannelParams.TauHT_V1 = val; break;
                case "tau_hT_k1": IonChannelParams.TauHT_k1 = val; break;
                case "tau_hT_base": IonChannelParams.TauHT_base = val; break;
                case "tau_hT_V2": IonChannelParams.TauHT_V2 = val; break;
                case "tau_hT_k2": IonChannelParams.TauHT_k2 = val; break;
                case "tau_hT_Q10": IonChannelParams.TauHT_Q10 = val; break;
                case "tau_hT_Tref": IonChannelParams.TauHT_Tref = val; break;
            }
        }

        private double ReadParam(string key) => key switch
        {
            "vtraub" => IonChannelParams.Vtraub,
            "alpha_m_A" => IonChannelParams.AlphaM_A,
            "alpha_m_V" => IonChannelParams.AlphaM_V,
            "alpha_m_k" => IonChannelParams.AlphaM_k,
            "beta_m_A" => IonChannelParams.BetaM_A,
            "beta_m_V" => IonChannelParams.BetaM_V,
            "beta_m_k" => IonChannelParams.BetaM_k,
            "alpha_h_A" => IonChannelParams.AlphaH_A,
            "alpha_h_V" => IonChannelParams.AlphaH_V,
            "alpha_h_k" => IonChannelParams.AlphaH_k,
            "beta_h_A" => IonChannelParams.BetaH_A,
            "beta_h_V" => IonChannelParams.BetaH_V,
            "beta_h_k" => IonChannelParams.BetaH_k,
            "alpha_n_A" => IonChannelParams.AlphaN_A,
            "alpha_n_V" => IonChannelParams.AlphaN_V,
            "alpha_n_k" => IonChannelParams.AlphaN_k,
            "beta_n_A" => IonChannelParams.BetaN_A,
            "beta_n_V" => IonChannelParams.BetaN_V,
            "beta_n_k" => IonChannelParams.BetaN_k,
            "shift" => IonChannelParams.Shift,
            "actshift" => IonChannelParams.ActShift,
            "inf_mT_Vh" => IonChannelParams.InfMT_Vh,
            "inf_mT_k" => IonChannelParams.InfMT_k,
            "inf_hT_Vh" => IonChannelParams.InfHT_Vh,
            "inf_hT_k" => IonChannelParams.InfHT_k,
            "tau_mT_base" => IonChannelParams.TauMT_base,
            "tau_mT_V1" => IonChannelParams.TauMT_V1,
            "tau_mT_k1" => IonChannelParams.TauMT_k1,
            "tau_mT_V2" => IonChannelParams.TauMT_V2,
            "tau_mT_k2" => IonChannelParams.TauMT_k2,
            "tau_mT_Q10" => IonChannelParams.TauMT_Q10,
            "tau_mT_Tref" => IonChannelParams.TauMT_Tref,
            "tau_hT_Vthresh" => IonChannelParams.TauHT_Vthresh,
            "tau_hT_V1" => IonChannelParams.TauHT_V1,
            "tau_hT_k1" => IonChannelParams.TauHT_k1,
            "tau_hT_base" => IonChannelParams.TauHT_base,
            "tau_hT_V2" => IonChannelParams.TauHT_V2,
            "tau_hT_k2" => IonChannelParams.TauHT_k2,
            "tau_hT_Q10" => IonChannelParams.TauHT_Q10,
            "tau_hT_Tref" => IonChannelParams.TauHT_Tref,
            _ => 0.0
        };

        private void RefreshAllFields()
        {
            foreach (var kv in _tb)
                kv.Value.Text = ReadParam(kv.Key).ToString(CultureInfo.InvariantCulture);
        }

        #endregion
    }
}
