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

#ifdef __cplusplus
}  // extern "C"
#endif

#endif  // SYSSUITE_NATIVE_API_H_
