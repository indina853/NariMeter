using System;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace NariMeter;

public sealed class DeviceNotifier : NativeWindow, IDisposable
{
    private const int WM_DEVICECHANGE           = 0x0219;
    private const int DBT_DEVICEARRIVAL         = 0x8000;
    private const int DBT_DEVICEREMOVECOMPLETE  = 0x8004;
    private const int DBT_DEVTYP_DEVICEINTERFACE = 5;
    private const int DEVICE_NOTIFY_WINDOW_HANDLE = 0;

    private static readonly Guid GuidDevInterfaceUsbDevice =
        new("A5DCBF10-6530-11D2-901F-00C04FB951ED");

    [StructLayout(LayoutKind.Sequential)]
    private struct DevBroadcastDeviceInterface
    {
        public int  Size;
        public int  DeviceType;
        public int  Reserved;
        public Guid ClassGuid;
        public short Name;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct DevBroadcastDeviceInterfaceData
    {
        public int  Size;
        public int  DeviceType;
        public int  Reserved;
        public Guid ClassGuid;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)]
        public string Name;
    }

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr RegisterDeviceNotification(IntPtr recipient, IntPtr filter, int flags);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool UnregisterDeviceNotification(IntPtr handle);

    private IntPtr _notificationHandle;

    public event EventHandler? DeviceArrived;
    public event EventHandler? DeviceRemoved;

    public DeviceNotifier()
    {
        CreateHandle(new CreateParams());
        Register();
    }

    private void Register()
    {
        var filter = new DevBroadcastDeviceInterface
        {
            Size       = Marshal.SizeOf<DevBroadcastDeviceInterface>(),
            DeviceType = DBT_DEVTYP_DEVICEINTERFACE,
            ClassGuid  = GuidDevInterfaceUsbDevice
        };

        IntPtr buffer = Marshal.AllocHGlobal(filter.Size);
        try
        {
            Marshal.StructureToPtr(filter, buffer, false);
            _notificationHandle = RegisterDeviceNotification(Handle, buffer, DEVICE_NOTIFY_WINDOW_HANDLE);
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }
    }

    protected override void WndProc(ref Message m)
    {
        if (m.Msg == WM_DEVICECHANGE && m.LParam != IntPtr.Zero)
        {
            int eventType = m.WParam.ToInt32();
            if (eventType is DBT_DEVICEARRIVAL or DBT_DEVICEREMOVECOMPLETE)
            {
                var data = Marshal.PtrToStructure<DevBroadcastDeviceInterfaceData>(m.LParam);
                if (data.DeviceType == DBT_DEVTYP_DEVICEINTERFACE &&
                    data.Name.Contains(UsbDevice.HardwareId, StringComparison.OrdinalIgnoreCase))
                {
                    if (eventType == DBT_DEVICEARRIVAL) DeviceArrived?.Invoke(this, EventArgs.Empty);
                    else DeviceRemoved?.Invoke(this, EventArgs.Empty);
                }
            }
        }
        base.WndProc(ref m);
    }

    public void Dispose()
    {
        if (_notificationHandle != IntPtr.Zero)
        {
            UnregisterDeviceNotification(_notificationHandle);
            _notificationHandle = IntPtr.Zero;
        }
        DestroyHandle();
    }
}
