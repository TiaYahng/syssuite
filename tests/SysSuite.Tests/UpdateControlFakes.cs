using System.Globalization;
using SysSuite.Core.System;

namespace SysSuite.Tests;

/// <summary>
/// <see cref="SysSuite.Core.System.IUpdateRegistryProbe"/> / <see cref="SysSuite.Core.System.IUpdateServiceProbe"/> 的假实现。
/// 独立成文件是为了守住 300 行门禁，也便于两个测试类共用同一套假数据。
///
/// 真实写入需要管理员权限且会改本机 Windows 更新配置，**绝不能进单测** ——
/// 这就是抽象层存在的理由。
/// </summary>
internal sealed class FakeRegistryProbe : IUpdateRegistryProbe
{
    public string? PolicyNoAutoUpdate { get; set; }

    public string? PolicyAuOptions { get; set; }

    public bool PolicyKeyExists => PolicyNoAutoUpdate is not null || PolicyAuOptions is not null;

    public string? ReadPolicyValue(string valueName) => valueName switch
    {
        "NoAutoUpdate" => PolicyNoAutoUpdate,
        "AUOptions" => PolicyAuOptions,
        _ => null,
    };

    public string? ReadWindowsUpdateValue(string valueName) => ReadPolicyValue(valueName);

    public bool WritePolicyValue(string valueName, int value)
    {
        var text = value.ToString(System.Globalization.CultureInfo.InvariantCulture);
        if (valueName == "NoAutoUpdate")
        {
            PolicyNoAutoUpdate = text;
        }
        else
        {
            PolicyAuOptions = text;
        }

        return true;
    }

    public bool DeletePolicyValue(string valueName)
    {
        if (valueName == "NoAutoUpdate")
        {
            PolicyNoAutoUpdate = null;
        }
        else
        {
            PolicyAuOptions = null;
        }

        return true;
    }

    public bool WriteWindowsUpdateValue(string valueName, int value) => WritePolicyValue(valueName, value);
}

internal sealed class FakeServiceProbe : IUpdateServiceProbe
{
    public Dictionary<string, string> StartTypes { get; } = new(StringComparer.OrdinalIgnoreCase);

    public Dictionary<string, string> TaskStates { get; } = new(StringComparer.OrdinalIgnoreCase);

    public string ReadStartType(string serviceName)
        => StartTypes.TryGetValue(serviceName, out var value) ? value : "Missing";

    public bool SetStartType(string serviceName, string startType)
    {
        StartTypes[serviceName] = startType;
        return true;
    }

    public bool StopService(string serviceName) => true;

    public string ReadTaskState(string taskPath)
        => TaskStates.TryGetValue(taskPath, out var value) ? value : "Missing";

    public bool SetTaskState(string taskPath, bool enabled)
    {
        TaskStates[taskPath] = enabled ? "Enabled" : "Disabled";
        return true;
    }
}
