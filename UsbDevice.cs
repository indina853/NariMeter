using System;
using LibUsbDotNet;
using LibUsbDotNet.Main;

namespace NariMeter;

public static class UsbDevice
{
    private const int VendorId       = 0x1532;
    private const int ProductId      = 0x051C;
    private const int Interface      = 5;
    private const int IdleThreshold  = 4;
    private const int ActiveThreshold = 4;

    private static readonly byte[] SetData = new byte[64]
    {
        0xFF, 0x0A, 0x00, 0xFD, 0x04, 0x12, 0xF1, 0x02, 0x05,
        0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,
        0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,
        0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,
        0,0,0,0,0,0,0
    };

    private static readonly byte[] Response = new byte[64];

    private static int  _idleCount    = 0;
    private static int  _activeCount  = 0;
    private static bool _initialized  = false;
    private static bool _poweredOn    = false;

    public static bool TryRead(out int millivolts, out bool poweredOn)
    {
        millivolts = 0;
        poweredOn  = false;

        LibUsbDotNet.UsbDevice? device = null;
        try
        {
            var finder = new UsbDeviceFinder(VendorId, ProductId);
            device = LibUsbDotNet.UsbDevice.OpenUsbDevice(finder);
            if (device == null) { Reset(); return false; }

            if (device is IUsbDevice wholeDevice)
                wholeDevice.ClaimInterface(Interface);

            var setupSet = new UsbSetupPacket(
                (byte)(UsbCtrlFlags.Direction_Out | UsbCtrlFlags.RequestType_Class | UsbCtrlFlags.Recipient_Interface),
                0x09, 0x03FF, (short)Interface, (short)SetData.Length);
            device.ControlTransfer(ref setupSet, SetData, SetData.Length, out _);

            var setupGet = new UsbSetupPacket(
                (byte)(UsbCtrlFlags.Direction_In | UsbCtrlFlags.RequestType_Class | UsbCtrlFlags.Recipient_Interface),
                0x01, 0x03FF, (short)Interface, 64);

            bool ok = device.ControlTransfer(ref setupGet, Response, 64, out int transferred);
            if (!ok || transferred < 14) return false;

            millivolts = (Response[12] << 8) | Response[13];

            if (!_initialized)
            {
                if (millivolts == 0) return false;
                _initialized = true;
            }

            bool isIdle = Response[1] == 0x01 && Response[2] == 0x00;

            if (isIdle)
            {
                _activeCount = 0;
                _idleCount++;
                if (_idleCount >= IdleThreshold)
                    _poweredOn = false;
            }
            else
            {
                _idleCount = 0;
                _activeCount++;
                if (_activeCount >= ActiveThreshold)
                    _poweredOn = true;
            }

            poweredOn = _poweredOn;
            return true;
        }
        catch { return false; }
        finally
        {
            try
            {
                if (device is IUsbDevice wd) wd.ReleaseInterface(Interface);
                device?.Close();
                LibUsbDotNet.UsbDevice.Exit();
            }
            catch { }
        }
    }

    private static void Reset()
    {
        _idleCount   = 0;
        _activeCount = 0;
        _initialized = false;
        _poweredOn   = false;
    }
}
