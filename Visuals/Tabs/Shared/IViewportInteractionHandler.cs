using System.Windows.Input;

namespace NeuronCAD.Visuals.Tabs.Shared
{
    /// <summary>
    /// 视口交互处理器接口，定义不同模式(建模/仿真)共用的鼠标事件处理契约。
    /// 实现者：InteractionController（建模模式）、SimulationInteractionController（仿真模式）。
    /// 调用者：MainWindow 根据当前标签页将视口鼠标事件路由到 _activeHandler（此接口实例）。
    /// </summary>
    public interface IViewportInteractionHandler
    {
        /// <summary>鼠标按下事件处理。由 MainWindow.OnViewportMouseDown 路由调用。</summary>
        void OnMouseDown(object sender, MouseButtonEventArgs e);

        /// <summary>鼠标移动事件处理。由 MainWindow.OnViewportMouseMove 路由调用。</summary>
        void OnMouseMove(object sender, MouseEventArgs e);

        /// <summary>鼠标释放事件处理。由 MainWindow.OnViewportMouseUp 路由调用。</summary>
        void OnMouseUp(object sender, MouseButtonEventArgs e);

        /// <summary>鼠标滚轮事件处理。由 MainWindow.OnViewportMouseWheel 路由调用。</summary>
        void OnMouseWheel(object sender, MouseWheelEventArgs e);

        /// <summary>
        /// 停用当前模式，取消所有正在进行的操作（放置、拖拽等）。
        /// 由 MainWindow.SwitchTab 在标签页切换时调用。
        /// </summary>
        void Deactivate();
    }
}
