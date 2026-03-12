using System.Windows;
using NeuronCAD.Backward;
using NeuronCAD.Visuals.Windows;

namespace NeuronCAD
{
    /// <summary>
    /// WPF 应用程序入口类，负责应用级别的生命周期管理。
    /// 启动 URI 在 App.xaml 中指向 MainWindow.xaml。
    /// </summary>
    public partial class App : Application
    {
        /// <summary>
        /// 应用程序启动时回调。当前仅调用基类逻辑。
        /// 预留了 DataManager 初始化和 SplashWindow 显示的扩展点。
        /// 被 WPF 框架在程序启动时自动调用。
        /// </summary>
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

        protected override void OnExit(ExitEventArgs e)
        {
            PythonWorker.Shutdown();
            base.OnExit(e);
        }
    }
}