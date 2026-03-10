using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Media3D;
using HelixToolkit.Wpf;
using NeuronCAD.Visuals.Tabs.Modeling;
using NeuronCAD.Visuals.Tabs.Modeling.Visuals;
using NeuronCAD.Visuals.Tabs.Simulation;

namespace NeuronCAD.Visuals.Tabs.Shared
{
    /// <summary>
    /// 跨模式共享的场景状态单例，持有实体列表、连接控制器、设备列表等核心数据。
    /// Modeling 和 Simulation 交互控制器通过引用此对象共享同一份三维场景数据。
    /// 由 MainWindow.InitializeControllers 创建，注入到各控制器中。
    /// </summary>
    public class SharedSceneState
    {
        /// <summary>HelixToolkit 三维视口控件引用，用于添加/移除 Visual3D 子元素。</summary>
        public HelixViewport3D HelixViewport { get; }

        /// <summary>视口控制器，负责环境初始化（网格、灯光）、手势配置和射线投影计算。</summary>
        public ViewportController ViewportController { get; }

        /// <summary>连接控制器，负责管理实体间连接线的增删查改和可视化更新。</summary>
        public ConnectionController ConnectionController { get; }

        /// <summary>仿真注册表，负责管理建模组件的全局登记和区室化切分。</summary>
        public SimulationRegistry SimulationRegistry { get; }

        /// <summary>
        /// 建模实体列表 (Soma, Axon, Dend)。
        /// 由 Modeling 模式的 InteractionController 创建和修改，Simulation 模式只读引用。
        /// </summary>
        public List<IVisualEntity> Entities { get; } = new();

        /// <summary>
        /// 附属设备列表 (Stimulation, Probe)。
        /// 由 Simulation 模式的 SimulationInteractionController 创建和修改，Modeling 模式只读引用。
        /// </summary>
        public List<IAttachedDevice> Devices { get; } = new();

        /// <summary>
        /// 构造函数，基于给定的 HelixViewport3D 创建视口控制器和连接控制器。
        /// 由 MainWindow.InitializeControllers 调用。
        /// </summary>
        /// <param name="helixViewport">XAML 中定义的 HelixViewport3D 实例</param>
        public SharedSceneState(HelixViewport3D helixViewport)
        {
            HelixViewport = helixViewport;
            ViewportController = new ViewportController(helixViewport);
            ConnectionController = new ConnectionController(helixViewport);
            SimulationRegistry = new SimulationRegistry();
        }
    }

    /// <summary>
    /// WPF 可视化树辅助工具类，提供 Visual3D 层级查询方法。
    /// 被 InteractionController 和 SimulationInteractionController 的命中测试逻辑广泛调用，
    /// 用于判断射线命中的 Visual3D 是否属于某个实体或设备。
    /// </summary>
    public static class VisualTreeUtils
    {
        /// <summary>
        /// 判断 hitVisual 是否是 selfVisual 本身或其子级 Visual3D。
        /// 通过向上遍历可视化树实现。
        /// 被 InteractionController.HitTestEntity、SimulationInteractionController.HitTestDevice 等方法调用。
        /// </summary>
        /// <param name="hitVisual">射线命中的 Visual3D</param>
        /// <param name="selfVisual">目标实体的根 Visual3D</param>
        /// <returns>true 表示 hitVisual 属于 selfVisual 的可视化树</returns>
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
