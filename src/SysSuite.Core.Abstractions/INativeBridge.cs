namespace SysSuite.Core.Abstractions;

public interface INativeBridge
{
    NativeBridgeResult GetVersion();
}

public readonly record struct NativeBridgeResult(bool Success, string Version, string ErrorMessage);
