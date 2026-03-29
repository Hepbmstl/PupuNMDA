using System;
using System.Collections.Generic;
using System.Windows.Media;

namespace NeuronCAD.Visuals.Tabs.Modeling.Visuals
{
    /// <summary>
    /// Ion channel property data class describing a single ion channel's name, color, and conductance/permeability.
    /// Instances are held by an IVisualEntity.Channels dictionary and referenced by the channel selector in PropertiesPanelController.
    /// Global predefined channel instances are defined in GlobalBiophysics.GlobalChannels.
    /// </summary>
    public class ChannelProperty
    {
        /// <summary>Unique channel identifier (GUID), generated automatically in the constructor.</summary>
        public string Id { get; private set; }

        /// <summary>Display name for the channel (e.g., "Na (Sodium)"), also used as the key in the GlobalChannels dictionary.</summary>
        public string Name { get; set; }

        /// <summary>Visualization color for rendering on 3D surfaces or scatter plots.</summary>
        public Color Color { get; set; }

        /// <summary>
        /// Ion channel conductance density (units: mS/cm²) or membrane permeability (units: cm/s when IsPermeability is true).
        /// Passed directly to Hines_method.py's add_channel_to_segment interface in the simulation.
        /// </summary>
        public float G_ion_channel { get; set; }

        /// <summary>
        /// When true, indicates that G_ion_channel stores membrane permeability P (cm/s) rather than conductance density g (mS/cm²).
        /// Used for channels (e.g., CaT) whose currents are computed via the GHK equation.
        /// </summary>
        public bool IsPermeability { get; set; }

        /// <summary>
        /// Constructor.
        /// </summary>
        /// <param name="name">Channel name</param>
        /// <param name="color">Rendering color</param>
        /// <param name="G">Conductance density (mS/cm²) or permeability (cm/s)</param>
        /// <param name="isPermeability">Whether this channel represents permeability</param>
        public ChannelProperty(string name, Color color, float G, bool isPermeability = false)
        {
            Name = name;
            Id = Guid.NewGuid().ToString();
            Color = color;
            G_ion_channel = G;
            IsPermeability = isPermeability;
        }

        /// <summary>
        /// Create a deep copy of this instance (new Id), used to maintain independent channel parameters per entity.
        /// </summary>
        public ChannelProperty Clone()
        {
            return new ChannelProperty(Name, Color, G_ion_channel, IsPermeability);
        }
    }

    /// <summary>
    /// Static class holding global biophysical parameters and definitions for available ion channels.
    /// ChannelProperty instances referenced by entities come from this dictionary.
    /// Read by PropertiesPanelController.InitializeChannelSelector to populate the channel selector UI.
    /// </summary>
    public static class GlobalBiophysics
    {
        /// <summary>
        /// Global ion channel dictionary, with keys aligned to Hines_method ("Na","K","L").
        /// Pre-populated with Na, K, and L channels in the static constructor.
        /// </summary>
        public static Dictionary<string, ChannelProperty> GlobalChannels { get; } = new Dictionary<string, ChannelProperty>();

        /// <summary>
        /// Static constructor initializing the default standard ion channels.
        /// </summary>
        static GlobalBiophysics()
        {
            ResetToDefaults();
        }

        /// <summary>Reset global channels to their default initial values.</summary>
        public static void ResetToDefaults()
        {
            GlobalChannels.Clear();
            // Use short keys to match the key names used by Hines_method
            // Default values aligned with Biophy.json (tcD model): gnabar=0.003 S/cm² = 3 mS/cm², gkbar=0.005 = 5, g_pas=3.79e-5 = 0.0379
            var naChannel = new ChannelProperty("Na", Colors.Red, 3.0f);
            var kChannel = new ChannelProperty("K", Colors.Blue, 5.0f);
            var leakChannel = new ChannelProperty("L", Colors.LightGreen, 0.0379f);
            // CaT: pcabar=1.7e-5 cm/s (from Biophy.json)
            var catChannel = new ChannelProperty("CaT", Colors.Orange, 1.7e-5f, isPermeability: true);

            GlobalChannels.Add(naChannel.Name, naChannel);
            GlobalChannels.Add(kChannel.Name, kChannel);
            GlobalChannels.Add(leakChannel.Name, leakChannel);
            GlobalChannels.Add(catChannel.Name, catChannel);
        }
    }
}