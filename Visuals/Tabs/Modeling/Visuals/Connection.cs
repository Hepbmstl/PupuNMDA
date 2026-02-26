using System.Windows.Media;
using System.Windows.Media.Media3D;
using HelixToolkit.Wpf;
using NeuronCAD.Visuals.Tabs.Modeling.Visuals;

namespace NeuronCAD.Visuals.Tabs.Modeling
{
    public sealed class ConnectionVisual
    {
        public ModelVisual3D Visual3D { get; } = new ModelVisual3D();

        public LinesVisual3D Line { get; } = new LinesVisual3D { Thickness = 1.5, Color = Colors.DeepSkyBlue };
        public SphereVisual3D EndA { get; } = new SphereVisual3D { Radius = 0.4, Fill = System.Windows.Media.Brushes.White };
        public SphereVisual3D EndB { get; } = new SphereVisual3D { Radius = 0.4, Fill = System.Windows.Media.Brushes.White };

        public string ConnectionId { get; }

        public ConnectionVisual(string connectionId)
        {
            ConnectionId = connectionId;
            Visual3D.Children.Add(Line);
            Visual3D.Children.Add(EndA);
            Visual3D.Children.Add(EndB);
        }

        public void Update(Point3D pA, Point3D pB)
        {
            Line.Points = new Point3DCollection { pA, pB };
            EndA.Center = pA;
            EndB.Center = pB;
        }
    }

    public sealed class ConnectionController
    {
        private readonly HelixViewport3D _viewport;

        public Dictionary<string, Connection> ConnectionsById { get; } = new();
        public Dictionary<string, ConnectionVisual> VisualsById { get; } = new();

        public ConnectionController(HelixViewport3D viewport)
        {
            _viewport = viewport;
        }

        public void Add(Connection connection)
        {
            ConnectionsById[connection.Id] = connection;

            var visual = new ConnectionVisual(connection.Id);
            VisualsById[connection.Id] = visual;
            _viewport.Children.Add(visual.Visual3D);

            Update(connection.Id);
        }

        public void Remove(string id)
        {
            if (VisualsById.TryGetValue(id, out var v))
            {
                _viewport.Children.Remove(v.Visual3D);
                VisualsById.Remove(id);
            }
            ConnectionsById.Remove(id);
        }

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

        public void UpdateAll()
        {
            foreach (var id in ConnectionsById.Keys)
            {
                Update(id);
            }
        }
    }

    

    
}