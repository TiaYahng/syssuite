using System.Text.Json;
using Microsoft.Win32;
using SysSuite.Core.Abstractions;
using SysSuite.Core.Abstractions.Data;

namespace SysSuite.Core.Data;

public sealed class KnownSoftwareProfileEnumerator
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true
    };

    private readonly string profilePath;

    public KnownSoftwareProfileEnumerator(string? profilePath = null)
    {
        this.profilePath = profilePath ?? Path.Combine(AppContext.BaseDirectory, "known-software-profiles.json");
    }

    public Result<IReadOnlyList<AppRecord>> Enumerate()
    {
        try
        {
            if (!File.Exists(profilePath))
            {
                return new Result<IReadOnlyList<AppRecord>>(ErrorType.None, string.Empty, []);
            }

            using var stream = File.OpenRead(profilePath);
            var archive = JsonSerializer.Deserialize<KnownSoftwareArchive>(stream, SerializerOptions);
            if (archive?.Profiles is null)
            {
                return new Result<IReadOnlyList<AppRecord>>(ErrorType.InvalidInput, "已知软件档案格式无效。");
            }

            var apps = new List<AppRecord>();
            foreach (var profile in archive.Profiles)
            {
                var serviceKeyPath = @"SYSTEM\CurrentControlSet\Services\" + profile.ServiceName;
                var serviceInstalled = Registry.LocalMachine.OpenSubKey(serviceKeyPath) is not null;
                var directoryInstalled = profile.InstallDirectories.Any(Directory.Exists);
                if (!serviceInstalled && !directoryInstalled)
                {
                    continue;
                }

                apps.Add(AppRecord.Create(
                    $"KNOWN|{profile.StableKey}",
                    profile.Name,
                    AppSource.Registry,
                    profile.Publisher,
                    profile.Version,
                    keyPath: serviceInstalled ? @"HKEY_LOCAL_MACHINE\" + serviceKeyPath : FirstInstallDirectory(profile.InstallDirectories),
                    installDir: FindExistingDirectory(profile.InstallDirectories),
                    hash: $"profile-v{archive.SchemaVersion}|{profile.SourceNote}"));
            }

            return new Result<IReadOnlyList<AppRecord>>(ErrorType.None, string.Empty, apps);
        }
        catch (Exception exception)
        {
            return new Result<IReadOnlyList<AppRecord>>(ErrorType.Internal, exception.Message);
        }
    }

    private static string? FindExistingDirectory(IReadOnlyList<string> directories)
    {
        for (var index = 0; index < directories.Count; index++)
        {
            if (Directory.Exists(directories[index]))
            {
                return directories[index];
            }
        }

        return null;
    }

    private static string? FirstInstallDirectory(IReadOnlyList<string> directories)
    {
        return directories.Count == 0 ? null : directories[0];
    }
}

public sealed record KnownSoftwareArchive(int SchemaVersion, IReadOnlyList<KnownSoftwareProfile> Profiles);

public sealed record KnownSoftwareProfile(
    string StableKey,
    string Name,
    string? Publisher,
    string? Version,
    string ServiceName,
    IReadOnlyList<string> InstallDirectories,
    string SourceNote);
