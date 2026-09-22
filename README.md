# SysSuite

Windows x64 综合系统工具。当前处于 MVP0 骨架阶段，已完成基础 UI 壳、崩溃诊断、设置持久化和 Watchdog 进程骨架。

- 计划：`docs/PLAN.md`（**进度与偏差以 `PLAN.md` §11 为准，已于 2026-09-22 按代码实证校准**）
- UI 规范：`docs/UI-SPEC.md`
- 原生 ABI 合同：`docs/native-abi.md`（C++ ↔ C# 互操作的唯一契约，G10）
- 构建：`dotnet build -c Debug`
- 原生模块（推荐，仅需 MSVC；缺失时自动降级，不影响托管侧构建）：
  `pwsh ./rules/Build-Native.ps1 -Configuration Release -Deploy`
  —— 该脚本自行定位 Visual Studio / MSVC 工具集 / Windows SDK 并调用 `cl.exe`，
  **不依赖 CMake 生成器探测，也不调用 `vcvarsall.bat`**（二者在部分环境下会失败，见 `PLAN.md` §11 偏差 D10）。
  `CMakeLists.txt` 保留给 IDE 与 Visual Studio 使用。
- 测试：`dotnet test -c Debug --no-build`
  （未先构建原生模块时，FFI 冒烟用例会**自动 Skip 而非误报失败**；构建后应显示 48 通过 / 0 跳过）
- 源码行数：源码文件上限 300 行，检查命令为 `pwsh ./rules/Check-SourceFileSize.ps1`
- 主程序需管理员权限；Watchdog 支持当前用户开机自启与命名管道 IPC。
