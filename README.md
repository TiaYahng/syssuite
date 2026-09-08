# SysSuite

Windows x64 综合系统工具。当前处于 MVP0 骨架阶段，已完成基础 UI 壳、崩溃诊断、设置持久化和 Watchdog 进程骨架。

- 计划：`docs/PLAN.md`
- UI 规范：`docs/UI-SPEC.md`
- 构建：`dotnet build -c Release`
- 测试：`dotnet test -c Release --no-build`
- 源码行数：源码文件上限 300 行，检查命令为 `pwsh ./rules/Check-SourceFileSize.ps1`
- 主程序需管理员权限；Watchdog 支持当前用户开机自启与命名管道 IPC。
