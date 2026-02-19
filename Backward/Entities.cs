using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Markup;
using System.Windows.Media.Media3D;
using HelixToolkit.Wpf;

namespace NeuronCAD.Backward
{
    public enum SectionType
    {
        Soma = 1, Dend = 2, Axon = 3, Custom = 4
    }

    public class Entities
    {
        public int ID { get; set; }

        public int ParentId { get; set; }

        public double ParentAnchorFraction { get; set; } = 1.0;

        public SectionType Type { get; set; } = SectionType.Dend;


        public double ProximalDiameter { get; set; }

        public double DistalDiameter { get; set; }

        public Vector3D DistalPosition { get; set; }

        public Vector3D ProximalPosition { get; set; }

        public Vector3D CentrePosition
        {
            get
            {
                return new Vector3D(
                    (ProximalPosition.X + DistalPosition.X) / 2,
                    (ProximalPosition.Y + DistalPosition.Y) / 2,
                    (ProximalPosition.Z + DistalPosition.Z) / 2
                    );
            }
        }

        public class ChannelProperty
        {
            public int ID_Channel { get; set; }
            //public string Color { get; set; } = " #ffffff00";
            ///?///
            /// 
            public DistributionType Type { get; set; }

        }

        public Dictionary<string, ChannelProperty> Channels { get; set; }
            = new Dictionary<string, ChannelProperty>();

        public Entities(int id, SectionType type)
        {
            ID = id;
            Type = type;
        }

        public void ConnectParent(int parent_id, int id, Vector3D connected_pos)
        {
            // need a trigger
        }

        public enum DistributionType
        {
            Uniform, Linear, Exp
        }

        public class NeuriteSection : Entities
        {
            public NeuriteSection(int id, SectionType type) : base(id, type)
            {

            }
        }

    }
}
