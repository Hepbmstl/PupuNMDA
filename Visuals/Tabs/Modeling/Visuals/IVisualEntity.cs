using System.Windows.Media;
using System.Windows.Media.Media3D;

namespace NeuronCAD.Visuals.Tabs.Modeling.Visuals
{
    public interface IVisualEntity
    {
        string Id { get; }
        ModelVisual3D Visual3D { get; }
        bool IsSelected { get; }
        Point3D CenterPosition { get; }

        void SetSelected(bool isSelected);
        void SetColor(Color color);
        
        /// <summary>
        /// 对齐到指定位置和法线
        /// </summary>
        void AlignTo(Point3D position, Vector3D normal);

        /// <summary>
        /// 设置是否参与射线检测
        /// </summary>
        void SetHitTestVisible(bool isVisible);

        // === 新增接口：尺寸获取与修改 ===
        
        /// <summary>
        /// 获取当前的尺寸描述（用于在弹窗中显示，如 "Radius: 5" 或 "L: 10, R: 2"）
        /// </summary>
        string GetDimensionInfo();
    }
}