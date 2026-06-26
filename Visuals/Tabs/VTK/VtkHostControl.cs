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

using System.Runtime.InteropServices;
using System.Text;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;

namespace NeuronCAD.Visuals.Tabs.VTK
{
    /// <summary>
    /// Lightweight Win32 child-window host used as the parent HWND for Python VTK render windows.
    /// </summary>
    public sealed class VtkHostControl : HwndHost
    {
        private const int WS_CHILD = 0x40000000;
        private const int WS_VISIBLE = 0x10000000;
        private const int WS_CAPTION = 0x00C00000;
        private const int WS_THICKFRAME = 0x00040000;
        private const int WS_MINIMIZEBOX = 0x00020000;
        private const int WS_MAXIMIZEBOX = 0x00010000;
        private const int WS_SYSMENU = 0x00080000;
        private const int HOST_ID = 0x56544B;
        private const int GWL_STYLE = -16;
        private const uint SWP_FRAMECHANGED = 0x0020;
        private const uint SWP_NOZORDER = 0x0004;
        private const uint SWP_NOACTIVATE = 0x0010;
        private const uint SWP_SHOWWINDOW = 0x0040;

        private IntPtr _hwndHost;
        private IntPtr _embeddedWindow;
        private int _embeddedProcessId;
        private int _lastWidth;
        private int _lastHeight;

        public IntPtr HostHwnd => _hwndHost;

        public bool AttachProcessMainWindow(int processId)
        {
            if (_hwndHost == IntPtr.Zero)
                return false;

            if (_embeddedWindow != IntPtr.Zero && _embeddedProcessId == processId && IsWindow(_embeddedWindow))
            {
                SyncChildWindowsToCurrentSize();
                return true;
            }

            _embeddedWindow = IntPtr.Zero;
            _embeddedProcessId = 0;

            IntPtr candidate = FindTopLevelWindowForProcess(processId);
            if (candidate == IntPtr.Zero)
                return false;

            _embeddedWindow = candidate;
            _embeddedProcessId = processId;
            SetParent(_embeddedWindow, _hwndHost);

            int style = GetWindowLong(_embeddedWindow, GWL_STYLE);
            style |= WS_CHILD | WS_VISIBLE;
            style &= ~(WS_CAPTION | WS_THICKFRAME | WS_MINIMIZEBOX | WS_MAXIMIZEBOX | WS_SYSMENU);
            SetWindowLong(_embeddedWindow, GWL_STYLE, style);

            SyncChildWindowsToCurrentSize();
            return true;
        }

        public void SyncChildWindowsToCurrentSize()
        {
            if (_hwndHost == IntPtr.Zero) return;

            (int width, int height) = GetPixelSize();
            if (width == _lastWidth && height == _lastHeight)
                return;

            _lastWidth = width;
            _lastHeight = height;

            if (_embeddedWindow != IntPtr.Zero)
            {
                SetWindowPos(_embeddedWindow, IntPtr.Zero, 0, 0, width, height,
                    SWP_NOZORDER | SWP_NOACTIVATE | SWP_SHOWWINDOW | SWP_FRAMECHANGED);
            }

            ResizeChildWindows(_hwndHost, width, height);
        }

        protected override HandleRef BuildWindowCore(HandleRef hwndParent)
        {
            (int width, int height) = GetPixelSize();
            _hwndHost = CreateWindowEx(
                0,
                "static",
                "",
                WS_CHILD | WS_VISIBLE,
                0,
                0,
                width,
                height,
                hwndParent.Handle,
                new IntPtr(HOST_ID),
                IntPtr.Zero,
                IntPtr.Zero);

            return new HandleRef(this, _hwndHost);
        }

        protected override void DestroyWindowCore(HandleRef hwnd)
        {
            DestroyWindow(hwnd.Handle);
            _hwndHost = IntPtr.Zero;
            _embeddedWindow = IntPtr.Zero;
            _embeddedProcessId = 0;
        }

        protected override void OnWindowPositionChanged(Rect rcBoundingBox)
        {
            base.OnWindowPositionChanged(rcBoundingBox);
            if (_hwndHost == IntPtr.Zero) return;

            (int width, int height) = RectToPixelSize(rcBoundingBox);
            SetWindowPos(
                _hwndHost,
                IntPtr.Zero,
                0,
                0,
                width,
                height,
                SWP_NOZORDER | SWP_NOACTIVATE);

            SyncChildWindowsToCurrentSize(width, height);
        }

        private void SyncChildWindowsToCurrentSize(int width, int height)
        {
            if (width == _lastWidth && height == _lastHeight)
                return;

            _lastWidth = width;
            _lastHeight = height;

            if (_embeddedWindow != IntPtr.Zero)
            {
                SetWindowPos(_embeddedWindow, IntPtr.Zero, 0, 0, width, height,
                    SWP_NOZORDER | SWP_NOACTIVATE | SWP_SHOWWINDOW | SWP_FRAMECHANGED);
            }

            ResizeChildWindows(_hwndHost, width, height);
        }

        private (int Width, int Height) GetPixelSize()
        {
            return DipToPixelSize(Math.Max(1.0, ActualWidth), Math.Max(1.0, ActualHeight));
        }

        private (int Width, int Height) RectToPixelSize(Rect rect)
        {
            return DipToPixelSize(Math.Max(1.0, rect.Width), Math.Max(1.0, rect.Height));
        }

        private (int Width, int Height) DipToPixelSize(double width, double height)
        {
            Matrix matrix = PresentationSource.FromVisual(this)?.CompositionTarget?.TransformToDevice ?? Matrix.Identity;
            return (
                Math.Max(1, (int)Math.Round(width * matrix.M11)),
                Math.Max(1, (int)Math.Round(height * matrix.M22)));
        }

        private static void ResizeChildWindows(IntPtr parentHwnd, int width, int height)
        {
            EnumChildWindows(parentHwnd, (childHwnd, _) =>
            {
                SetWindowPos(childHwnd, IntPtr.Zero, 0, 0, width, height,
                    SWP_NOZORDER | SWP_NOACTIVATE | SWP_SHOWWINDOW);
                return true;
            }, IntPtr.Zero);
        }

        private static IntPtr FindTopLevelWindowForProcess(int processId)
        {
            IntPtr result = IntPtr.Zero;

            EnumWindows((hwnd, _) =>
            {
                GetWindowThreadProcessId(hwnd, out uint windowProcessId);
                if (windowProcessId != processId || !IsWindowVisible(hwnd))
                    return true;

                string title = GetWindowTextValue(hwnd);
                string className = GetClassNameValue(hwnd);
                if (title.Contains("NeuronCAD", StringComparison.OrdinalIgnoreCase) ||
                    title.Contains("VTK", StringComparison.OrdinalIgnoreCase) ||
                    className.Contains("vtk", StringComparison.OrdinalIgnoreCase))
                {
                    result = hwnd;
                    return false;
                }

                if (result == IntPtr.Zero)
                    result = hwnd;
                return true;
            }, IntPtr.Zero);

            return result;
        }

        private static string GetWindowTextValue(IntPtr hwnd)
        {
            var builder = new StringBuilder(512);
            GetWindowText(hwnd, builder, builder.Capacity);
            return builder.ToString();
        }

        private static string GetClassNameValue(IntPtr hwnd)
        {
            var builder = new StringBuilder(256);
            GetClassName(hwnd, builder, builder.Capacity);
            return builder.ToString();
        }

        [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        private static extern IntPtr CreateWindowEx(
            int dwExStyle,
            string lpClassName,
            string lpWindowName,
            int dwStyle,
            int x,
            int y,
            int nWidth,
            int nHeight,
            IntPtr hWndParent,
            IntPtr hMenu,
            IntPtr hInstance,
            IntPtr lpParam);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool DestroyWindow(IntPtr hwnd);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern IntPtr SetParent(IntPtr hWndChild, IntPtr hWndNewParent);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern int GetWindowLong(IntPtr hWnd, int nIndex);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern int SetWindowLong(IntPtr hWnd, int nIndex, int dwNewLong);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool SetWindowPos(
            IntPtr hWnd,
            IntPtr hWndInsertAfter,
            int x,
            int y,
            int cx,
            int cy,
            uint uFlags);

        private delegate bool EnumWindowsProc(IntPtr hwnd, IntPtr lParam);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool EnumChildWindows(
            IntPtr hWndParent,
            EnumWindowsProc lpEnumFunc,
            IntPtr lParam);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool EnumWindows(
            EnumWindowsProc lpEnumFunc,
            IntPtr lParam);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern uint GetWindowThreadProcessId(
            IntPtr hWnd,
            out uint lpdwProcessId);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool IsWindowVisible(IntPtr hWnd);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool IsWindow(IntPtr hWnd);

        [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        private static extern int GetWindowText(IntPtr hWnd, StringBuilder lpString, int nMaxCount);

        [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        private static extern int GetClassName(IntPtr hWnd, StringBuilder lpClassName, int nMaxCount);
    }
}
