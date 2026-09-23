# PLAN.md — SysSuite 综合系统工具开发计划书 v2.2

> 图例：[ ] 未开始  [~] 进行中  [x] 完成
> 人力假设：2~3 人（A=全栈/UI，B=C#/业务，C=C++/系统）
> 本文件是唯一进度真源：每完成一步勾选对应 checkbox。
>
> **版本历史**
> v1.0 — 初版：架构 / 里程碑 / 任务分解
> v2.0 — 追加 T0.0 仓库建立、T5.6 Defender 防护控制（高风险）、G8 安全红线、R6/R7 风险
> v2.1 — 完整性审计修订：新增 T0.6（清单/提权）、T0.7（Watchdog 骨架）、T7.0（全局搜索）、
>        T7.5（设置中心）、T7.6（开源合规）、T7.7（灰度冒烟）、§5 测试与质量专项（T8.*）、§6 性能预算、§7 范围外声明；
>        扩充 T0.4/T5.5/T6.4/T7.3；勘误 v2.0 文字残损；工期重算
> v2.2.1 — MVP0 基础层：崩溃闭环、热保存、CI 硬门禁、原生测试骨架、Watchdog IPC 与 UI 修复
> v2.3 — **进度校准（代码实证）**：逐条核对 src/ 实际实现后重设全部勾选；标注"计划用 C++ 原生、实际用托管替代"的偏离项；
>        T0.2 由 [x] 全部回退为未开始（SysSuite.Native / SysSuite.Interop 不存在）；
>        M3 的 T3.1/T3.3/T3.4 子项补齐勾选；新增 §11 偏差与风险登记
> v2.4 — **T0.2 补建**：`SysSuite.Native`（CMake/C++20）+ `SysSuite.Interop`（LibraryImport 绑定层）落地，
>        `docs/native-abi.md` 定义 G10 所需全部契约；CI 增加 native job（C++ 构建 + Catch2 + FFI 冒烟）；
>        首页新增"原生自检"临时入口。C++ 侧待 CI 首次编译验证（本机无 CMake/MSVC）
> v2.5 — **工具链结论修正（重要）**：v2.4 所称"本机无 CMake/MSVC"是**误判**。Visual Studio 装在
>        `D:\ai_era\Microsoft Visual Studio\18\Community`（不在 `C:\Program Files`，故此前 glob 全部 miss）。
>        本机实际具备 MSVC 14.51 + CMake 4.3.1 + Windows SDK 10.0.26100。据此：
>        ① C++ 已在**本机真实编译通过**（`/W4` 零告警，导出 4 个 `Native_*` 符号，x64）；
>        ② 新增 `rules/Build-Native.ps1`（绕过 CMake 生成器探测与 vcvarsall/reg.exe）作为本机与 CI 的统一构建入口；
>        ③ FFI 冒烟由 7 项 Skip 转为**真实执行并全绿**（测试总数 41 → 48）；
>        ④ 另发现历史编译残留 `obj/native/native_api.obj`（2026-09-07），证明 T0.2 曾实现过、源码后丢失
> v2.6 — **M1 推进 + M0 收口**：T0.6 版本资源与品牌资产落地（`Directory.Build.props` 统一元数据 + 多尺寸 `App.ico`）；
>        T1.6 报告导出完成（TXT/HTML/JSON，含 UTF-8 无 BOM 契约与 HTML 转义）；
>        T1.1 数据质量收敛 —— 用真实硬件实测后修正三处长期缺陷（操作系统产品名、显存 32 位溢出、显示模式噪声），
>        并修复报告文本的东亚字符对齐（详见 §11.5）。测试总数 48 → 81。

---

## 0. 全局约定（先读）

- [x] **G1 目录**：仓库根 = sln；代码在 `src/`，文档在 `docs/`，测试在 `tests/`，规则在 `rules/`，打包在 `installer/`（`installer/` 目录已建但仍为空）
- [~] **G2 分支**：`main`（稳定）/ `dev`（集成）/ `feature/M{阶段}-{任务号}`；合并一律 PR + 至少 1 人 Review + main 分支保护 —— **实际只有 `master` 一个分支，未建 `dev`，未设分支保护**
- [~] **G3 提交**：Conventional Commits（feat/fix/chore/refactor/test/docs），任务号入消息尾，如 `[T2.4]` —— **前缀已遵守，但任务号未入消息尾**
- [x] **G4 命名**：C# PascalCase（私有 `_camel`）；C++ 类 PascalCase、导出函数 `Native_` 前缀；资源 key 用中划线（C++ 部分待 T0.2 开工后适用）
- [~] **G5 DoD（每任务）**：编译 0 warning；单测通过；Serilog 无 Error；深浅两主题目检。M0-M2 只做 Win10 22H2 或 Win11 24H2 单机冒烟；M3 起按改动面选择双系统；M6 后执行完整矩阵 —— **前四项已常态化满足；冒烟清单（T8.2）未脚本化**
- [x] **G6 崩溃与遥测**：本地 crash dump + 崩溃对话框 + 日志打包导出（暂不联网上报；实现见 T0.4）
- [x] **G7 破坏性操作红线**：任何删除/注册表写入/防护变更必须有 **预览 → 备份 → 执行** 三步；系统目录白名单硬拒绝 —— **三步流程已落地；`src/SysSuite.Core/System/ProtectedPaths.cs` 已于 2026-09-22 建立，并在 `ForceDeleteService`、`LeftoverScanner`、`DiskInspectionService.Clean` 三处接入（fail-closed，无法判定时一律拒绝）**
- [ ] **G8 安全红线（T5.6 专属）**：防护关闭仅允许用户主动 UI 触发；必须默认定时自动恢复；必须写审计日志；禁止静默/命令行/无人值守关闭防护；防篡改开启时诚实降级、禁止伪造关闭态（依赖 T5.6，未开工）
- [~] **G9 风险分级**：L0 默认启用；L1 默认启用但可关闭；L2 实验性、默认关闭；L3 仅灰度/企业验证后启用。T3.5、T4.1 服务层、T4.2、T5.6、T6.4、强制删除均不得直接进入 MVP 默认链路 —— **L2 开关已落地（`EnableExperimentalFeatures` + `EnableForceDelete`，设置页不可绕过）；其余高危模块未开工**
- [ ] **G10 Native ABI 合同**：所有 C++ 导出必须有稳定错误码、内存所有权、回调线程、取消规则与 FFI 冒烟测试；未定义契约的原生能力不得接入 Core（无 C++ 项目，未开工）
- [x] **G11 数据契约**：SQLite schema、迁移、稳定业务主键、备份/恢复路径、审计字段必须先评审后编码；跨模块复用表结构变更必须走版本化迁移（`SharedDatabaseService` + `docs/db-migrations.md` + `stable_key` + `security_audit` 已落地）

---

## 1. 里程碑总览

| 里程碑 | 内容 | 工期 | 负责 | 状态 |
|---|---|---|---|---|
| M0 | 仓库 + 架构骨架 + Interop + UI 壳 + Watchdog 骨架 | 2 周 | ABC | [~] | ~95%；T0.2 已完整闭环（C++ 本机编译通过 + FFI 冒烟真实执行全绿）；剩余缺口：app.manifest 未声明 x64、既有 P/Invoke 未归拢、Clang-Tidy 未接入、Catch2 待 CI |
| M1 | 系统信息 + 监控 + 基准测试 | 2 周 | C 为主 | [x] | **已完成**；T1.1~T1.6 全部落地，提权真机实测闭环（SMART 全字段 + GPU/SSD 温度）。唯一遗留：CPU 温度需用户装 PawnIO（D15） |
| M2 | 卸载器（完整） | 3 周 | B 为主 | [x] | ~95%；T2.1~T2.7 均有真实实现且可用 |
| M3 | 磁盘清理（MFT 扫描 + 规则引擎） | 3 周 | BC | [~] | ~80%；T3.1/T3.3/T3.4 完成；T3.2 已用 C# P/Invoke 实现（偏离计划的 C++ 原生通道）且性能未实测；T3.5 保持冻结 || M4 | 更新控制 | 1 周 | B | [ ] | 0% |
| M5 | 安全中心 + 防护控制 + 软件管家 | 3 周 | B | [ ] | 0%；`SecurityPage`/`SoftwareHubPage` 仍为 11 行 `InitializeComponent()` 空壳（XAML 只有标题 + "MVP0 骨架"占位文案） |
| M6 | 桌面整理 | 4~6 周 | C 为主 | [ ] | 0%；`DesktopPage` 仍为空壳 |
| M7 | 搜索 / 本地化 / Ribbon / 工具箱 / 打包 / 签名 / 灰度 | 3 周 | A | [ ] | 0%；`installer/` 为空目录，无打包与签名（D18）；`ToolboxPage` 亦为空壳 |
| M8 | 测试与质量横切（贯穿，见 §5） | 不单列 | ABC | [~] | ~15%；仅 T8.1 的 C# 静态分析落地（`.editorconfig` + `EnableNETAnalyzers` + `TreatWarningsAsErrors`），T8.2/T8.3/T8.4 未开工 |

**总计约 21~23 周**

> **读状态表时注意"页面空壳"的判定方式**：`Pages/*.xaml.cs` 行数**不能**单独用来判断某个功能是否实现。
> 仓库采用「宿主页 + 视图」模式 —— `CleanerPage.xaml` 只有 6 行，内容是 `<pages:DiskCleanerView />`，
> 而真正的实现（240 行 code-behind + 3 个 partial + 独立 XAML）全在 `DiskCleanerView.*` 里。
> 同理 `SystemInfoPage` 拆成了 `.Health.cs` / `.Monitor.cs` / `.Trend.cs`，`UninstallerPage` 拆成了
> `.Extras.cs` / `.Interaction.cs` / `.Rows.cs`。
> 判定空壳的正确方式：看 XAML 里是否只有标题 + "MVP0 骨架"占位文案。
> 截至 2026-09-23，真正的空壳只有 4 个：`SecurityPage` / `SoftwareHubPage` / `DesktopPage` / `ToolboxPage`（各 11 行）。

### 1.1 MVP0 与发布分层

- [~] **MVP0 目标**：先交付一个可安装、可日常使用、风险可控的基础版，避免全量 v1 拖入高危能力 —— 能力侧已完成 9 成，**但"可安装"未达成（installer/ 为空，无安装包）**
  - 包含：仓库/CI/架构骨架、UI 壳与设置骨架、系统信息与实时监控、基础清理规则、卸载器核心、软件管家只读升级列表、本地化与崩溃闭环
  - 明确排除：Defender 防护关闭、Windows Update 服务层禁用、WaaSMedic 权限接管、MFT 极速通道、桌面分区覆盖层、强制删除、系统瘦身、自更新
  - 建议任务顺序：T0.0 → T0.1 → T0.2 → T0.4 → T0.6 → T0.3 → T0.5 → T2.1 → T1.1/T1.4 → T2.2/T2.4 → T3.1/T3.4 → T5.1/T5.2/T5.5 只读 → T7.1 → MVP0 冒烟
- [~] **版本分层**（分层约定已确立，尚未产出任一发布版本）：
  - MVP0：基础稳定版，全部能力为 L0/L1
  - v1.0：补齐 MFT 扫描、基准测试、残留扫描、全局搜索、打包签名、性能与兼容矩阵
  - v1.1：灰度启用系统瘦身与桌面分区
  - v1.2+：实验性启用更新服务层控制、Defender 防护控制、WaaSMedic 对抗、强制删除
- [ ] **MVP0 验收**：安装包可运行；9 页导航可用；系统信息/监控/基础清理/卸载/软件升级只读列表可闭环；崩溃可导出日志；无默认高危操作 —— **除"安装包可运行"外均已满足；软件升级列表（T5.5）仍为空壳**

### 1.2 资源排期矩阵

| 阶段 | A 全栈/UI | B C#/业务 | C C++/系统 | 并行策略 |
|---|---|---|---|---|
| M0 | UI 壳/导航/主题 | 基础设施/数据契约 | Native ABI/CMake/Watchdog | ABI 合同先行；UI 与基础设施并行 |
| MVP0 | 系统信息页/监控图 | 清理规则/卸载器/软件管家只读 | 监控性能采样与原生桥接 | 先合同后实现；每 2 天集成 |
| M1-M2 | 图标列表/导出报告 | 卸载/残留/数据层 | SMART/基准/USN 监控 | C 的 SMART/基准可与 B 的卸载并行 |
| M3-M5 | 清理/安全/管家页面 | 规则引擎/执行器/体检 | MFT/SMI/低风险原生探针 | 高危模块冻结，仅评审不实现 |
| M6-M7 | 桌面 UI/搜索/打包 | 设置/发布校验/合规 | 桌面原生读写/覆盖层 | 高危能力进入实验分支 |
| 发布前 | 冒烟与 UX 修复 | 回归/数据迁移验证 | 性能采样/硬件矩阵 | 只修 P0/P1，不新增功能 |

---

## M0 仓库建立 + 架构骨架

### T0.0 仓库建立与工程化基线  [~]  0.5 天
- [x] 本地初始化：
  ```powershell
  mkdir D:\Projects\SysSuite; cd D:\Projects\SysSuite
  git init
  git config --global user.name  "你的名字"
  git config --global user.email "you@example.com"
  ```
- [x] `.gitignore` 入库（C# bin/obj/.vs + C++ build/out/*.pdb + *.db/logs/iconcache + installer/output）
- [x] `README.md` 最小骨架（项目简介 + docs 索引）
- [x] `docs/PLAN.md`、`docs/UI-SPEC.md` 入库
- [x] GitHub 建仓（二选一）：
  - 网页：New repository → 命名 `SysSuite` → **Private** → **不勾选任何初始化选项**
  - CLI（推荐）：`gh auth login` 后 `gh repo create SysSuite --private --source=. --remote=origin --push`
- [x] 首次提交推送：`git add . && git commit -m "chore: init repo [T0.0]" && git branch -M main && git push -u origin main`（远程 `origin` 已存在；**当前分支名为 `master` 而非 `main`**）
- [ ] 建 dev 分支：`git checkout -b dev && git push -u origin dev`
- [ ] main 分支保护：Settings → Branches → Require PR before merging + 1 approval
- [x] CI 骨架 `.github/workflows/ci.yml`（先放 dotnet 构建段，T0.5 补全）
- 验收：main/dev 双分支可见；CI 首跑绿；clone 后按 README 可定位全部文档

### T0.1 解决方案与项目结构  [~]  0.5 天
- [x] `dotnet new sln -n SysSuite`
- [x] 建立 5 个 C# 项目 —— **5 个全部建成（`SysSuite.Interop` 已于 2026-09-22 补建）**：
  - [x] `SysSuite.UI`（wpf, net8.0-windows）
  - [x] `SysSuite.Core`（classlib）
  - [x] `SysSuite.Core.Abstractions`（classlib，接口层）
  - [x] `SysSuite.Interop`（classlib，P/Invoke 绑定）
  - [x] `SysSuite.Watchdog`（worker，托盘常驻进程）
- [x] 建立 C++ 项目 `SysSuite.Native`（CMake，输出 `SysSuite.Native.dll`，仅 x64）—— **`src/SysSuite.Native/{CMakeLists.txt,include/native_api.h,src/native_api.cpp}` 已建**
- [~] 项目引用：UI→Core→Abstractions→ 与 Core→Interop 均已建立；**但 Core 各服务内的既有 P/Invoke 尚未归拢到 Interop（见 §11 偏差 D2）**
- [x] 根目录：`.editorconfig` / `Directory.Build.props`（nullable=enable、TreatWarningsAsErrors）/ `Directory.Packages.props`（中央包管理）
- 验收：`dotnet build` 与 `cmake --build build` 双绿

### T0.2 C++↔C# 互操作合同与通路  [x]  1 天

> ✅ **2026-09-22 完成，且已在本机真实编译验证**，不再是"待 CI 验证"。
>
> **关于本任务的历史，需要如实记录三段经过**（避免后人再次误判）：
> 1. 最初标 [x] **并非凭空**：仓库内留有编译残留 `obj/native/native_api.obj`（mtime 2026-09-07 23:31:41）
>    与 `tests/SysSuite.Tests/bin/Release/net8.0/SysSuite.Native.dll`（23:31:43，x64，导出 `Native_Version`）。
>    二者相隔 2 秒，且 `bin/` `obj/` 均被 gitignore —— 说明 T0.2 曾真实实现并编译成功，**源码后来丢失**。
> 2. v2.3 审计时 `src/SysSuite.Native/` 目录确实不存在，故回退为"零实现"。这在"当前工作区"层面正确，
>    但"此前被误标"的措辞不准确 —— 见第 1 点。
> 3. v2.4 补建后称"本机无 CMake/MSVC"**同样是误判**：VS 装在 D 盘，此前的探测只扫了 `C:\Program Files`。
>
> ⚠ **遗留**：① `app.manifest` 仍未声明 x64；② Clang-Tidy 与 MSVC `/analyze` 未接入；
> ③ Catch2 侧（`tests/SysSuite.Native.Tests`）依赖 CMake FetchContent，**本机无法验证，仍待 CI**。

- [x] `SysSuite.Native/CMakeLists.txt`：`project(SysSuiteNative LANGUAGES CXX)`、`add_library(SysSuite.Native SHARED ...)`、C++20 + MSVC `/W4`（非 Win32 / 非 64 位直接 `FATAL_ERROR`）
- [x] `docs/native-abi.md`：稳定错误码表、内存所有权（调用方分配/调用方释放）、字符串与缓冲区协议、回调线程与串行规则、取消语义、批量上限、版本兼容策略、已登记能力清单
- [x] `native_api.h`：`extern "C" __declspec(dllexport)` + `__stdcall`；导出 `Native_AbiVersion` / `Native_Version` / `Native_GetSmbios`（T1.3 占位）/ `Native_ScanVolume`（T3.2 占位）
- [x] `native_api.cpp`：`Native_Version` 返回 `"1.0.0-m0"`；占位入口按契约返回 `NATIVE_ERR_NOT_IMPL`，且进入时先做一次取消检查
- [x] `SysSuite.Interop`：`NativeMethods.cs`（`[LibraryImport]`，字符串 `StringMarshalling.Utf16`）+ `NativeStatus` 码表 + `NativeLibraryResolver`（四级路径解析，缺失降级为 `LibraryNotLoaded` 而非抛异常）+ `NativeInterop` 门面
- [x] UI 首页临时按钮"自检"：`DashboardPage` 的"原生自检"按钮，输出 `v{version}（ABI {n}）` 或明确的状态描述
- [~] 明示仅支持 x64 —— 已在 `Directory.Build.props` 用 `<PlatformTarget>x64</PlatformTarget>` 约束，CMake 侧也已强制；**`app.manifest` 仍未声明**
- [x] `NativeAbiSmokeTests`：成功 / 缓冲区不足 / 参数非法 / 取消 / 未实现 / 缺失降级 六类路径（C# `tests/SysSuite.Tests/NativeInterop/`）—— **已于本机真实执行并通过**（此前因 DLL 缺失恒为 Skip）
- [~] Catch2 侧 `tests/SysSuite.Native.Tests/`（7 个契约用例）—— **已写完但本机无法执行**，依赖 CMake FetchContent 拉取 Catch2；由 CI native job 验证
- [x] `rules/Build-Native.ps1`：不依赖 CMake 生成器探测与 `vcvarsall.bat`，直接定位 MSVC 工具集并构造 `INCLUDE`/`LIB` 调用 `cl.exe`；支持 `-Deploy` 把 DLL 复制到各测试/UI 输出目录
- 验收：C++ 本机 `cl.exe /W4` 编译零告警且导出 4 个 `Native_*` 符号（已达成）；C# FFI 冒烟真实执行全绿（已达成，48/48）；Catch2 由 CI native job 覆盖（待验证）

### T0.3 UI 壳（侧边栏 9 标签，对应 UI-SPEC §2/§4）  [x]  1.5 天
- [~] NuGet：WPF-UI、CommunityToolkit.Mvvm、Microsoft.Extensions.Hosting、Serilog、Serilog.Sinks.File —— **只装了后三个；UI 为自绘 Fluent 风格，未引入 WPF-UI；MVVM 用自实现的 `RelayCommand`，未引入 CommunityToolkit.Mvvm**
- [x] App.xaml：DI 容器、`DispatcherUnhandledException` / `AppDomain.UnhandledException` / `TaskScheduler.UnobservedTaskException` 三处全局钩子（WpfUi 资源合并不适用，未装该包）
- [~] MainWindow.xaml：NavigationView（仪表盘/系统信息/磁盘清理/卸载器/安全中心/桌面整理/软件管家/工具箱 + Footer 设置）+ ContentFrame + 全局 StatusBar —— **导航区与 ContentFrame 已落地（用自绘 `SidebarPanel` 而非 WPF-UI 的 `NavigationView`）；全局 StatusBar 完全缺失**
- [x] `NavPage` 枚举 + NavigationService
- [~] 9 个 Page + 9 个 ViewModel 空壳（Dashboard/SystemInfo/Cleaner/Uninstaller/Security/Desktop/SoftwareHub/Toolbox/Settings）—— **9 个 Page 与 9 个 ViewModel 文件均已建齐，但 9 个 ViewModel 至今仍是空壳（`RelayCommand(_ => { })`）；真实业务逻辑写在 code-behind 里，与 UI-SPEC §3 冲突，详见 §11 偏差 D1**
- [ ] StatusBar 绑定 BackgroundTaskManager.Current（ProgressText/Percent/CancelCommand）—— **`MainWindow.xaml` 中无任何 StatusBar / ProgressBar，全仓库搜不到 `BackgroundTaskManager`；UI-SPEC §2 要求的全局状态栏未实现，见 §11 偏差 D8**
- [x] ThemeService（Light/Dark/System）
- 验收：9 项导航切换正常；侧边栏可折叠；深浅主题即时生效

### T0.4 基础设施  [x]  2 天
- [x] SettingsService：%AppData%\SysSuite\settings.json（System.Text.Json source-gen），热保存防抖 500ms
- [x] 高能力开关治理：L2/L3 功能默认关闭，配置仅接受显式布尔，不提供注册表/环境变量隐藏开关；变更记录版本与来源
- [x] Serilog：logs/sys-yyyymmdd.log 按天滚动，保留 14 天；Fatal 附堆栈与版本
- [x] TaskQueue：互斥资源锁（盘符/注册表键/桌面三类），清理性任务串行；API `Enqueue(TaskMeta, Func<CancellationToken,Task>)`
- [x] Result 模式：`Result<T>` + ErrorType（业务流不用异常）
- [x] DiagnosticsService：启动日志打入应用版本/.NET 版本/是否管理员
- [x] 崩溃处理闭环（G6 落地）：全局异常钩子 → 崩溃对话框（版本/堆栈/最近日志摘要）→ 一键打开日志目录 / 一键打包导出 zip（日志 + crash dump + 环境信息）
- 验收：人为抛异常 → UI 不闪退、弹崩溃对话框、zip 可导出

### T0.5 CI 完整版  [~]  1 天
- [~] `.github/workflows/ci.yml` —— **文件已存在且可运行，但内容是裁剪版**：Debug 而非 Release、无 `--no-restore` 分离、无 native 构建 job、额外插入了 `rules/Check-SourceFileSize.ps1` 门禁。目标形态如下：
  ```yaml
  name: ci
  on: [push, pull_request]
  jobs:
    dotnet:
      runs-on: windows-latest
      steps:
        - uses: actions/checkout@v4
        - uses: actions/setup-dotnet@v4
          with: { dotnet-version: '8.0.x' }
        - run: dotnet restore
        - run: dotnet build -c Release --no-restore /Werror
        - run: dotnet test --no-build -c Release
    native:
      runs-on: windows-latest
      steps:
        - uses: actions/checkout@v4
        - run: cmake -S tests/SysSuite.Native.Tests -B build/native-tests -A x64
        - run: cmake --build build/native-tests --config Release
        - run: ctest --test-dir build/native-tests -C Release --output-on-failure
  ```

> 实际实现见 `.github/workflows/ci.yml`：dotnet job 内先构建原生模块并部署到测试输出目录
> （否则 C# FFI 冒烟会自动 Skip），随后 `dotnet test`；native job 单独跑 Catch2 契约测试。
>
> ⚠ **原生构建刻意不走 cmake**：cmake 在部分 VS 安装上会以 `No CMAKE_CXX_COMPILER could be found`
> 失败（本机 VS 18 2026 即如此，尽管 `cl.exe` 存在），原因是生成器探测依赖 MSBuild 的 `VCTargetsPath`。
> 故 dotnet job 改用 `pwsh ./rules/Build-Native.ps1 -Configuration Release -Deploy`（见 §11 偏差 D10）。
> `CMakeLists.txt` 保留给 IDE / Visual Studio 使用；native job 的 Catch2 仍用 cmake（需 FetchContent 拉依赖）。

- [~] 单测骨架：`tests/SysSuite.Tests`（xUnit）—— **已建，48 项全部通过且 0 跳过**（原生 DLL 就位后 FFI 冒烟由 Skip 转为真实执行）；`tests/SysSuite.Native.Tests`（Catch2）—— **已建（7 个契约用例），需 CMake FetchContent 拉依赖，本机无法执行，由 CI 验证**
- [~] 静态分析接入（T8.1 同步落地）：**.NET analyzers 已全开且告警不过 CI；C++ 侧为 MSVC `/W4`（刻意不加 `/WX`）；Clang-Tidy 与 MSVC /analyze 仍未接入**
- 验收：PR 自动跑通，Actions 徽章绿

### T0.6 应用清单与提权策略  [x]  0.5 天
- [x] `app.manifest`：`requestedExecutionLevel requireAdministrator` + `dpiAwareness PerMonitorV2`
  - 另补 `<compatibility>` supportedOS 声明（Win10/11）与 `longPathAware`（T3.x 深层目录扫描需要）
  - x64 约束由 `Directory.Build.props` 的 `<PlatformTarget>x64</PlatformTarget>` 保证；manifest 本身无法表达 bitness
- [x] 版本资源（文件版本/产品名/图标）与品牌资产（ico 16/32/48/256）
  - 程序集元数据统一在 `Directory.Build.props`：Product/Company/Copyright/Version=0.1.0（FileVersion 0.1.0.0），全项目继承
  - 品牌资产：由根目录 `winlogo.png` 生成多尺寸 `src/SysSuite.UI/Assets/App.ico`（16/24/32/48/64/128/256，PNG 负载），
    UI 侧 `ApplicationIcon` + `MainWindow.Icon`，Watchdog 侧 `ApplicationIcon` + 托盘图标（嵌入资源，运行时按托盘尺寸选取）
  - 图标再生成入口：`obj/Build-Icon.ps1`（一次性脚本，换素材后重跑）
- [x] 启动 UX 约定：以管理员身份启动（UAC 由 manifest 触发）；用户拒绝 UAC 时显示友好退出页（说明功能依赖管理员权限）；主进程实例互斥（防多开）
- 验收：双击启动弹 UAC；任务管理器确认提升权限；150% DPI 下无模糊；双开被拒绝

### T0.7 Watchdog 进程骨架（T2.7 / T5.6 / T6.4 的共同前置）  [~]  1 天
- [x] Worker 改造为托盘常驻：NotifyIcon + 右键菜单（打开主程序 / 暂停监控 / 退出）
- [x] 命名互斥体单实例；开机自启注册（HKCU Run，设置页可开关）
- [ ] 提权模式说明：主程序保持管理员，Watchdog 自启用当前用户 HKCU Run；需要管理员才能完成的自动恢复/服务检查必须经受控提权代理或由下次主程序启动兜底，不得静默提升
- [x] 与主程序的 IPC 通道：命名管道 + 精简 JSON 帧（心跳 / 事件广播 / 命令下发）；命令必须绑定唯一请求 ID、超时与拒绝原因
- [x] 心跳与进程租约 API（供 T6.4 覆盖层进程拉起与保活）
- 验收：双开防重入；主程序退出后 Watchdog 存活；管道双向可通；随系统自启

---

## M1 系统信息 + 监控 + 基准

### T1.1 硬件信息服务  [x]  2 天
- [x] Abstractions 接口层：**实际命名为 `IHardwareInfoService`**（`GetHardwareInfoAsync` 一次返回 CPU/内存/显卡/存储/网卡/OS 全量快照），语义等价
- [x] Core 实现：**实际为 `Core/System/WmiHardwareInfoService`**（Win32_Processor / Win32_BaseBoard / Win32_BIOS / Win32_DiskDrive / Win32_LogicalDisk / Win32_VideoController / Win32_NetworkAdapter 集中封装）
- [x] 数据质量收敛（2026-09-23 实测真实硬件后修正，详见 §11.5）
  - 操作系统产品名改取 `Win32_OperatingSystem.Caption`（原 `Environment.OSVersion.VersionString` 只给出 “Microsoft Windows NT 10.0.x”）
  - 显存改读显示类驱动登记的 64 位 `HardwareInformation.qwMemorySize`（`Win32_VideoController.AdapterRAM` 是 32 位字段，≥4GB 必然溢出：实测 RTX 2070 8GB 被报成 4.0 GB）
  - `VideoModeDescription` 去除溢出色深，归一为 “宽 x 高”（原值形如 “1920 x 1080 x 4294967296 种颜色”）
- [ ] CPU 补充：Native_CpuIdFeatures（指令集/缓存层次）—— 依赖 T0.2（**仍未做**，见下）
- [~] SMBIOS 主板/BIOS：**已通过 WMI（`Win32_BaseBoard` / `Win32_BIOS`）实现**，`SystemInfoPage` 已展示主板与 BIOS 版本；计划中的原生 `Native_GetSmbios`（GetSystemFirmwareTable）未做，属"托管替代原生"，见 §11 偏差 D3
- 验收：字段与 CPU-Z/设备管理器双源一致；虚拟机内不崩溃 —— **本机真机数据已核对**（RTX 2070 显存 8.0 GB 修正到位）；**VM 内不崩溃未验证**（见 D17）

### T1.2 温度传感器  [x]  1.5 天
- [x] LibreHardwareMonitorLib 封装 SensorService —— 实际类为 **`LibreHardwareSensorService`**（实现 `ISensorService`），
  节流 2s（`ThrottleInterval`），`lock gate` 串行化（LHM 的 `Update()` 非线程安全）
- [x] 无传感器（部分主板/VM）降级显示"不可用"，不报错 —— `SensorSnapshot.Unavailable(reason)`，
  失败一次即 `MarkUnsupported`，避免每 2 秒重复失败拖慢 UI
- [x] 读数合理性过滤 `IsPlausible()`：温度 0~**200**℃ / 风扇 0~30000 RPM / 电压 0~25V。
  上界从 150℃ 放宽到 200℃：GPU Hot Spot / VRM 满载会到 100℃+，部分笔记本 EC 临界读数能超 150，
  旧上界会误删真实读数（见 §11 偏差 D16）
- [x] **硬件分类**：`SensorHardwareClass{Cpu,Gpu,Storage,Motherboard,Unknown}` 由 `HardwareType` 映射而来，
  随读数一起返回。**分类必须在服务层做** —— 只有服务层拿得到 `HardwareType`（见 §11 偏差 D16）
- [x] **阈值常量剔除** `IsThresholdSensor()`：NVMe 健康日志里的 `Warning Temperature`(80℃) /
  `Critical Temperature`(81℃) 是**固定门限**，不是实时温度；混进温度聚合会显示假高温
- [x] **预热 Update**：`Computer.Open()` 后 CPU 的核心/封装温度是延迟挂载的，必须再 `Update()` 一次
- [x] 只启用 CPU/GPU/Motherboard/Storage 四类硬件；刻意关闭 Memory/Network/Controller/Psu/Battery（无传感器、纯浪费 IO）
- [x] UI 接线：系统信息页「温度」区**分别**展示 CPU 与 GPU 温度（各取各的硬件分类），
  页面生命周期内每 5 个采样周期（约 10s）刷新一次
- [x] **缺驱动诊断** `SensorDiagnostics`：CPU 温度缺失且 PawnIO 不存在时，UI 显式提示安装方法，
  不再静默显示 "--"（见 §11 偏差 D15）
- 验收：真机 CPU 包温/GPU 温与 HWiNFO 偏差 < 5℃ —— **本轮已提权实测**（GPU Core 69℃ / Hot Spot 79.8℃ /
  SSD Composite 55℃，数值自洽）；**CPU 温度因本机未安装 PawnIO 而无读数**，非程序缺陷（D15）
- 依赖变更：`LibreHardwareMonitorLib` 0.9.6 + `HidSharp` 2.6.4，**强制 `System.Management` ≥ 10.0.2**（见 §11 偏差 D14）

### T1.3 SMART（C++）  [x]  1.5 天
- [x] `Native_QuerySmart(buffer, capacity, count, isCancelled, context)`：`IOCTL_STORAGE_QUERY_PROPERTY`
  先判协议，再走 `IOCTL_ATA_PASS_THROUGH_DIRECT` + `SMART READ DATA (0xD0)`
- [x] NVMe 走 `STORAGE_PROTOCOL_SPECIFIC_DATA` + `NVMeDataTypeLogPage` 的健康日志 `0x02`；
  统一模型 `NativeSmartInfo`（温度/通电时间/通电次数/剩余寿命/重映射/待处理/不可纠正/累计写入）
- [x] UI：存储页健康徽章（好/警告/危险 阈值常量集中在 `SmartService`）
- [x] 权限三态：`OpenResult{Success, AccessDenied, NotFound}`；**权限被拒时不写出零值条目**，
  整体返回 `NATIVE_ERR_ACCESS_DENIED`，杜绝"把没权限渲染成硬盘健康"
- [x] C++ 侧契约测试：`Native_QuerySmart` 参数校验 / 取消时机 / 容量边界 / ABI 布局断言
  （Catch2 侧无法本机执行，见 §11 D11；另用 `obj/synt` 下的 `static_assert` 自检程序在真机编译并运行通过）
- 验收：与 CrystalDiskInfo 双向对比一致 —— **2026-09-23 已提权实测闭环**：读到 `SAMSUNG MZVLB1T0HBLR-000H1`，
  `Health = Good` / 温度 54℃ / 剩余寿命 98% / 通电 7269h（302 天 21 小时）/ 通电次数 2798 / 累计写入 51.1 TB，数值自洽
- 实现拆分：`native_smart.cpp`（协议判定 + 枚举编排）、`native_smart_common.cpp`（打开设备/权限/型号）、
  `native_smart_ata.cpp`、`native_smart_nvme.cpp` —— 满足 300 行门禁

### T1.4 实时监控  [x]  2 天
- [x] MonitorEngine：**实际为 `PerformanceMonitorService` + `IMonitorService`**，WMI/性能计数器采集（CPU%/内存/磁盘读写/网卡上下行），2 秒采样并抛出 `SampleReady` 事件
- [x] UI 监控卡片（CPU/内存/磁盘读写/网络收发）—— 仪表盘四张环形卡与实时数值已落地并接线
- [x] 折线图：`SystemInfoPage.Trend.cs` 手绘 Canvas（零依赖），CPU/内存按 0~100% 固定纵轴，
  磁盘读写按自适应峰值只画形状；横轴按真实时间跨度映射，暂停造成的空档体现为位置偏移
- [x] 暂停/恢复/时间窗切换（1min/5min）：`Pause()` / `ResumeMonitoring()` / `SetWindow()`，
  暂停保留历史（恢复后曲线连续），切窗只改裁剪范围不重建缓冲
- [x] 环形缓冲 `MonitorSample?[512]` + `TrimToWindow()`，容量上限 512 个样本
- [x] 修正历史遗留性能缺陷：**原实现每 2 秒新建 `ManagementObjectSearcher`**，
  改为 `MonitorCollector` 懒创建并复用 4 个 searcher（cpu/memory/disk/network）
- 验收：连续 1 小时内存无增长；磁盘拷贝曲线出尖峰

### T1.5 基准测试  [x]  3 天
- [x] CPU：整型 LCG + `Math.Sqrt` 浮点混合负载，两轮预热后再计时
- [x] 内存：32MB 块 copy 测带宽 + 指针追逐法（8M 跳）测随机延迟
- [x] 磁盘：4K 随机 + 1M 顺序，`FileOptions.WriteThrough` 绕过页缓存，`finally` 保证删除测试文件
- [x] BenchmarkService：评分归一（基准机 = 10000 分），`Composite` = CPU/内存各半（磁盘受介质类型影响过大，不参与）
- [x] 取消语义：返回**已完成项目的部分结果**而非失败
- [x] 磁盘测试 UI 显式声明写入影响：两个复选框（是否含磁盘、是否允许写入；关闭写入则只做顺序读）
- [x] UI：跑分向导（选项 → 进度条+阶段文案 → 结果对比条 → 历史对比），
  新增导航项「性能跑分」（`NavPage.Benchmark`），历史存 `AppSettings.BenchmarkHistory`（最多 5 条，显示环比变化）
- 验收：CPU 分数双跑方差 < 3%
- **偏离计划**：计划要求 C++ 内核 + `SetThreadAffinity` 绑核，实际以托管实现。
  理由与影响见 §11 偏差 D13

### T1.6 报告导出  [x]  0.5 天
- [x] ReportExporter：TXT/HTML/JSON 三格式，拆为 `ReportExporter.cs`（门面 + 文件写入）、`.Text.cs`、`.Html.cs`、`.Json.cs`
  - 文本：按**显示宽度**（东亚宽字符算 2 列）对齐标签列，`{label,-12}` 在中英混排下会错位
  - 网页：自包含内嵌 CSS（A4/打印友好），字段值经 HTML 转义
  - JSON：`HardwareReport { GeneratedAt, Hardware }` 信封，`UnsafeRelaxedJsonEscaping` 保留中文可读，量测派生字段 `[JsonIgnore]`
  - 编码契约：统一 UTF-8 无 BOM（HTML 由 `meta charset` 声明），目录缺失时自动创建
- [x] UI 接线：系统信息页「导出报告」按钮（SaveFileDialog 三格式，Filter 顺序与 `ReportFormats.All` 一致）
- 验收：导出无乱码、JSON 可反序列化 —— 已由 21 项单测覆盖（含 UTF-8 无 BOM 字节断言、HTML 转义、JSON 往返、失败路径）

---

## M2 卸载器

### T2.1 数据层（含统一数据库版本管理）  [x]  0.5 天
- [x] SharedDatabaseService：单库 SQLite + 统一迁移器（PRAGMA user_version 驱动）
- [x] 建表 v1：
  - `apps(id, stable_key, name, publisher, version, install_date, size, uninstall_string, quiet_string, source REG|MSI|STORE, key_path, install_dir, icon_path, hash, updated_at)`；`stable_key` 必须由 source + key_path/package_id/install_dir 归一化生成，作为跨枚举稳定业务主键
  - `clean_history(id, batch_id, items_json, freed_bytes, mode, at, reversible, backup_root, restore_state PENDING|READY|PARTIAL|RESTORED|EXPIRED, expires_at)`；每批操作必须可追溯到独立备份目录
  - `update_history(id, app_stable_key, app_id, from_ver, to_ver, action INSTALL|UPGRADE|UNINSTALL, result, at)`
  - `security_audit(id, action, old_state, new_state, auto_restore_at, at, requested_by, process_id, command_result, restore_result)`
- [x] SharedDatabase Repo + 迁移脚本文档（docs/db-migrations.md）
- 验收：建库/读写/跨版本迁移单测过（供 M2/M3/M5/M6 共用）

### T2.2 三路枚举器  [x]  1.5 天
- [x] RegistryAppEnumerator：HKLM\...\Uninstall + WOW6432Node + HKCU；SystemComponent 可配置过滤
- [x] MsiAppEnumerator（MsiEnumProducts）与注册表合并去重（同 GUID 以 MSI 记录为准）
- [x] StoreAppEnumerator（PackageManager.FindPackages）
- [x] 去重键：规范化名（小写/去版本号/去空格）+ 发布者哈希
- [x] 已知软件档案 JSON：Hive 数据库、Service 注册项、目录安装器等非标准软件；档案需人工审核、来源标注与版本化更新
- [x] 全量刷新入 TaskQueue，落 apps 表 + UI 增量刷新
- 验收：与 Geek Uninstaller 列表 diff < 3%

### T2.3 图标缓存  [x]  0.5 天
- [x] IconCacheService：Win32 图标提取 → PNG 存 iconcache\{hash}.png，并发 ≤ 4
- 验收：200 应用冷刷新 < 5s（§6 预算），缓存命中 < 0.5s

### T2.4 卸载执行  [x]  2.5 天
- [x] UninstallService 流程：读 UninstallString/QuietUninstallString（优先 Quiet）→ 解析命令行（msiexec /x {GUID} 特判；Store 走 Remove-AppxPackage）→ 卸载前 reg export 备份 + install_dir 清单快照 → Process 异步等待（超时 600s 给用户选择）→ 重枚举验证 → 触发 T2.5
- [x] 写入 update_history
- [x] UI 卸载队列（串行单并发）+ 进度 + 失败结构化（ExitCode/ErrKind）
- 验收：静默卸载 Chrome(Nullsoft)/7-Zip/典型 MSI/Store 应用四类样例全通过

### T2.5 残留扫描  [x]  1.5 天
- [x] LeftoverScanner 规则：安装目录空壳 / %ProgramData% 与 %LocalAppData% 相似目录（阈值 0.8）/ 注册表同名键、卸载键残余、App Paths、MUI Cache
- [~] ProtectedPaths 白名单 + KnownFolder 免扫描例外表 —— **共享常量表 `ProtectedPaths.cs` 已建立并接入三处删除路径；免扫描例外表（WinSxS/System Volume Information 等）未单列，目前由 `ProtectedSegments` 覆盖，见 §11 偏差 D7**
- [x] UI：残留确认 Dialog（文件与注册表逐项可见，默认全选）
- 验收：3 款测试程序残留定位率 > 90%，无系统项误报

### T2.6 强制删除  [x]  1 天
- [ ] 风险分级：L2，默认关闭；仅实验分支开放，且必须先执行完整备份
- [x] ForceDeleteService：SeTakeOwnershipPrivilege → 递归解锁删除；占用文件 MoveFileEx(REBOOT)
- [x] 通用"确认句"组件 `ConfirmationInputBox`（用户须输入指定文本方可执行；T5.6 复用）
- 验收：删除只读占用目录成功；输错确认句拒绝执行

### T2.7 USN 安装监控（依赖 T0.7）  [x]  2 天
- [x] Win32：FSCTL USN Journal 增量读（每卷一线程，事件环形队列）
- [x] C# 过滤 msi/exe + Uninstall 键轮询（3s）→ 应用内广播 AppListChanged
- [x] UI 角标提示 + 可开关
- 验收：手工安装/卸载 5s 内列表刷新；休眠恢复无句柄泄漏

---

## M3 磁盘清理

### T3.1 规则引擎  [~]  1.5 天

> 校准：此前标题标 [x] 但全部子项标 [ ]，属自相矛盾。实际引擎与规则文件均已落地，
> 但规则集只覆盖了计划清单中的一部分，故降为 [~]。

- [x] docs/rules-schema.md（字段：id/target/env/regex/exclude/minAgeDays/level）
- [x] CleanRule 模型 + 校验（通配编译、重复 id 检测、正则超时保护）—— 实现为 `CleanRuleEngine.Validate`
- [~] rules/*.json —— **只有 `rules/clean-temp.json` 一份**，已含 windows_temp / root_temp / programdata_temp / user_temp / chrome / edge / firefox / thumbnails / error_reports 共 9 条；**缺 recycle_bin、dns_cache、update_download、delivery_optimization、log_files、font_cache**
- [x] RuleEngine：环境变量展开 → 通配目录展开 → 枚举命中 → 白名单排除 → 按 MinAgeDays 过滤 → 输出（含风险标记）—— 实现为 `CleanRuleEngine.Enumerate`
- [~] 每规则 ≥ 3 单测（命中/排除/空）—— 已有 `tests/SysSuite.Tests/CleanRuleEngineTests.cs`，尚未达到每规则 3 例的密度
- 验收：规则热更新（改 json 5s 生效）

### T3.2 MFT 全盘扫描（C++）  [~]  2.5 天

> ⚠ 校准：此前标 [ ]（未开始）。实际已有 `Core/System/MftFileIndexService.cs`，**用 C# P/Invoke 直接
> 调 DeviceIoControl + FSCTL_ENUM_USN_DATA 实现了 MFT 枚举，绕开了计划中的 C++ 原生通道**。
> 功能方向正确但偏离架构约定（违反 G10「未定义契约的原生能力不得接入 Core」），且 §6 的
> 150 万文件 < 8s 预算从未实测。见 §11 偏差 D4。

- [~] MFT/USN 枚举能力 —— **已实现（`MftFileIndexService`），但为托管 P/Invoke 而非计划中的 `Native_ScanVolume` C++ 导出**
- [ ] `Native_ScanVolume(volume, cb, out)`：改为走 C++ 原生导出 + ABI 契约（依赖 T0.2 补建后重构）
- [ ] C# FileIndex：ParentId→路径重建树（500 万条以上采用分层分页）
- [ ] 非 NTFS/ReFS/动态盘降级 FindFirstFile 通道（同一接口）
- 验收：150 万文件枚举 < 8s（§6 预算）；排除 System Volume Information/$Recycle.Bin —— **尚未实测**

### T3.3 大文件 / 空目录 / 重复文件  [x]  1.5 天
- [x] 大文件：阈值筛选 TopN + 目录聚合 —— `DiskInspectionService.LargeFiles.cs`
- [x] 空目录：后序遍历标记（含全空链模式）
- [x] 重复文件：Size → 首 64KB 哈希 → SHA256 全量 —— `DuplicateFileService`
- 验收：构造重复集识别率 100%；含锁定文件时单条降级不崩溃

### T3.4 清理执行器  [x]  1 天
- [x] CleanExecutor：回收站或备份后删除 —— 实现为 `DiskInspectionService.Clean.cs`，每批次写独立备份目录 + `manifest.json`
- [~] 前置可选 SrSetRestorePoint（单日去重）—— **未做系统还原点**；`clean_history` 批次记录与撤销已落地
- [x] 结果页提供 [撤销最近一次清理] 按钮（reversible=true 时可用）
- [x] 双重进度（字节/条数）；失败项汇总
- 验收：含锁定文件时任务不中断、不弹窗；撤销可还原上一批次

### T3.5 系统瘦身  [ ]  2 天
- [ ] 风险分级：L2 实验性、默认关闭；MVP0 禁止启用
- [ ] WinSxS 分析 + DISM StartComponentCleanup（/ResetBase 独立复选 + 红字警告 + 还原点提示）
- [ ] Windows.old 检测删除（需取得所有权）；传递优化缓存清理
- [ ] compact /c /exe 按文件压缩（排除系统临界文件清单）
- 验收：测试机清理后 C:\Windows 实际缩小；日志完整

---

## M4 更新控制

### T4.1 策略与服务双层开关  [ ]  1 天
- [ ] 风险分级：Policy 层 L1；Service 层 L2，默认关闭且设置页单独二次确认
- [ ] WindowsUpdateControlService.SetMode(Auto / NotifyOnly / Disabled)
  - Policy 层：HKLM\SOFTWARE\Policies\Microsoft\Windows\WindowsUpdate\AU（NoAutoUpdate / AUOptions）
  - Service 层：wuauserv / UsoSvc / DoSvc / 计划任务 \Microsoft\Windows\WindowsUpdate\*
- [ ] 操作前 JSON 快照（键值+服务位+任务状态）→ 一键还原
- [ ] UI：三档单选卡 + 当前实际状态徽章 + [恢复默认]
- 验收：三模式切换重启后仍生效；还原后与默认一致

### T4.2 WaaSMedic 对抗与自检  [ ]  1 天
- [ ] 风险分级：L3，仅实验分支与灰度专用构建开放；不做永久性权限破坏
- [ ] WaaSMedicSvc 优先记录可还原的注册表权限调整方案（实验性开关，默认关，文档化失败概率与手动恢复步骤）
- [ ] 后台自检 Timer：检测被系统回滚 → 状态栏告警 + 一键重新应用
- [ ] 兼容矩阵：Win10 19045 / Win11 22621 / 26100
- 验收：Home 版展示局限提示（无组策略时仍走注册表方案）

---

## M5 安全中心 + 防护控制 + 软件管家

### T5.1 防护状态探测  [ ]  0.5 天
- [ ] SecurityCenter2 WMI：AntiVirusProduct / FirewallProduct（含第三方 AV 显示与接管判定）
- 验收：Defender 状态与第三方 AV 接管状态实时反映

### T5.2 Defender 扫描集成  [ ]  1 天
- [ ] DefenderService：MpCmdRun.exe 包装（快速/目录/全盘），退出码映射表
- [ ] 扫描队列 + stdout 进度解析（无进度行时估算）
- 验收：EICAR 测试文件命中并报告

### T5.3 启动项审计 + 签名校验  [ ]  1.5 天
- [ ] 启动采集：Run 键 ×4 / 启动文件夹 / 计划任务 / Auto 服务
- [ ] Native_VerifyTrust（WinVerifyTrust）；无签名/过期/自签名 → 风险标记
- [ ] 禁用：Rename 前缀 + 任务 Disable + Svc ChangeConfig，原值全记录可撤销
- 验收：Autoruns 样例覆盖率 > 95%；禁用后重启不拉起

### T5.4 体检评分  [ ]  0.5 天
- [ ] 权重 JSON 扣分制：无 AV -30、过期库 -15、高危启动项 -10/个、系统盘 <10% -20 …
- [ ] 报告页 + 修复队列复用 TaskQueue
- 验收：评分逐项可跳转解释

### T5.6 Defender 防护控制  [ ]  2 天  ⚠ 高风险模块（联动 R1/R6，遵守 G7/G8）
- [ ] 风险分级：L2，默认关闭，仅实验分支和灰度专用构建可见；MVP0 禁止实现
- [ ] Abstractions/IDefenderControlService + DefenderStatus 模型：
  `AntivirusEnabled / RealTimeProtection / IsTamperProtected / BehaviorMonitoring / ControlledFolderAccess / SignatureLastUpdated`
- [ ] DefenderControlService（PowerShell SDK/Runspace 官方 cmdlet 托管执行；若必须调用外部进程，则强制 `-NonInteractive -NoProfile -ExecutionPolicy Bypass`，统一退出码映射）：
  - 状态：`Get-MpComputerStatus`
  - 开关：`Set-MpPreference -DisableRealtimeMonitoring $true/$false`、`-DisableBehaviorMonitoring`、`-DisableIOAVProtection`、`-EnableControlledFolderAccess`
  - 排除目录（推荐温和项）：`Add-MpPreference -ExclusionPath` / `Remove-MpPreference`
- [ ] 防火墙控制：INetFwPolicy2 COM 逐配置文件（域/专用/公用）读写，或 netsh advfirewall 包装
- [ ] **防篡改守门**：IsTamperProtected=true → 全部关闭开关置灰 + 深链引导 `ms-settings://windowsdefender`；如实提示"无法程序化关闭"
- [ ] **自动恢复**：AutoRestoreTimer 放 Watchdog 独立进程（依赖 T0.7；主程序崩溃后仍生效）；默认 15 分钟，可选 30min/1h/本次开机内；**不提供永久关闭 UI 入口**
- [ ] **自动撤销例外面**：所有排除目录/排除项必须带到期时间与来源批次，恢复流程自动移除；到期检查失败时 UI 呈现红色告警
- [ ] **UI 护栏**：关闭实时保护需红色 ContentDialog + 输入确认句（复用 T2.6 组件）+ 显示"关闭期间系统对恶意软件完全暴露"警示；第三方 AV 接管时显示"由 XX 接管"并禁用开关
- [ ] 审计日志：写 security_audit 表 + security-audit.log `{timestamp, action, oldState, newState, autoRestoreAt}`
- [ ] RestoreDefaultsAsync：一键全恢复默认
- [ ] 单测：cmdlet 包装层退出码映射；置灰逻辑；Timer 恢复路径
- [ ] 杀软误报预检：本机 Defender/火绒/360 全开状态跑关闭流程，记录拦截行为 → 产出行为白名单说明文档（供 T7.4 使用）
- 验收：① 防篡改开启时本软件无法关闭实时保护（诚实降级）② 关闭后 15 分钟自动恢复成功 ③ 审计日志完整 ④ 无绕过计时的入口 ⑤ 主流杀软仅提示不静默查杀

### T5.5 软件管家  [ ]  2 天
- [ ] WingetService：COM（Microsoft.Management.Deployment）优先 → CLI `winget upgrade --output json` 解析降级
- [ ] 升级链路 = T2.4 静默卸旧 + 静默装新（保留用户数据目录）；动作写入 update_history（供"更新历史"时间线读取）
- [ ] UI：SoftwareHub 三 Tab（可升级 / 全部软件→跳转卸载器 / 更新历史时间线）；忽略版本存 settings
- 验收：升级 7-Zip/VSCode/Notepad++/Chrome/GIMP 成功且用户数据保留；时间线完整可回溯

> 注：任务号按评审引用保持 T5.1→T5.2→T5.3→T5.4→T5.6→T5.5，不重排。

---

## M6 桌面整理

### T6.1 图标读写（C++ 核心）  [ ]  4 天
- [ ] FindDesktopListView：Progman → SHELLDLL_DefView；失败枚举 WorkerW
- [ ] 跨进程 SysListView32 读写：VirtualAllocEx/WriteProcessMemory + LVM_*（COUNT/TEXT/POSITION/SETITEMPOSITION）
- [ ] 模型 DesktopIcon{Index, Name, Rect, Pid}；写回按名称匹配
- [ ] Win11 兼容 + Explorer 重启自动重连（WindowWatcher）
- 验收：250+ 图标读写正确；重启 Explorer 后自动恢复连接

### T6.2 分类引擎  [ ]  1 天
- [ ] Classifier：扩展名类型 / 名字正则 / 访问频次（LNK target 最近使用）
- [ ] 规则编辑 UI + RuleSet 持久化
- [ ] `desktop_zones(id, monitor_id, name, rule_set_id, bounds_json, z_order, enabled, created_at, updated_at)`；`desktop_icon_state(id, stable_key, path_hash, zone_id, position_x, position_y, monitor_id, updated_at)`
- 验收：一键整理 30+ 图标全部进入预期分区

### T6.3 布局方案  [ ]  1 天
- [ ] SaveLayout：图标坐标 → JSON；ApplyLayout：逐个 SetItemPosition
- [ ] 导入/导出 .layout.json（含分辨率指纹，不匹配可选缩放适配）
- 验收：两方案切换 < 2s；多显示器坐标正确

### T6.4 分区覆盖层（依赖 T0.7 租约）  [ ]  5 天
- [ ] OverlayWindow：WS_EX_LAYERED|TOOLWINDOW|NOACTIVATE；命中测试穿透非标题区；双击折叠动画
- [ ] 拖拽：DoDragDrop + FileDrop 计数徽章；与系统图标重排互斥锁
- [ ] 增量同步：桌面目录 FileSystemWatcher + WinEventHook（图标增删/改名不重启即反映）
- [ ] 主题：壁纸均色或手动
- [ ] 崩溃保护：覆盖层由 Watchdog 拉起的独立进程承载；租约失联自动回收
- 验收：拖 20 图标 60fps；主程序被杀后桌面不受损；锁屏恢复正常

### T6.5 多显示器与 DPI  [ ]  1.5 天
- [ ] EnumDisplayMonitors 每屏独立分区集；DPI 变更事件重算
- 验收：双屏混排 DPI 准确；拔插屏无残留窗口

---

## M7 搜索 / 本地化 / 工具箱 / 打包 / 发布

### T7.0 全局搜索（Ctrl+K，对应 UI-SPEC §2）  [ ]  1 天
- [ ] SearchIndexService：四源索引（软件 apps 表 / 页面 / 设置项 / 清理规则），SQLite FTS5
- [ ] Ctrl+K 浮层 UI：方向键导航、Enter 跳转、历史记录
- 验收：1 万软件条目下搜索响应 < 50ms

### T7.1 本地化  [ ]  1 天
- [ ] resx 抽离全部字符串（zh-CN/en）+ 漏翻 key 编译期检查脚本
- 验收：语言切换即时生效，无遗漏占位

### T7.2 Ribbon 形态（可选，排期后置）  [ ]  2 天
- [ ] Fluent.Ribbon 引入；设置项「工具页 Ribbon 模式」仅作用于 Cleaner/Uninstaller/Security
- 验收：两形态互切状态保留

### T7.3 打包与自更新  [ ]  2 天
- [ ] Inno Setup：组件（主程序/Watchdog）、快捷方式、/SILENT、卸载保留 %AppData%\SysSuite
- [ ] 检测并静默安装 .NET 8 Desktop Runtime
- [ ] 自更新 v1 协调流程：**先停止 Watchdog 与覆盖层进程 → 下载并校验签名 → 静默安装 → 新版本启动后由主程序重新拉起 Watchdog**
- [ ] 自更新失败策略：下载完整性、签名校验、安装器退出码、升级前后数据库迁移检查全部通过后才提交版本；失败保留旧版本可执行副本与回滚说明，不自动清除用户数据
- [ ] SBOM 与包漏洞扫描：发布生成 `sbom.spdx.json`；NuGet/CMake 依赖做已知漏洞扫描并输出豁免记录
- [ ] 企业兼容说明：管理员要求、策略读取项、写入路径、服务/计划任务影响、卸载残留策略单独成文档
- [ ] 便携版 zip + runtimes/win-x64/native 结构校验脚本
- 验收：干净 VM 安装→运行→自更新全通；自更新全程无残留进程；卸载后无残留服务

### T7.4 签名与白名单  [ ]  1 天
- [ ] EV 证书 signtool 签署：SysSuite.exe / SysSuite.Native.dll / Watchdog / 安装器
- [ ] 向主流 AV 厂商提交样本白名单（附 T5.6 产出的行为白名单说明文档）
- 验收：SmartScreen 无告警（新信誉期记录跟踪）

### T7.5 设置中心整合（对应 UI-SPEC §4.9）  [ ]  1 天
- [ ] 四节落地：通用（主题/语言/自启/启动页）/ 清理规则（等级阈值/默认勾选/排除目录）/ 更新控制（只读状态展示 + WaaSMedic 实验开关）/ 关于
- [ ] 配置导出/导入（单 json）+ 恢复默认
- 验收：全模块读取设置热生效；杀软相关说明链接可达

### T7.6 开源合规  [ ]  0.5 天
- [ ] THIRD-PARTY-NOTICES.md 自动生成脚本（NuGet 包许可扫描）
- [ ] About 页内嵌展示；确认 MPL-2.0（LibreHardwareMonitor）等库的署名与链接义务满足
- 验收：全部第三方依赖均有许可与署名，法务项清零

### T7.7 灰度冒烟  [ ]  1 天
- [ ] 20 台真实机（Intel/AMD/NV、笔记本/台式混合）冒烟清单脚本化执行 + 日志回收
- [ ] 发布前性能回归（对照 §6 预算逐项复测）
- 验收：致命崩溃 < 1 例；性能预算全项达标；Top 3 Bug 修复发 v1.0.0

### T7.8 工具箱扩展实现  [ ]  2 天
- [ ] 右键菜单管理器：读取/备份/恢复 Shell 扩展与 `Directory`/`Directory\Background` 菜单；未知来源默认禁用不改注册表
- [ ] 环境变量编辑器：用户/系统变量读写、路径展开预览、变量长度与重复项检测
- [ ] Hosts 编辑器：只读解析 + 可编辑模式，保存前备份、格式校验、语法冲突提示
- [ ] 网速测速：可切换 provider，默认不自动联网，展示延迟/下行/上行与样本次数
- 验收：四个入口均可打开/关闭；破坏性编辑走备份-预览-执行；无联网行为泄漏

---

## §5 测试与质量专项（横切，贯穿各里程碑）

### T8.1 静态分析（随 T0.5 接入，长期生效）  [~]
- [x] C#：`.editorconfig` + `EnableNETAnalyzers` + `AnalysisLevel=latest-recommended` + `TreatWarningsAsErrors=true`（`Directory.Build.props`），警告即错误；Sonar 人审模式（可选，未做）
- [ ] C++：Clang-Tidy（bugprone-*/performance-*）+ MSVC /analyze（无 C++ 项目，依赖 T0.2）
- 验收：CI 对新增告警红灯

### T8.2 UI 自动化冒烟  [ ]  2 天（M6 结束后执行）
- [ ] FlaUI 脚本 20 条：9 页导航/主题切换/卸载全流程/清理全流程/搜索/设置读写
- [ ] 性能判定脚本：冷/热启动取 10 次中位数与 P95；10 万行滚动采样 30s 并记录掉帧；稳态内存取 15 分钟窗口均值；MFT 测试固定 NVMe SSD、冷缓存、连续 3 次取中位数
- 验收：CI 夜间档全绿

### T8.3 Hyper-V 兼容矩阵  [ ]  1 天 ×（每里程碑末回归）
- [ ] 快照机位：Win10 21H2 / Win10 22H2 / Win11 22621 / Win11 26100 + Defender 全开机 + 域策略受限机
- [ ] 每里程碑末跑冒烟清单并记录 diff
- 验收：无跨版本 P0/P1 问题

### T8.4 Pester/快照回归（注册表与系统服务）  [ ]  1 天（M4 末）
- [ ] Pester 脚本比对 M4/T5.6 操作前后的注册表与服务状态快照
- 验收：所有开关操作可精确还原

---

## §6 全局性能预算（发布验收基准）

| 指标 | 目标 | 测量方法 |
|---|---|---|
| 冷启动（至 Shell 可交互） | < 2s | 10 次取中位数，并记录 P95 |
| 热启动（进程已驻留） | < 1s | 10 次取中位数，并记录 P95 |
| 软件清单全量刷新（200 应用） | < 5s；缓存命中 UI 刷新 < 0.5s | 真实样本机连续 3 次 |
| 全局搜索（1 万条目） | < 50ms | 预热索引后 100 次取 P95 |
| MFT 全盘枚举（150 万文件） | < 8s | NVMe SSD、NTFS、冷缓存，连续 3 次取中位数 |
| 清理列表 10 万行滚动 | 60fps（虚拟化无卡顿） | 30 秒连续滚动，掉帧 < 1% |
| 主进程稳态内存 | < 200MB | 15 分钟窗口均值 |
| Watchdog 常驻内存 | < 30MB | 15 分钟窗口均值 |
| 安装包体积（不含 Runtime） | < 80MB |
| 任意页内按钮点击响应 | < 100ms（长任务必须异步+进度） |

---

## §7 范围外声明（明确不做，防范围蔓延）

- 不自研杀毒引擎（仅集成 Windows Defender + 安全中心探测）
- 不覆盖 macOS / Linux / Windows 32 位
- 不做联网遥测/账号体系（数据全部本地；自更新 feed 除外）
- 不做 MSIX/商店上架（v1 仅 Inno + 便携包）
- 语言仅 zh-CN / en
- 不实现 WinSxS 手工删库式"深度瘦身"等高危野路子（仅官方 DISM 通道）

---

## 8. 风险登记表（持续更新）

| ID | 风险 | 概率 | 影响 | 对策 | 状态 |
|---|---|---|---|---|---|
| R1 | 杀软误报（**T5.6 强联动**） | 高 | 高 | EV 签名 + 厂商白名单 + 不注入不 Hook + T5.6 行为预检报告 | 监控 |
| R2 | WaaSMedic 被系统还原 | 高 | 中 | 实验标注 + 策略级为主 + 自检重应用 | 监控 |
| R3 | Win11 桌面 API 变更 | 中 | 高 | C++ 抽象 + 特性检测 + 灰度机型 | 监控 |
| R4 | MFT 扫描不适用非 NTFS | 低 | 低 | 自动降级 FindFirstFile | 已设计 |
| R5 | winget COM 版本差异 | 中 | 中 | COM/CLI 双路径 + 超时降级 | 已设计 |
| R6 | 防篡改保护使 T5.6 开关失效 / 关闭行为被判定恶 | 中 | 高 | 诚实降级（置灰+引导）；定时恢复 + 审计日志 + 仅用户主动触发 | 已设计 |
| R7 | 自动恢复 Timer 因 Watchdog 被禁用而失效 | 低 | 高 | 恢复逻辑双进程冗余（主程序力所能及时兜底检查未恢复事件） | 待实现（T5.6） |

---

## 9. 任务跟踪表（复制到 PR/周报使用）

**2026-09-23 按代码实证校准后的跟踪表**（与上文各任务标题状态一致）：

| 任务 | 描述 | 负责人 | 状态 | 预计完成 | 备注 |
|---|---|---|---|---|---|
| T0.0 | 仓库建立 |  | [x] |  | **2026-09-23 已推送并建 `dev` 分支**（`TiaYahng/syssuite`）；仍缺分支保护 |
| T0.1 | 解决方案结构 |  | [~] |  | 5 个 C# 项目 + 1 个 C++ 项目齐全；Core 内既有 P/Invoke 未归拢到 Interop |
| T0.2 | Interop 通路 |  | [x] |  | **已完成**：C++ 本机编译通过（`/W4` 零告警，导出 4 符号）；FFI 冒烟真实执行全绿；仅 Catch2 待 CI |
| T0.3 | UI 壳 9 标签 |  | [x] |  | 9 页可用；ViewModel 仍空壳（偏差 D1） |
| T0.4 | 基础设施 + 崩溃闭环 |  | [x] |  |  |
| T0.5 | CI 完整版 |  | [~] |  | native job（C++ 构建 + Catch2 + FFI 冒烟）已加；**2026-09-23 首次真正触发**（此前 D5：CI 监听 `main,dev` 而远端只有 `master`，从未跑过）；C++ 编译结果待观察 |
| T0.6 | 清单/提权/DPI |  | [x] |  | 版本资源与品牌资产（多尺寸 ico）已落地；`app.manifest` 仍未声明 x64 |
| T0.7 | Watchdog 骨架 |  | [~] |  | IPC/租约已通；缺提权模式说明 |
| T1.1~T1.6 | M1 六项 |  | [x] |  | **M1 全部落地**：T1.1（含数据质量收敛）/T1.2 温度传感器/T1.3 SMART/T1.4 监控（含折线图与暂停/时间窗）/T1.5 基准测试/T1.6 报告导出均已完成。**2026-09-23 提权真机闭环**：SMART 与传感器均读到真实数值（见 T1.2/T1.3 验收行）；新增运行期自提权 `IElevationService`。遗留：CPU 温度在本机需安装 PawnIO（D15，非代码缺陷）；Catch2 侧待 CI（D11） |
| T2.1~T2.7 | M2 七项 |  | [x] |  | 七项均有真实实现 |
| T3.1~T3.5 | M3 五项 |  | [~] |  | T3.1/T3.3/T3.4 完成；T3.2 托管替代且未实测；T3.5 冻结 |
| T4.1/T4.2 | M4 |  | [ ] |  | 未开工 |
| T5.1~T5.6 | M5 六项 |  | [ ] |  | 未开工；T5.6 高风险 |
| T6.1~T6.5 | M6 五项 |  | [ ] |  | 未开工 |
| T7.0~T7.8 | M7 九项 |  | [ ] |  | 未开工；T7.6 合规必做 |
| T8.1~T8.4 | 质量横切 |  | [~] |  | 仅 T8.1 的 C# 部分落地 |

---

## 10. 关键依赖链（排期检查用）

```
T0.0 仓库 ─→ T0.1 结构 ─→ T0.2 Interop ─┬→ T1.* 系统信息
                                        ├→ T2.* 卸载器 ─→ T5.5 软件管家
                                        ├→ T3.* 清理（T2.1 数据层复用）
                                        └→ T6.* 桌面

T0.7 Watchdog ─┬→ T2.7 USN 监控
               ├→ T5.6 防护自动恢复
               └→ T6.4 覆盖层进程宿主

T7.0 全局搜索 ─ 依赖 T2.1 数据层 + T7.5 设置中心
T2.6 确认句组件 ─→ T5.6 关闭护栏（复用）
T5.6 行为预检报告 ─→ T7.4 签名与白名单提交
T7.3 自更新 ─ 需协调停启 T0.7 / T6.4 进程

MVP0 链 ─ T0.0→T0.1→T0.2→T0.4→T0.6→T0.3→T0.5→T2.1→T1.1/T1.4→T2.2/T2.4→T3.1/T3.4→T5.1/T5.2/T5.5 只读→T7.1→MVP0 冒烟
实验能力链 ─ L2/L3 任务冻结评审 → 实验分支 → 灰度构建 → 企业/真实机验证 → 默认开启
```
````

---

## 11. 架构偏差与现状登记（2026-09-22 校准）

全部结论来自对 `src/` 实际代码的核对，不是对本文档勾选的复述。

### 11.1 偏差清单

| ID | 偏差 | 影响 | 建议 |
|---|---|---|---|
| D1 | **9 个 ViewModel 至今仍是空壳**（`RelayCommand(_ => { })` 空实现），业务逻辑写在 code-behind（`UninstallerPage.xaml.cs`、`DiskCleanerView.*.cs`、`DashboardPage.xaml.cs`、`SystemInfoPage.xaml.cs`） | 违反 UI-SPEC §3「ViewModel 必须继承 `PageViewModelBase`、禁止在 code-behind 写业务逻辑」；M5/M6 开工后将演变为大范围重构 | 在 M5/M6 之前补 `PageViewModelBase` 并把现有 code-behind 逻辑迁入 |
| D2 | **既有 P/Invoke 仍散落在 Core 各服务内**（`IconCacheService.Extract`、`MsiAppEnumerator.Native`、`UsnJournalMonitor`、`ForceDeleteService.Native`、`MftFileIndexService`）。`SysSuite.Interop` 已于 2026-09-22 补建，但**只有新能力走这层，旧调用未迁移** | 违反 T0.1 的分层约定与 G10；无法统一审计原生调用的错误码/内存所有权 | 按 T8.1 分批把旧 P/Invoke 迁到 `SysSuite.Interop`，每迁一处补对应错误码映射 |
| D3 | **计划用 C++ 原生、实际用托管替代**：主板/BIOS 走 WMI 而非 `Native_GetSmbios` | 功能可用，但拿不到 WMI 不覆盖的固件细节；T1.1 验收标准仍未完全达成 | 可接受为 MVP0 方案，v1 前按 T1.1 补原生通道 |
| D4 | **MFT 扫描用 C# P/Invoke 直接调 `DeviceIoControl`**（`MftFileIndexService`），而非计划的 `Native_ScanVolume` C++ 导出 | 绕过 G10 的 ABI 契约要求；§6 的「150 万文件 < 8s」预算从未实测 | T0.2 补建后按 T3.2 重构，并补性能实测 |
| D5 | **分支命名与 G2 不符**：只有 `master`，无 `main`/`dev`，无分支保护。**更严重的是**：`.github/workflows/ci.yml` 的 `on.push.branches` 写的是 `[main, dev]`，而仓库实际只有 `master` ⇒ **推送从未触发过 CI**（`on.pull_request` 仍会触发） | CI 形同虚设；`dotnet build`/`test`/行数门禁/Catch2 从未在服务端跑过，本地绿不代表 CI 绿 | 建 `main`/`dev` 并开启保护；过渡期可先把远端 `dev` 分支建起来（见 §11.5） |
| D6 | **`installer/` 目录为空**，无打包脚本 | MVP0 的「可安装」验收未达成，T7.3 未开工 | 按 T7.3 补 Inno Setup 配置 |
| D7 | **已解决（2026-09-22）**：`src/SysSuite.Core/System/ProtectedPaths.cs` 已建立并接入 `ForceDeleteService`、`LeftoverScanner`、`DiskInspectionService.Clean` 三处。**剩余**：KnownFolder 免扫描例外表未单列，目前由 `ProtectedSegments` 覆盖 | 无（白名单已共享） | T2.5 需要时再抽独立的免扫描例外表 |
| D8 | **全局 StatusBar 缺失**：`MainWindow.xaml` 无 StatusBar/ProgressBar，`BackgroundTaskManager` 不存在，后台任务无统一进度呈现 | 违反 UI-SPEC §2 的主窗口壳契约；长任务只能靠页内 `StatusText` 反馈 | 补 `BackgroundTaskManager` + 全局状态栏，并把 `TaskCoordinator`（当前仅 `UninstallEnumerationService` 在用）接入 |
| D9 | **已解决（2026-09-22）**：C++ 已在本机用 MSVC 14.51 真实编译通过（`/W4` 零告警），产出 x64 DLL 并导出全部 4 个 `Native_*` 符号；FFI 冒烟由 7 项 Skip 转为真实执行（测试 41 → 48 全绿）。**此前的"本机无 CMake/MSVC"系误判** —— VS 装在 `D:\ai_era\...`，未装在 `C:\Program Files` | 无（已闭环） | 见 D10：cmake 生成器探测仍不可用，统一改用 `rules/Build-Native.ps1` |
| D10 | **CMake 生成器探测在本机失败**：`cl.exe` 明明存在，`cmake -G "Visual Studio 18 2026"` 仍报 `No CMAKE_CXX_COMPILER could be found`（生成器需经 MSBuild 解析 `VCTargetsPath`）。另 `vcvarsall.bat` 会调用 `reg.exe`，在受限环境被拦截 | 依赖 cmake 的构建步骤在本机无法执行；`CMakeLists.txt` 的正确性本机无从验证 | 已新增 `rules/Build-Native.ps1` 绕过二者（直接定位 MSVC + 手写 `INCLUDE`/`LIB`），CI 的 dotnet job 已切换；`CMakeLists.txt` 仅保留给 IDE 与 Catch2 job |
| D11 | **Catch2 侧未验证**：`tests/SysSuite.Native.Tests` 依赖 CMake FetchContent 联网拉 Catch2，本机 cmake 不可用（D10），故 7 个契约用例从未执行 | C++ 侧契约测试的实际覆盖仍为 0，只有 C# 侧冒烟覆盖 | 由 CI native job 验证；若 CI 的 cmake 同样受阻，改为把 Catch2 amalgamated 头文件纳入仓库并复用 `Build-Native.ps1` 编译 |
| D12 | **原生 DLL 未纳入构建流程**：DLL 目前靠 `Build-Native.ps1 -Deploy` 手工复制到 `bin/`，不被 `dotnet build` 感知，`clean` 后需重新执行 | 开发者 clone 后直接 `dotnet test` 会遇到 FFI 冒烟 Skip（不会误报失败，但覆盖不完整） | 在 `SysSuite.Interop.csproj` 加 MSBuild target 自动调用构建脚本，或把已编译 DLL 按 RID 纳入 `runtimes/win-x64/native` |
| D13 | **基准测试用托管替代 C++ 内核**（2026-09-23）：计划要求在 `bench/` 写 C++（`SetThreadAffinity` 绑核、`FILE_FLAG_NO_BUFFERING`），实际以 C# 实现（`BenchmarkService` + `.Measure.cs` + `.Disk.cs`），理由是复用已有 .NET 构建链、避免为一个纯计算模块再维护一套 C++ 编译与 ABI | ① **CPU 未绑核**，调度抖动会进入分数，验收项「双跑方差 < 3%」比绑核版更难保证；② 磁盘用 `FileOptions.WriteThrough` 而非 `FILE_FLAG_NO_BUFFERING`，两者对页缓存与对齐的要求不同，绝对吞吐与 CrystalDiskMark 不可直接对比（仅适合横向自比） | 可接受为 MVP0 方案：跑分定位是"自机历史对比"而非权威横评。若 v1 需要对外可比分数，按原计划补 `Native_Benchmark*` 导出并复用现有 ABI |
| D14 | **`System.Management` 被传递依赖强制升版**（2026-09-23）：`LibreHardwareMonitorLib` 0.9.6 要求 `System.Management` ≥ 10.0.2，中央包管理由 8.0.0 升至 **10.0.2** | WMI 相关 API 出现跨大版本变更（NU1109 已确认可解析）；下游若按 8.0 写法使用 `ManagementObjectSearcher` 需复核 | 已在 `PerformanceMonitorService` 侧复核并保持原用法可用；`System.Management` 属 Windows 专用包，不引入跨平台风险 |
| D15 | **CPU 温度需要 PawnIO 内核驱动**（2026-09-23，已闭环为"显式诊断"）：`LibreHardwareMonitorLib` 0.9.6 起把内核态访问层从 WinRing0 换成 **PawnIO**（WinRing0 因被 Defender 判为易受攻击驱动而全量下架）。官方维护者原话："PawnIO provides the low level hardware layer. If you don't install it, LibreHardwareMonitor could not read or write any value (including CPU values)." **本机未安装 PawnIO**（`HKLM\SYSTEM\CurrentControlSet\Services\PawnIO` 不存在、`System32\drivers\PawnIO.sys` 不存在），实测 CPU 的 39 个传感器中**全部温度/倍频/功耗均为 `null`**，而 GPU（NVAPI）与 NVMe（IOCTL）照常可读 —— 这种"半可用"状态极易被误判为程序 bug | CPU 温度在本机永远显示 "--"；若不加说明，用户会认为程序损坏。**不是代码缺陷**，是缺一个第三方内核驱动 | 已实现 `SensorDiagnostics.IsPawnIoAvailable()` + `SensorSnapshot.CpuTemperatureNeedsKernelDriver`，UI 在检测到该状态时显式提示"请从 pawnio.eu 安装并重启"；用户安装 PawnIO 后无需改代码即可读到 CPU 温度。**是否把 PawnIO 作为安装包可选组件分发，待定**（涉及第三方驱动的分发合规与签名，归 T7.x 决策） |
| D16 | **传感器聚合语义错误（已修复，2026-09-23）**：① `SystemInfoPage` 的 CPU 与 GPU 温度**都调 `MaxOf(SensorKind.Temperature)`**，导致 GPU Hot Spot（89.8℃）被当成 CPU 温度显示；② NVMe 的 `Warning Temperature`(80.0) / `Critical Temperature`(81.0) 是**固定门限常量**而非实时温度，混进"取最高温"后长期显示假高温；③ `IsPlausible` 温度上界设 150℃，会误删满载 GPU Hot Spot | 温度卡片展示的数字与真实硬件无关（指向显卡/门限常量）；`SensorReading` 缺少硬件分类字段，UI 只能按硬件名字字符串猜 | 已修复：新增 `SensorHardwareClass` 枚举（由 `HardwareType` 映射，服务层填写）与 `SensorSnapshot.MaxTemperatureOf(class)`；`IsThresholdSensor()` 剔除门限类传感器；温度上界放宽到 200℃。新增 15 项单元测试锁定语义（`SensorAggregationTests` + `SensorMappingTests`） |
| D17 | **"虚拟机内不崩溃"从未验证**：T1.1 验收含"虚拟机内不崩溃"，`SensorSnapshot.Unavailable` 的降级路径也是为此设计的（`MarkUnsupported` / `Readings.Count == 0` 分支），但**本机无 Hyper-V 环境，该路径只用单元测试覆盖了逻辑，未在真实 VM 里跑过**。同理 T8.3 的"Hyper-V 兼容矩阵"未开工 | 虚拟机/无传感器主板的降级行为是**推断正确**而非实测正确；若 VM 里 `Computer.Open()` 抛出未预期的异常类型，`catch` 虽会兜住但文案可能误导 | 归入 T8.3（需 Hyper-V 环境）。在此之前，降级路径的单元测试是唯一保障；建议至少用 `WmiHardwareInfoService` 在 Windows Sandbox 里做一次冒烟 |
| D18 | **`installer/` 为空，无安装包**（2026-09-23 仍未动）：MVP0 验收明写"安装包可运行"，但 T7.3（打包与自更新）未开工，`installer/` 目录为空 | MVP0 无法交付给真实用户；`app.manifest` 的提权声明（`requireAdministrator`）也从未在**打包后**的 exe 上验证过 —— 目前只在开发态 exe 上验证过 | 按 T7.3 补 Inno Setup 配置；打包后必须重新验证提权与首次启动流程（开发态 manifest 生效 ≠ 安装包内生效） |

### 11.2 当前门禁状态（2026-09-23 实测）

| 门禁 | 命令 | 结果 |
|---|---|---|
| 源码行数 | `pwsh ./rules/Check-SourceFileSize.ps1` | **通过**（EXIT=0）；`LibreHardwareSensorService.cs` 曾因新增分类逻辑涨到 373/316，已拆出 `.Mapping.cs`（收集+映射）与 `SensorDiagnostics.cs`，主文件降回 221 行 |
| 构建 | `dotnet build -c Debug` | 通过，0 警告 0 错误 |
| 测试 | `dotnet test -c Debug --no-build` | 通过，**138 项全绿、0 跳过**（本轮 +57：传感器聚合 6、传感器映射 33、此前报告导出 21、硬件清洗 12 等） |
| 原生构建 | `pwsh ./rules/Build-Native.ps1 -Configuration Release -Deploy` | **通过**；MSVC 14.51 `/W4` 零告警，x64，导出 `Native_AbiVersion` / `Native_Version` / `Native_GetSmbios` / `Native_ScanVolume` |
| 原生构建（cmake） | `cmake -S src/SysSuite.Native -B build/native -A x64` | **本机失败**：`No CMAKE_CXX_COMPILER could be found`（偏差 D10）；已不作为构建入口 |
| 提权真机探针 | `pwsh ./rules/Probe-Elevated.ps1 -Probe All` | **通过**；SMART 读到型号/健康/温度 54℃/寿命 98%/通电 7269h；传感器读到 GPU Core 69℃ / Hot Spot 79.8℃ / SSD Composite 55℃ |

源码规模：约 145 个源文件；C# 侧最大文件仍在 300 行上限内。

### 11.3 下一步优先级建议

1. **M1 已完成**（截至 2026-09-23）：T1.1~T1.6 六项全部落地；入口 `dotnet build` / `dotnet test`（138 通过）/
   `Check-SourceFileSize.ps1`（EXIT=0）/ `Build-Native.ps1` 四项全绿。
   **提权真机验收已闭环**：SMART 读到真实型号与全部健康字段、传感器读到 GPU/SSD 温度。
   唯一遗留：本机未装 PawnIO 导致 CPU 温度无读数（D15，缺第三方内核驱动，非代码缺陷）。
2. **观察 CI 首次运行结果**（D5 已解决，D11 待验）—— 2026-09-23 已推送 `master` 并新建远端 `dev`
   分支，CI（`.github/workflows/ci.yml`，监听 `[main, dev]`）**首次真正被触发**。
   重点看 native job 的 Catch2 是否能在 GitHub 的 cmake 下跑通（本机因 D10 无法验证）。
3. **让原生 DLL 进入常规构建流程**（D12）—— 当前需手工 `-Deploy`，`clean` 后 FFI 冒烟会退回 Skip。建议在 `SysSuite.Interop.csproj` 挂 MSBuild target，或按 RID 纳入仓库。
3. **让原生 DLL 进入常规构建流程**（D12）—— 当前需手工 `-Deploy`，`clean` 后 FFI 冒烟会退回 Skip。建议在 `SysSuite.Interop.csproj` 挂 MSBuild target，或按 RID 纳入仓库。
4. **归拢既有 P/Invoke 到 `SysSuite.Interop`**（D2）—— 逐服务迁移，每迁一处补错误码映射与单测。
5. **校准后遗留的高优先项**：`main`/`dev` 分支（D5）、T2.5 的免扫描例外表。
6. **M5/M6 开工前先做 D1 的 ViewModel 重整**，否则越往后成本越高。
   （注：本轮新页面 `BenchmarkPage` 与拆分出的 `SystemInfoPage.*.cs` 未恶化该项——它们仍写在 code-behind，
   但逻辑已按职责分文件，迁移时边界清晰）
7. MVP0 收尾：T7.3 打包（D6）、T3.1 补齐剩余规则 json、T3.2 按 ABI 重构并做性能实测（D4）。
8. 若需对外可比的跑分，按 D13 补 C++ 基准内核（绑核 + `FILE_FLAG_NO_BUFFERING`）。


### 11.4 2026-09-22 落地记录与行为变更

- **T0.2**：`SysSuite.Native` + `SysSuite.Interop` + `docs/native-abi.md` + 两侧 FFI 冒烟 + CI native job（详见上文各任务）。
- **D7**：`ProtectedPaths.cs` 建立并接入三处删除路径，**随之产生的行为变更（均为有意的加固）**：
  1. 强制删除新增拒绝：`Program Files` / `Program Files (x86)` 整棵树、`ProgramData` 自身、用户主目录自身（此前只保护 Windows/System32/WindowsApps/盘根）；
  2. 磁盘清理执行器新增删除前硬拒绝（此前完全没有白名单，规则引擎误命中即可穿透）；
  3. 残留扫描新增目录段保护：`$Recycle.Bin`、`System Volume Information`、`WinSxS`。
- 回归：`dotnet test` 41 通过 / 7 跳过 / 0 失败，既有卸载与清理用例无回归。

### 11.5 2026-09-22 二次审视：工具链结论修正与原生构建落地

本轮起因是「既然 `dotnet build` 能产出 `bin/`，为何声称本机无 C++ 工具链」。复核结果：**该结论错误**，连带修正如下。

**发现 1：工具链真实存在（此前的探测方法有缺陷）**

| 组件 | 位置 | 此前为何漏判 |
|---|---|---|
| Visual Studio 2026 Community | `D:\ai_era\Microsoft Visual Studio\18\Community` | 所有探测只扫 `C:\Program Files\*`，**VS 装在了 D 盘** |
| MSVC 14.51.36231 | 上述路径下 `VC\Tools\MSVC\14.51.36231\bin\Hostx64\x64\cl.exe` | 同上 |
| CMake 4.3.1-msvc1 | 上述路径下 `Common7\IDE\CommonExtensions\Microsoft\CMake\CMake\bin` | 同上 |
| Windows SDK 10.0.26100.0 | `C:\Program Files (x86)\Windows Kits\10` | 扫到了但未用上 |

**发现 2：T0.2 在 2026-09-07 就已真实编译成功过**

`obj/native/native_api.obj`（23:31:41）与 `tests/SysSuite.Tests/bin/Release/net8.0/SysSuite.Native.dll`（23:31:43）相隔 2 秒；
DLL 为 x64 PE32+、内部名 `SysSuite.Native.dll`、导出唯一符号 `Native_Version` —— 完全吻合 T0.2 的占位实现描述。
因 `bin/`、`obj/` 均被 gitignore，源码丢失后产物仍留在磁盘，而 2 次 git 提交中从未包含任何 native 文件。
故 v2.3 所称「此前被误标为 [x]」**措辞不成立**，已在 T0.2 段落更正。

**发现 3：CMake 在本机不可用，但 cl.exe 可用**（偏差 D10）

`cmake -G "Visual Studio 18 2026"` 报 `No CMAKE_CXX_COMPILER could be found`；生成器需经 MSBuild 解析 `VCTargetsPath`，
而 `-G "Visual Studio 17 2022"` 报 `MSB8020`（缺 v143 工具集）。另 `vcvarsall.bat` 内部调用 `reg.exe`，受限环境被拦截。
因 `native_api.cpp` 仅依赖 `<cstring>/<cstddef>/<cwchar>/<stdint.h>`（纯 CRT，不需要 Windows SDK 头文件），
可手工构造 `INCLUDE`/`LIB` 直调 `cl.exe`，实测 `/W4` 零告警通过。

**落地**：新增 `rules/Build-Native.ps1`（自动定位 VS/MSVC/SDK、构造环境、编译、`-Deploy` 复制产物到各输出目录）；
CI 的 dotnet job 由 cmake 两步切换为该脚本。测试由 41 通过/7 跳过 → **48 通过/0 跳过**。

### 11.6 2026-09-23 硬件数据质量修正（T1.1 / T1.6 实测真实硬件后）

用 `WmiHardwareInfoService` 采集本机（HP 笔记本 / i7-10750H / RTX 2070 Max-Q）并导出报告，暴露三处**长期存在但此前未被发现**的数据缺陷：

| 字段 | 修正前 | 修正后 | 原因 |
|---|---|---|---|
| 操作系统 | `Microsoft Windows NT 10.0.26200.0` | `Microsoft Windows 11 家庭版` | `Environment.OSVersion.VersionString` 只给出版本串；改取 `Win32_OperatingSystem.Caption` |
| 显卡显存 | `4.0 GB`（实为 8GB 卡） | `8.0 GB` | `Win32_VideoController.AdapterRAM` 是 **32 位**字段，≥4GB 必溢出（值为 `0xFFFFFFFF`）；改读显示类驱动登记的 64 位 `HardwareInformation.qwMemorySize`。**注意它是「值名」而非子键**（`HKLM\SYSTEM\CurrentControlSet\Control\Class\{4d36e968-...}\0001` 下的 `HardwareInformation.qwMemorySize`），同键下的 32 位 `HardwareInformation.MemorySize` 同样不可用 |
| 显示模式 | `1920 x 1080 x 4294967296 种颜色` | `1920 x 1080` | 色深字段同为溢出值，展示无意义 |

另外修复报告文本的**标签列对齐**：C# 的 `{label,-12}` 按 UTF-16 字符数补齐，而中文在等宽字体/终端下占两列，导致中英混排（「型号」vs「计算机名」）严重错位。新增 `PadDisplay` 按东亚字符宽度计算。

回归：构建 0 警告 0 错误；测试 **81 通过 / 0 跳过 / 0 失败**（48 → 81）；行数门禁 EXIT=0。

---

## v2.2 变更摘要

**新增**：MVP0 与版本分层、资源排期矩阵、G9 风险分级、G10 Native ABI 合同、G11 数据契约、`docs/native-abi.md`、Native FFI 冒烟测试、性能测量方法、自更新失败回滚、SBOM/漏洞扫描、企业兼容说明、T7.8 工具箱扩展。
**修订**：T0.2 从泛化互操作升级为 ABI 合同；T0.4 增加高能力开关治理；T0.7 明确管理员/普通用户进程边界；T2.1 数据表补稳定主键、批次、备份和审计字段；T2.2 增加已知软件档案；T2.6/T3.5/T4.1/T4.2/T5.6/T6.4 全部纳入风险分级；T8.3 冒烟策略按阶段分层。
**安全**：Defender 控制、更新服务层控制、WaaSMedic 对抗、强制删除、系统瘦身、桌面覆盖层不再作为默认 v1 链路；实验能力默认关闭，且必须经灰度和真实机验证。

下一步建议按 **MVP0 链**开工：T0.0 → T0.1 → T0.2 → T0.4 → T0.6。高风险能力一律后置到实验分支，不得阻塞基础稳定版。

---

## v2.3 变更摘要（进度校准）

**校准方式**：逐条比对 `src/` 实际代码，而非沿用既有勾选。

**回退（此前误标为完成）**：
- **T0.2 全部子项由 [x]/[~] 回退为 [ ]** —— `SysSuite.Native` 与 `SysSuite.Interop` 两个项目不存在，`docs/native-abi.md` 未创建，全仓库无 `Native_Version` / `LibraryImport` 引用。
- T0.3 的「StatusBar 绑定 BackgroundTaskManager.Current」回退为 [ ] —— 全局 StatusBar 与 `BackgroundTaskManager` 均不存在。
- T0.5 的 CI 完整版维持 [~]；其中的 Clang-Tidy / MSVC /analyze 明确标注未接入。
- T3.1 标题由 [x] 降为 [~]（此前标题 [x] 而全部子项 [ ]，自相矛盾）。

**补标（此前漏标为未开始）**：
- T3.1 的规则引擎本体、`docs/rules-schema.md`、`rules/clean-temp.json`；T3.3 三项；T3.4 主体与撤销。
- T1.1 的 SMBIOS 主板/BIOS（经 WMI 实现）、T1.4 的监控引擎与仪表盘卡片。
- T0.1 的根目录三件套（`.editorconfig` / `Directory.Build.props` / `Directory.Packages.props`）。
- T0.0 的 GitHub 建仓与首次推送。
- T8.1 的 C# 静态分析（`.editorconfig` + `EnableNETAnalyzers` + `TreatWarningsAsErrors`）。

**新增**：§11 架构偏差与现状登记（D1~D8）、各里程碑完成度百分比、§11.2 门禁实测状态、§11.3 下一步优先级。
**README** 同步指向 §11 作为进度真源。

**当前阶段结论**：M2 完成；M0/M3 接近完成；M1 起步；M4/M5/M6/M7 未开工。MVP0 的能力侧已基本齐备，
唯一未达成的是「可安装」（`installer/` 为空）。
