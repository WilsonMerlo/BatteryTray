using System.ComponentModel;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace BatteryTray.Core.Hid;

/// <summary>
/// Canal mínimo para enviar/recibir feature reports por la API HID de Windows.
///
/// Se abre el dispositivo con acceso 0 (ni lectura ni escritura): alcanza para
/// HidD_SetFeature/HidD_GetFeature y funciona aunque Windows tenga tomada en exclusiva
/// la colección del mouse o del teclado. No lee el input, así que no compite con Synapse
/// ni con el sistema por los clics y las teclas.
/// </summary>
internal sealed class HidFeatureChannel : IDisposable
{
    const uint FileShareRead = 0x1;
    const uint FileShareWrite = 0x2;
    const uint OpenExisting = 3;

    readonly SafeFileHandle _handle;

    HidFeatureChannel(SafeFileHandle handle) => _handle = handle;

    public static HidFeatureChannel Open(string devicePath)
    {
        var handle = CreateFileW(devicePath, 0, FileShareRead | FileShareWrite, IntPtr.Zero, OpenExisting, 0, IntPtr.Zero);
        if (handle.IsInvalid)
            throw new Win32Exception(Marshal.GetLastWin32Error(), $"No se pudo abrir {devicePath}");
        return new HidFeatureChannel(handle);
    }

    /// <summary>buffer[0] es el Report ID.</summary>
    public void SetFeature(byte[] buffer)
    {
        if (!HidD_SetFeature(_handle, buffer, buffer.Length))
            throw new Win32Exception(Marshal.GetLastWin32Error(), "HidD_SetFeature falló");
    }

    /// <summary>buffer[0] debe traer el Report ID pedido; Windows completa el resto.</summary>
    public void GetFeature(byte[] buffer)
    {
        if (!HidD_GetFeature(_handle, buffer, buffer.Length))
            throw new Win32Exception(Marshal.GetLastWin32Error(), "HidD_GetFeature falló");
    }

    public void Dispose() => _handle.Dispose();

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    static extern SafeFileHandle CreateFileW(string fileName, uint desiredAccess, uint shareMode,
        IntPtr securityAttributes, uint creationDisposition, uint flagsAndAttributes, IntPtr templateFile);

    [DllImport("hid.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.U1)]
    static extern bool HidD_SetFeature(SafeFileHandle device, byte[] buffer, int length);

    [DllImport("hid.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.U1)]
    static extern bool HidD_GetFeature(SafeFileHandle device, byte[] buffer, int length);
}
