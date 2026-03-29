using System.Windows.Media;
using System.Windows.Media.Media3D;

namespace NeuronCAD.Visuals.Tabs.Modeling.Visuals
{
    /// <summary>
    /// Visual entity for the cell body (Soma), rendered as a frustum (reuses AxonVisual logic).
    /// Inherits AxonVisual and injects the "Soma" type identifier in the constructor, following the same pattern as DendVisual.
    /// Created by MainWindow.OnAddSomaClick and enters the placement flow via InteractionController.StartPlacing.
    /// </summary>
    public class SomaVisual : AxonVisual
    {
        /// <summary>
        /// Constructor that creates the Soma visual entity.
        /// Automatically sets VisualType to "Soma".
        /// </summary>
        public SomaVisual(Point3D start, Point3D end, double radius, Color color)
            : base(start, end, radius, color, "Soma")
        {
        }
    }
}