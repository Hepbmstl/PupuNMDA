using System.Windows;
using NeuronCAD.Backward;
using NeuronCAD.Visuals.Windows;

namespace NeuronCAD
{
    /// <summary>
    /// WPF application entry class, responsible for application-level lifecycle management.
    /// The startup URI in App.xaml points to MainWindow.xaml.
    /// </summary>
    public partial class App : Application
    {
        /// <summary>
        /// Callback invoked when the application starts. Currently only calls the base logic.
        /// Extension points for DataManager initialization and showing a SplashWindow are reserved.
        /// Automatically called by the WPF framework on startup.
        /// </summary>
        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);

            // Initialize DataManager or IOService here
            // var dataManager = new DataManager();

            // Splash window logic placeholder
            // var splash = new SplashWindow();
            // splash.Show();

            // MainWindow is defined in XAML StartupUri; it could be started here manually:
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