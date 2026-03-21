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
    /// 全局离子通道参数静态容器，在应用生命周期内持久化 HH / Ca T-type 通道参数。
    /// 值由 IonChannelSettingWindow 修改，由 SimulationRunner 在每次仿真前推送到 Python。
    /// </summary>
    public static class IonChannelParams
    {
        // ── HH Gating ──
        public static double AlphaM_A = 0.1, AlphaM_Vs = 35.0, AlphaM_k = 10.0;
        public static double BetaM_A = 4.0, BetaM_Vs = 60.0, BetaM_k = 18.0;
        public static double AlphaH_A = 0.07, AlphaH_Vs = 60.0, AlphaH_k = 20.0;
        public static double BetaH_A = 1.0, BetaH_Vs = 30.0, BetaH_k = 10.0;
        public static double AlphaN_A = 0.01, AlphaN_Vs = 50.0, AlphaN_k = 10.0;
        public static double BetaN_A = 0.125, BetaN_Vs = 60.0, BetaN_k = 80.0;

        // ── Ca²⁺ T-type ──
        public static double InfMT_Vh = 56.0, InfMT_k = 6.2;
        public static double InfHT_Vh = 80.0, InfHT_k = 4.0;
        public static double TauMT_base = 0.612, TauMT_V1 = 132.0, TauMT_k1 = 16.7;
        public static double TauMT_V2 = 16.8, TauMT_k2 = 18.2, TauMT_Q10 = 5.0, TauMT_Tref = 24.0;
        public static double TauHT_Vthresh = -80.0;
        public static double TauHT_V1 = 467.0, TauHT_k1 = 66.6;
        public static double TauHT_base = 28.0, TauHT_V2 = 22.0, TauHT_k2 = 10.5;
        public static double TauHT_Q10 = 3.0, TauHT_Tref = 24.0;

        /// <summary>生成 HH 参数 JSON 字符串，用于传递给 Python set_hh_params。</summary>
        public static string GetHHParamsJson()
        {
            return string.Format(CultureInfo.InvariantCulture,
                "{{" +
                "\"alpha_m_A\":{0},\"alpha_m_Vs\":{1},\"alpha_m_k\":{2}," +
                "\"beta_m_A\":{3},\"beta_m_Vs\":{4},\"beta_m_k\":{5}," +
                "\"alpha_h_A\":{6},\"alpha_h_Vs\":{7},\"alpha_h_k\":{8}," +
                "\"beta_h_A\":{9},\"beta_h_Vs\":{10},\"beta_h_k\":{11}," +
                "\"alpha_n_A\":{12},\"alpha_n_Vs\":{13},\"alpha_n_k\":{14}," +
                "\"beta_n_A\":{15},\"beta_n_Vs\":{16},\"beta_n_k\":{17}" +
                "}}",
                AlphaM_A, AlphaM_Vs, AlphaM_k,
                BetaM_A, BetaM_Vs, BetaM_k,
                AlphaH_A, AlphaH_Vs, AlphaH_k,
                BetaH_A, BetaH_Vs, BetaH_k,
                AlphaN_A, AlphaN_Vs, AlphaN_k,
                BetaN_A, BetaN_Vs, BetaN_k);
        }

        /// <summary>生成 Ca T-type 参数 JSON 字符串，用于传递给 Python set_ca_params。</summary>
        public static string GetCaParamsJson()
        {
            return string.Format(CultureInfo.InvariantCulture,
                "{{" +
                "\"inf_mT_Vh\":{0},\"inf_mT_k\":{1}," +
                "\"inf_hT_Vh\":{2},\"inf_hT_k\":{3}," +
                "\"tau_mT_base\":{4},\"tau_mT_V1\":{5},\"tau_mT_k1\":{6}," +
                "\"tau_mT_V2\":{7},\"tau_mT_k2\":{8},\"tau_mT_Q10\":{9},\"tau_mT_Tref\":{10}," +
                "\"tau_hT_Vthresh\":{11}," +
                "\"tau_hT_V1\":{12},\"tau_hT_k1\":{13}," +
                "\"tau_hT_base\":{14},\"tau_hT_V2\":{15},\"tau_hT_k2\":{16}," +
                "\"tau_hT_Q10\":{17},\"tau_hT_Tref\":{18}" +
                "}}",
                InfMT_Vh, InfMT_k,
                InfHT_Vh, InfHT_k,
                TauMT_base, TauMT_V1, TauMT_k1,
                TauMT_V2, TauMT_k2, TauMT_Q10, TauMT_Tref,
                TauHT_Vthresh,
                TauHT_V1, TauHT_k1,
                TauHT_base, TauHT_V2, TauHT_k2,
                TauHT_Q10, TauHT_Tref);
        }

        /// <summary>重置所有参数为默认值。</summary>
        public static void ResetToDefault()
        {
            AlphaM_A = 0.1; AlphaM_Vs = 35.0; AlphaM_k = 10.0;
            BetaM_A = 4.0; BetaM_Vs = 60.0; BetaM_k = 18.0;
            AlphaH_A = 0.07; AlphaH_Vs = 60.0; AlphaH_k = 20.0;
            BetaH_A = 1.0; BetaH_Vs = 30.0; BetaH_k = 10.0;
            AlphaN_A = 0.01; AlphaN_Vs = 50.0; AlphaN_k = 10.0;
            BetaN_A = 0.125; BetaN_Vs = 60.0; BetaN_k = 80.0;

            InfMT_Vh = 56.0; InfMT_k = 6.2;
            InfHT_Vh = 80.0; InfHT_k = 4.0;
            TauMT_base = 0.612; TauMT_V1 = 132.0; TauMT_k1 = 16.7;
            TauMT_V2 = 16.8; TauMT_k2 = 18.2; TauMT_Q10 = 5.0; TauMT_Tref = 24.0;
            TauHT_Vthresh = -80.0;
            TauHT_V1 = 467.0; TauHT_k1 = 66.6;
            TauHT_base = 28.0; TauHT_V2 = 22.0; TauHT_k2 = 10.5;
            TauHT_Q10 = 3.0; TauHT_Tref = 24.0;
        }
    }

    /// <summary>
    /// 离子通道参数设置窗口，包含 HH 门控参数和 Ca²⁺ T-type 通道参数两个标签页。
    /// 渲染经典 HH 方程并提供可编辑的玻尔兹曼分布常数。
    /// 由 MainWindow Edit 菜单的 "Ion Channel Setting" 菜单项打开。
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
            // ── Na⁺ m gate ──
            AddSection(HHPanel, "Na⁺ Activation — m gate");

            AddEquation(HHPanel,
                "\u03B1m(V) = A \u00B7 (V + V\u2080) / (1 \u2212 exp(\u2212(V + V\u2080) / k))",
                ("A", "alpha_m_A", IonChannelParams.AlphaM_A),
                ("V\u2080", "alpha_m_Vs", IonChannelParams.AlphaM_Vs),
                ("k", "alpha_m_k", IonChannelParams.AlphaM_k));

            AddEquation(HHPanel,
                "\u03B2m(V) = A \u00B7 exp(\u2212(V + V\u2080) / k)",
                ("A", "beta_m_A", IonChannelParams.BetaM_A),
                ("V\u2080", "beta_m_Vs", IonChannelParams.BetaM_Vs),
                ("k", "beta_m_k", IonChannelParams.BetaM_k));

            // ── Na⁺ h gate ──
            AddSection(HHPanel, "Na⁺ Inactivation — h gate");

            AddEquation(HHPanel,
                "\u03B1h(V) = A \u00B7 exp(\u2212(V + V\u2080) / k)",
                ("A", "alpha_h_A", IonChannelParams.AlphaH_A),
                ("V\u2080", "alpha_h_Vs", IonChannelParams.AlphaH_Vs),
                ("k", "alpha_h_k", IonChannelParams.AlphaH_k));

            AddEquation(HHPanel,
                "\u03B2h(V) = A / (1 + exp(\u2212(V + V\u2080) / k))",
                ("A", "beta_h_A", IonChannelParams.BetaH_A),
                ("V\u2080", "beta_h_Vs", IonChannelParams.BetaH_Vs),
                ("k", "beta_h_k", IonChannelParams.BetaH_k));

            // ── K⁺ n gate ──
            AddSection(HHPanel, "K⁺ Activation — n gate");

            AddEquation(HHPanel,
                "\u03B1n(V) = A \u00B7 (V + V\u2080) / (1 \u2212 exp(\u2212(V + V\u2080) / k))",
                ("A", "alpha_n_A", IonChannelParams.AlphaN_A),
                ("V\u2080", "alpha_n_Vs", IonChannelParams.AlphaN_Vs),
                ("k", "alpha_n_k", IonChannelParams.AlphaN_k));

            AddEquation(HHPanel,
                "\u03B2n(V) = A \u00B7 exp(\u2212(V + V\u2080) / k)",
                ("A", "beta_n_A", IonChannelParams.BetaN_A),
                ("V\u2080", "beta_n_Vs", IonChannelParams.BetaN_Vs),
                ("k", "beta_n_k", IonChannelParams.BetaN_k));
        }

        #endregion

        #region Build Ca Tab

        private void BuildCaPanel()
        {
            // ── Steady-state ──
            AddSection(CaPanel, "T-type Ca²⁺ Steady-state Activation / Inactivation");

            AddEquation(CaPanel,
                "m\u221E(V) = 1 / (1 + exp(\u2212(V + V\u00BD) / k))",
                ("V\u00BD", "inf_mT_Vh", IonChannelParams.InfMT_Vh),
                ("k", "inf_mT_k", IonChannelParams.InfMT_k));

            AddEquation(CaPanel,
                "h\u221E(V) = 1 / (1 + exp((V + V\u00BD) / k))",
                ("V\u00BD", "inf_hT_Vh", IonChannelParams.InfHT_Vh),
                ("k", "inf_hT_k", IonChannelParams.InfHT_k));

            // ── Time constants ──
            AddSection(CaPanel, "T-type Ca²⁺ Time Constants");

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

            AddSection(ChannelsPanel, "Global Ion Channel Defaults");

            foreach (var kvp in GlobalBiophysics.GlobalChannels)
            {
                var ch = kvp.Value;
                var block = new StackPanel { Margin = new Thickness(0, 0, 0, 10) };
                var row = new WrapPanel { Margin = new Thickness(8, 0, 0, 0) };

                // Name
                row.Children.Add(new TextBlock
                {
                    Text = "Name:",
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
                string unitLabel = ch.IsPermeability ? "P (cm/s):" : "g (mS/cm\u00b2):";
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
                        Text = "  \u26a0 Permeability (GHK), not conductance",
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
                    MessageBox.Show($"Channel name cannot be empty (original: '{origKey}').",
                        "Validation Error", MessageBoxButton.OK, MessageBoxImage.Warning);
                    nameTb.Focus();
                    return false;
                }
                if (!float.TryParse(valTb.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out float val))
                {
                    MessageBox.Show($"Invalid value for channel '{newName}': {valTb.Text}",
                        "Validation Error", MessageBoxButton.OK, MessageBoxImage.Warning);
                    valTb.Focus();
                    return false;
                }
                if (newChannels.ContainsKey(newName))
                {
                    MessageBox.Show($"Duplicate channel name: '{newName}'.",
                        "Validation Error", MessageBoxButton.OK, MessageBoxImage.Warning);
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
                    MessageBox.Show($"Invalid value for parameter '{kv.Key}': {kv.Value.Text}",
                        "Validation Error", MessageBoxButton.OK, MessageBoxImage.Warning);
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
                case "alpha_m_A": IonChannelParams.AlphaM_A = val; break;
                case "alpha_m_Vs": IonChannelParams.AlphaM_Vs = val; break;
                case "alpha_m_k": IonChannelParams.AlphaM_k = val; break;
                case "beta_m_A": IonChannelParams.BetaM_A = val; break;
                case "beta_m_Vs": IonChannelParams.BetaM_Vs = val; break;
                case "beta_m_k": IonChannelParams.BetaM_k = val; break;
                case "alpha_h_A": IonChannelParams.AlphaH_A = val; break;
                case "alpha_h_Vs": IonChannelParams.AlphaH_Vs = val; break;
                case "alpha_h_k": IonChannelParams.AlphaH_k = val; break;
                case "beta_h_A": IonChannelParams.BetaH_A = val; break;
                case "beta_h_Vs": IonChannelParams.BetaH_Vs = val; break;
                case "beta_h_k": IonChannelParams.BetaH_k = val; break;
                case "alpha_n_A": IonChannelParams.AlphaN_A = val; break;
                case "alpha_n_Vs": IonChannelParams.AlphaN_Vs = val; break;
                case "alpha_n_k": IonChannelParams.AlphaN_k = val; break;
                case "beta_n_A": IonChannelParams.BetaN_A = val; break;
                case "beta_n_Vs": IonChannelParams.BetaN_Vs = val; break;
                case "beta_n_k": IonChannelParams.BetaN_k = val; break;
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
            "alpha_m_A" => IonChannelParams.AlphaM_A,
            "alpha_m_Vs" => IonChannelParams.AlphaM_Vs,
            "alpha_m_k" => IonChannelParams.AlphaM_k,
            "beta_m_A" => IonChannelParams.BetaM_A,
            "beta_m_Vs" => IonChannelParams.BetaM_Vs,
            "beta_m_k" => IonChannelParams.BetaM_k,
            "alpha_h_A" => IonChannelParams.AlphaH_A,
            "alpha_h_Vs" => IonChannelParams.AlphaH_Vs,
            "alpha_h_k" => IonChannelParams.AlphaH_k,
            "beta_h_A" => IonChannelParams.BetaH_A,
            "beta_h_Vs" => IonChannelParams.BetaH_Vs,
            "beta_h_k" => IonChannelParams.BetaH_k,
            "alpha_n_A" => IonChannelParams.AlphaN_A,
            "alpha_n_Vs" => IonChannelParams.AlphaN_Vs,
            "alpha_n_k" => IonChannelParams.AlphaN_k,
            "beta_n_A" => IonChannelParams.BetaN_A,
            "beta_n_Vs" => IonChannelParams.BetaN_Vs,
            "beta_n_k" => IonChannelParams.BetaN_k,
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
