using System.Windows.Media;
using System.Windows.Media.Media3D;

namespace NeuronCAD.Visuals.Tabs.Modeling.Visuals
{
    /// <summary>
    /// 细胞体 (Soma) 可视化实体，以圆台形式渲染（复用 AxonVisual 逻辑）。
    /// 继承 AxonVisual 并在构造时注入 "Soma" 类型标识，与 DendVisual 采用相同模式。
    /// 由 MainWindow.OnAddSomaClick 创建，通过 InteractionController.StartPlacing 进入放置流程。
    /// </summary>
    public class SomaVisual : AxonVisual
    {
        /// <summary>
        /// 构造函数，创建细胞体可视化实体。
        /// 自动将 VisualType 设为 "Soma"。
        /// </summary>
        public SomaVisual(Point3D start, Point3D end, double radius, Color color)
            : base(start, end, radius, color, "Soma")
        {
        }
    }
}