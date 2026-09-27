using System;
using System.ComponentModel;
using System.Text;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace AutoInput
{
    internal static class KeyboardSender
    {
        // INPUT union에는 MOUSEINPUT도 포함해야 x64에서 정확한 크기가 됩니다.
        [StructLayout(LayoutKind.Sequential)]
        private struct Input { public uint type; public InputUnion data; }
        [StructLayout(LayoutKind.Explicit)]
        private struct InputUnion
        {
            [FieldOffset(0)] public KeyboardInput keyboard;
            [FieldOffset(0)] public MouseInput mouse;
        }
        [StructLayout(LayoutKind.Sequential)]
        private struct KeyboardInput
        {
            public ushort key, scan;
            public uint flags, time;
            public UIntPtr extra;
        }
        [StructLayout(LayoutKind.Sequential)]
        private struct MouseInput
        {
            public int x, y;
            public uint data, flags, time;
            public UIntPtr extra;
        }
        [DllImport("user32.dll", SetLastError = true)]
        private static extern uint SendInput(uint count, Input[] inputs, int size);
        [DllImport("user32.dll")]
        internal static extern IntPtr GetForegroundWindow();
        [DllImport("user32.dll")]
        internal static extern bool IsWindow(IntPtr handle);
        [DllImport("user32.dll")]
        private static extern uint GetWindowThreadProcessId(IntPtr handle, out uint process);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        private static extern int GetWindowText(IntPtr handle, StringBuilder text, int count);
        internal static uint ProcessOf(IntPtr handle)
        {
            uint process;
            GetWindowThreadProcessId(handle, out process);
            return process;
        }
        internal static string TitleOf(IntPtr handle)
        {
            var title = new StringBuilder(256);
            GetWindowText(handle, title, title.Capacity);
            return title.Length == 0 ? "(제목 없음)" : title.ToString();
        }
        [DllImport("user32.dll", SetLastError = true)]
        internal static extern bool RegisterHotKey(IntPtr handle, int id, uint modifiers, uint key);
        [DllImport("user32.dll")]
        internal static extern bool UnregisterHotKey(IntPtr handle, int id);

        internal static void Send(Keys key, bool release, bool extended = false)
        {
            var input = new Input { type = 1 };
            input.data.keyboard.key = (ushort)key;
            input.data.keyboard.flags = release ? 2u : 0u;
            if (extended || key == Keys.Left || key == Keys.Right || key == Keys.Up || key == Keys.Down)
                input.data.keyboard.flags |= 1u;
            if (SendInput(1, new[] { input }, Marshal.SizeOf(typeof(Input))) != 1)
                throw new Win32Exception(Marshal.GetLastWin32Error(), "키 입력에 실패했습니다. 대상 창의 권한을 확인하세요.");
        }
    }
}
