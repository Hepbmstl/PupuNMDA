using System.Windows;

namespace NeuronCAD.Visuals.Windows
{
    /// <summary>
    /// NeuronCAD 主窗口
    /// 职责：应用程序的外壳容器，负责容纳菜单栏和各功能标签页。
    /// </summary>
    public partial class MainWindow : Window
    {
        public MainWindow()
        {
            InitializeComponent();
        }

        // 后续可添加:
        // - Window_Closing: 处理退出前的保存提示
        // - KeyDown: 处理全局快捷键
    }
}