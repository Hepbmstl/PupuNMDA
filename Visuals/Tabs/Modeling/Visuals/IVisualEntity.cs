using System.Collections.Generic;
using System.Windows.Media;
using System.Windows.Media.Media3D;

namespace NeuronCAD.Visuals.Tabs.Modeling.Visuals
{
    public enum VisualDisplayMode
    {
        Normal,
        Wireframe // 透明 框架 万向轮
    }

    public interface IVisualEntity
    {
        string Id { get; }
        ModelVisual3D Visual3D { get; }
        bool IsSelected { get; }
        Point3D CenterPosition { get; }

        Dictionary<string, ChannelProperty> Channels { get; }
        Color CurrentColor { get; } // 供面板读取当前颜色

        void SetSelected(bool isSelected);
        void SetColor(Color color);
        void SetOpacity(double opacity);

        void AlignTo(Point3D position, Vector3D normal);
        void SetHitTestVisible(bool isVisible);
        void SetDisplayMode(VisualDisplayMode mode);

        string GetDimensionInfo();
        void UpdateChannelVisuals();
    }

    public interface IAnchoredEntity
    {
        bool TryWorldPointToAnchor(Point3D worldPoint, out AnchorRef anchor);
        bool TryAnchorToWorldPoint(AnchorRef anchor, out Point3D worldPoint);
    }
}