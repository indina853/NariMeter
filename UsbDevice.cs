using System.Runtime.InteropServices;

namespace NariMeter;

public static class UsbDevice
{
    public  const string DeviceName = "Razer Nari";
    public  const string HardwareId = "VID_1532&PID_051C";

    private const int IdleThreshold   = 4;
    private const int ActiveThreshold = 4;

    private const int ErrorInsufficientBuffer = 122;
    private const int DevicePathOffset        = 4;

    private static readonly byte[] SetData = new byte[64]
    {
        0xFF, 0x0A, 0x00, 0xFD, 0x04, 0x12, 0xF1, 0x02, 0x05,
        0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,
        0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,
        0,0,0,0,0,0,0
    };

    private static readonly byte[] Response = new byte[64];

    private static IntPtr _deviceHandle = IntPtr.Zero;

    private static int  _idleCount   = 0;
    private static int  _activeCount = 0;
    private static bool _initialized = false;
    private static bool _poweredOn   = false;

    public static bool TransitionPending =>
        _poweredOn ? _idleCount > 0 : _activeCount > 0;

    public static bool TryRead(out int millivolts, out bool poweredOn, out bool isCharging, out bool isFullyCharged, out int percent)
    {
        millivolts     = 0;
        poweredOn      = false;
        isCharging     = false;
        isFullyCharged = false;
        percent        = 0;

        try
        {
            if (!EnsureOpen()) return false;

            Response[0] = 0xFF;

            if (!HidD_SetFeature(_deviceHandle, SetData, (uint)SetData.Length) ||
                !HidD_GetFeature(_deviceHandle, Response, (uint)Response.Length))
            {
                CloseDevice();
                return false;
            }

            if (Response[0] != 0xFF)
            {
                CloseDevice();
                return false;
            }

            millivolts     = (Response[12] << 8) | Response[13];
            percent        = Response[14];
            isCharging     = Response[9] == 0x05;
            isFullyCharged = Response[9] == 0x06;

            if (!_initialized)
            {
                if (millivolts == 0 && !isCharging && !isFullyCharged) return false;
                _initialized = true;
            }

            bool isIdle = Response[1] == 0x01 && Response[2] == 0x00;

            if (isIdle)
            {
                _activeCount = 0;
                if (++_idleCount >= IdleThreshold)
                    _poweredOn = false;
            }
            else
            {
                _idleCount = 0;
                if (++_activeCount >= ActiveThreshold)
                    _poweredOn = true;
            }

            poweredOn = _poweredOn;
            return true;
        }
        catch
        {
            CloseDevice();
            return false;
        }
    }

    private static bool EnsureOpen()
    {
        if (_deviceHandle != IntPtr.Zero) return true;

        CloseDevice();
        _deviceHandle = OpenNariDevice();
        return _deviceHandle != IntPtr.Zero;
    }

    private static IntPtr OpenNariDevice()
    {
        Guid hidGuid = HidGuid;
        IntPtr infoSet = SetupDiGetClassDevs(ref hidGuid, IntPtr.Zero, IntPtr.Zero, DigcfPresent | DigcfDeviceInterface);
        if (infoSet == InvalidHandleValue) return IntPtr.Zero;

        try
        {
            var ifaceData = new SP_DEVICE_INTERFACE_DATA { cbSize = (uint)Marshal.SizeOf<SP_DEVICE_INTERFACE_DATA>() };

            for (uint index = 0; SetupDiEnumDeviceInterfaces(infoSet, IntPtr.Zero, ref hidGuid, index, ref ifaceData); index++)
            {
                if (!SetupDiGetDeviceInterfaceDetail(infoSet, ref ifaceData, IntPtr.Zero, 0, out uint required, IntPtr.Zero))
                {
                    if (Marshal.GetLastWin32Error() != ErrorInsufficientBuffer)
                        continue;
                }

                IntPtr detail = Marshal.AllocHGlobal((int)required);
                try
                {
                    Marshal.WriteInt32(detail, (int)DetailDataSize);
                    if (!SetupDiGetDeviceInterfaceDetail(infoSet, ref ifaceData, detail, required, out _, IntPtr.Zero))
                        continue;

                    string? path = Marshal.PtrToStringAuto((IntPtr)(detail.ToInt64() + DevicePathOffset));
                    if (string.IsNullOrEmpty(path)) continue;
                    if (!path.Contains("vid_1532&pid_051c", StringComparison.OrdinalIgnoreCase)) continue;
                    if (!path.Contains("mi_05", StringComparison.OrdinalIgnoreCase)) continue;
                    if (!path.Contains("col03", StringComparison.OrdinalIgnoreCase)) continue;

                    IntPtr handle = CreateFile(path, GenericRead | GenericWrite, FileShareRead | FileShareWrite,
                                               IntPtr.Zero, OpenExisting, 0, IntPtr.Zero);
                    if (handle == InvalidHandleValue) continue;

                    var attrs = new HIDD_ATTRIBUTES { Size = (uint)Marshal.SizeOf<HIDD_ATTRIBUTES>() };
                    if (HidD_GetAttributes(handle, ref attrs) &&
                        attrs.VendorID == 0x1532 && attrs.ProductID == 0x051C)
                    {
                        return handle;
                    }

                    CloseHandle(handle);
                }
                finally
                {
                    Marshal.FreeHGlobal(detail);
                }
            }

            return IntPtr.Zero;
        }
        finally
        {
            SetupDiDestroyDeviceInfoList(infoSet);
        }
    }

    public static void CloseDevice()
    {
        if (_deviceHandle == IntPtr.Zero) return;

        CloseHandle(_deviceHandle);
        _deviceHandle = IntPtr.Zero;
    }

    public static void Reset()
    {
        _idleCount   = 0;
        _activeCount = 0;
        _initialized = false;
        _poweredOn   = false;
    }

    private const uint GenericRead   = 0x80000000;
    private const uint GenericWrite  = 0x40000000;
    private const uint FileShareRead = 0x00000001;
    private const uint FileShareWrite = 0x00000002;
    private const uint OpenExisting  = 3;
    private const uint DigcfPresent  = 0x00000002;
    private const uint DigcfDeviceInterface = 0x00000010;

    private static readonly IntPtr InvalidHandleValue = new(-1);

    private static readonly Guid HidGuid = new("4D1E55B2-F16F-11CF-88CB-001111000030");

    private static uint DetailDataSize => (uint)(IntPtr.Size == 8 ? 8 : 4);

    [StructLayout(LayoutKind.Sequential)]
    private struct HIDD_ATTRIBUTES
    {
        public uint Size;
        public ushort VendorID;
        public ushort ProductID;
        public ushort VersionNumber;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct SP_DEVICE_INTERFACE_DATA
    {
        public uint cbSize;
        public Guid InterfaceClassGuid;
        public uint Flags;
        public IntPtr Reserved;
    }

    [DllImport("setupapi.dll", CharSet = CharSet.Auto, SetLastError = true)]
    private static extern IntPtr SetupDiGetClassDevs(ref Guid classGuid, IntPtr enumerator, IntPtr hwndParent, uint flags);

    [DllImport("setupapi.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetupDiEnumDeviceInterfaces(IntPtr deviceInfoSet, IntPtr deviceInfoData, ref Guid interfaceClassGuid, uint memberIndex, ref SP_DEVICE_INTERFACE_DATA deviceInterfaceData);

    [DllImport("setupapi.dll", CharSet = CharSet.Auto, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetupDiGetDeviceInterfaceDetail(IntPtr deviceInfoSet, ref SP_DEVICE_INTERFACE_DATA deviceInterfaceData, IntPtr deviceInterfaceDetailData, uint deviceInterfaceDetailDataSize, out uint requiredSize, IntPtr deviceInfoData);

    [DllImport("setupapi.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetupDiDestroyDeviceInfoList(IntPtr deviceInfoSet);

    [DllImport("kernel32.dll", CharSet = CharSet.Auto, SetLastError = true)]
    private static extern IntPtr CreateFile(string fileName, uint desiredAccess, uint shareMode, IntPtr securityAttributes, uint creationDisposition, uint flagsAndAttributes, IntPtr templateFile);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CloseHandle(IntPtr handle);

    [DllImport("hid.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool HidD_GetAttributes(IntPtr hidDeviceObject, ref HIDD_ATTRIBUTES attributes);

    [DllImport("hid.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool HidD_SetFeature(IntPtr hidDeviceObject, byte[] reportBuffer, uint reportBufferLength);

    [DllImport("hid.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool HidD_GetFeature(IntPtr hidDeviceObject, byte[] reportBuffer, uint reportBufferLength);
}
