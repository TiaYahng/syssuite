using System.Runtime.InteropServices;

namespace SysSuite.Interop;

/// <summary>
/// <c>NativeSmartInfo</c>（native_api.h）的托管镜像。
///
/// ⚠ 布局即 ABI：字段顺序、类型、大小必须与 C++ 结构体逐字节一致，
/// 任何修改都必须同步修订 docs/native-abi.md 并递增 NATIVE_ABI_VERSION。
/// 使用 <see cref="LayoutKind.Sequential"/> + <see cref="Pack"/> = 8 对齐 C++ 侧的 #pragma pack(8)。
/// </summary>
[StructLayout(LayoutKind.Sequential, Pack = 8, CharSet = CharSet.Unicode)]
internal unsafe struct NativeSmartInfo
{
    /// <summary>必须由托管侧填入 <c>sizeof(NativeSmartInfo)</c>，供原生侧做版本兼容写入。</summary>
    internal uint StructSize;

    internal uint IsAvailable;
    internal uint DriveNumber;
    internal uint HealthStatus;
    internal uint TemperatureCelsius;
    internal uint RemainingLifePercent;
    internal uint ReallocatedSectors;
    internal uint PendingSectors;
    internal uint UncorrectableErrors;
    internal ulong PowerOnHours;
    internal ulong PowerCycleCount;
    internal ulong TotalBytesWritten;
    internal uint AtaSmartAttributeCount;
    internal uint Reserved0;

    /// <summary>型号，L'\0' 结尾（含终止符最多 64 字符）。</summary>
    internal fixed char Model[64];

    /// <summary>序列号。</summary>
    internal fixed char Serial[64];

    /// <summary>固件版本。</summary>
    internal fixed char Firmware[16];

    internal static int SizeOf => sizeof(NativeSmartInfo);

    // 固定大小缓冲区只能通过指针访问；用一个显式 fixed 包裹整个读取过程
    internal string ReadModel() => ReadField(nameof(Model), 64);

    internal string ReadSerial() => ReadField(nameof(Serial), 64);

    internal string ReadFirmware() => ReadField(nameof(Firmware), 16);

    private string ReadField(string field, int capacity)
    {
        fixed (char* basePointer = Model)
        {
            var pointer = field switch
            {
                nameof(Serial) => basePointer + 64,
                nameof(Firmware) => basePointer + 128,
                _ => basePointer,
            };

            var length = 0;
            while (length < capacity && pointer[length] != '\0')
            {
                length++;
            }

            return length == 0 ? string.Empty : new string(pointer, 0, length);
        }
    }
}
