using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Media3D;
using HelixToolkit.Wpf;
using NeuronCAD.Visuals.Tabs.Modeling;
using NeuronCAD.Visuals.Tabs.Modeling.Visuals;

namespace NeuronCAD.Visuals.Tabs.Shared
{
    /// <summary>
    /// 跨模式共享的场景状态，持有实体列表、连接控制器、设备列表等
    /// Modeling 和 Simulation 交互控制器通过引用此对象共享数据
    /// </summary>
    public class SharedSceneState
    {
        public HelixViewport3D HelixViewport { get; }
        public ViewportController ViewportController { get; }
        public ConnectionController ConnectionController { get; }

        /// <summary>
        /// 建模实体列表 (Soma, Axon, Dend)，Modeling 创建，Simulation 只读引用
        /// </summary>
        public List<IVisualEntity> Entities { get; } = new();

        /// <summary>
        /// 附属设备列表 (Stimulation, Probe)，Simulation 创建，Modeling 只读引用
        /// </summary>
        public List<IAttachedDevice> Devices { get; } = new();

        public SharedSceneState(HelixViewport3D helixViewport)
        {
            HelixViewport = helixViewport;
            ViewportController = new ViewportController(helixViewport);
            ConnectionController = new ConnectionController(helixViewport);
        }
    }

    /// <summary>
    /// 可视化树工具方法
    /// </summary>
    public static class VisualTreeUtils
    {
        public static bool IsSelfOrChild(Visual3D hitVisual, Visual3D selfVisual)
        {
            if (hitVisual == selfVisual) return true;
            DependencyObject curr = hitVisual;
            while (curr != null)
            {
                if (curr == selfVisual) return true;
                curr = VisualTreeHelper.GetParent(curr);
            }
            return false;
        }
    }
}
