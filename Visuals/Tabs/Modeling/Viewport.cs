using System;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Media3D;
using HelixToolkit.Wpf;

namespace NeuronCAD.Visuals.Tabs.Modeling
{
    public class ViewportController
    {
        private readonly HelixViewport3D _viewport;

        public ViewportController(HelixViewport3D viewport)
        {
            _viewport = viewport;
            InitializeEnvironment();
            ConfigureGestures();
        }

        public Point3D CameraPosition => _viewport.Camera.Position;

        private void InitializeEnvironment()
        {
            _viewport.ShowCoordinateSystem = true;
            _viewport.CoordinateSystemLabelForeground = Brushes.White;
            _viewport.Background = Brushes.Black;
            _viewport.Children.Add(new DefaultLights());

            // 基础网格 (Z=0 平面)
            var grid = new GridLinesVisual3D
            {
                Width = 200,
                Length = 200,
                MinorDistance = 5,
                MajorDistance = 10,
                Thickness = 0.05,
                Fill = Brushes.DimGray,
                Center = new Point3D(0, 0, 0),
                Normal = new Vector3D(0, 0, 1)
            };
            _viewport.Children.Add(grid);
        }

        private void ConfigureGestures()
        {
            _viewport.RotateGesture = new MouseGesture(MouseAction.RightClick);
            _viewport.PanGesture = new MouseGesture(MouseAction.LeftClick);
            _viewport.ZoomExtentsWhenLoaded = true;
        }

        public Ray3D GetRay(Point screenPoint)
        {
            return Viewport3DHelper.Point2DtoRay3D(_viewport.Viewport, screenPoint);
        }

        /// <summary>
        /// [新增] 将屏幕坐标投影到指定高度的水平面 (Z = planeHeight)
        /// </summary>
        public Point3D? UnProjectToZPlane(Point screenPoint, double planeHeight)
        {
            var ray = GetRay(screenPoint);

            // 平面方程: Z = planeHeight
            // 射线 Z(t) = Origin.Z + t * Direction.Z
            // 解 t: planeHeight = Origin.Z + t * Direction.Z
            // t = (planeHeight - Origin.Z) / Direction.Z

            if (Math.Abs(ray.Direction.Z) < 1e-6) return null; // 射线平行于平面

            double t = (planeHeight - ray.Origin.Z) / ray.Direction.Z;

            // t < 0 表示交点在摄像机后方
            if (t < 0) return null;

            return ray.Origin + ray.Direction * t;
        }
    }
}