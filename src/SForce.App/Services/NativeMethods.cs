using System.Runtime.InteropServices;

namespace SForce.App.Services;

internal static partial class NativeMethods
{
    private const int HwndBroadcast = 0xffff;
    private const int WmFontChange = 0x001D;
    private const int WmSettingChange = 0x001A;
    private const uint SmtoAbortIfHung = 0x0002;

    [LibraryImport("gdi32.dll", EntryPoint = "AddFontResourceExW", StringMarshalling = StringMarshalling.Utf16)]
    internal static partial int AddFontResourceEx(string name, uint flags, nint reserved);

    [LibraryImport("user32.dll", EntryPoint = "SendMessageTimeoutW", StringMarshalling = StringMarshalling.Utf16)]
    private static partial nint SendMessageTimeout(nint window, uint message, nint wParam, string? lParam, uint flags, uint timeout, out nint result);

    [LibraryImport("user32.dll", EntryPoint = "SystemParametersInfoW", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool SystemParametersInfo(uint action, uint parameter, nint data, uint flags);

    internal static void BroadcastFontChange()
    {
        _ = SendMessageTimeout(HwndBroadcast, WmFontChange, nint.Zero, null, SmtoAbortIfHung, 1500, out _);
        _ = SendMessageTimeout(HwndBroadcast, WmSettingChange, nint.Zero, "WindowMetrics", SmtoAbortIfHung, 1500, out _);
    }
}
