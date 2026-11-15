using System.Net;
using DataControls.Application.Ai;
using DataControls.Core.Catalog;
using DataControls.Core.Controls;
using DataControls.Core.Sql;
using DataControls.Infrastructure.Ai;
using DataControls.Infrastructure.Catalog;
using DataControls.Infrastructure.Controls;
using DataControls.Infrastructure.Execution;
using DataControls.Infrastructure.Data;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace DataControls.Infrastructure;

public static class DependencyInjection
{
    /// <summary>Registers data access and other infrastructure services.</summary>
    public static IServiceCollection AddInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        services.AddSingleton(TimeProvider.System);
        services.AddSingleton<ISqlConnectionFactory>(_ =>
            new SqlConnectionFactory(configuration.GetConnectionString("DataControlsDb") ?? string.Empty));

        services.Configure<CatalogCacheOptions>(configuration.GetSection(CatalogCacheOptions.Section));
        services.AddSingleton<ICatalogLoader, CatalogLoader>();
        services.AddSingleton<ICatalogProvider, CachedCatalogProvider>();

        services.AddSingleton<IControlRepository, ControlRepository>();

        services.Configure<QueryExecutionOptions>(configuration.GetSection(QueryExecutionOptions.Section));
        services.AddSingleton<IQueryExecutor, SqlQueryExecutor>();

        AddAiClient(services, configuration);

        return services;
    }

    /// <summary>
    /// Typed HttpClient for the AI service. The whole call (a retry included) is capped at the configured timeout;
    /// only a connection failure or 503 is retried, once - an answer rejected as invalid would just be paid for twice.
    /// </summary>
    private static void AddAiClient(IServiceCollection services, IConfiguration configuration)
    {
        var section = configuration.GetSection(AiServiceOptions.Section);
        services.Configure<AiServiceOptions>(section);
        var options = section.Get<AiServiceOptions>() ?? new AiServiceOptions();
        var timeout = TimeSpan.FromSeconds(options.TimeoutSeconds);

        services.AddHttpClient<IControlSuggestionClient, PythonAiServiceClient>(http =>
            {
                http.BaseAddress = options.BaseUrl;
                http.Timeout = Timeout.InfiniteTimeSpan;   // the resilience pipeline owns the timeouts
            })
            .AddStandardResilienceHandler(o =>
            {
                o.TotalRequestTimeout.Timeout = timeout;
                o.AttemptTimeout.Timeout = timeout;
                o.CircuitBreaker.SamplingDuration = timeout * 2;   // must be at least twice the attempt timeout
                o.Retry.MaxRetryAttempts = 1;
                o.Retry.ShouldHandle = args => ValueTask.FromResult(
                    args.Outcome.Exception is HttpRequestException
                    || args.Outcome.Result?.StatusCode == HttpStatusCode.ServiceUnavailable);
            });
    }
}
