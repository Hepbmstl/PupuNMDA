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
using System.Windows;
using System.Windows.Interop;

namespace NeuronCAD.Visuals.Tabs.VTK
{
    /// <summary>
    /// Lightweight Win32 child-window host used as the parent HWND for Python VTK render windows.
    /// </summary>
    public sealed class VtkHostControl : HwndHost
    {
        private const int WS_CHILD = 0x40000000;
        private const int WS_VISIBLE = 0x10000000;
        private const int HOST_ID = 0x56544B;
        private const uint SWP_NOZORDER = 0x0004;
        private const uint SWP_NOACTIVATE = 0x0010;

        private IntPtr _hwndHost;

        public IntPtr HostHwnd => _hwndHost;

        protected override HandleRef BuildWindowCore(HandleRef hwndParent)
        {
            _hwndHost = CreateWindowEx(
                0,
                "static",
                "",
                WS_CHILD | WS_VISIBLE,
                0,
                0,
                (int)Math.Max(1, ActualWidth),
                (int)Math.Max(1, ActualHeight),
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
        }

        protected override void OnWindowPositionChanged(Rect rcBoundingBox)
        {
            base.OnWindowPositionChanged(rcBoundingBox);
            if (_hwndHost == IntPtr.Zero) return;

            SetWindowPos(
                _hwndHost,
                IntPtr.Zero,
                0,
                0,
                Math.Max(1, (int)rcBoundingBox.Width),
                Math.Max(1, (int)rcBoundingBox.Height),
                SWP_NOZORDER | SWP_NOACTIVATE);

            ResizeChildWindows(_hwndHost, Math.Max(1, (int)rcBoundingBox.Width), Math.Max(1, (int)rcBoundingBox.Height));
        }

        private static void ResizeChildWindows(IntPtr parentHwnd, int width, int height)
        {
            EnumChildWindows(parentHwnd, (childHwnd, _) =>
            {
                SetWindowPos(childHwnd, IntPtr.Zero, 0, 0, width, height, SWP_NOZORDER | SWP_NOACTIVATE);
                return true;
            }, IntPtr.Zero);
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
    }
}
