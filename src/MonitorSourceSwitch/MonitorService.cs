using System.Runtime.InteropServices;

namespace MonitorSourceSwitch;

public static class MonitorService
{
    public static int Run(int displayIndex, byte inputValue, byte commandCode, byte registerAddress)
    {
        // Initialize NVAPI.
        var status = NvApi.Initialize();
        if (status != NvApi.Ok)
        {
            Console.Error.WriteLine($"NvAPI_Initialize() failed with status {status}");
            return 1;
        }

        // Enumerate display handles.
        var displayHandles = new IntPtr[NvApi.MaxPhysicalGpus * NvApi.MaxDisplayHeads];
        status = NvApi.Ok;
        for (uint i = 0; status == NvApi.Ok; i++)
        {
            status = NvApi.EnumNvidiaDisplayHandle(i, out IntPtr displayHandle);

            if (status == NvApi.Ok)
            {
                displayHandles[i] = displayHandle;
            }
            else if (status != NvApi.EndEnumeration)
            {
                Console.Error.WriteLine($"NvAPI_EnumNvidiaDisplayHandle() failed with status {status}");
                return 1;
            }
        }

        if (displayIndex < 0 || displayIndex >= displayHandles.Length || displayHandles[displayIndex] == IntPtr.Zero)
        {
            Console.Error.WriteLine($"Display index {displayIndex} is not a valid NVIDIA display.");
            return 1;
        }

        var hDisplay = displayHandles[displayIndex];

        // Get GPU id associated with display ID.
        var gpuHandles = new IntPtr[NvApi.MaxPhysicalGpus];
        status = NvApi.GetPhysicalGpusFromDisplay(hDisplay, gpuHandles, out uint gpuCount);
        if (status != NvApi.Ok)
        {
            Console.Error.WriteLine($"NvAPI_GetPhysicalGPUsFromDisplay() failed with status {status}");
            return 1;
        }

        var hGpu = gpuHandles[0];

        // Get the display id for subsequent I2C calls via NVAPI.
        status = NvApi.GetAssociatedDisplayOutputId(hDisplay, out uint outputId);
        if (status != NvApi.Ok)
        {
            Console.Error.WriteLine($"NvAPI_GetAssociatedDisplayOutputId() failed with status {status}");
            return 1;
        }

        if (!WriteValueToMonitor(hGpu, outputId, inputValue, commandCode, registerAddress))
        {
            Console.Error.WriteLine("Changing input failed");
            return 1;
        }

        return 0;
    }

    /// <summary>
    /// Writes the input value to the display over the I2C bus by issuing the
    /// commands and data expected by the monitor (VCP-style write).
    /// </summary>
    private static bool WriteValueToMonitor(IntPtr hPhysicalGpu, uint displayId, byte inputValue, byte commandCode, byte registerAddress)
    {
        // The 7-bit I2C address for the display is 0x37; since I2C addresses are
        // always sent as 8 bits, it is shifted left and the LSB holds the
        // read/write flag (write = 0, read = 1).
        const byte i2CDeviceAddress = 0x37;
        const byte i2CWriteDeviceAddress = i2CDeviceAddress << 1; // 0x6E

        // Packet bytes:
        // 0x6E - i2cWriteDeviceAddr
        // 0x?? - register_address
        // 0x84 - 0x80 OR n where n = 4 bytes for "modify a value" request
        // 0x03 - change a value flag
        // 0x?? - command_code
        // 0x00 - input_value high byte
        // 0x?? - input_value low byte
        // 0x?? - checksum, xor'ing all the above bytes
        byte[] registerAddr = [registerAddress];
        byte[] modifyBytes = [0x84, 0x03, commandCode, 0x00, inputValue, 0xDD];

        var regAddressPtr = Marshal.AllocHGlobal(registerAddr.Length);
        var dataPtr = Marshal.AllocHGlobal(modifyBytes.Length);
        try
        {
            Marshal.Copy(registerAddr, 0, regAddressPtr, registerAddr.Length);
            Marshal.Copy(modifyBytes, 0, dataPtr, modifyBytes.Length);

            var i2CInfo = new NvApi.I2CInfo
            {
                Version = NvApi.I2CInfo.NvapiVersion,
                DisplayMask = displayId,
                IsDdcPort = 1,
                I2cDevAddress = i2CWriteDeviceAddress,
                I2cRegAddress = regAddressPtr,
                RegAddrSize = (uint)registerAddr.Length,
                Data = dataPtr,
                CbSize = (uint)modifyBytes.Length,
                // The original code used the pre-V3 semantics (i2cSpeed = 27 kHz).
                // In V3 the field is deprecated and the speed is selected with
                // I2cSpeedKhz; 0 = NVAPI_I2C_SPEED_DEFAULT (use current setting).
                I2cSpeed = 0xFFFF,
                I2cSpeedKhz = 0,
                PortId = 0,
                IsPortIdSet = 0,
            };

            CalculateI2CChecksum(ref i2CInfo);

            var status = NvApi.I2CWrite(hPhysicalGpu, ref i2CInfo);

            if (status == NvApi.Ok) return true;
			
            Console.Error.WriteLine($"  NvAPI_I2CWrite (revise brightness) failed with status {status}");
			
            return false;
        }
        finally
        {
            Marshal.FreeHGlobal(regAddressPtr);
            Marshal.FreeHGlobal(dataPtr);
        }
    }

    /// <summary>
    /// Calculates the (XOR) checksum of the I2C packet and places the value
    /// into the last byte of the packet. The checksum is the result of XOR'ing
    /// the device address, the register address bytes and all data bytes except
    /// the last byte (which holds the checksum itself).
    /// </summary>
    private static void CalculateI2CChecksum(ref NvApi.I2CInfo i2CInfo)
    {
        var checksum = i2CInfo.I2cDevAddress;

        for (uint i = 0; i < i2CInfo.RegAddrSize; i++)
        {
            checksum ^= Marshal.ReadByte(i2CInfo.I2cRegAddress, (int)i);
        }

        for (uint i = 0; i < i2CInfo.CbSize - 1; i++)
        {
            checksum ^= Marshal.ReadByte(i2CInfo.Data, (int)i);
        }

        Marshal.WriteByte(i2CInfo.Data, (int)i2CInfo.CbSize - 1, checksum);
    }

    public static byte ParseHexByte(string value)
    {
        var hex = value.StartsWith("0x", StringComparison.OrdinalIgnoreCase) ? value[2..] : value;
		
        return (byte)Convert.ToInt64(hex, 16);
    }
}