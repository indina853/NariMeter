using LibUsbDotNet;
using LibUsbDotNet.Main;

namespace NariMeter;

public static class UsbDevice
{
    public  const string DeviceName      = "Razer Nari";
    public  const string HardwareId      = "VID_1532&PID_051C";
    private const int    VendorId        = 0x1532;
    private const int    ProductId       = 0x051C;
    private const int    Interface       = 5;
    private const int    IdleThreshold   = 4;
    private const int    ActiveThreshold = 4;

    private static readonly byte[] SetData =
    {
        0xFF, 0x0A, 0x00, 0xFD, 0x04, 0x12, 0xF1, 0x02, 0x05,
        0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00,
        0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00,
        0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00,
        0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00
    };

    private static readonly byte[] Response = new byte[64];

    private static LibUsbDotNet.UsbDevice? _device;

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

            var setupSet = new UsbSetupPacket(
                (byte)(UsbCtrlFlags.Direction_Out | UsbCtrlFlags.RequestType_Class | UsbCtrlFlags.Recipient_Interface),
                0x09, 0x03FF, (short)Interface, (short)SetData.Length);
            _device!.ControlTransfer(ref setupSet, SetData, SetData.Length, out _);

            var setupGet = new UsbSetupPacket(
                (byte)(UsbCtrlFlags.Direction_In | UsbCtrlFlags.RequestType_Class | UsbCtrlFlags.Recipient_Interface),
                0x01, 0x03FF, (short)Interface, 64);

            bool ok = _device.ControlTransfer(ref setupGet, Response, Response.Length, out int transferred);
            if (!ok || transferred < 15)
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
        if (_device is { IsOpen: true }) return true;

        CloseDevice();

        var finder = new UsbDeviceFinder(VendorId, ProductId);
        _device = LibUsbDotNet.UsbDevice.OpenUsbDevice(finder);
        if (_device == null) return false;

        if (_device is IUsbDevice wholeDevice)
            wholeDevice.ClaimInterface(Interface);

        return true;
    }

    public static void CloseDevice()
    {
        if (_device == null) return;

        try
        {
            if (_device is IUsbDevice wd) wd.ReleaseInterface(Interface);
            _device.Close();
            LibUsbDotNet.UsbDevice.Exit();
        }
        catch { }

        _device = null;
    }

    public static void Reset()
    {
        _idleCount   = 0;
        _activeCount = 0;
        _initialized = false;
        _poweredOn   = false;
    }
}
