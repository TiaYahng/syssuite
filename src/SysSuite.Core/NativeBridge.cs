using SysSuite.Core.Abstractions;
using SysSuite.Interop;

namespace SysSuite.Core;

public sealed class NativeBridge : INativeBridge
{
    public NativeBridgeResult GetVersion()
    {
        try
        {
            var version = NativeMethods.TryGetVersion();
            return version is null
                ? new NativeBridgeResult(false, string.Empty, "Native_Version returned an error code.")
                : new NativeBridgeResult(true, version, string.Empty);
        }
        catch (DllNotFoundException exception)
        {
            return new NativeBridgeResult(false, string.Empty, exception.Message);
        }
        catch (EntryPointNotFoundException exception)
        {
            return new NativeBridgeResult(false, string.Empty, exception.Message);
        }
    }
}
