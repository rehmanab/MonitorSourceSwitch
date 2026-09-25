using System.Runtime.InteropServices;

namespace MonitorSourceSwitch;

/// <summary>
/// Minimal P/Invoke layer over NVIDIA's NVAPI (nvapi64.dll / nvapi.dll).
/// Function addresses are obtained through nvapi_QueryInterface, mirroring
/// the way nvapi64.lib resolves the exported entry points.
/// </summary>
internal static class NvApi
{
    public const int Ok = 0;
    public const int EndEnumeration = -7;

    public const int MaxPhysicalGpus = 64;
    public const int MaxDisplayHeads = 2;

    private const uint IdInitialize = 0x0150E828;
    private const uint IdEnumNvidiaDisplayHandle = 0x9ABDD40D;
    private const uint IdGetPhysicalGpusFromDisplay = 0x34EF9506;
    private const uint IdGetAssociatedDisplayOutputId = 0xD995937E;
    private const uint IdI2CWrite = 0xE812EB07;

    public static readonly InitializeDelegate Initialize;
    public static readonly EnumNvidiaDisplayHandleDelegate EnumNvidiaDisplayHandle;
    public static readonly GetPhysicalGpusFromDisplayDelegate GetPhysicalGpusFromDisplay;
    public static readonly GetAssociatedDisplayOutputIdDelegate GetAssociatedDisplayOutputId;
    public static readonly I2CWriteDelegate I2CWrite;

    static NvApi()
    {
        var module = TryLoadLibrary("nvapi64.dll") ?? TryLoadLibrary("nvapi.dll")
            ?? throw new DllNotFoundException("Could not load nvapi64.dll or nvapi.dll. An NVIDIA display driver is required.");

        var queryInterfacePtr = NativeLibrary.GetExport(module, "nvapi_QueryInterface");
        var queryInterface = Marshal.GetDelegateForFunctionPointer<QueryInterfaceDelegate>(queryInterfacePtr);

        Initialize = Resolve<InitializeDelegate>(queryInterface, IdInitialize);
        EnumNvidiaDisplayHandle = Resolve<EnumNvidiaDisplayHandleDelegate>(queryInterface, IdEnumNvidiaDisplayHandle);
        GetPhysicalGpusFromDisplay = Resolve<GetPhysicalGpusFromDisplayDelegate>(queryInterface, IdGetPhysicalGpusFromDisplay);
        GetAssociatedDisplayOutputId = Resolve<GetAssociatedDisplayOutputIdDelegate>(queryInterface, IdGetAssociatedDisplayOutputId);
        I2CWrite = Resolve<I2CWriteDelegate>(queryInterface, IdI2CWrite);
    }

    private static IntPtr? TryLoadLibrary(string libraryName)
    {
        try
        {
            return NativeLibrary.TryLoad(libraryName, out IntPtr handle) ? handle : null;
        }
        catch (Exception ex) when (ex is DllNotFoundException or BadImageFormatException)
        {
            return null;
        }
    }

    private static T Resolve<T>(QueryInterfaceDelegate queryInterface, uint functionId) where T : Delegate
    {
        var functionPtr = queryInterface(functionId);
		
        return functionPtr != IntPtr.Zero 
            ? Marshal.GetDelegateForFunctionPointer<T>(functionPtr) 
            : throw new MissingMethodException($"NVAPI function 0x{functionId:X8} is not available on this driver.");
    }

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate IntPtr QueryInterfaceDelegate(uint functionId);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    public delegate int InitializeDelegate();

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    public delegate int EnumNvidiaDisplayHandleDelegate(uint thisEnum, out IntPtr nvDisplayHandle);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    public delegate int GetPhysicalGpusFromDisplayDelegate(IntPtr nvDisplayHandle, [In, Out] IntPtr[] nvGpuHandles, out uint gpuCount);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    public delegate int GetAssociatedDisplayOutputIdDelegate(IntPtr nvDisplayHandle, out uint outputId);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    public delegate int I2CWriteDelegate(IntPtr hPhysicalGpu, ref I2CInfo i2CInfo);

    /// <summary>
    /// NV_I2C_INFO_V3 from nvapi.h.
    /// </summary>
    [StructLayout(LayoutKind.Sequential, Pack = 8)]
    public struct I2CInfo
    {
        public uint Version;
        public uint DisplayMask;
        public byte IsDdcPort;
        public byte I2cDevAddress;
        public IntPtr I2cRegAddress;
        public uint RegAddrSize;
        public IntPtr Data;
        public uint CbSize;
        public uint I2cSpeed;
        public int I2cSpeedKhz;
        public byte PortId;
        public uint IsPortIdSet;

        /// <summary>
        /// MAKE_NVAPI_VERSION(NV_I2C_INFO_V3, 3).
        /// </summary>
        public static uint NvapiVersion => (uint)(Marshal.SizeOf<I2CInfo>() | (3 << 16));
    }
}