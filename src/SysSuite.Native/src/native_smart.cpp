// SysSuite.Native —— SMART 健康查询入口（T1.3）
//
// 设计说明：
//   * 走 IOCTL_STORAGE_QUERY_PROPERTY 判定协议（ATA / NVMe），再按协议分派：
//       - ATA/SATA ：IOCTL_ATA_PASS_THROUGH_DIRECT + SMART READ DATA
//       - NVMe     ：STORAGE_PROTOCOL_SPECIFIC_DATA + SMART/Health Information Log (0x02)
//   * 全程只读：不发送任何带写语义的 ATA 命令，不写任何设备寄存器。
//   * 单盘失败不中断整体枚举，只把该盘 isAvailable 置 0。
//
// 实现按职责拆到 native_smart_common.cpp（设备打开/权限判定）、
// native_smart_ata.cpp（ATA 属性解析）、native_smart_nvme.cpp（NVMe 日志解析），
// 本文件只做协议判定与枚举编排 —— 单文件行数门禁要求 ≤ 300 行。
#include "native_api.h"
#include "native_smart_internal.h"

#include <windows.h>
#include <winioctl.h>
#include <ntddscsi.h>
#include <ntddstor.h>

#include <cstring>
#include <cwchar>

namespace {

using syssuite::smart::OpenResult;
using syssuite::smart::OpenPhysicalDrive;
using syssuite::smart::FillModelFields;

// 判定物理盘总线类型。失败时按 ATA 处理（兼容老设备与虚拟磁盘）。
//
// 用字节缓冲区而非裸 STORAGE_DEVICE_DESCRIPTOR：该结构是变长的，
// 型号/序列号字符串紧随其后；只读 BusType 虽然用固定结构也够，
// 但用缓冲区更贴合驱动实际写入的布局，避免驱动返回更多字节时被截断。
bool IsNvmeDevice(HANDLE device)
{
    STORAGE_PROPERTY_QUERY query{};
    query.PropertyId = StorageDeviceProperty;
    query.QueryType = PropertyStandardQuery;

    BYTE buffer[1024]{};
    DWORD returned = 0;
    if (!DeviceIoControl(device, IOCTL_STORAGE_QUERY_PROPERTY, &query, sizeof(query),
                         buffer, sizeof(buffer), &returned, nullptr))
    {
        return false;
    }

    if (returned < sizeof(STORAGE_DEVICE_DESCRIPTOR))
    {
        return false;
    }

    const auto* descriptor = reinterpret_cast<const STORAGE_DEVICE_DESCRIPTOR*>(buffer);
    return descriptor->BusType == BusTypeNvme;
}

// 查询单盘 SMART。denied 用于向调用方报告"失败原因是权限"。
bool QueryOneDrive(uint32_t driveNumber, NativeSmartInfo& info, bool& denied)
{
    denied = false;

    HANDLE device = INVALID_HANDLE_VALUE;
    // 部分设备不允许写打开，退化为只读打开重试（SMART/健康日志读取都只需要读权限）
    auto opened = OpenPhysicalDrive(driveNumber, GENERIC_READ | GENERIC_WRITE, device);
    if (opened == OpenResult::AccessDenied)
    {
        opened = OpenPhysicalDrive(driveNumber, GENERIC_READ, device);
    }

    if (opened != OpenResult::Success)
    {
        denied = opened == OpenResult::AccessDenied;
        return false;
    }

    info.driveNumber = driveNumber;
    FillModelFields(info, device);

    // 先判定协议，避免对 NVMe 发 ATA 命令（会返回 ERROR_INVALID_FUNCTION）；
    // 同时保留双向兜底，应对 BusType 报告不准的廉价转接卡。
    bool parsed = false;
    if (IsNvmeDevice(device))
    {
        parsed = syssuite::smart::TryParseNvmeSmart(device, info);
        if (!parsed)
        {
            parsed = syssuite::smart::TryParseAtaSmart(device, info);
        }
    }
    else
    {
        parsed = syssuite::smart::TryParseAtaSmart(device, info);
        if (!parsed)
        {
            parsed = syssuite::smart::TryParseNvmeSmart(device, info);
        }
    }

    CloseHandle(device);
    return parsed;
}

// 探测物理盘号是否可被打开（0 访问权限），用于区分"盘不存在"与"权限不足"。
// 返回：1 = 存在，0 = 不存在（应停止枚举），-1 = 权限被拒。
int ProbePhysicalDrive(uint32_t driveNumber)
{
    wchar_t path[64]{};
    if (swprintf_s(path, L"\\\\.\\PhysicalDrive%u", driveNumber) <= 0)
    {
        return 0;
    }

    HANDLE probe = CreateFileW(path, 0, FILE_SHARE_READ | FILE_SHARE_WRITE,
                               nullptr, OPEN_EXISTING, 0, nullptr);
    if (probe != INVALID_HANDLE_VALUE)
    {
        CloseHandle(probe);
        return 1;
    }

    const DWORD error = GetLastError();
    if (error == ERROR_ACCESS_DENIED || error == ERROR_PRIVILEGE_NOT_HELD)
    {
        return -1;
    }

    if (error == ERROR_FILE_NOT_FOUND || error == ERROR_PATH_NOT_FOUND)
    {
        return 0;
    }

    return -1;
}

}  // namespace

extern "C" int NATIVE_CALL Native_QuerySmart(NativeSmartInfo* buffer,
                                             int capacity,
                                             int* count,
                                             NativeCancelCheck isCancelled,
                                             void* context)
{
    if (buffer == nullptr || capacity <= 0 || count == nullptr)
    {
        return NATIVE_ERR_INVALID_ARG;
    }

    *count = 0;

    // 契约（与 Native_ScanVolume 一致）：进入时先做一次取消检查
    if (isCancelled != nullptr && isCancelled(context) != 0)
    {
        return NATIVE_ERR_CANCELLED;
    }

    const uint32_t callerStructSize = buffer[0].structSize;
    const uint32_t writeSize = (callerStructSize == 0 || callerStructSize > sizeof(NativeSmartInfo))
        ? static_cast<uint32_t>(sizeof(NativeSmartInfo))
        : callerStructSize;

    int written = 0;
    bool denied = false;

    for (uint32_t driveNumber = 0; driveNumber < 64u; ++driveNumber)
    {
        if (written >= capacity)
        {
            break;
        }

        if (isCancelled != nullptr && isCancelled(context) != 0)
        {
            // 已写入的部分结果保留：UI 可以展示已探测到的盘
            *count = written;
            return NATIVE_ERR_CANCELLED;
        }

        const int probe = ProbePhysicalDrive(driveNumber);
        if (probe == 0)
        {
            // 盘号连续编号，遇到第一个不存在的即可停止枚举
            break;
        }

        if (probe < 0)
        {
            denied = true;
            continue;
        }

        NativeSmartInfo entry{};
        entry.structSize = static_cast<uint32_t>(sizeof(NativeSmartInfo));
        entry.driveNumber = driveNumber;

        bool driveDenied = false;
        const bool ok = QueryOneDrive(driveNumber, entry, driveDenied);
        if (driveDenied && !ok)
        {
            // 权限被拒：不写出零值条目（否则 UI 会误报为"硬盘不支持 SMART"）
            denied = true;
            continue;
        }

        entry.isAvailable = ok ? 1u : 0u;
        if (!ok)
        {
            entry.healthStatus = 0;
        }

        // 按调用方声明的结构体大小写入，兼容旧版调用方
        std::memcpy(&buffer[written], &entry, writeSize);
        written++;
    }

    *count = written;

    // 一个盘都没打开且明确被拒 → 权限错误；否则一律 OK（单盘不支持不算失败）
    if (written == 0 && denied)
    {
        return NATIVE_ERR_ACCESS_DENIED;
    }

    return NATIVE_OK;
}
