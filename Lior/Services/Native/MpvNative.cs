using System.Runtime.InteropServices;

namespace Lior.Services.Native;

internal static class MpvNative
{
    private const string DllName = "mpv-2.dll";

    [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
    internal static extern nint mpv_create();

    [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
    internal static extern int mpv_initialize(nint handle);

    [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
    internal static extern void mpv_terminate_destroy(nint handle);

    [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
    internal static extern int mpv_set_option_string(nint handle, nint name, nint value);

    [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
    internal static extern int mpv_command(nint handle, nint args);

    [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
    internal static extern int mpv_get_property(nint handle, nint name, int format, nint data);

    [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
    internal static extern int mpv_set_property(nint handle, nint name, int format, nint data);
}
