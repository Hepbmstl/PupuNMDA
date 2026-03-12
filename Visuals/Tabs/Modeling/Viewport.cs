using System;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Media3D;
using HelixToolkit.Wpf;

namespace NeuronCAD.Visuals.Tabs.Modeling
{
    /// <summary>
    /// 视口控制器，负责初始化 3D 视口环境（照明、网格、手势配置）和提供射线投影工具方法。
    /// 由 MainWindow.InitializeControllers 创建，存储在 SharedSceneState.ViewportController 中。
    /// 被 InteractionController/SimulationInteractionController 的 UpdateCrosshair 和 UpdateObjectPosition 使用。
    /// </summary>
    public class ViewportController
    {
        /// <summary>HelixViewport3D 实例引用，由构造函数注入。</summary>
        private readonly HelixViewport3D _viewport;

        /// <summary>
        /// 构造函数，初始化视口环境和手势配置。
        /// 由 MainWindow.InitializeControllers 调用。
        /// </summary>
        /// <param name="viewport">HelixViewport3D 实例</param>
        public ViewportController(HelixViewport3D viewport)
        {
            _viewport = viewport;
            InitializeEnvironment();
            ConfigureGestures();
        }

        /// <summary>获取当前摄像机位置（世界坐标）。被外部查询调用。</summary>
        public Point3D CameraPosition => _viewport.Camera.Position;

        /// <summary>
        /// 初始化视口环境：坐标系、黑色背景、默认灯光、Z=0 平面网格。
        /// 由构造函数调用。
        /// </summary>
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

        /// <summary>
        /// 配置视口手势：右键旋转、左键平移、加载时自动缩放适配。
        /// 由构造函数调用。
        /// </summary>
        private void ConfigureGestures()
        {
            _viewport.RotateGesture = new MouseGesture(MouseAction.RightClick);
            _viewport.PanGesture = new MouseGesture(MouseAction.LeftClick);
            _viewport.ZoomExtentsWhenLoaded = true;
        }

        /// <summary>
        /// 将屏幕坐标转换为 3D 射线。
        /// 被 UnProjectToZPlane 内部调用。
        /// </summary>
        /// <param name="screenPoint">屏幕坐标</param>
        /// <returns>3D 射线</returns>
        public Ray3D GetRay(Point screenPoint)
        {
            return Viewport3DHelper.Point2DtoRay3D(_viewport.Viewport, screenPoint);
        }

        /// <summary>
        /// 将屏幕坐标投影到指定高度的水平面 (Z = planeHeight)。
        /// 被 InteractionController.UpdateObjectPosition 和 UpdateCrosshair 调用，用于在无实体命中时将鼠标投影到地面。
        /// </summary>
        /// <param name="screenPoint">屏幕坐标</param>
        /// <param name="planeHeight">平面高度 (Z 值)</param>
        /// <returns>投影点世界坐标，或 null（射线平行于平面或交点在摄像机后方）</returns>
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