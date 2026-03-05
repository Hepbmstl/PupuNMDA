using System;
using System.Collections.Generic;
using System.Windows.Media;

namespace NeuronCAD.Visuals.Tabs.Modeling.Visuals
{
    /// <summary>
    /// 离子通道属性数据类，描述单个离子通道的名称、颜色和浓度/密度。
    /// 被 IVisualEntity.Channels 字典持有，由 PropertiesPanelController 的通道选择器创建引用。
    /// 全局实例在 GlobalBiophysics.GlobalChannels 中定义。
    /// </summary>
    public class ChannelProperty
    {
        /// <summary>通道唯一标识符 (GUID)，在构造时自动生成。</summary>
        public string Id { get; private set; }

        /// <summary>通道显示名称（如 "Na+ (Sodium)"），也作为 GlobalChannels 字典的 Key。</summary>
        public string Name { get; set; }

        /// <summary>通道可视化颜色，用于三维表面散点渲染。</summary>
        public Color Color { get; set; }

        /// <summary>
        /// 离子通道表面浓度/密度 (每单位面积的点数)。
        /// 被 VisualEntityBase.UpdateChannelVisuals 用于计算散点数量：pointCount = totalArea * C_ion_channel。
        /// </summary>
        public float C_ion_channel { get; set; }

        /// <summary>
        /// 构造函数。
        /// </summary>
        /// <param name="name">通道名称</param>
        /// <param name="color">渲染颜色</param>
        /// <param name="C">表面浓度/密度</param>
        public ChannelProperty(string name, Color color, float C)
        {
            Name = name;
            Id = Guid.NewGuid().ToString();
            Color = color;
            C_ion_channel = C;
        }
    }

    /// <summary>
    /// 全局生物物理参数静态类，持有所有可用离子通道的定义。
    /// 所有实体引用的 ChannelProperty 均来源于此字典。
    /// 被 PropertiesPanelController.InitializeChannelSelector 读取以填充通道选择器弹窗按钮。
    /// </summary>
    public static class GlobalBiophysics
    {
        /// <summary>
        /// 全局离子通道字典，Key 为通道名称。
        /// 在静态构造函数中预置 Na+、K+、Leak 三种通道。
        /// </summary>
        public static Dictionary<string, ChannelProperty> GlobalChannels { get; } = new Dictionary<string, ChannelProperty>();

        /// <summary>
        /// 静态构造函数，初始化预置的三种标准离子通道。
        /// </summary>
        static GlobalBiophysics()
        {
            var naChannel = new ChannelProperty("Na+ (Sodium)", Colors.Red, 50.0f);
            var kChannel = new ChannelProperty("K+ (Potassium)", Colors.Blue, 30.0f);
            var leakChannel = new ChannelProperty("Leak", Colors.LightGreen, 10.0f);

            GlobalChannels.Add(naChannel.Name, naChannel);
            GlobalChannels.Add(kChannel.Name, kChannel);
            GlobalChannels.Add(leakChannel.Name, leakChannel);
        }
    }
}