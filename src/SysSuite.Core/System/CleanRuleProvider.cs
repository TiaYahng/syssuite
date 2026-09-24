using SysSuite.Core.Abstractions.System;

namespace SysSuite.Core.System;

/// <summary>
/// 清理规则的加载与热更新（T3.1 验收项：改 json 5 秒内生效）。
/// </summary>
/// <remarks>
/// 两个容易踩的空：
///
/// 1. **防抖**：编辑器保存一次会连续抛出多个 Changed 事件，且写入中途文件是不完整的 JSON。
///    直接重载会读到半截文件，表现为"改了规则之后清理器突然什么都不认识了"。
///    故事件只置脏位，实际重载推迟到防抖窗口之后、下次取用时。
/// 2. **扫描期间不得换规则集**：<see cref="GetRules"/> 返回的是同一个快照引用，
///    重载只在取用瞬间发生。若允许扫描中途替换，同一轮里前后文件的判定标准就不一致，
///    结果无法复现，也无法向用户解释"为什么这个文件被算了那个没被算"。
///
/// 空文件不覆盖既有规则：宁可沿用上一版，也不要在用户保存的中途退化成内置兜底规则集。
/// </remarks>
public sealed class CleanRuleProvider : IDisposable
{
    private static readonly TimeSpan DebounceWindow = TimeSpan.FromMilliseconds(1500);
    private static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(5);

    private readonly string filePath;
    private readonly object gate = new();
    private readonly FileSystemWatcher? watcher;

    private List<ValidatedCleanRule> rules;
    private bool dirty;
    private DateTimeOffset reloadAfter = DateTimeOffset.MinValue;
    private DateTimeOffset lastProbe = DateTimeOffset.MinValue;
    private DateTimeOffset loadedTimestamp;
    private bool disposed;

    public CleanRuleProvider(string filePath)
    {
        this.filePath = filePath;
        rules = [.. CleanRuleEngine.Load(filePath)];
        loadedTimestamp = ReadTimestamp();
        watcher = TryCreateWatcher(filePath);
    }

    /// <summary>规则集发生变化（重载成功后触发）。</summary>
    public event EventHandler? RulesChanged;

    /// <summary>最近一次成功加载的规则集快照。</summary>
    public IReadOnlyList<ValidatedCleanRule> Current
    {
        get
        {
            lock (gate)
            {
                return rules;
            }
        }
    }

    /// <summary>
    /// 取当前规则集，必要时先重载。一次扫描只在开始时调用一次并持有返回值，
    /// 即可保证整轮扫描用的是同一套规则。
    /// </summary>
    public IReadOnlyList<ValidatedCleanRule> GetRules()
    {
        bool reloaded;
        lock (gate)
        {
            var now = DateTimeOffset.UtcNow;
            if (dirty && now >= reloadAfter)
            {
                Reload();
                reloaded = true;
            }
            else if (!dirty && watcher is null && now - lastProbe >= PollInterval)
            {
                // 监听不可用时退回轮询：成本只是一次元数据读取，换来"改了一定会被看到"
                lastProbe = now;
                if (ReadTimestamp() != loadedTimestamp)
                {
                    reloadAfter = now;
                    dirty = true;
                }

                reloaded = false;
            }
            else
            {
                reloaded = false;
            }

            if (reloaded)
            {
                dirty = false;
            }
        }

        if (reloaded)
        {
            RulesChanged?.Invoke(this, EventArgs.Empty);
        }

        return Current;
    }

    /// <summary>强制重载（设置页"重新加载规则"用）。</summary>
    public void Reload()
    {
        var loaded = CleanRuleEngine.Load(filePath);
        lock (gate)
        {
            if (loaded.Count > 0)
            {
                rules = [.. loaded];
                loadedTimestamp = ReadTimestamp();
            }

            dirty = false;
            reloadAfter = DateTimeOffset.MinValue;
        }
    }

    public void Dispose()
    {
        if (disposed)
        {
            return;
        }

        disposed = true;
        watcher?.Dispose();
        GC.SuppressFinalize(this);
    }

    private void OnFileChanged(object sender, FileSystemEventArgs e)
    {
        lock (gate)
        {
            dirty = true;
            reloadAfter = DateTimeOffset.UtcNow.Add(DebounceWindow);
        }
    }

    private FileSystemWatcher? TryCreateWatcher(string path)
    {
        var directory = Path.GetDirectoryName(path);
        if (string.IsNullOrEmpty(directory) || !Directory.Exists(directory))
        {
            return null;
        }

        try
        {
            var created = new FileSystemWatcher(directory, Path.GetFileName(path))
            {
                NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.Size | NotifyFilters.CreationTime,
                IncludeSubdirectories = false,
            };
            created.Changed += OnFileChanged;
            created.Created += OnFileChanged;
            created.Renamed += OnFileChanged;
            created.EnableRaisingEvents = true;
            return created;
        }
        catch (IOException)
        {
            return null;
        }
        catch (PlatformNotSupportedException)
        {
            return null;
        }
    }

    private DateTimeOffset ReadTimestamp()
    {
        try
        {
            return File.Exists(filePath) ? File.GetLastWriteTimeUtc(filePath) : DateTimeOffset.MinValue;
        }
        catch (IOException)
        {
            return DateTimeOffset.MinValue;
        }
    }
}
