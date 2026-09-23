namespace SysSuite.Core.System;

/// <summary>
/// 注册表读取探针。存在的唯一理由是**让服务可测**：真实注册表写入是需要管理员权限的
/// 破坏性动作，不能进单元测试；有了这层间接，测试可以断言"档位 → 应写的键值"这层逻辑本身。
///
/// 返回 null 表示"读不到"（键不存在或没有权限），这与"读到空值"是不同的语义。
/// </summary>
internal interface IUpdateRegistryProbe
{
    string? ReadPolicyValue(string valueName);

    string? ReadWindowsUpdateValue(string valueName);

    bool WritePolicyValue(string valueName, int value);

    bool DeletePolicyValue(string valueName);

    bool WriteWindowsUpdateValue(string valueName, int value);

    bool PolicyKeyExists { get; }
}

/// <summary>
/// 服务与计划任务读取探针。与 <see cref="IUpdateRegistryProbe"/> 同样的可测性考虑。
/// </summary>
internal interface IUpdateServiceProbe
{
    /// <summary>读服务的启动类型，返回 "Auto" / "Manual" / "Disabled" / "Unknown"。</summary>
    string ReadStartType(string serviceName);

    bool SetStartType(string serviceName, string startType);

    bool StopService(string serviceName);

    /// <summary>读计划任务状态，返回 "Enabled" / "Disabled" / "Missing"。</summary>
    string ReadTaskState(string taskPath);

    bool SetTaskState(string taskPath, bool enabled);
}
