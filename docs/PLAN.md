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

---

## 0. 全局约定（先读）

- [ ] **G1 目录**：仓库根 = sln；代码在 `src/`，文档在 `docs/`，测试在 `tests/`，规则在 `rules/`，打包在 `installer/`
- [ ] **G2 分支**：`main`（稳定）/ `dev`（集成）/ `feature/M{阶段}-{任务号}`；合并一律 PR + 至少 1 人 Review + main 分支保护
- [ ] **G3 提交**：Conventional Commits（feat/fix/chore/refactor/test/docs），任务号入消息尾，如 `[T2.4]`
- [ ] **G4 命名**：C# PascalCase（私有 `_camel`）；C++ 类 PascalCase、导出函数 `Native_` 前缀；资源 key 用中划线
- [ ] **G5 DoD（每任务）**：编译 0 warning；单测通过；Serilog 无 Error；深浅两主题目检。M0-M2 只做 Win10 22H2 或 Win11 24H2 单机冒烟；M3 起按改动面选择双系统；M6 后执行完整矩阵
- [ ] **G6 崩溃与遥测**：本地 crash dump + 崩溃对话框 + 日志打包导出（暂不联网上报；实现见 T0.4）
- [ ] **G7 破坏性操作红线**：任何删除/注册表写入/防护变更必须有 **预览 → 备份 → 执行** 三步；系统目录白名单硬拒绝（常量表 `ProtectedPaths.cs`）
- [ ] **G8 安全红线（T5.6 专属）**：防护关闭仅允许用户主动 UI 触发；必须默认定时自动恢复；必须写审计日志；禁止静默/命令行/无人值守关闭防护；防篡改开启时诚实降级、禁止伪造关闭态
- [ ] **G9 风险分级**：L0 默认启用；L1 默认启用但可关闭；L2 实验性、默认关闭；L3 仅灰度/企业验证后启用。T3.5、T4.1 服务层、T4.2、T5.6、T6.4、强制删除均不得直接进入 MVP 默认链路
- [ ] **G10 Native ABI 合同**：所有 C++ 导出必须有稳定错误码、内存所有权、回调线程、取消规则与 FFI 冒烟测试；未定义契约的原生能力不得接入 Core
- [ ] **G11 数据契约**：SQLite schema、迁移、稳定业务主键、备份/恢复路径、审计字段必须先评审后编码；跨模块复用表结构变更必须走版本化迁移

---

## 1. 里程碑总览

| 里程碑 | 内容 | 工期 | 负责 | 状态 |
|---|---|---|---|---|
| M0 | 仓库 + 架构骨架 + Interop + UI 壳 + Watchdog 骨架 | 2 周 | ABC | [ ] |
| M1 | 系统信息 + 监控 + 基准测试 | 2 周 | C 为主 | [ ] |
| M2 | 卸载器（完整） | 3 周 | B 为主 | [ ] |
| M3 | 磁盘清理（MFT 扫描 + 规则引擎） | 3 周 | BC | [ ] |
| M4 | 更新控制 | 1 周 | B | [ ] |
| M5 | 安全中心 + 防护控制 + 软件管家 | 3 周 | B | [ ] |
| M6 | 桌面整理 | 4~6 周 | C 为主 | [ ] |
| M7 | 搜索 / 本地化 / Ribbon / 工具箱 / 打包 / 签名 / 灰度 | 3 周 | A | [ ] |
| M8 | 测试与质量横切（贯穿，见 §5） | 不单列 | ABC | [ ] |

**总计约 21~23 周**

### 1.1 MVP0 与发布分层

- [ ] **MVP0 目标**：先交付一个可安装、可日常使用、风险可控的基础版，避免全量 v1 拖入高危能力
  - 包含：仓库/CI/架构骨架、UI 壳与设置骨架、系统信息与实时监控、基础清理规则、卸载器核心、软件管家只读升级列表、本地化与崩溃闭环
  - 明确排除：Defender 防护关闭、Windows Update 服务层禁用、WaaSMedic 权限接管、MFT 极速通道、桌面分区覆盖层、强制删除、系统瘦身、自更新
  - 建议任务顺序：T0.0 → T0.1 → T0.2 → T0.4 → T0.6 → T0.3 → T0.5 → T2.1 → T1.1/T1.4 → T2.2/T2.4 → T3.1/T3.4 → T5.1/T5.2/T5.5 只读 → T7.1 → MVP0 冒烟
- [ ] **版本分层**：
  - MVP0：基础稳定版，全部能力为 L0/L1
  - v1.0：补齐 MFT 扫描、基准测试、残留扫描、全局搜索、打包签名、性能与兼容矩阵
  - v1.1：灰度启用系统瘦身与桌面分区
  - v1.2+：实验性启用更新服务层控制、Defender 防护控制、WaaSMedic 对抗、强制删除
- [ ] **MVP0 验收**：安装包可运行；9 页导航可用；系统信息/监控/基础清理/卸载/软件升级只读列表可闭环；崩溃可导出日志；无默认高危操作

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
- [ ] GitHub 建仓（二选一）：
  - 网页：New repository → 命名 `SysSuite` → **Private** → **不勾选任何初始化选项**
  - CLI（推荐）：`gh auth login` 后 `gh repo create SysSuite --private --source=. --remote=origin --push`
- [ ] 首次提交推送：`git add . && git commit -m "chore: init repo [T0.0]" && git branch -M main && git push -u origin main`
- [ ] 建 dev 分支：`git checkout -b dev && git push -u origin dev`
- [ ] main 分支保护：Settings → Branches → Require PR before merging + 1 approval
- [x] CI 骨架 `.github/workflows/ci.yml`（先放 dotnet 构建段，T0.5 补全）
- 验收：main/dev 双分支可见；CI 首跑绿；clone 后按 README 可定位全部文档

### T0.1 解决方案与项目结构  [~]  0.5 天
- [x] `dotnet new sln -n SysSuite`
- [x] 建立 5 个 C# 项目：
  - `SysSuite.UI`（wpf, net8.0-windows）
  - `SysSuite.Core`（classlib）
  - `SysSuite.Core.Abstractions`（classlib，接口层）
  - `SysSuite.Interop`（classlib，P/Invoke 绑定）
  - `SysSuite.Watchdog`（worker，托盘常驻进程）
- [x] 建立 C++ 项目 `SysSuite.Native`（CMake，输出 `SysSuite.Native.dll`，仅 x64）
- [x] 项目引用：UI→Core→Abstractions；Core→Interop；Interop 不引用其它
- [~] 根目录：`.editorconfig` / `Directory.Build.props`（nullable=enable、TreatWarningsAsErrors）/ `Directory.Packages.props`（中央包管理）
- 验收：`dotnet build` 与 `cmake --build build` 双绿

### T0.2 C++↔C# 互操作合同与通路  [~]  1 天
- [x] `SysSuite.Native/CMakeLists.txt`：`project(SysSuiteNative LANGUAGES CXX)`、`add_library(SysSuite.Native SHARED ...)`、`/std:c++20 /W4`
- [x] `docs/native-abi.md`：定义稳定错误码、内存所有权、回调线程、取消规则、缓冲区上限与版本兼容策略；MVP0 只允许低风险原生探针接入
- [x] `native_api.h`：`extern "C" __declspec(dllexport) int Native_Version(wchar_t* buf, int len);`
- [x] `native_api.cpp`：返回 `"1.0.0-m0"`（验证调用链用占位实现）
- [x] `SysSuite.Interop/NativeMethods.cs`：`[LibraryImport]` + `StringMarshalling.Utf16`
- [x] UI 首页临时按钮"自检"：调用 Native_Version 并显示结果
- [x] 明示仅支持 x64（app.manifest，见 T0.6）
- [~] `NativeAbiSmokeTests`：调用成功/缓冲区不足/取消三类路径
- 验收：UI 点自检弹出 native 版本串；进程退出码 0（无堆损坏）；FFI 冒烟全绿

### T0.3 UI 壳（侧边栏 9 标签，对应 UI-SPEC §2/§4）  [x]  1.5 天
- [~] NuGet：WPF-UI、CommunityToolkit.Mvvm、Microsoft.Extensions.Hosting、Serilog、Serilog.Sinks.File
- [~] App.xaml：WpfUi 资源合并、DI Host 启动链、`DispatcherUnhandledException` 全局钩子
- [x] MainWindow.xaml：NavigationView（仪表盘/系统信息/磁盘清理/卸载器/安全中心/桌面整理/软件管家/工具箱 + Footer 设置）+ ContentFrame + 全局 StatusBar
- [x] `NavPage` 枚举 + NavigationService
- [~] 9 个 Page + 9 个 ViewModel 空壳（Dashboard/SystemInfo/Cleaner/Uninstaller/Security/Desktop/SoftwareHub/Toolbox/Settings）
- [x] StatusBar 绑定 BackgroundTaskManager.Current（ProgressText/Percent/CancelCommand 空实现）
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
- [ ] `.github/workflows/ci.yml`：
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
        - run: cmake -S src/SysSuite.Native -B build -A x64
        - run: cmake --build build --config Release
  ```
- [~] 单测骨架：`tests/SysSuite.Tests`（xUnit）、`tests/SysSuite.Native.Tests`（Catch2；本机缺 CMake，CI 执行）
- [x] 静态分析接入（T8.1 同步落地）：.NET analyzers 全开 + Clang-Tidy + /analyze，告警不过 CI
- 验收：PR 自动跑通，Actions 徽章绿

### T0.6 应用清单与提权策略  [~]  0.5 天
- [x] `app.manifest`：`requestedExecutionLevel requireAdministrator` + `dpiAwareness PerMonitorV2`
- [ ] 版本资源（文件版本/产品名/图标）与品牌资产（ico 16/32/48/256）
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

### T1.1 硬件信息服务  [~]  2 天
- [~] Abstractions/ISystemInfoService.cs：GetCpuAsync / GetMemoryAsync / GetGpusAsync / GetDisksAsync / GetNicsAsync / GetOsAsync
- [~] Core/SystemInfo/WmiSystemInfoService.cs 实现（Win32_* 集中封装 + 重试）
- [ ] CPU 补充：Native_CpuIdFeatures（指令集/缓存层次）
- [ ] SMBIOS：Native_GetSmbios（GetSystemFirmwareTable）解析主板/BIOS
- 验收：字段与 CPU-Z/设备管理器双源一致；虚拟机内不崩溃

### T1.2 温度传感器  [ ]  1.5 天
- [ ] LibreHardwareMonitorLib 封装 SensorService（只读、轮询节流 2s）
- [ ] 无传感器（部分主板/VM）降级显示"不可用"，不报错
- 验收：真机 CPU 包温/GPU 温与 HWiNFO 偏差 < 5℃

### T1.3 SMART（C++）  [ ]  1.5 天
- [ ] `Native_QuerySmart(physicalDrive, out, count)`：STORAGE_PROPERTY_QUERY + ATA PASS THROUGH
- [ ] NVMe 走 NVME_HEALTH_INFO_LOG；统一模型（温度/通电时间/剩余寿命/坏块）
- [ ] UI：存储页健康徽章（好/警告/危险 阈值常量）
- 验收：与 CrystalDiskInfo 双向对比一致

### T1.4 实时监控  [~]  2 天
- [~] MonitorEngine：WMI 采集（CPU%/内存/磁盘读写/网卡上下行），2 秒采样与事件输出
- [~] UI 监控卡片（CPU/内存/磁盘读写/网络收发）；折线图后续补齐
- [ ] 暂停/恢复/时间窗切换（1min/5min）
- 验收：连续 1 小时内存无增长；磁盘拷贝曲线出尖峰

### T1.5 基准测试（C++ 内核）  [ ]  3 天
- [ ] bench/：ScoreCpu（整型+浮点混合、SetThreadAffinity 绑核）、ScoreMemory（memcpy 带宽+随机延迟）、ScoreDisk（FILE_FLAG_NO_BUFFERING：4K 随机 + 1M 顺序，各 5s）
- [ ] BenchmarkService：空跑校准、评分归一（10000 分基准机）
- [ ] 磁盘测试页显式声明写入影响
- [ ] UI：跑分向导（选项→动画面板→结果对比条）
- 验收：CPU 分数双跑方差 < 3%

### T1.6 报告导出  [ ]  0.5 天
- [ ] ReportExporter：TXT/HTML/JSON 三格式（HTML 内嵌模板）
- 验收：导出无乱码、JSON 可反序列化

---

## M2 卸载器

### T2.1 数据层（含统一数据库版本管理）  [ ]  0.5 天
- [ ] SharedDatabaseService：单库 SQLite + 统一迁移器（PRAGMA user_version 驱动）
- [ ] 建表 v1：
  - `apps(id, stable_key, name, publisher, version, install_date, size, uninstall_string, quiet_string, source REG|MSI|STORE, key_path, install_dir, icon_path, hash, updated_at)`；`stable_key` 必须由 source + key_path/package_id/install_dir 归一化生成，作为跨枚举稳定业务主键
  - `clean_history(id, batch_id, items_json, freed_bytes, mode, at, reversible, backup_root, restore_state PENDING|READY|PARTIAL|RESTORED|EXPIRED, expires_at)`；每批操作必须可追溯到独立备份目录
  - `update_history(id, app_stable_key, app_id, from_ver, to_ver, action INSTALL|UPGRADE|UNINSTALL, result, at)`
  - `security_audit(id, action, old_state, new_state, auto_restore_at, at, requested_by, process_id, command_result, restore_result)`
- [ ] Dapper Repo + 迁移脚本文档（docs/db-migrations.md）
- 验收：建库/读写/跨版本迁移单测过（供 M2/M3/M5/M6 共用）

### T2.2 三路枚举器  [ ]  1.5 天
- [ ] RegistryAppEnumerator：HKLM\...\Uninstall + WOW6432Node + HKCU；SystemComponent 可配置过滤
- [ ] MsiAppEnumerator（MsiEnumProducts）与注册表合并去重（同 GUID 以 MSI 记录为准）
- [ ] StoreAppEnumerator（PackageManager.FindPackages）
- [ ] 去重键：规范化名（小写/去版本号/去空格）+ 发布者哈希
- [ ] 已知软件档案 JSON：Hive 数据库、Service 注册项、目录安装器等非标准软件；档案需人工审核、来源标注与版本化更新
- [ ] 全量刷新入 TaskQueue，落 apps 表 + UI 增量刷新
- 验收：与 Geek Uninstaller 列表 diff < 3%

### T2.3 图标缓存  [ ]  0.5 天
- [ ] IconCacheService：SHGetFileInfo → PNG 存 iconcache\{hash}.png，并发 ≤ 4
- 验收：200 应用冷刷新 < 5s（§6 预算），缓存命中 < 0.5s

### T2.4 卸载执行  [ ]  2.5 天
- [ ] UninstallService 流程：读 UninstallString/QuietUninstallString（优先 Quiet）→ 解析命令行（msiexec /x {GUID} 特判；Store 走 PackageManager.Remove）→ 卸载前 reg export 备份 + install_dir 清单快照 → Process 异步等待（超时 600s 给用户选择）→ 重枚举验证 → 触发 T2.5
- [ ] 写入 update_history
- [ ] UI 卸载队列（串行单并发）+ 进度 + 失败结构化（ExitCode/ErrKind）
- 验收：静默卸载 Chrome(Nullsoft)/7-Zip/典型 MSI/Store 应用四类样例全通过

### T2.5 残留扫描  [ ]  1.5 天
- [ ] LeftoverScanner 规则：安装目录空壳 / %ProgramData% 与 %LocalAppData% 相似目录（阈值 0.8）/ 注册表同名键、卸载键残余、App Paths、MUI Cache
- [ ] ProtectedPaths 白名单 + KnownFolder 免扫描例外表
- [ ] UI：残留确认 Dialog（文件与注册表逐项可见，默认全选）
- 验收：3 款测试程序残留定位率 > 90%，无系统项误报

### T2.6 强制删除  [ ]  1 天
- [ ] 风险分级：L2，默认关闭；仅实验分支开放，且必须先执行完整备份
- [ ] Native_ForceDelete：SeTakeOwnershipPrivilege → 递归解锁删除；占用文件 MoveFileEx(REBOOT)
- [ ] 通用"确认句"组件 `ConfirmationInputBox`（用户须输入指定文本方可执行；T5.6 复用）
- 验收：删除只读占用目录成功；输错确认句拒绝执行

### T2.7 USN 安装监控（依赖 T0.7）  [ ]  2 天
- [ ] Native：FSCTL USN Journal 增量读（每卷一线程，事件环形队列）
- [ ] C# 过滤 msi/exe + Uninstall 键轮询（3s）→ 经 IPC 广播 AppListChanged
- [ ] UI 角标提示 + 可开关
- 验收：手工安装/卸载 5s 内列表刷新；休眠恢复无句柄泄漏

---

## M3 磁盘清理

### T3.1 规则引擎  [ ]  1.5 天
- [ ] docs/rules-schema.md（字段：id/target/env/regex/exclude/minAgeDays/level）
- [ ] CleanRule 模型 + RuleValidator（通配编译、冲突检测）
- [ ] rules/*.json：temp、windows_temp、browser_chrome/edge/firefox、thumbnails、recycle_bin、dns_cache、update_download、delivery_optimization、log_files、error_reports、font_cache
- [ ] RuleEngine：环境变量展开 → 枚举命中 → 白名单排除 → CleanItem 输出（含风险标记）
- [ ] 每规则 ≥ 3 单测（命中/排除/空）
- 验收：规则热更新（改 json 5s 生效）

### T3.2 MFT 全盘扫描（C++）  [ ]  2.5 天
- [ ] `Native_ScanVolume(volume, cb, out)`：FSCTL_ENUM_USN_DATA 直读，增量批量回传
- [ ] C# FileIndex：ParentId→路径重建树（500 万条以上采用分层分页）
- [ ] 非 NTFS/ReFS/动态盘降级 FindFirstFile 通道（同一接口）
- 验收：150 万文件枚举 < 8s（§6 预算）；排除 System Volume Information/$Recycle.Bin

### T3.3 大文件 / 空目录 / 重复文件  [ ]  1.5 天
- [ ] 大文件：阈值筛选 TopN + 目录聚合
- [ ] 空目录：后序遍历标记（含全空链模式）
- [ ] 重复文件：Size → 首 64KB 哈希 → SHA256 全量
- 验收：构造重复集识别率 100%；含锁定文件时单条降级不崩溃

### T3.4 清理执行器  [ ]  1 天
- [ ] CleanExecutor：回收站（SHFileOperation FOF_ALLOWUNDO）或永久删除（写日志）
- [ ] 前置可选 SrSetRestorePoint（单日去重）；恒写 clean_history（支持"撤销最近一次"）
- [ ] 结果页提供 [撤销最近一次清理] 按钮（reversible=true 时可用）
- [ ] 双重进度（字节/条数）；失败项汇总为可重试列表
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

### T8.1 静态分析（随 T0.5 接入，长期生效）  [ ]
- [ ] C#：.editorconfig + .NET Analyzers 全开，警告即错误；Sonar 人审模式（可选）
- [ ] C++：Clang-Tidy（bugprone-*/performance-*）+ MSVC /analyze
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

| 任务 | 描述 | 负责人 | 状态 | 预计完成 | 备注 |
|---|---|---|---|---|---|
| T0.0 | 仓库建立 |  | [ ] |  | 阻塞一切 |
| T0.1 | 解决方案结构 |  | [ ] |  |  |
| T0.2 | Interop 通路 |  | [ ] |  | 前置依赖 |
| T0.3 | UI 壳 9 标签 |  | [ ] |  |  |
| T0.4 | 基础设施 + 崩溃闭环 |  | [ ] |  |  |
| T0.5 | CI 完整版 |  | [ ] |  |  |
| T0.6 | 清单/提权/DPI |  | [ ] |  |  |
| T0.7 | Watchdog 骨架 |  | [ ] |  | T2.7/T5.6/T6.4 前置 |
| T1.1~T1.6 | M1 六项 |  | [ ] |  |  |
| T2.1~T2.7 | M2 七项 |  | [ ] |  |  |
| T3.1~T3.5 | M3 五项 |  | [ ] |  |  |
| T4.1/T4.2 | M4 |  | [ ] |  |  |
| T5.1~T5.6 | M5 六项 |  | [ ] |  | T5.6 高风险 |
| T6.1~T6.5 | M6 五项 |  | [ ] |  |  |
| T7.0~T7.8 | M7 九项 |  | [ ] |  | T7.6 合规必做 |
| T8.1~T8.4 | 质量横切 |  | [ ] |  | 按节拍回归 |

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

## v2.2 变更摘要

**新增**：MVP0 与版本分层、资源排期矩阵、G9 风险分级、G10 Native ABI 合同、G11 数据契约、`docs/native-abi.md`、Native FFI 冒烟测试、性能测量方法、自更新失败回滚、SBOM/漏洞扫描、企业兼容说明、T7.8 工具箱扩展。
**修订**：T0.2 从泛化互操作升级为 ABI 合同；T0.4 增加高能力开关治理；T0.7 明确管理员/普通用户进程边界；T2.1 数据表补稳定主键、批次、备份和审计字段；T2.2 增加已知软件档案；T2.6/T3.5/T4.1/T4.2/T5.6/T6.4 全部纳入风险分级；T8.3 冒烟策略按阶段分层。
**安全**：Defender 控制、更新服务层控制、WaaSMedic 对抗、强制删除、系统瘦身、桌面覆盖层不再作为默认 v1 链路；实验能力默认关闭，且必须经灰度和真实机验证。

下一步建议按 **MVP0 链**开工：T0.0 → T0.1 → T0.2 → T0.4 → T0.6。高风险能力一律后置到实验分支，不得阻塞基础稳定版。
