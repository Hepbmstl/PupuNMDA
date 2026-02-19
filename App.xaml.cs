using System.Windows;
using NeuronCAD.Visuals.Windows;

namespace NeuronCAD
{
    /// <summary>
    /// Interaction logic for App.xaml
    /// </summary>
    public partial class App : Application
    {
        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);

            // 在此处可以初始化 DataManager 或 IOService
            // var dataManager = new DataManager();

            // Splash Window 逻辑预留
            // var splash = new SplashWindow();
            // splash.Show();

            // MainWindow 已在 XAML 的 StartupUri 中定义，也可以在此处手动启动：
            //MainWindow mainWindow = new MainWindow();
            //mainWindow.Show();
        }
    }
}