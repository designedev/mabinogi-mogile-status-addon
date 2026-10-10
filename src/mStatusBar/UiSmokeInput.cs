using System.Runtime.InteropServices;
using System.Windows;

namespace mStatusBar;

// Integration tests click only an app-owned surface and restore the original cursor.
internal static class UiSmokeInput
{
    internal static async Task ClickAsync(FrameworkElement target, Point local, bool rightButton = false)
    {
        if (!target.IsVisible) throw new InvalidOperationException("검증 대상이 표시되지 않았습니다.");
        var position = target.PointToScreen(local);
        GetCursorPos(out var previous);
        try
        {
            SetCursorPos((int)position.X, (int)position.Y);
            await Task.Delay(50);
            var inputs = new[] { new Input { Mouse = new MouseInput { Flags = rightButton ? 0x0008u : 0x0002u } }, new Input { Mouse = new MouseInput { Flags = rightButton ? 0x0010u : 0x0004u } } };
            if (SendInput(2, inputs, Marshal.SizeOf<Input>()) != 2) throw new InvalidOperationException("UI 클릭 검증 입력을 전달하지 못했습니다.");
            await Task.Delay(150);
        }
        finally { SetCursorPos(previous.X, previous.Y); }
    }



    [StructLayout(LayoutKind.Sequential)] private struct PointInt { public int X, Y; }
    [StructLayout(LayoutKind.Sequential)] private struct MouseInput { public int X, Y; public uint Data, Flags, Time; public UIntPtr Extra; }
    [StructLayout(LayoutKind.Explicit)] private struct Input
    {
        [FieldOffset(0)] public uint Type;
        [FieldOffset(8)] public MouseInput Mouse;
    }
    [DllImport("user32.dll")] private static extern uint SendInput(uint count, Input[] inputs, int size);
    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool SetCursorPos(int x, int y);
    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool GetCursorPos(out PointInt point);
}

