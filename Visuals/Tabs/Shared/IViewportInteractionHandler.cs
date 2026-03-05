using System.Windows.Input;

namespace NeuronCAD.Visuals.Tabs.Shared
{
    /// <summary>
    /// 视口交互处理器接口，不同模式(建模/仿真)各自实现
    /// </summary>
    public interface IViewportInteractionHandler
    {
        void OnMouseDown(object sender, MouseButtonEventArgs e);
        void OnMouseMove(object sender, MouseEventArgs e);
        void OnMouseUp(object sender, MouseButtonEventArgs e);
        void OnMouseWheel(object sender, MouseWheelEventArgs e);

        /// <summary>
        /// 停用当前模式，取消所有正在进行的操作
        /// </summary>
        void Deactivate();
    }
}
