// SysSuite.Native —— SMART 实现的内部共享声明（非导出 ABI）
//
// 只在 native_smart*.cpp 之间使用，不进 include/ 公开目录。
#ifndef SYSSUITE_NATIVE_SMART_INTERNAL_H
#define SYSSUITE_NATIVE_SMART_INTERNAL_H

#include "native_api.h"

#include <windows.h>

namespace syssuite::smart {

// 打开物理盘的结果。
enum class OpenResult {
    Success,
    AccessDenied,
    NotFound,
};

// 依次尝试 GENERIC_READ | GENERIC_WRITE → GENERIC_READ。
//
// 关键：把"权限被拒"(ERROR_ACCESS_DENIED) 与"盘不存在"(ERROR_FILE_NOT_FOUND)
// 严格区分，否则 UI 会把"没管理员权限"误报成"硬盘不支持 SMART"。
OpenResult OpenPhysicalDrive(uint32_t driveNumber, DWORD access, HANDLE& device);

// 用 STORAGE_DEVICE_DESCRIPTOR 填充型号/序列号/固件（比 ATA IDENTIFY 更通用）。
void FillModelFields(NativeSmartInfo& info, HANDLE device);

// ATA/SATA 路径：SMART READ DATA (0xD0) + 属性解析。
bool TryParseAtaSmart(HANDLE device, NativeSmartInfo& info);

// NVMe 路径：SMART/Health Information Log (0x02)。
bool TryParseNvmeSmart(HANDLE device, NativeSmartInfo& info);

}  // namespace syssuite::smart

#endif  // SYSSUITE_NATIVE_SMART_INTERNAL_H
