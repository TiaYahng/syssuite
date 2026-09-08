using SysSuite.Core.Abstractions.Settings;

namespace SysSuite.Core.Abstractions;

public interface ISettingsService
{
    AppSettings Current { get; }

    Task<Result> SaveAsync(CancellationToken cancellationToken = default);

    void SaveDebounced();
}
