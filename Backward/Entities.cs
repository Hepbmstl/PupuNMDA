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
    /// <summary>
    /// 神经元区段类型枚举，用于标识 Entities 的形态学分类。
    /// 被 Entities.Type 属性引用，贯穿建模与导出流程。
    /// </summary>
    public enum SectionType
    {
        /// <summary>细胞体</summary>
        Soma = 1,
        /// <summary>树突</summary>
        Dend = 2,
        /// <summary>轴突</summary>
        Axon = 3,
        /// <summary>用户自定义区段</summary>
        Custom = 4
    }

    /// <summary>
    /// 神经元形态学数据实体（纯数据类），供 UI 层 (Visuals) 与后端 (IOService) 共享。
    /// 每个实例代表一个几何区段 (Section)，存储空间坐标、直径、父子关系及离子通道信息。
    /// 调用者：Visuals 层的 VisualEntityBase 将在未来绑定此数据对象；ExportUtils 用于序列化导出。
    /// </summary>
    public class Entities
    {
        /// <summary>区段唯一标识符。由构造函数注入。</summary>
        public int ID { get; set; }

        /// <summary>父区段 ID，用于描述神经元树形拓扑。根节点的 ParentId 通常为 -1 或 0。</summary>
        public int ParentId { get; set; }

        /// <summary>
        /// 在父区段上的连接位置比例 (0.0~1.0)，0.0 表示父区段近端，1.0 表示远端。
        /// 默认值 1.0 表示连接在父区段的远端（最常见情况）。
        /// </summary>
        public double ParentAnchorFraction { get; set; } = 1.0;

        /// <summary>区段类型（Soma/Dend/Axon/Custom），默认为树突。</summary>
        public SectionType Type { get; set; } = SectionType.Dend;

        /// <summary>区段近端 (靠近细胞体一侧) 的直径，单位 μm。</summary>
        public double ProximalDiameter { get; set; }

        /// <summary>区段远端 (远离细胞体一侧) 的直径，单位 μm。</summary>
        public double DistalDiameter { get; set; }

        /// <summary>区段远端在三维空间中的位置。</summary>
        public Vector3D DistalPosition { get; set; }

        /// <summary>区段近端在三维空间中的位置。</summary>
        public Vector3D ProximalPosition { get; set; }

        /// <summary>
        /// 区段中心位置（只读），为近端与远端的算术平均值。
        /// 可用于快速定位区段在场景中的大致位置。
        /// </summary>
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

        /// <summary>
        /// 后端离子通道属性定义（旧版数据结构，区别于 Visuals 层的 ChannelProperty）。
        /// 用于 IOService 序列化/反序列化场景中使用。
        /// </summary>
        public class ChannelProperty
        {
            /// <summary>通道唯一 ID。</summary>
            public int ID_Channel { get; set; }
            /// <summary>通道颜色的十六进制字符串，用于后端数据存储。</summary>
            public string Color { get; set; } = " #ffffff00";
        }

        /// <summary>
        /// 该区段绑定的离子通道字典，Key 为通道名称，Value 为通道属性。
        /// 在建模流程中由用户通过面板操作填充。
        /// </summary>
        public Dictionary<string, ChannelProperty> Channels { get; set; }
            = new Dictionary<string, ChannelProperty>();

        /// <summary>
        /// 构造函数，创建指定 ID 和类型的区段实体。
        /// </summary>
        /// <param name="id">区段唯一标识符</param>
        /// <param name="type">区段类型</param>
        public Entities(int id, SectionType type)
        {
            ID = id;
            Type = type;
        }

        /// <summary>
        /// 将当前区段连接到指定父区段（预留接口，尚未实现触发逻辑）。
        /// 未来将触发拓扑更新事件，通知 UI 层重绘连接线。
        /// </summary>
        /// <param name="parent_id">父区段 ID</param>
        /// <param name="id">当前区段 ID</param>
        /// <param name="connected_pos">连接点的世界坐标</param>
        public void ConnectParent(int parent_id, int id, Vector3D connected_pos)
        {
            // need a trigger
        }

        /// <summary>
        /// 离子通道/参数在区段上的空间分布类型枚举。
        /// 预留用于描述通道密度沿区段的分布规律。
        /// </summary>
        public enum DistributionType
        {
            /// <summary>均匀分布</summary>
            Uniform,
            /// <summary>线性梯度分布</summary>
            Linear,
            /// <summary>指数衰减分布</summary>
            Exp
        }

        /// <summary>
        /// 神经突区段子类，继承自 Entities。
        /// 预留用于为树突/轴突增加特有属性（如分支角度、髓鞘信息等）。
        /// </summary>
        public class NeuriteSection : Entities
        {
            public NeuriteSection(int id, SectionType type) : base(id, type)
            {

            }
        }

    }
}
