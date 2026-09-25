using System.Diagnostics;
using System.Globalization;
using MonitorSourceSwitch;

// LG UltraGear evo 45GX950B
// use on Windows to check which monitors you have, a popup is displayed:  mstsc /l
// LG UltraGear evo 45GX950B have to have extended display first

if (args.Length is 0)
{
	Console.Error.WriteLine("No arguments provided!");
	Console.WriteLine("Usage: MonitorSourceSwitch.exe <inputName> [<displayswitch> (true || false)]");
    return 1;
}

var inputParameters = args[0].ToLowerInvariant() switch
{
	"hdmi1" => ["1", "0x90", "0xF4", "0x50"],
	"hdmi2" => ["1", "0x91", "0xF4", "0x50"],
	"dp" => ["1", "0xD0", "0xF4", "0x50"],
	"usbc" => ["1", "0xD1", "0xF4", "0x50"],
	_ => args
};

try
{
	const string @internal = "internal";
	const string extend = "extend";
	string? displaySwitchMode = null;

	if (args.Length > 1)
	{
		displaySwitchMode = bool.TryParse(args[1], out var extendOut) && extendOut
			? extend
			: @internal;
	}
	
	// Extend first if arg was passed as true, then wait for the monitor to be ready, otherwise the I2C write will fail
	if(displaySwitchMode is extend)
	{
		await ExtendInternalDisplayAsync(displaySwitchMode);
		await Task.Delay(1000);
	}
	
	var displayIndex = int.Parse(inputParameters[0], CultureInfo.InvariantCulture);
	var inputValue = MonitorService.ParseHexByte(inputParameters[1]);
	var commandCode = MonitorService.ParseHexByte(inputParameters[2]);
	var registerAddress = inputParameters.Length == 4 ? MonitorService.ParseHexByte(inputParameters[3]) : (byte)0x51;

	var result = MonitorService.Run(displayIndex, inputValue, commandCode, registerAddress);
	
	// make display internal last if arg was passed as false
	if(displaySwitchMode is @internal)
	{
		await Task.Delay(1000);
		await ExtendInternalDisplayAsync(displaySwitchMode);
	}
	
	Console.WriteLine();
	
	return result;
}
catch (Exception ex)
{
    var cause = ex is TypeInitializationException { InnerException: not null } tie
        ? tie.InnerException!
        : ex;

    switch (cause)
    {
	    case FormatException or OverflowException:
		    Console.Error.WriteLine("Invalid argument(s)!");
		    break;
	    case DllNotFoundException or BadImageFormatException or MissingMethodException or EntryPointNotFoundException:
		    Console.Error.WriteLine(cause.Message);
		    break;
	    default:
		    throw;
    }

    return 1;
}

static async Task ExtendInternalDisplayAsync(string mode)
{
	var startInfo = new ProcessStartInfo("displayswitch", $"/{mode}")
	{
		UseShellExecute = false,
		CreateNoWindow = true
	};

	using var process = Process.Start(startInfo);
	if (process is not null)
	{
		await process.WaitForExitAsync();
	}
}