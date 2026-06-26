/*
 * Copyright 2026 [Hepbmstl Hepupu]
 *
 * Pupu NMDA / NeuronCAD
 * A Multi-Compartment Neuron Modeling and Dynamics Analysis Platform
 *
 * Licensed under the Apache License, Version 2.0 (the "License");
 * you may not use this file except in compliance with the License.
 * You may obtain a copy of the License at
 *
 * http://www.apache.org/licenses/LICENSE-2.0
 *
 * Unless required by applicable law or agreed to in writing, software
 * distributed under the License is distributed on an "AS IS" BASIS,
 * WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
 * See the License for the specific language governing permissions and
 * limitations under the License.
 */

using System;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Media3D;
using HelixToolkit.Wpf;

namespace NeuronCAD.Visuals.Tabs.Modeling
{
    /// <summary>
    /// Viewport controller responsible for initializing the 3D viewport environment (lighting, grid, gesture configuration)
    /// and providing ray projection utility methods.
    /// Created by MainWindow.InitializeControllers and stored in SharedSceneState.ViewportController.
    /// Used by InteractionController/SimulationInteractionController's UpdateCrosshair and UpdateObjectPosition.
    /// </summary>
    public class ViewportController
    {
        private const double RollFallbackDegreesPerPixel = 0.35;

        /// <summary>Reference to the HelixViewport3D instance, injected via the constructor.</summary>
        private readonly HelixViewport3D _viewport;
        private bool _isRollingCamera;
        private Point _lastRollPoint;

        /// <summary>
        /// Constructor that initializes the viewport environment and gesture configuration.
        /// Called by MainWindow.InitializeControllers.
        /// </summary>
        /// <param name="viewport">HelixViewport3D instance</param>
        public ViewportController(HelixViewport3D viewport)
        {
            _viewport = viewport;
            InitializeEnvironment();
            ConfigureGestures();
        }

        /// <summary>Get the current camera position (world coordinates). Used by external callers.</summary>
        public Point3D CameraPosition => _viewport.Camera.Position;

        /// <summary>
        /// Initialize the viewport environment: coordinate system, black background, default lights, and a Z=0 plane grid.
        /// Called by the constructor.
        /// </summary>
        private void InitializeEnvironment()
        {
            _viewport.ShowCoordinateSystem = true;
            _viewport.CoordinateSystemLabelForeground = Brushes.White;
            _viewport.Background = Brushes.Black;
            _viewport.Children.Add(new DefaultLights());

            // Base grid (Z=0 plane)
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
        /// Configure viewport gestures: left-click pan, right-click rotate, wheel zoom, and middle-drag camera roll.
        /// Called by the constructor.
        /// </summary>
        private void ConfigureGestures()
        {
            _viewport.RotateGesture = new MouseGesture(MouseAction.RightClick);
            _viewport.RotateGesture2 = new MouseGesture(MouseAction.None);
            _viewport.PanGesture = new MouseGesture(MouseAction.LeftClick);
            _viewport.PanGesture2 = new MouseGesture(MouseAction.None);
            _viewport.ZoomGesture = new MouseGesture(MouseAction.None);
            _viewport.ZoomGesture2 = new MouseGesture(MouseAction.None);
            _viewport.CameraRotationMode = CameraRotationMode.Trackball;
            _viewport.FixedRotationPointEnabled = true;
            _viewport.FixedRotationPoint = new Point3D(0, 0, 0);
            _viewport.ModelUpDirection = new Vector3D(0, 1, 0);
            _viewport.ZoomExtentsWhenLoaded = true;
            _viewport.PreviewMouseDown += OnViewportPreviewMouseDown;
            _viewport.PreviewMouseMove += OnViewportPreviewMouseMove;
            _viewport.PreviewMouseUp += OnViewportPreviewMouseUp;
            _viewport.LostMouseCapture += (_, _) => _isRollingCamera = false;
        }

        private void OnViewportPreviewMouseDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ChangedButton != MouseButton.Middle)
                return;

            _isRollingCamera = true;
            _lastRollPoint = e.GetPosition(_viewport);
            _viewport.CaptureMouse();
            e.Handled = true;
        }

        private void OnViewportPreviewMouseMove(object sender, MouseEventArgs e)
        {
            if (!_isRollingCamera)
                return;

            if (e.MiddleButton != MouseButtonState.Pressed)
            {
                EndCameraRoll();
                return;
            }

            Point current = e.GetPosition(_viewport);
            RollCamera(_lastRollPoint, current);
            _lastRollPoint = current;
            e.Handled = true;
        }

        private void OnViewportPreviewMouseUp(object sender, MouseButtonEventArgs e)
        {
            if (e.ChangedButton != MouseButton.Middle)
                return;

            EndCameraRoll();
            e.Handled = true;
        }

        private void EndCameraRoll()
        {
            _isRollingCamera = false;
            if (_viewport.IsMouseCaptured)
                _viewport.ReleaseMouseCapture();
        }

        private void RollCamera(Point previous, Point current)
        {
            if (_viewport.Camera is not ProjectionCamera camera)
                return;

            Vector3D lookDirection = camera.LookDirection;
            if (lookDirection.LengthSquared < 1e-12)
                return;

            double angle = CalculateRollAngle(previous, current);
            if (Math.Abs(angle) < 1e-6)
                return;

            var rotation = new AxisAngleRotation3D(lookDirection, angle);
            var transform = new RotateTransform3D(rotation);
            Vector3D up = transform.Transform(camera.UpDirection);
            if (up.LengthSquared < 1e-12)
                return;

            up.Normalize();
            camera.UpDirection = up;
        }

        private double CalculateRollAngle(Point previous, Point current)
        {
            Point center = new(_viewport.ActualWidth * 0.5, _viewport.ActualHeight * 0.5);
            Vector previousVector = previous - center;
            Vector currentVector = current - center;

            double angle;
            if (previousVector.Length > 8.0 && currentVector.Length > 8.0)
            {
                double previousAngle = Math.Atan2(previousVector.Y, previousVector.X);
                double currentAngle = Math.Atan2(currentVector.Y, currentVector.X);
                double delta = currentAngle - previousAngle;
                if (delta > Math.PI) delta -= 2.0 * Math.PI;
                else if (delta < -Math.PI) delta += 2.0 * Math.PI;
                angle = delta * 180.0 / Math.PI;
            }
            else
            {
                angle = (current.X - previous.X) * RollFallbackDegreesPerPixel;
            }

            return Math.Max(-30.0, Math.Min(30.0, angle));
        }

        /// <summary>
        /// Convert screen coordinates to a 3D ray.
        /// Used internally by UnProjectToZPlane.
        /// </summary>
        /// <param name="screenPoint">Screen coordinates</param>
        /// <returns>3D ray</returns>
        public Ray3D GetRay(Point screenPoint)
        {
            return Viewport3DHelper.Point2DtoRay3D(_viewport.Viewport, screenPoint);
        }

        /// <summary>
        /// Project screen coordinates onto the horizontal plane at the specified height (Z = planeHeight).
        /// Called by InteractionController.UpdateObjectPosition and UpdateCrosshair to project the mouse to the ground when no entity is hit.
        /// </summary>
        /// <param name="screenPoint">Screen coordinates</param>
        /// <param name="planeHeight">Plane height (Z value)</param>
        /// <returns>Projected point in world coordinates, or null (if the ray is parallel to the plane or the intersection is behind the camera)</returns>
        public Point3D? UnProjectToZPlane(Point screenPoint, double planeHeight)
        {
            var ray = GetRay(screenPoint);

            // Plane equation: Z = planeHeight
            // Ray Z(t) = Origin.Z + t * Direction.Z
            // Solve for t: planeHeight = Origin.Z + t * Direction.Z
            // t = (planeHeight - Origin.Z) / Direction.Z

            if (Math.Abs(ray.Direction.Z) < 1e-6) return null; // Ray is parallel to the plane

            double t = (planeHeight - ray.Origin.Z) / ray.Direction.Z;

            // t < 0 means the intersection is behind the camera
            if (t < 0) return null;

            return ray.Origin + ray.Direction * t;
        }
    }
}
