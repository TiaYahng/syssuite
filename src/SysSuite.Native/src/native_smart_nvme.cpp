// SysSuite.Native —— NVMe SMART/Health Information Log 读取（T1.3）
//
// 走 IOCTL_STORAGE_QUERY_PROPERTY(StorageDeviceProtocolSpecificProperty)，
// 只读日志页 0x02，不写任何 NVMe 寄存器或 feature。
//
// 缓冲区布局（来自 ntddstor.h 的明确约定 + 实测踩坑）：
//
//   输入 = STORAGE_PROPERTY_QUERY，其 AdditionalParameters 字段处放
//          STORAGE_PROTOCOL_SPECIFIC_DATA，数据区紧跟其后。
//
//   ⚠ 协议数据的放置偏移必须用 `offsetof(STORAGE_PROPERTY_QUERY, AdditionalParameters)`（= 8），
//     **不能**用 `sizeof(STORAGE_PROPERTY_QUERY)`（= 12）。
//     原因：AdditionalParameters 声明为 UCHAR[1]，但后续有 4 字节对齐填充，
//     于是 sizeof 比该字段的实际偏移大 3。按 sizeof 放置会多跳 3 个填充字节，
//     驱动直接返回 ERROR_INVALID_PARAMETER (87)，看起来像"设备不支持"。
//
//   输出 = STORAGE_PROTOCOL_DATA_DESCRIPTOR
//          { ULONG Version; ULONG Size; STORAGE_PROTOCOL_SPECIFIC_DATA; ... }
//          **不是**裸的 STORAGE_PROTOCOL_SPECIFIC_DATA —— 少了 Version/Size
//          这两个 ULONG 会让后续所有字段整体错位 8 字节，解析出全 0。
#include "native_smart_internal.h"

#include <winioctl.h>
#include <ntddstor.h>
#include <nvme.h>

#include <cstddef>
#include <cstring>
#include <vector>

namespace syssuite::smart {

namespace {

/// 协议数据在 IOCTL 输入缓冲区中的起始偏移。见文件头说明，务必用 offsetof 而非 sizeof。
constexpr size_t ProtocolPayloadOffset = offsetof(STORAGE_PROPERTY_QUERY, AdditionalParameters);

// NVMe 健康日志里的计数字段是 16 字节小端无符号整数（规范预留 128 位宽度）。
// 本机不可能达到 2^64，因此只取低 8 字节；高位非 0 时按饱和处理。
uint64_t ReadNvmeCounter(const UCHAR (&field)[16])
{
    uint64_t value = 0;
    for (int i = 7; i >= 0; --i)
    {
        value = (value << 8) | static_cast<uint64_t>(field[i]);
    }

    for (int i = 8; i < 16; ++i)
    {
        if (field[i] != 0)
        {
            return UINT64_MAX;
        }
    }

    return value;
}

// 输出描述符：Version + Size + 协议特定数据 + 日志页
#pragma pack(push, 8)
struct NvmePayload
{
    STORAGE_PROTOCOL_SPECIFIC_DATA protocol;
    NVME_HEALTH_INFO_LOG log;
};
#pragma pack(pop)

}  // namespace

bool TryParseNvmeSmart(HANDLE device, NativeSmartInfo& info)
{
    // ---- 输入缓冲 ----
    // 协议数据必须落在 AdditionalParameters 的实际偏移处（见文件头说明）
    constexpr size_t QuerySize = ProtocolPayloadOffset;

    std::vector<BYTE> buffer(QuerySize + sizeof(NvmePayload) + 512, 0);

    auto* query = reinterpret_cast<STORAGE_PROPERTY_QUERY*>(buffer.data());
    query->PropertyId = StorageDeviceProtocolSpecificProperty;
    query->QueryType = PropertyStandardQuery;

    auto* payload = reinterpret_cast<NvmePayload*>(buffer.data() + QuerySize);
    payload->protocol.ProtocolType = ProtocolTypeNvme;
    payload->protocol.DataType = NVMeDataTypeLogPage;
    payload->protocol.ProtocolDataRequestValue = NVME_LOG_PAGE_HEALTH_INFO;
    payload->protocol.ProtocolDataRequestSubValue = 0;
    // 偏移是"从 STORAGE_PROTOCOL_SPECIFIC_DATA 自身起始算起"
    payload->protocol.ProtocolDataOffset = sizeof(STORAGE_PROTOCOL_SPECIFIC_DATA);
    payload->protocol.ProtocolDataLength = sizeof(NVME_HEALTH_INFO_LOG);

    DWORD returned = 0;
    if (!DeviceIoControl(device, IOCTL_STORAGE_QUERY_PROPERTY,
                         buffer.data(), static_cast<DWORD>(buffer.size()),
                         buffer.data(), static_cast<DWORD>(buffer.size()),
                         &returned, nullptr))
    {
        return false;
    }

    // ---- 解析输出 ----
    // 输出是 STORAGE_PROTOCOL_DATA_DESCRIPTOR：Version/Size 之后才是协议数据。
    if (returned < sizeof(STORAGE_PROTOCOL_DATA_DESCRIPTOR))
    {
        return false;
    }

    const auto* descriptor = reinterpret_cast<const STORAGE_PROTOCOL_DATA_DESCRIPTOR*>(buffer.data());
    const STORAGE_PROTOCOL_SPECIFIC_DATA& protocol = descriptor->ProtocolSpecificData;

    // 驱动回填的数据区偏移同样相对协议结构体自身
    const size_t dataOffset = QuerySize + protocol.ProtocolDataOffset;
    if (protocol.ProtocolDataLength < sizeof(NVME_HEALTH_INFO_LOG)
        || dataOffset + sizeof(NVME_HEALTH_INFO_LOG) > returned)
    {
        return false;
    }

    NVME_HEALTH_INFO_LOG log{};
    std::memcpy(&log, buffer.data() + dataOffset, sizeof(NVME_HEALTH_INFO_LOG));

    // 温度：Kelvin，减 273 转摄氏；0 表示不可用
    if (log.Temperature[0] != 0 || log.Temperature[1] != 0)
    {
        const USHORT kelvin = static_cast<USHORT>(log.Temperature[0] | (log.Temperature[1] << 8));
        if (kelvin > 273)
        {
            info.temperatureCelsius = kelvin - 273;
        }
    }

    // 剩余寿命：0..100 的百分比
    if (log.PercentageUsed <= 100)
    {
        info.remainingLifePercent = 100u - log.PercentageUsed;
    }

    info.powerCycleCount = ReadNvmeCounter(log.PowerCycle);
    info.powerOnHours = ReadNvmeCounter(log.PowerOnHours);

    // NVMe 规范：Data Units Written 以 1000 × 512 字节为单位
    const uint64_t writtenUnits = ReadNvmeCounter(log.DataUnitWritten);
    info.totalBytesWritten = writtenUnits * 1000ull * 512ull;

    // CriticalWarning 是 union，必须用 .AsUchar 取整字节视图
    info.healthStatus = log.CriticalWarning.AsUchar == 0 ? 1u : 3u;
    if (info.healthStatus == 1u && info.remainingLifePercent > 0 && info.remainingLifePercent <= 10)
    {
        info.healthStatus = 2u;
    }

    return true;
}

}  // namespace syssuite::smart
