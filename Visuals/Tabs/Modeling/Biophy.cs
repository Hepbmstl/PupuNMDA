using System;
using System.Collections.Generic;
using System.Windows.Media;

namespace NeuronCAD.Visuals.Tabs.Modeling.Visuals
{
    /// <summary>
    /// 离子通道属性数据类，描述单个离子通道的名称、颜色和电导密度。
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
        /// 离子通道电导密度（单位：µS/cm²）。
        /// 在仿真中用于电导相关计算；在渲染时通过 GlobalBiophysics.ConductanceToRenderDensity 转换为视觉散点密度。
        /// 注意：渲染密度仅具有视觉意义，不代表物理点数。
        /// </summary>
        public float G_ion_channel { get; set; }

        /// <summary>
        /// 构造函数。
        /// </summary>
        /// <param name="name">通道名称</param>
        /// <param name="color">渲染颜色</param>
        /// <param name="G">电导密度 (µS/cm²)</param>
        public ChannelProperty(string name, Color color, float G)
        {
            Name = name;
            Id = Guid.NewGuid().ToString();
            Color = color;
            G_ion_channel = G;
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
        /// 全局离子通道字典，Key 为通道名称（与 Hines_method 中使用的键对齐："Na","K","L"）。
        /// 在静态构造函数中预置 Na、K、L 三种通道。
        /// </summary>
        public static Dictionary<string, ChannelProperty> GlobalChannels { get; } = new Dictionary<string, ChannelProperty>();

        /// <summary>
        /// 静态构造函数，初始化预置的三种标准离子通道。
        /// </summary>
        static GlobalBiophysics()
        {
            // 名称使用短键以与 Hines_method 的键名一致
            var naChannel = new ChannelProperty("Na", Colors.Red, 50.0f);
            var kChannel = new ChannelProperty("K", Colors.Blue, 30.0f);
            var leakChannel = new ChannelProperty("L", Colors.LightGreen, 10.0f);

            GlobalChannels.Add(naChannel.Name, naChannel);
            GlobalChannels.Add(kChannel.Name, kChannel);
            GlobalChannels.Add(leakChannel.Name, leakChannel);
        }

        /// <summary>
        /// 将电导密度（µS/cm²）转换为用于渲染的点云密度（无物理意义，仅用于视觉效果）。
        /// 该转换函数统一渲染尺度到微级量纲，返回值为每单位面积的渲染点密度。
        /// </summary>
        public static float ConductanceToRenderDensity(double g_uS_per_cm2)
        {
            // 简单线性缩放并截断，比例因子可根据视觉效果调整。
            const double scale = 0.1; // 每 µS/cm² 对应的视觉密度比例
            double v = g_uS_per_cm2 * scale;
            if (v < 0) v = 0;
            if (v > 10000) v = 10000;
            return (float)v;
        }
    }
}