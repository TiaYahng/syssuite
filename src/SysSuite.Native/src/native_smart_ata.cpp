// SysSuite.Native —— ATA/SATA SMART 读取与属性解析（T1.3）
//
// 只发送只读命令：SMART READ DATA (0xD0)。不写设备寄存器、不发 SMART ENABLE。
#include "native_smart_internal.h"

#include <winioctl.h>
#include <ntddscsi.h>

#include <cstring>

namespace syssuite::smart {

namespace {

constexpr UCHAR kSmartReadData = 0xD0;
constexpr int kAtaSmartDataSize = 512;

// ATA SMART 属性 ID
constexpr int kAttrReallocatedSectors = 5;
constexpr int kAttrPowerOnHours = 9;
constexpr int kAttrPowerCycleCount = 12;
constexpr int kAttrTemperature = 194;
constexpr int kAttrPendingSectors = 197;
constexpr int kAttrUncorrectable = 187;

#pragma pack(push, 1)
struct AtaSmartAttribute
{
    UCHAR id;
    USHORT flags;
    UCHAR current;
    UCHAR worst;
    UCHAR raw[6];
    UCHAR reserved;
};

struct AtaSmartData
{
    USHORT revision;
    AtaSmartAttribute attributes[30];
    UCHAR offlineDataCollectionStatus;
    UCHAR selfTestExecutionStatus;
    USHORT totalTime;
    UCHAR vendorSpecific[2];
};
#pragma pack(pop)

// 读取 ATA SMART 数据区（512 字节）。
//
// 使用裸缓冲区手写布局：SDK 的 *_WITH_BUFFERS 结构在不同 SDK 版本间字段顺序有差异，
// 手写可确保与驱动期望的 ATA_PASS_THROUGH_EX 完全一致。
bool ReadAtaSmartData(HANDLE device, UCHAR feature, void* output)
{
    struct Request
    {
        ATA_PASS_THROUGH_EX command;
        UCHAR data[kAtaSmartDataSize];
    } request{};

    request.command.Length = sizeof(ATA_PASS_THROUGH_EX);
    request.command.AtaFlags = ATA_FLAGS_DATA_IN;
    request.command.DataTransferLength = kAtaSmartDataSize;
    request.command.TimeOutValue = 5;
    request.command.DataBufferOffset = FIELD_OFFSET(Request, data);
    request.command.CurrentTaskFile[0] = feature;
    request.command.CurrentTaskFile[1] = 1;              // 每次传 1 个扇区
    request.command.CurrentTaskFile[2] = 1;              // 1 个扇区
    request.command.CurrentTaskFile[3] = 0;
    request.command.CurrentTaskFile[4] = 0;
    request.command.CurrentTaskFile[5] = 0;
    request.command.CurrentTaskFile[6] = 0x40 | 0x04;    // SMART 命令子集 + DRDY
    request.command.CurrentTaskFile[7] = 0xB0;           // ATA SMART 命令

    // 使用独立的数据缓冲区再整体拷贝回调用方。
    // 若直接让驱动写入 output，编译器会认为数据区偏移 512 越过了 output 起始处，
    // 从而对 output 指向的 368 字节的 NativeSmartInfo 误报 C4789 溢出。
    UCHAR data[kAtaSmartDataSize]{};

    DWORD returned = 0;
    const DWORD requestSize = static_cast<DWORD>(FIELD_OFFSET(Request, data) + kAtaSmartDataSize);
    if (!DeviceIoControl(device, IOCTL_ATA_PASS_THROUGH_DIRECT,
                         &request, requestSize,
                         &request, requestSize,
                         &returned, nullptr))
    {
        return false;
    }

    if (request.command.CurrentTaskFile[0] != 0)
    {
        return false;
    }

    std::memcpy(output, data, kAtaSmartDataSize);
    return true;
}

// 把 raw[6] 拼成 48 位小端整数（各厂商对高位编码不一致，按低位解释最稳妥）
ULONGLONG Raw48(const UCHAR (&raw)[6])
{
    return static_cast<ULONGLONG>(raw[0]) |
        (static_cast<ULONGLONG>(raw[1]) << 8) |
        (static_cast<ULONGLONG>(raw[2]) << 16) |
        (static_cast<ULONGLONG>(raw[3]) << 24) |
        (static_cast<ULONGLONG>(raw[4]) << 32) |
        (static_cast<ULONGLONG>(raw[5]) << 40);
}

}  // namespace

bool TryParseAtaSmart(HANDLE device, NativeSmartInfo& info)
{
    AtaSmartData data{};
    if (!ReadAtaSmartData(device, kSmartReadData, &data))
    {
        return false;
    }

    // 校验 SMART 数据结构修订号（0 与 0xFFFF 都是无效/未初始化）
    if (data.revision == 0 || data.revision == 0xFFFF)
    {
        return false;
    }

    info.ataSmartAttributeCount = 0;
    for (const auto& attribute : data.attributes)
    {
        if (attribute.id == 0 && attribute.current == 0)
        {
            continue;
        }

        info.ataSmartAttributeCount++;
        const ULONGLONG raw48 = Raw48(attribute.raw);

        // 约定：扇区类计数取低 32 位，时长/次数类取低 48 位
        switch (attribute.id)
        {
        case kAttrReallocatedSectors:
            info.reallocatedSectors = static_cast<uint32_t>(raw48 & 0xFFFFFFFFull);
            break;
        case kAttrPendingSectors:
            info.pendingSectors = static_cast<uint32_t>(raw48 & 0xFFFFFFFFull);
            break;
        case kAttrUncorrectable:
            info.uncorrectableErrors = static_cast<uint32_t>(raw48 & 0xFFFFFFFFull);
            break;
        case kAttrPowerOnHours:
            info.powerOnHours = raw48 & 0x0000FFFFFFFFFFFFull;
            break;
        case kAttrPowerCycleCount:
            info.powerCycleCount = raw48 & 0x0000FFFFFFFFFFFFull;
            break;
        case kAttrTemperature:
            // 属性 194 通常 raw[0] 为当前温度；部分硬盘放在 raw[1]
            if (attribute.raw[0] > 0 && attribute.raw[0] < 150)
            {
                info.temperatureCelsius = attribute.raw[0];
            }
            else if (attribute.raw[1] > 0 && attribute.raw[1] < 150)
            {
                info.temperatureCelsius = attribute.raw[1];
            }
            break;
        default:
            break;
        }
    }

    // ATA 侧没有统一的"剩余寿命"字段，用重映射/待处理/不可纠正三项判定健康度
    info.healthStatus = (info.reallocatedSectors == 0 && info.pendingSectors == 0 && info.uncorrectableErrors == 0)
        ? 1u
        : ((info.pendingSectors > 0 || info.uncorrectableErrors > 0) ? 3u : 2u);
    return true;
}

}  // namespace syssuite::smart
