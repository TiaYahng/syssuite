# Source File Size Rule

- 上限：300 行（含空行）；新增代码建议控制在 250 行以内。
- 适用：`src/`、`tests/`、`installer/` 下的 `.cs`、`.xaml`、`.cpp`、`.h`、`.hpp`、`.cc`、`.cxx`。
- 排除：构建与生成目录（`bin/`、`obj/`、`build/`、`out/`）。
- 执行：`pwsh ./rules/Check-SourceFileSize.ps1`。
- 调整：临时验证可用 `-MaxLines <value>`；默认值变更必须同步更新本文件和 CI。
- 处理：超过上限时优先按职责拆分组件、服务、辅助类型或资源，而不是复制隐藏逻辑。
