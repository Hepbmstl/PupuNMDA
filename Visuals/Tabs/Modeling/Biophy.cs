using System;
using System.Collections.Generic;
using System.Windows.Media;

namespace NeuronCAD.Visuals.Tabs.Modeling.Visuals
{
    public class ChannelProperty
    {
        public string Id { get; private set; }
        public string Name { get; set; }
        public Color Color { get; set; }
        public float C_ion_channel { get; set; } // 浓度/密度

        public ChannelProperty(string name, Color color, float C)
        {
            Name = name;
            Id = Guid.NewGuid().ToString();
            Color = color;
            C_ion_channel = C;
        }
    }

    // 全局静态字典：所有实体引用的ChannelProperty均来源于此
    public static class GlobalBiophysics
    {
        public static Dictionary<string, ChannelProperty> GlobalChannels { get; } = new Dictionary<string, ChannelProperty>();

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