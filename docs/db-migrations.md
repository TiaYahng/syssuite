# SQLite 迁移

## 规则

- `SharedDatabaseService` 使用 `PRAGMA user_version` 驱动迁移，当前版本为 `1`。
- 迁移在单一事务内执行；遇到更高 schema 版本时必须失败，禁止应用旧代码访问新表。
- 所有新增或变更表结构必须在本文档追加版本条目，并同步更新 `CurrentSchemaVersion`。

## v1

- `apps`：卸载器三路枚举合并后的应用记录；`stable_key` 是稳定业务主键，由 `source + key_path/package_id/install_dir` 归一化生成。
- `clean_history`：磁盘清理批次记录；`batch_id` 唯一，`backup_root` 指向独立备份目录，`restore_state` 标识撤销生命周期。
- `update_history`：应用安装、升级、卸载历史；`app_id` 外键关联 `apps`。
- `security_audit`：安全能力状态变更审计，包含旧状态、新状态、自动恢复时间、进程与命令结果。
