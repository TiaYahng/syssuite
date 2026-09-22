using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using SysSuite.Core.Abstractions.Data;

namespace SysSuite.Core.Data;

public static partial class UninstallCatalogMerger
{
    [GeneratedRegex(@"\s+(version|v)?\d+(\.\d+)+$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex TrailingVersion();

    public static IReadOnlyList<AppRecord> Merge(IEnumerable<AppRecord> records)
    {
        var uniqueStableKeys = new Dictionary<string, AppRecord>(StringComparer.OrdinalIgnoreCase);
        foreach (var record in records)
        {
            var key = record.StableKey.Trim();
            if (!uniqueStableKeys.TryGetValue(key, out var existing) || HasHigherPriority(record, existing))
            {
                uniqueStableKeys[key] = record;
            }
        }

        var groups = new Dictionary<string, List<AppRecord>>(StringComparer.OrdinalIgnoreCase);
        foreach (var record in uniqueStableKeys.Values)
        {
            var deduplicationKey = CreateDeduplicationKey(record);
            if (!groups.TryGetValue(deduplicationKey, out var group))
            {
                group = [];
                groups[deduplicationKey] = group;
            }

            group.Add(record);
        }

        var merged = new List<AppRecord>();
        foreach (var group in groups.Values)
        {
            var winner = group.OrderBy((AppRecord record) => record, PriorityComparer.Instance).First();
            var combined = winner;
            foreach (var candidate in group.OrderByDescending(CountPopulatedFields))
            {
                combined = combined with
                {
                    Publisher = combined.Publisher ?? candidate.Publisher,
                    Version = combined.Version ?? candidate.Version,
                    InstallDate = combined.InstallDate ?? candidate.InstallDate,
                    Size = combined.Size ?? candidate.Size,
                    UninstallString = combined.UninstallString ?? candidate.UninstallString,
                    QuietString = combined.QuietString ?? candidate.QuietString,
                    KeyPath = combined.KeyPath ?? candidate.KeyPath,
                    InstallDir = combined.InstallDir ?? candidate.InstallDir,
                    IconPath = combined.IconPath ?? candidate.IconPath,
                    Hash = combined.Hash ?? candidate.Hash,
                    UpdatedAt = group.Max(record => record.UpdatedAt)
                };
            }

            merged.Add(combined);
        }

        return merged.OrderBy(record => record.Name, StringComparer.CurrentCultureIgnoreCase)
                     .ThenBy(record => record.StableKey, StringComparer.OrdinalIgnoreCase)
                     .ToList();
    }

    private static string CreateDeduplicationKey(AppRecord record)
    {
        var name = TrailingVersion().Replace(record.Name.Trim(), string.Empty).ToLowerInvariant();
        name = string.Join(" ", name.Split(' ', StringSplitOptions.RemoveEmptyEntries));
        var publisher = string.IsNullOrWhiteSpace(record.Publisher)
            ? string.Empty
            : Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(record.Publisher.Trim())));
        var productCode = GetProductCode(record);
        return productCode is null ? $"{name}|{publisher}" : $"MSI|{productCode}";
    }

    private static string? GetProductCode(AppRecord record)
    {
        return record.Source == AppSource.Msi
            ? record.StableKey["MSI|".Length..]
            : record.KeyPath is null ? null : ExtractProductCode(record.KeyPath);
    }

    private static string? ExtractProductCode(string value)
    {
        var start = value.IndexOf('{');
        var end = start >= 0 ? value.IndexOf('}', start) : -1;
        return start >= 0 && end > start ? value[start..(end + 1)] : null;
    }

    private static bool HasHigherPriority(AppRecord candidate, AppRecord current)
    {
        return PriorityComparer.Instance.Compare(candidate, current) < 0;
    }

    private static int CountPopulatedFields(AppRecord? record)
    {
        if (record is null)
        {
            return 0;
        }

        return Convert.ToInt32(record.Publisher is not null)
            + Convert.ToInt32(record.Version is not null)
            + Convert.ToInt32(record.InstallDate is not null)
            + Convert.ToInt32(record.Size is not null)
            + Convert.ToInt32(record.UninstallString is not null)
            + Convert.ToInt32(record.QuietString is not null)
            + Convert.ToInt32(record.KeyPath is not null)
            + Convert.ToInt32(record.InstallDir is not null)
            + Convert.ToInt32(record.IconPath is not null);
    }

    private sealed class PriorityComparer : IComparer<AppRecord>
    {
        public static readonly PriorityComparer Instance = new();

        public int Compare(AppRecord? left, AppRecord? right)
        {
            var priority = GetPriority(left) - GetPriority(right);
            if (priority != 0)
            {
                return priority;
            }

            var completeness = CountPopulatedFields(right) - CountPopulatedFields(left);
            return completeness != 0 ? completeness : string.CompareOrdinal(left?.StableKey, right?.StableKey);
        }

        private static int GetPriority(AppRecord? record)
        {
            return record?.Source switch
            {
                AppSource.Msi => 0,
                AppSource.Registry => 1,
                AppSource.Store => 2,
                _ => 3
            };
        }

    }
}
