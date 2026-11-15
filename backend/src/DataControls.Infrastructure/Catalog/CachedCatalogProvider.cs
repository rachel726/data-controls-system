using DataControls.Core.Catalog;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace DataControls.Infrastructure.Catalog;

public sealed class CatalogCacheOptions
{
    public const string Section = "Catalog";

    /// <summary>How long a loaded catalog is reused before it is read again.</summary>
    public TimeSpan CacheDuration { get; set; } = TimeSpan.FromMinutes(10);
}

/// <summary>
/// Serves the catalog from memory and reloads it after <see cref="CatalogCacheOptions.CacheDuration"/>.
/// Concurrent callers share ONE load (no stampede); a failed load is not cached, so the next call retries.
/// The shared load is not bound to any single caller's cancellation token - each caller only stops waiting.
/// </summary>
public sealed partial class CachedCatalogProvider(
    ICatalogLoader loader,
    IOptions<CatalogCacheOptions> options,
    TimeProvider time,
    ILogger<CachedCatalogProvider> logger) : ICatalogProvider
{
    private readonly Lock _gate = new();
    private Task<MetadataCatalog>? _current;
    private DateTimeOffset _expiresAt;

    public Task<MetadataCatalog> GetCatalogAsync(CancellationToken cancellationToken)
    {
        lock (_gate)
        {
            if (_current is null || _current.IsFaulted || _current.IsCanceled || time.GetUtcNow() >= _expiresAt)
            {
                _current = LoadAsync();
                _expiresAt = time.GetUtcNow() + options.Value.CacheDuration;
            }

            return _current.WaitAsync(cancellationToken);
        }
    }

    private async Task<MetadataCatalog> LoadAsync()
    {
        try
        {
            var catalog = await loader.LoadAsync(CancellationToken.None).ConfigureAwait(false);
            if (logger.IsEnabled(LogLevel.Information))
            {
                var fieldCount = catalog.Entities.Sum(e => e.Fields.Count);
                LogLoaded(logger, catalog.Entities.Count, fieldCount);
            }
            return catalog;
        }
        catch (Exception ex)
        {
            LogLoadFailed(logger, ex);
            throw;
        }
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Business catalog loaded: {Entities} entities, {Fields} fields.")]
    private static partial void LogLoaded(ILogger logger, int entities, int fields);

    [LoggerMessage(Level = LogLevel.Error, Message = "Loading the business catalog failed; the next request will retry.")]
    private static partial void LogLoadFailed(ILogger logger, Exception exception);
}
