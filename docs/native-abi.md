# SysSuite Native ABI 合同（G10）

> 本文件是 C++（`src/SysSuite.Native`）与 C#（`src/SysSuite.Interop`，经 `SysSuite.Core` 消费）之间**唯一**的互操作契约。
> **未经评审写入本文件的原生能力，禁止接入 Core。**

- 权威头文件：`src/SysSuite.Native/include/native_api.h`
- 绑定层：`src/SysSuite.Interop/NativeMethods.cs`
- 门面：`src/SysSuite.Interop/NativeInterop.cs`
- 状态：MVP0 仅 `Native_AbiVersion` / `Native_Version` 可用，其余入口为**契约占位**，返回 `NATIVE_ERR_NOT_IMPL`

---

## 1. 版本与兼容策略

| 项 | 值 | 说明 |
|---|---|---|
| ABI 版本 | `NATIVE_ABI_VERSION = 1` | C# 侧常量 `NativeMethods.ExpectedAbiVersion = 1` |
| 用户可见版本 | `1.0.0-m0` | `Native_Version` 返回串，格式 `<主>.<次>.<修订>-<里程碑>` |
| 平台 | 仅 Windows x64 | CMake 对非 Win32 / 非 64 位直接 `FATAL_ERROR` |

**变更规则**

1. 错误码数值一旦发布**禁止复用、禁止重排**，只能追加。
2. 已发布函数的签名**禁止就地修改**；如需变更，新增 `Native_Xxx_V2` 并保留旧入口一个里程碑。
3. 任何不兼容变更必须递增 `NATIVE_ABI_VERSION`；C# 侧握手失败返回 `NativeStatus.AbiMismatch` 并拒绝调用其余入口。
4. 新增能力必须先在本文件 §9 登记（含所有权、取消、线程、上限），再编码。

**握手流程**：加载 DLL → `Native_AbiVersion()` → 与托管侧常量比对 → 不匹配则整体降级，不得继续调用。

---

## 2. 调用约定与类型映射

| C/C++ | C# | 备注 |
|---|---|---|
| `int NATIVE_CALL`（`__stdcall`） | `[UnmanagedFunctionPointer(CallingConvention.StdCall)]` | 所有导出与回调统一 |
| `wchar_t*` | `char*`（unsafe）/ `string` + `StringMarshalling.Utf16` | Windows 下 `wchar_t` 为 2 字节 UTF-16 |
| `uint8_t*` | `byte*`（unsafe） | 二进制缓冲区 |
| `uint64_t` / `uint32_t` | `ulong` / `uint` | 定宽，禁止使用 `long`（Windows LLP64） |
| 函数指针 | 委托 + `UnmanagedFunctionPointer` | 见 §6 生命周期 |

> **为何不使用 `Span<T>` 直接绑定**：`Span<char>`/`Span<byte>` 需要 `DisableRuntimeMarshalling`，而该特性会禁用"委托 → 函数指针"的运行时封送（本项目回调必需）。因此 P/Invoke 层使用裸指针，门面层用 `Span<T>` + `fixed` 对外暴露，见 `NativeInterop`。

---

## 3. 内存所有权（硬性）

1. **调用方分配、调用方释放。** 原生层只写入传入缓冲区，**绝不返回需要调用方释放的裸指针**。
2. 原生层内部如需暂存，必须自行管理生命周期；回调返回后不得保留传入指针。
3. 所有输出缓冲区必须在函数返回前完成写入；**不得**把缓冲区指针留存到后续异步回调。
4. 冒泡式分配上限：单次回传条目数 ≤ `NATIVE_SCAN_BATCH_MAX`（4096）。

---

## 4. 字符串与缓冲区协议

- 字符串一律 UTF-16（`wchar_t`），以 `L'\0'` 结尾。
- `capacity` 参数单位为**字符数**（非字节），包含终止符。
- **缓冲区不足时返回 `NATIVE_ERR_BUFFER_SMALL`，且不得写入任何字节**——调用方不会拿到截断串。
- `capacity <= 0` 或指针为 `nullptr` 统一返回 `NATIVE_ERR_INVALID_ARG`（优先于 `BUFFER_SMALL`）。
- 二进制输出（如 SMBIOS）通过 `written` 回填实际字节数；失败时 `written` 置 0。

---

## 5. 错误码

| 码 | 宏 | 托管枚举 | 含义 |
|---|---|---|---|
| 0 | `NATIVE_OK` | `Ok` | 成功 |
| 1 | `NATIVE_ERR_INVALID_ARG` | `InvalidArgument` | 参数为 null / 容量非正 / 格式非法 |
| 2 | `NATIVE_ERR_BUFFER_SMALL` | `BufferTooSmall` | 缓冲区不足，未写入 |
| 3 | `NATIVE_ERR_NOT_IMPL` | `NotImplemented` | 能力未实现（占位） |
| 4 | `NATIVE_ERR_OUT_OF_MEMORY` | `OutOfMemory` | 原生侧分配失败 |
| 5 | `NATIVE_ERR_ACCESS_DENIED` | `AccessDenied` | 权限不足 / 被拦截 |
| 6 | `NATIVE_ERR_IO` | `IoError` | 设备或文件 I/O 失败 |
| 7 | `NATIVE_ERR_CANCELLED` | `Cancelled` | 调用方主动中止 |
| 8 | `NATIVE_ERR_INTERNAL` | `InternalError` | 未分类内部错误 |

托管侧附加状态（不出现在 ABI）：`LibraryNotLoaded = 100`、`AbiMismatch = 101`、`Unknown = 102`。

---

## 6. 回调、线程与取消

**回调类型**

```c
typedef int (NATIVE_CALL* NativeScanCallback)(void* context, const uint64_t* entryIds,
                                              uint32_t entryCount, uint64_t processed, uint64_t total);
typedef int (NATIVE_CALL* NativeCancelCheck)(void* context);
```

**规则**

1. 回调在**调用方线程**执行（原生层不创建线程回调托管代码）。若未来引入工作线程，必须在本文件登记线程模型并做 UI 线程封送。
2. 同一 `context` 的回调**串行**调用，禁止并发。
3. `NativeScanCallback` 返回非 0 → 原生层停止并回 `NATIVE_ERR_CANCELLED`。
4. `NativeCancelCheck` 返回非 0 → 立即回 `NATIVE_ERR_CANCELLED`。**每个导出函数在进入时必须先做一次取消检查**，保证"请求即取消"可被确定性观测（这也是 FFI 冒烟测试的观测点）。
5. 回调可为 `nullptr`（表示仅探测/不需要进度）；原生层必须容忍。
6. **生命周期**：委托实例在调用期间必须保持可达。C# 门面在 P/Invoke 后调用 `GC.KeepAlive(delegate)`。
7. 回调内**禁止**抛出托管异常跨越 ABI 边界。

---

## 7. 加载与降级（托管侧）

`NativeLibraryResolver` 按以下顺序查找 `SysSuite.Native.dll`：

1. 环境变量 `SYSSUITE_NATIVE_PATH`（目录或文件路径）
2. `AppContext.BaseDirectory`
3. `AppContext.BaseDirectory/runtimes/win-x64/native/`
4. 从输出目录向上最多 6 层，查找 `build/native/{Release,Debug}`、`build/Release`、`artifacts/native`

**降级原则（G6）**：未找到 DLL 时，所有入口返回 `NativeStatus.LibraryNotLoaded`，**绝不抛 `DllNotFoundException` 打断 UI 启动**。UI 侧应把该状态显式呈现为"原生模块未构建"，而非静默失败。

---

## 8. FFI 冒烟（DoD 的一部分）

每个接入 Core 的原生能力必须有覆盖以下路径的测试：

| 路径 | 观测 |
|---|---|
| 成功 | 返回 `NATIVE_OK`，输出缓冲区内容正确 |
| 缓冲区不足 | 返回 `BUFFER_SMALL`，且缓冲区未被写入 |
| 取消 | 取消回调返回非 0 时返回 `CANCELLED` |
| 未实现 | 占位入口返回 `NOT_IMPL`，参数校验仍生效 |

- C# 侧：`tests/SysSuite.Tests/NativeInterop/NativeAbiSmokeTests.cs`（DLL 缺失时自动 Skip）
- C++ 侧：`tests/SysSuite.Native.Tests/test_native_api.cpp`（Catch2，CI 执行）

---

## 9. 已登记能力

| 入口 | 关联任务 | 状态 | 所有权 | 取消 | 备注 |
|---|---|---|---|---|---|
| `Native_AbiVersion` | T0.2 | 可用 | 无输出缓冲 | 不适用 | 握手用，永不失败 |
| `Native_Version` | T0.2 | 可用 | 调用方缓冲区 ≥ 32 字符 | 不适用 | 验证调用链的探针 |
| `Native_GetSmbios` | T1.3 | 占位 `NOT_IMPL` | 调用方 `byte[]` + `written` 回填 | 不适用 | 需提权，接入前须评估 `AccessDenied` |
| `Native_ScanVolume` | T3.2 | 占位 `NOT_IMPL` | 调用方批量缓冲（≤4096/批） | 支持 `NativeCancelCheck` | 目标：150 万文件 < 8s（§6 预算） |

**MVP0 允许接入的范围**：仅上表标记为"可用"者。任何需要提权、写磁盘、修改系统状态的能力，默认不得进入 MVP0 链路（G9）。
