using System.Windows.Media;
using System.Windows.Media.Media3D;
using HelixToolkit.Wpf;
using NeuronCAD.Visuals.Tabs.Modeling.Visuals;

namespace NeuronCAD.Visuals.Tabs.Modeling
{
    /// <summary>
    /// 连接线的可视化表示，包含一条线段和两个端点球体。
    /// 每个 ConnectionVisual 对应一个 Connection 数据对象。
    /// 由 ConnectionController.Add 创建，端点球体可被 InteractionController 拖拽。
    /// </summary>
    public sealed class ConnectionVisual
    {
        /// <summary>Visual3D 根节点容器，持有线段和两个端点球体作为子元素。</summary>
        public ModelVisual3D Visual3D { get; } = new ModelVisual3D();

        /// <summary>连接线段可视化对象（深天蓝色线段）。</summary>
        public LinesVisual3D Line { get; } = new LinesVisual3D { Thickness = 1.5, Color = Colors.DeepSkyBlue };

        /// <summary>端点 A 的球体可视化对象（白色，可拖拽改变锚点位置）。</summary>
        public SphereVisual3D EndA { get; } = new SphereVisual3D { Radius = 0.4, Fill = System.Windows.Media.Brushes.White };

        /// <summary>端点 B 的球体可视化对象（白色，可拖拽改变锚点位置）。</summary>
        public SphereVisual3D EndB { get; } = new SphereVisual3D { Radius = 0.4, Fill = System.Windows.Media.Brushes.White };

        /// <summary>对应的 Connection 数据对象 ID，用于与 ConnectionController 的字典索引关联。</summary>
        public string ConnectionId { get; }

        /// <summary>
        /// 构造函数，创建连接线可视化并将子元素添加到容器。
        /// 由 ConnectionController.Add 调用。
        /// </summary>
        /// <param name="connectionId">关联的 Connection ID</param>
        public ConnectionVisual(string connectionId)
        {
            ConnectionId = connectionId;
            Visual3D.Children.Add(Line);
            Visual3D.Children.Add(EndA);
            Visual3D.Children.Add(EndB);
        }

        /// <summary>
        /// 更新连接线两端的世界坐标位置。
        /// 被 ConnectionController.Update 调用。
        /// </summary>
        /// <param name="pA">端点 A 的世界坐标</param>
        /// <param name="pB">端点 B 的世界坐标</param>
        public void Update(Point3D pA, Point3D pB)
        {
            Line.Points = new Point3DCollection { pA, pB };
            EndA.Center = pA;
            EndB.Center = pB;
        }
    }

    /// <summary>
    /// 连接控制器，管理所有实体间连接 (Connection) 的生命周期和可视化更新。
    /// 持有连接数据字典和对应的可视化字典。
    /// 由 SharedSceneState 构造时创建，被 InteractionController（创建/拖拽连接端点）
    /// 和 MainWindow（每帧渲染时调用 UpdateAll）使用。
    /// </summary>
    public sealed class ConnectionController
    {
        /// <summary>HelixToolkit 视口引用，用于添加/移除连接线的 Visual3D。</summary>
        private readonly HelixViewport3D _viewport;

        /// <summary>连接数据字典，Key 为 Connection.Id。</summary>
        public Dictionary<string, Connection> ConnectionsById { get; } = new();

        /// <summary>连接可视化字典，Key 为 Connection.Id，与 ConnectionsById 一一对应。</summary>
        public Dictionary<string, ConnectionVisual> VisualsById { get; } = new();

        /// <summary>
        /// 构造函数。由 SharedSceneState 创建。
        /// </summary>
        /// <param name="viewport">HelixToolkit 视口实例</param>
        public ConnectionController(HelixViewport3D viewport)
        {
            _viewport = viewport;
        }

        /// <summary>
        /// 添加一条新连接并创建对应的可视化对象。
        /// 被 InteractionController.ConfirmAction（放置时自动连接）和右键菜单 Connect 操作调用。
        /// </summary>
        /// <param name="connection">连接数据对象</param>
        public void Add(Connection connection)
        {
            ConnectionsById[connection.Id] = connection;

            var visual = new ConnectionVisual(connection.Id);
            VisualsById[connection.Id] = visual;
            _viewport.Children.Add(visual.Visual3D);

            Update(connection.Id);
        }

        /// <summary>
        /// 移除指定连接及其可视化对象。
        /// 预留接口，可在删除实体时联动清理。
        /// </summary>
        /// <param name="id">连接 ID</param>
        public void Remove(string id)
        {
            if (VisualsById.TryGetValue(id, out var v))
            {
                _viewport.Children.Remove(v.Visual3D);
                VisualsById.Remove(id);
            }
            ConnectionsById.Remove(id);
        }

        /// <summary>
        /// 更新指定连接的可视化位置。通过 IAnchoredEntity.TryAnchorToWorldPoint 计算端点世界坐标。
        /// 被 InteractionController.UpdateDraggingConnectionEndpoint（拖拽连接端点时）和 UpdateAll 调用。
        /// </summary>
        /// <param name="id">连接 ID</param>
        public void Update(string id)
        {
            if (!ConnectionsById.TryGetValue(id, out var c)) return;
            if (!VisualsById.TryGetValue(id, out var v)) return;

            if (c.A is not IAnchoredEntity aEnt) return;
            if (c.B is not IAnchoredEntity bEnt) return;

            if (!aEnt.TryAnchorToWorldPoint(c.AnchorA, out Point3D pA)) return;
            if (!bEnt.TryAnchorToWorldPoint(c.AnchorB, out Point3D pB)) return;

            v.Update(pA, pB);
        }

        /// <summary>
        /// 更新所有连接的可视化位置。
        /// 被 MainWindow.InitializeControllers 注册到 CompositionTarget.Rendering 事件中每帧调用，
        /// 以及 InteractionController.UpdateObjectPosition 在实体移动时调用。
        /// </summary>
        public void UpdateAll()
        {
            foreach (var id in ConnectionsById.Keys)
            {
                Update(id);
            }
        }
    }




}