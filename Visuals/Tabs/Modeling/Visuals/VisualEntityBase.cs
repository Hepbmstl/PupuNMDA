using HelixToolkit.Wpf;
using NeuronCAD.Visuals.Tabs.Modeling.Visuals;
using System;
using System.Windows.Media;
using System.Windows.Media.Media3D;

namespace NeuronCAD.Visuals.Tabs.Modeling.Visuals
{
    public abstract class VisualEntityBase : IVisualEntity
    {
        public string Id { get; private set; }
        public ModelVisual3D Visual3D { get; private set; }
        public bool IsSelected { get; private set; }
        public abstract Point3D CenterPosition { get; }

        protected GeometryModel3D MainModel;
        protected Material _defaultMaterial;
        protected Material _selectedMaterial;

        protected VisualEntityBase()
        {
            Id = Guid.NewGuid().ToString();
            Visual3D = new ModelVisual3D();
            MainModel = new GeometryModel3D();

            _defaultMaterial = MaterialHelper.CreateMaterial(Colors.Gray);
            _selectedMaterial = MaterialHelper.CreateMaterial(Colors.Orange);

            MainModel.Material = _defaultMaterial;
            MainModel.BackMaterial = _defaultMaterial;
            Visual3D.Content = MainModel;
        }

        public void SetSelected(bool isSelected)
        {
            IsSelected = isSelected;
            MainModel.Material = isSelected ? _selectedMaterial : _defaultMaterial;
            MainModel.BackMaterial = isSelected ? _selectedMaterial : _defaultMaterial;
        }

        public void SetColor(Color color)
        {
            _defaultMaterial = MaterialHelper.CreateMaterial(color);
            if (!IsSelected)
            {
                MainModel.Material = _defaultMaterial;
                MainModel.BackMaterial = _defaultMaterial;
            }
        }

        public void SetHitTestVisible(bool isVisible)
        {
            // 通过 ModelVisual3D 的 Content (GeometryModel3D) 也可以控制，
            // 但最直接的是在创建时将 Geometry 设为不可见，或者使用 HelixToolkit 的特定属性。
            // 在 WPF 3D 中，通常通过把 IsHitTestVisible 设为 false (Visual层级)
            // 但 ModelVisual3D 没有 IsHitTestVisible 属性，那是 UIElement 的属性。
            // 这是一个常见的误区。
            // 解决方法：HelixToolkit 的 HitTest 逻辑通常会检查模型。
            // 我们通过一个标志位，在 Interaction 逻辑中过滤掉自己（见 Interaction.cs 的实现），
            // 或者暂时将 Geometry 设为 null (不推荐)，或者暂时移除。

            // 为了架构整洁，我们在这里不做实际操作，
            // 而是依靠 InteractionController 在 FindHits 时过滤掉当前 ActiveEntity。
            // 如果非要物理屏蔽，可以暂存 Geometry 并设为 null，但这会造成闪烁。
            // 所以此方法暂时留空，逻辑上移至 InteractionController。
        }

        // Provide a common contract so derived classes can supply dimension info
        public abstract string GetDimensionInfo();

        public abstract void AlignTo(Point3D position, Vector3D normal);

        protected abstract void UpdateGeometry();
    }
}