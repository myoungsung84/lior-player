using System.Runtime.InteropServices;
using System.Windows.Interop;

namespace Lior.Views;

public sealed class MpvHost : HwndHost
{
    private const int WsChild = 0x40000000;
    private const int WsVisible = 0x10000000;
    private const int WsClipSiblings = 0x04000000;
    private const int WsClipChildren = 0x02000000;

    private nint _windowHandle;

    public nint WindowHandle => _windowHandle;

    protected override HandleRef BuildWindowCore(HandleRef hwndParent)
    {
        _windowHandle = CreateWindowEx(
            0,
            "static",
            string.Empty,
            WsChild | WsVisible | WsClipSiblings | WsClipChildren,
            0,
            0,
            0,
            0,
            hwndParent.Handle,
            nint.Zero,
            nint.Zero,
            nint.Zero);

        if (_windowHandle == nint.Zero)
        {
            throw new InvalidOperationException("Failed to create the embedded mpv host window.");
        }

        return new HandleRef(this, _windowHandle);
    }

    protected override void DestroyWindowCore(HandleRef hwnd)
    {
        if (hwnd.Handle != nint.Zero)
        {
            DestroyWindow(hwnd.Handle);
        }

        _windowHandle = nint.Zero;
    }

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern nint CreateWindowEx(
        int exStyle,
        string className,
        string windowName,
        int style,
        int x,
        int y,
        int width,
        int height,
        nint parentHandle,
        nint menuHandle,
        nint instanceHandle,
        nint parameter);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DestroyWindow(nint handle);
}
