using System.ComponentModel;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace G502Lite.Device;

public record HidInterface(string Path, ushort Vendor, ushort Product, ushort UsagePage, ushort Usage, ushort InputLength, ushort OutputLength);

internal static class NativeHid
{
    [StructLayout(LayoutKind.Sequential)] private struct DeviceInterfaceData { public uint Size; public Guid ClassGuid; public uint Flags; public UIntPtr Reserved; }
    [StructLayout(LayoutKind.Sequential)] private struct Attributes { public uint Size; public ushort Vendor; public ushort Product; public ushort Version; }
    [StructLayout(LayoutKind.Sequential)] private struct Caps
    {
        public ushort Usage, UsagePage, InputReportByteLength, OutputReportByteLength, FeatureReportByteLength;
        [MarshalAs(UnmanagedType.ByValArray, SizeConst=17)] public ushort[] Reserved;
        public ushort NumberLinkCollectionNodes, NumberInputButtonCaps, NumberInputValueCaps, NumberInputDataIndices, NumberOutputButtonCaps, NumberOutputValueCaps, NumberOutputDataIndices, NumberFeatureButtonCaps, NumberFeatureValueCaps, NumberFeatureDataIndices;
    }
    [DllImport("hid.dll")] private static extern void HidD_GetHidGuid(out Guid guid);
    [DllImport("hid.dll", SetLastError=true)] [return:MarshalAs(UnmanagedType.U1)] private static extern bool HidD_GetAttributes(SafeFileHandle handle, ref Attributes attrs);
    [DllImport("hid.dll")] [return:MarshalAs(UnmanagedType.U1)] private static extern bool HidD_GetPreparsedData(SafeFileHandle handle, out IntPtr data);
    [DllImport("hid.dll")] [return:MarshalAs(UnmanagedType.U1)] private static extern bool HidD_FreePreparsedData(IntPtr data);
    [DllImport("hid.dll")] private static extern int HidP_GetCaps(IntPtr data, out Caps caps);
    [DllImport("setupapi.dll", SetLastError=true, CharSet=CharSet.Unicode)] private static extern IntPtr SetupDiGetClassDevs(ref Guid guid, string? enumerator, IntPtr hwnd, uint flags);
    [DllImport("setupapi.dll", SetLastError=true)] private static extern bool SetupDiEnumDeviceInterfaces(IntPtr set, IntPtr info, ref Guid guid, uint index, ref DeviceInterfaceData data);
    [DllImport("setupapi.dll", EntryPoint="SetupDiGetDeviceInterfaceDetailW", SetLastError=true, CharSet=CharSet.Unicode)] private static extern bool SetupDiGetDeviceInterfaceDetail(IntPtr set, ref DeviceInterfaceData data, IntPtr detail, uint length, out uint required, IntPtr info);
    [DllImport("setupapi.dll")] private static extern bool SetupDiDestroyDeviceInfoList(IntPtr set);
    [DllImport("kernel32.dll", SetLastError=true, CharSet=CharSet.Unicode)] private static extern SafeFileHandle CreateFile(string path, uint access, uint share, IntPtr security, uint creation, uint flags, IntPtr template);

    public static IReadOnlyList<HidInterface> EnumerateG502()
    {
        var result = new List<HidInterface>();
        HidD_GetHidGuid(out var guid);
        var set = SetupDiGetClassDevs(ref guid, null, IntPtr.Zero, 0x12);
        if (set == new IntPtr(-1)) throw new Win32Exception(Marshal.GetLastWin32Error());
        try
        {
            for (uint index=0;;index++)
            {
                var data = new DeviceInterfaceData { Size=(uint)Marshal.SizeOf<DeviceInterfaceData>() };
                if (!SetupDiEnumDeviceInterfaces(set, IntPtr.Zero, ref guid, index, ref data))
                {
                    var error = Marshal.GetLastWin32Error();
                    if (error == 259) break;
                    throw new Win32Exception(error);
                }
                SetupDiGetDeviceInterfaceDetail(set, ref data, IntPtr.Zero, 0, out var required, IntPtr.Zero);
                var ptr = Marshal.AllocHGlobal((int)required);
                try
                {
                    Marshal.WriteInt32(ptr, IntPtr.Size==8?8:6);
                    if (!SetupDiGetDeviceInterfaceDetail(set, ref data, ptr, required, out _, IntPtr.Zero)) continue;
                    var path = Marshal.PtrToStringUni(ptr+4);
                    if (path is null || !path.Contains("vid_046d&pid_c08b", StringComparison.OrdinalIgnoreCase)) continue;
                    using var handle=CreateFile(path,0,3,IntPtr.Zero,3,0,IntPtr.Zero);
                    if(handle.IsInvalid) continue;
                    var attr=new Attributes { Size=(uint)Marshal.SizeOf<Attributes>() };
                    if(!HidD_GetAttributes(handle,ref attr)||attr.Vendor!=0x046d||attr.Product!=0xc08b) continue;
                    if(!HidD_GetPreparsedData(handle,out var pp)) continue;
                    try { if(HidP_GetCaps(pp,out var caps)>=0) result.Add(new(path,attr.Vendor,attr.Product,caps.UsagePage,caps.Usage,caps.InputReportByteLength,caps.OutputReportByteLength)); }
                    finally { HidD_FreePreparsedData(pp); }
                }
                finally { Marshal.FreeHGlobal(ptr); }
            }
        }
        finally { SetupDiDestroyDeviceInfoList(set); }
        return result;
    }

    public static FileStream Open(HidInterface endpoint)
    {
        var handle=CreateFile(endpoint.Path,0xc0000000,3,IntPtr.Zero,3,0x40000000,IntPtr.Zero);
        if(handle.IsInvalid) { var error=Marshal.GetLastWin32Error();handle.Dispose();throw new Win32Exception(error,"No se pudo abrir la interfaz HID del G502."); }
        // HID is a full-duplex message device. FileStream buffering serializes a
        // pending read with writes; disabling it lets requests wake the reader.
        try { return new FileStream(handle,FileAccess.ReadWrite,1,true); }
        catch { handle.Dispose();throw; }
    }
}
