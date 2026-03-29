using System.Windows.Input;

namespace NeuronCAD.Visuals.Tabs.Shared
{
    /// <summary>
    /// Viewport interaction handler interface defining mouse event contracts shared by different modes (Modeling/Simulation).
    /// Implementations: InteractionController (modeling mode), SimulationInteractionController (simulation mode).
    /// Caller: MainWindow routes viewport mouse events to the _activeHandler (this interface instance) based on the current tab.
    /// </summary>
    public interface IViewportInteractionHandler
    {
        /// <summary>Handle mouse down events. Routed from MainWindow.OnViewportMouseDown.</summary>
        void OnMouseDown(object sender, MouseButtonEventArgs e);

        /// <summary>Handle mouse move events. Routed from MainWindow.OnViewportMouseMove.</summary>
        void OnMouseMove(object sender, MouseEventArgs e);

        /// <summary>Handle mouse up events. Routed from MainWindow.OnViewportMouseUp.</summary>
        void OnMouseUp(object sender, MouseButtonEventArgs e);

        /// <summary>Handle mouse wheel events. Routed from MainWindow.OnViewportMouseWheel.</summary>
        void OnMouseWheel(object sender, MouseWheelEventArgs e);

        /// <summary>
        /// Deactivate the current mode and cancel any ongoing operations (placing, dragging, etc.).
        /// Called by MainWindow.SwitchTab when switching tabs.
        /// </summary>
        void Deactivate();
    }
}
