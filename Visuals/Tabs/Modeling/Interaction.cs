using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Input;
using System.Windows.Controls;
using System.Windows.Media; // <--- 添加此行
using System.Windows.Media.Media3D;
using HelixToolkit.Wpf;
using NeuronCAD.Visuals.Tabs.Modeling.Visuals;

namespace NeuronCAD.Visuals.Tabs.Modeling
{
    public enum InteractionState
    {
        Idle,
        Placing,
        Moving
    }

    public class InteractionController
    {
        private readonly ModelingPage _page;
        private readonly ViewportController _viewportController;
        private readonly HelixViewport3D _helixViewport;

        private InteractionState _currentState = InteractionState.Idle;
        private List<IVisualEntity> _entities = new List<IVisualEntity>();
        private IVisualEntity _activeEntity; 

        private Point _mouseDownPos;
        private bool _isDraggingViewport = false;
        private double _gridZOffset = 0.0; 

        public InteractionController(ModelingPage page, ViewportController viewportController, HelixViewport3D helixViewport)
        {
            _page = page;
            _viewportController = viewportController;
            _helixViewport = helixViewport;
        }

        #region Public API
        public void StartPlacing(IVisualEntity newEntity)
        {
            if (_currentState != InteractionState.Idle) return;
            _activeEntity = newEntity;
            // 放置时关闭自身HitTest，防止射线检测到自己
            _activeEntity.SetHitTestVisible(false); 
            _helixViewport.Children.Add(_activeEntity.Visual3D);
            _currentState = InteractionState.Placing;
            _activeEntity.SetSelected(true);
        }
        
        public void DeleteSelected()
        {
            if (_activeEntity != null && _entities.Contains(_activeEntity))
            {
                _helixViewport.Children.Remove(_activeEntity.Visual3D);
                _entities.Remove(_activeEntity);
                _activeEntity = null;
            }
        }
        
        public void StartMovingSelected()
        {
            if (_activeEntity != null && _entities.Contains(_activeEntity))
            {
                _currentState = InteractionState.Moving;
                _activeEntity.SetSelected(true);
                // 移动时也要暂时忽略自身的 HitTest
                _activeEntity.SetHitTestVisible(false);
            }
        }
        #endregion

        #region Input Handlers
        public void OnMouseDown(object sender, MouseButtonEventArgs e)
        {
            _mouseDownPos = e.GetPosition(_helixViewport);
            _isDraggingViewport = false;

            if (_currentState == InteractionState.Placing || _currentState == InteractionState.Moving)
            {
                e.Handled = true; 
                if (e.ChangedButton == MouseButton.Left) ConfirmAction();
                else if (e.ChangedButton == MouseButton.Right) CancelAction();
            }
        }

        public void OnMouseMove(object sender, MouseEventArgs e)
        {
            var mousePos = e.GetPosition(_helixViewport);

            // 逻辑更新：无论什么状态，都计算十字准星的坐标
            UpdateCrosshair(mousePos);

            if (_currentState == InteractionState.Placing || _currentState == InteractionState.Moving)
            {
                UpdateObjectPosition(mousePos);
            }
            else if (_currentState == InteractionState.Idle)
            {
                if (e.LeftButton == MouseButtonState.Pressed || e.RightButton == MouseButtonState.Pressed)
                {
                    if ((mousePos - _mouseDownPos).Length > 2) _isDraggingViewport = true;
                }
            }
        }

        public void OnMouseUp(object sender, MouseButtonEventArgs e)
        {
            if (_currentState != InteractionState.Idle) return;
            if (_isDraggingViewport) { _isDraggingViewport = false; return; }

            if (e.ChangedButton == MouseButton.Left) PerformHitTest(e.GetPosition(_helixViewport));
            else if (e.ChangedButton == MouseButton.Right)
            {
                // 如果右键点击了当前选中物体的范围，才弹出菜单
                // 为了简化体验，只要当前有选中物体，右键点击哪里都弹出菜单
                if (_activeEntity != null && _activeEntity.IsSelected) ShowContextMenu();
            }
        }

        public void OnMouseWheel(object sender, MouseWheelEventArgs e)
        {
            bool isCtrlPressed = (Keyboard.Modifiers & ModifierKeys.Control) == ModifierKeys.Control;
            if ((_currentState == InteractionState.Placing || _currentState == InteractionState.Moving) && isCtrlPressed)
            {
                double delta = e.Delta > 0 ? 0.5 : -0.5;
                _gridZOffset += delta;
                UpdateObjectPosition(Mouse.GetPosition(_helixViewport));
                e.Handled = true; 
            }
        }
        #endregion

        #region Core Logic

        // 逻辑更新2：更新十字准星和 HUD
        private void UpdateCrosshair(Point mousePos)
        {
            // 发射射线寻找最近的物体表面
            var hits = _helixViewport.Viewport.FindHits(mousePos);
            
            // 排除正在放置的物体(如果有)
            var validHit = hits?.FirstOrDefault(h => _activeEntity == null || !IsSelfOrChild(h.Visual, _activeEntity.Visual3D));
            
            Point3D? worldPos = null;

            if (validHit != null)
            {
                // 如果击中物体，显示接触点
                worldPos = validHit.Position;
            }
            else
            {
                // 如果没击中，显示网格平面上的点 (Z=0)
                // 注意：这里用 Z=0 而不是 _gridZOffset，因为准星是测量工具，通常测量绝对坐标
                worldPos = _viewportController.UnProjectToZPlane(mousePos, 0);
            }

            _page.UpdateCursorInfo(mousePos, worldPos);
        }

        private void UpdateObjectPosition(Point mousePos)
        {
            if (_activeEntity == null) return;

            var allHits = _helixViewport.Viewport.FindHits(mousePos);
            var validHit = allHits.FirstOrDefault(h => !IsSelfOrChild(h.Visual, _activeEntity.Visual3D));

            if (validHit != null)
            {
                _activeEntity.AlignTo(validHit.Position, validHit.Normal);
            }
            else
            {
                // 使用当前的 Z Offset
                Point3D planeCenter = new Point3D(0, 0, _gridZOffset);
                Vector3D planeNormal = new Vector3D(0, 0, 1);
                var hitPoint = _viewportController.UnProjectToZPlane(mousePos, _gridZOffset);

                if (hitPoint.HasValue)
                {
                    _activeEntity.AlignTo(hitPoint.Value, planeNormal);
                }
            }
        }

        // 逻辑更新1：放置后自动放弃选中
        private void ConfirmAction()
        {
            if (_activeEntity == null) return;
            
            if (_currentState == InteractionState.Placing)
            {
                _entities.Add(_activeEntity);
            }

            // 恢复 HitTest 可见性
            _activeEntity.SetHitTestVisible(true);
            
            // 自动放弃选中 (需求1)
            _activeEntity.SetSelected(false);
            _activeEntity = null; // 清空当前指针

            _currentState = InteractionState.Idle;
        }

        private void CancelAction()
        {
            if (_currentState == InteractionState.Placing && _activeEntity != null)
            {
                _helixViewport.Children.Remove(_activeEntity.Visual3D);
                _activeEntity = null;
            }
            else if (_currentState == InteractionState.Moving && _activeEntity != null)
            {
                 // 移动取消时，也要恢复 HitTest
                 _activeEntity.SetHitTestVisible(true);
                 // 暂时回到 Idle，位置可能没复原(需 Memento)
            }
            _currentState = InteractionState.Idle;
        }

        private void PerformHitTest(Point mousePos)
        {
            // 先清空旧的选中
            if (_activeEntity != null) { _activeEntity.SetSelected(false); _activeEntity = null; }

            var hits = _helixViewport.Viewport.FindHits(mousePos);
            if (hits != null && hits.Count > 0)
            {
                var nearest = hits.OrderBy(h => h.Distance).First();
                foreach (var entity in _entities)
                {
                    if (IsSelfOrChild(nearest.Visual, entity.Visual3D))
                    {
                        _activeEntity = entity;
                        _activeEntity.SetSelected(true);
                        break; // 选中一个即可
                    }
                }
            }
        }

        private bool IsSelfOrChild(Visual3D hitVisual, Visual3D selfVisual)
        {
            if (hitVisual == selfVisual) return true;
            DependencyObject curr = hitVisual;
            while (curr != null)
            {
                if (curr == selfVisual) return true;
                curr = VisualTreeHelper.GetParent(curr);
            }
            return false;
        }

        // 逻辑更新3：右键菜单增加 Resize 选项
        private void ShowContextMenu()
        {
            var contextMenu = new ContextMenu();
            
            // 移动
            var moveItem = new MenuItem { Header = "Move" };
            moveItem.Click += (s, e) => StartMovingSelected();
            
            // 调整尺寸 (Resize)
            var resizeItem = new MenuItem { Header = "Resize..." };
            resizeItem.Click += (s, e) =>
            {
                // 获取鼠标位置以显示弹窗
                var mousePos = Mouse.GetPosition(_page); 
                _page.ShowEditPopup(_activeEntity, mousePos);
            };

            // 删除
            var deleteItem = new MenuItem { Header = "Delete" };
            deleteItem.Click += (s, e) => DeleteSelected();

            contextMenu.Items.Add(moveItem);
            contextMenu.Items.Add(resizeItem);
            contextMenu.Items.Add(new Separator());
            contextMenu.Items.Add(deleteItem);

            contextMenu.IsOpen = true;
        }
        #endregion
    }
}