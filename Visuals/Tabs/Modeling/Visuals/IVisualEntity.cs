using System;
using System.Collections.Generic;
using System.Windows.Media;
using System.Windows.Media.Media3D;

namespace NeuronCAD.Visuals.Tabs.Modeling.Visuals
{
    /// <summary>
    /// 可视化实体的显示模式枚举。
    /// 被 IVisualEntity.SetDisplayMode 和 VisualEntityBase.SetDisplayMode 使用。
    /// </summary>
    public enum VisualDisplayMode
    {
        /// <summary>正常渲染模式，显示材质和离子通道散点</summary>
        Normal,
        /// <summary>线框模式，用于被选中编辑时显示透明框架和万向轮操纵器</summary>
        Wireframe
    }

    /// <summary>
    /// 可视化实体接口，定义所有三维建模对象（Soma、Axon、Dend）的公共行为契约。
    /// 实现者：VisualEntityBase（抽象基类）→ SomaVisual, AxonVisual, DendVisual。
    /// 调用者：InteractionController（选中/放置/移动）、PropertiesPanelController（属性面板）、
    /// MainWindow（编辑弹窗）、ConnectionController（连接线位置计算）等。
    /// </summary>
    public interface IVisualEntity
    {
        /// <summary>实体唯一标识符 (GUID)。用于面板 UI 节点索引和连接映射。</summary>
        string Id { get; }

        /// <summary>HelixToolkit 三维视觉对象根节点，包含网格模型和离子通道散点子元素。</summary>
        ModelVisual3D Visual3D { get; }

        /// <summary>是否处于选中状态。由 InteractionController.ForceSelect 设置。</summary>
        bool IsSelected { get; }

        /// <summary>实体在世界坐标系中的中心位置。用于连接线锚点回退和设备法线计算。</summary>
        Point3D CenterPosition { get; }

        /// <summary>
        /// 实体绑定的离子通道字典，Key 为通道名称，Value 为 ChannelProperty。
        /// 由 PropertiesPanelController 的通道选择器弹窗添加/删除。
        /// </summary>
        Dictionary<string, ChannelProperty> Channels { get; }

        /// <summary>比膜电容 (µF/cm²)，标准值 1.0。用于仿真区室化计算。</summary>
        double Cm { get; set; }

        /// <summary>轴向电阻率 (Ω·cm)，标准值 35.4~100。用于仿真区室化计算。</summary>
        double Ra { get; set; }

        /// <summary>仿真后该实体被切分的区室数量。未仿真时为 0。</summary>
        int CompartmentCount { get; set; }

        /// <summary>仿真后该实体拥有的区室全局 ID 列表。未仿真时为空。</summary>
        List<int> CompartmentIds { get; set; }

        /// <summary>当前实体颜色，供 PropertiesPanelController 面板读取显示。</summary>
        Color CurrentColor { get; }

        /// <summary>设置实体选中状态，切换材质为选中色/默认色。被 InteractionController.ForceSelect 调用。</summary>
        void SetSelected(bool isSelected);

        /// <summary>设置实体颜色。被 PropertiesPanelController 面板中颜色编辑文本框 LostFocus 回调调用。</summary>
        void SetColor(Color color);

        /// <summary>设置实体透明度 (0.0~1.0)。预留接口，可用于半透明显示。</summary>
        void SetOpacity(double opacity);

        /// <summary>
        /// 将实体对齐到指定世界坐标和法线方向。
        /// 被 InteractionController.UpdateObjectPosition 在放置/移动时调用，使实体吸附到命中表面。
        /// </summary>
        void AlignTo(Point3D position, Vector3D normal);

        /// <summary>设置实体是否参与 HitTest。在放置/移动时禁用以避免自命中。被 InteractionController 调用。</summary>
        void SetHitTestVisible(bool isVisible);

        /// <summary>
        /// 切换显示模式（Normal/Wireframe）。
        /// 被 InteractionController.ShowGimbal（进入线框模式）和 HideGimbal（恢复正常模式）调用。
        /// </summary>
        void SetDisplayMode(VisualDisplayMode mode);

        /// <summary>获取实体尺寸信息字符串。预留接口，可用于状态栏或提示信息。</summary>
        string GetDimensionInfo();

        /// <summary>
        /// 刷新离子通道散点可视化。根据 Channels 字典重建表面散点分布。
        /// 被 PropertiesPanelController 中添加/删除通道后调用。
        /// </summary>
        void UpdateChannelVisuals();
    }

    /// <summary>
    /// 锚点实体接口，定义支持在实体表面进行精确锚点定位的行为。
    /// 实现者：SomaVisual, AxonVisual (及其子类 DendVisual)。
    /// 调用者：InteractionController（创建连接时计算锚点）、ConnectionController（更新连接线端点位置）、
    /// SimulationInteractionController（放置设备时计算吸附锚点）、AttachedDeviceBase（设备位置更新）。
    /// </summary>
    public interface IAnchoredEntity
    {
        /// <summary>
        /// 将世界坐标点转换为实体表面锚点引用。
        /// 被 InteractionController.ConfirmAction（创建连接）和 SimulationInteractionController.UpdatePlacingDevice 调用。
        /// </summary>
        /// <param name="worldPoint">世界坐标系中的点</param>
        /// <param name="anchor">输出的锚点引用</param>
        /// <returns>转换是否成功</returns>
        bool TryWorldPointToAnchor(Point3D worldPoint, out AnchorRef anchor);

        /// <summary>
        /// 将锚点引用转换回世界坐标点。
        /// 被 ConnectionController.Update（刷新连接线端点）和 AttachedDeviceBase.UpdatePosition（更新设备箭头位置）调用。
        /// </summary>
        /// <param name="anchor">锚点引用</param>
        /// <param name="worldPoint">输出的世界坐标</param>
        /// <returns>转换是否成功</returns>
        bool TryAnchorToWorldPoint(AnchorRef anchor, out Point3D worldPoint);
    }
    public interface IAnchoredEntity
    {
        bool TryWorldPointToAnchor(Point3D worldPoint, out AnchorRef anchor);
        bool TryAnchorToWorldPoint(AnchorRef anchor, out Point3D worldPoint);
    }
}