using DataControls.Core.Catalog;
using DataControls.Core.Tests.Fixtures;
using DataControls.Infrastructure.Catalog;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Shouldly;

namespace DataControls.IntegrationTests.Catalog;

/// <summary>Cache behaviour - no database needed (fake loader, controllable clock).</summary>
public class CachedCatalogProviderTests
{
    private sealed class ManualTime : TimeProvider
    {
        public DateTimeOffset Now { get; set; } = new(2026, 11, 12, 10, 0, 0, TimeSpan.Zero);

        public override DateTimeOffset GetUtcNow() => Now;
    }

    private sealed class FakeLoader : ICatalogLoader
    {
        public int Calls;
        public Func<Task<MetadataCatalog>> Next { get; set; } = () => Task.FromResult(TestCatalog.Instance);

        public Task<MetadataCatalog> LoadAsync(CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref Calls);
            return Next();
        }
    }

    private static (CachedCatalogProvider Provider, FakeLoader Loader, ManualTime Time) Create()
    {
        var loader = new FakeLoader();
        var time = new ManualTime();
        var provider = new CachedCatalogProvider(loader,
            Options.Create(new CatalogCacheOptions { CacheDuration = TimeSpan.FromMinutes(10) }), time,
            NullLogger<CachedCatalogProvider>.Instance);
        return (provider, loader, time);
    }

    [Fact]
    public async Task Loads_once_and_serves_from_memory_until_expiry()
    {
        var (provider, loader, time) = Create();

        var first = await provider.GetCatalogAsync(CancellationToken.None);
        time.Now += TimeSpan.FromMinutes(9);
        var second = await provider.GetCatalogAsync(CancellationToken.None);

        second.ShouldBeSameAs(first);
        loader.Calls.ShouldBe(1);

        time.Now += TimeSpan.FromMinutes(2);   // 11 minutes in total
        await provider.GetCatalogAsync(CancellationToken.None);
        loader.Calls.ShouldBe(2);
    }

    [Fact]
    public async Task Concurrent_callers_share_one_load()
    {
        var (provider, loader, _) = Create();
        var gate = new TaskCompletionSource<MetadataCatalog>(TaskCreationOptions.RunContinuationsAsynchronously);
        loader.Next = () => gate.Task;

        var callers = Enumerable.Range(0, 50).Select(_ => provider.GetCatalogAsync(CancellationToken.None)).ToList();
        gate.SetResult(TestCatalog.Instance);
        var results = await Task.WhenAll(callers);

        loader.Calls.ShouldBe(1);
        results.ShouldAllBe(c => ReferenceEquals(c, TestCatalog.Instance));
    }

    [Fact]
    public async Task A_failed_load_is_not_cached()
    {
        var (provider, loader, _) = Create();
        loader.Next = () => Task.FromException<MetadataCatalog>(new InvalidOperationException("database down"));

        await Should.ThrowAsync<InvalidOperationException>(() => provider.GetCatalogAsync(CancellationToken.None));

        loader.Next = () => Task.FromResult(TestCatalog.Instance);
        (await provider.GetCatalogAsync(CancellationToken.None)).ShouldBeSameAs(TestCatalog.Instance);
        loader.Calls.ShouldBe(2);
    }

    [Fact]
    public async Task A_cancelled_caller_stops_waiting_without_cancelling_the_shared_load()
    {
        var (provider, loader, _) = Create();
        var gate = new TaskCompletionSource<MetadataCatalog>(TaskCreationOptions.RunContinuationsAsynchronously);
        loader.Next = () => gate.Task;
        using var cts = new CancellationTokenSource();

        var impatient = provider.GetCatalogAsync(cts.Token);
        var patient = provider.GetCatalogAsync(CancellationToken.None);
        await cts.CancelAsync();
        await Should.ThrowAsync<OperationCanceledException>(() => impatient);

        gate.SetResult(TestCatalog.Instance);
        (await patient).ShouldBeSameAs(TestCatalog.Instance);
        loader.Calls.ShouldBe(1);
    }
}
