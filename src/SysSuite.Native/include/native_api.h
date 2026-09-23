// SysSuite.Native —— C++ ↔ C# 互操作 ABI
//
// 本头文件是 Core 与原生层之间唯一的稳定契约，完整语义见 docs/native-abi.md。
// 变更规则：
//   1. 错误码数值一经发布不得复用、不得重排；
//   2. 导出函数签名不得就地修改，只能新增 Native_*_2 形式的新入口；
//   3. ABI 版本（NATIVE_ABI_VERSION）在任何不兼容变更时必须递增。
//
// 内存所有权：所有输出缓冲区一律由调用方（C#）分配、由调用方释放；
// 原生层只写入，绝不返回需要调用方释放的裸指针。
#ifndef SYSSUITE_NATIVE_API_H_
#define SYSSUITE_NATIVE_API_H_

#include <stdint.h>

#if !defined(_WIN32)
#error "SysSuite.Native 仅支持 Windows（x64）"
#endif

#if defined(SYSSUITE_NATIVE_EXPORTS)
#define NATIVE_API __declspec(dllexport)
#else
#define NATIVE_API __declspec(dllimport)
#endif

// 所有导出函数统一 stdcall，与 C# UnmanagedFunctionPointer(CallingConvention.StdCall) 对齐
#define NATIVE_CALL __stdcall

// ABI 合同版本。C# 启动自检时会比对，不匹配则拒绝加载原生能力。
#define NATIVE_ABI_VERSION 1

// ---------------------------------------------------------------------------
// 稳定错误码（禁止复用既有数值）
// ---------------------------------------------------------------------------
#define NATIVE_OK 0                // 成功
#define NATIVE_ERR_INVALID_ARG 1   // 参数为 null / 容量非正 / 格式非法
#define NATIVE_ERR_BUFFER_SMALL 2  // 调用方缓冲区不足，未写入任何数据
#define NATIVE_ERR_NOT_IMPL 3      // 该能力在当前版本未实现（下游任务占位）
#define NATIVE_ERR_OUT_OF_MEMORY 4 // 原生层自身分配失败
#define NATIVE_ERR_ACCESS_DENIED 5 // 权限不足（需要提权 / 被安全软件拦截）
#define NATIVE_ERR_IO 6            // 设备或文件 I/O 失败
#define NATIVE_ERR_CANCELLED 7     // 调用方通过取消回调主动中止
#define NATIVE_ERR_INTERNAL 8      // 未分类内部错误（需查日志）

// Native_Version 所需的最小缓冲区容量（含结尾 L'\0'）
#define NATIVE_VERSION_CAPACITY_MIN 32

// 单次回调回传的最大条目数上限，防止 C# 侧单次分配过大
#define NATIVE_SCAN_BATCH_MAX 4096

// SMART 查询所需的最小缓冲区（字节）。容纳本次支持的物理盘数量上限。
#define NATIVE_SMART_CAPACITY_MIN 64

// 单块物理盘的 SMART 结果。
// 结构体布局是 ABI 的一部分：只允许在尾部追加字段，字段不得重排、不得改类型。
// 所有字段为 0 表示"该项不可用"，调用方据此区分"读到 0" 与 "没读出来"。
#pragma pack(push, 8)
typedef struct NativeSmartInfo
{
    uint32_t structSize;        // 必须由调用方填 sizeof(NativeSmartInfo)，用于版本兼容
    uint32_t isAvailable;       // 1 = 该盘成功读到 SMART；0 = 不支持/读取失败
    uint32_t driveNumber;       // 物理盘序号，对应 \\.\PhysicalDriveN
    uint32_t healthStatus;      // 0 未知 / 1 良好 / 2 警告 / 3 危险（由原生侧按原始值判定）
    uint32_t temperatureCelsius;// 当前温度；0 表示不可用
    uint32_t remainingLifePercent; // 剩余寿命百分比（NVMe 与部分 SSD）；0 表示不可用
    uint32_t reallocatedSectors;   // 重映射扇区数（ATA 属性 5）；0 表示不可用
    uint32_t pendingSectors;       // 待处理扇区数（ATA 属性 197）；0 表示不可用
    uint32_t uncorrectableErrors;  // 不可纠正错误数（ATA 属性 187）
    uint64_t powerOnHours;      // 通电时间（小时）
    uint64_t powerCycleCount;   // 通电次数
    uint64_t totalBytesWritten;  // 累计写入量（字节，NVMe 专用）；0 表示不可用
    uint32_t ataSmartAttributeCount; // 有效属性数，供 UI 判断"有无详细数据"
    uint32_t reserved0;         // 对齐保留，必须为 0
    wchar_t  model[64];         // 型号，L'\0' 结尾（含终止符最多 64 字符）
    wchar_t  serial[64];        // 序列号
    wchar_t  firmware[16];      // 固件版本
} NativeSmartInfo;
#pragma pack(pop)

// 批量进度回调：返回 0 继续；返回非 0 视为调用方请求中止（等价于取消）
typedef int(NATIVE_CALL* NativeScanCallback)(void* context,
                                             const uint64_t* entryIds,
                                             uint32_t entryCount,
                                             uint64_t processed,
                                             uint64_t total);

// 取消检查回调：返回 0 继续；返回非 0 立即中止并返回 NATIVE_ERR_CANCELLED
typedef int(NATIVE_CALL* NativeCancelCheck)(void* context);

#ifdef __cplusplus
extern "C" {
#endif

// 返回 NATIVE_ABI_VERSION，用于加载期版本握手。永不失败。
NATIVE_API int NATIVE_CALL Native_AbiVersion(void);

// 写入原生模块版本串（UTF-16，以 L'\0' 结尾）。
// buffer == nullptr 或 capacity <= 0                  → NATIVE_ERR_INVALID_ARG
// capacity < 实际所需（含终止符）                      → NATIVE_ERR_BUFFER_SMALL，且不写入任何字节
NATIVE_API int NATIVE_CALL Native_Version(wchar_t* buffer, int capacity);

// SMBIOS 原始表读取（T1.3）。
// buffer/capacity 由调用方提供；written 回填实际字节数。当前返回 NATIVE_ERR_NOT_IMPL。
NATIVE_API int NATIVE_CALL Native_GetSmbios(uint8_t* buffer, int capacity, int* written);

// MFT / USN 卷扫描（T3.2）。
// volume 形如 L"\\\\?\\C:"；onBatch 可为 nullptr（仅探测）；isCancelled 可为 nullptr。
// 当前除取消与参数校验外，返回 NATIVE_ERR_NOT_IMPL。
NATIVE_API int NATIVE_CALL Native_ScanVolume(const wchar_t* volume,
                                             NativeScanCallback onBatch,
                                             void* context,
                                             NativeCancelCheck isCancelled);

// 枚举物理盘的 SMART 健康信息（T1.3）。
//
// buffer：调用方提供的 NativeSmartInfo 数组，capacity 为其**元素个数**（非字节数）。
// count ：回填实际写入的元素个数；失败时置 0。
//
// 契约：
//   * 需要管理员权限（IOCTL 直达设备）。权限不足返回 NATIVE_ERR_ACCESS_DENIED。
//   * 单块盘不支持 SMART（如 RAID 虚拟盘、部分 USB 桥）不影响其它盘，
//     该盘 isAvailable = 0，整体仍返回 NATIVE_OK。
//   * 取消：进入时先检查一次，每块盘之间再检查一次。
//   * structSize 字段由调用方预填；原生侧只写不超过该大小的内容，向后兼容旧调用方。
NATIVE_API int NATIVE_CALL Native_QuerySmart(NativeSmartInfo* buffer,
                                             int capacity,
                                             int* count,
                                             NativeCancelCheck isCancelled,
                                             void* context);

#ifdef __cplusplus
}  // extern "C"
#endif

#endif  // SYSSUITE_NATIVE_API_H_
