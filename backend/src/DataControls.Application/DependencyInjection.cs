using DataControls.Application.Ai;
using DataControls.Application.Catalog;
using DataControls.Application.Controls;
using DataControls.Application.Runs;
using DataControls.Core.Sql;
using DataControls.Core.Validation;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace DataControls.Application;

public static class DependencyInjection
{
    /// <summary>Registers the use-case services and the (stateless) engine components.</summary>
    public static IServiceCollection AddApplication(this IServiceCollection services, IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        services.Configure<ValidationLimits>(configuration.GetSection("Validation"));
        // One instance of the limits for the validator, the planner, the UI (GET /api/catalog/limits) and the AI context.
        services.AddSingleton(sp => sp.GetRequiredService<Microsoft.Extensions.Options.IOptions<ValidationLimits>>().Value);
        services.AddSingleton(sp => new DefinitionValidator(sp.GetRequiredService<ValidationLimits>()));
        services.AddSingleton<ISqlRenderer, SqlServerRenderer>();
        services.TryAddSingleton(TimeProvider.System);

        services.Configure<ControlRunOptions>(configuration.GetSection(ControlRunOptions.Section));
        services.AddScoped<CatalogService>();
        services.AddScoped<ControlService>();
        services.AddScoped<ControlRunService>();
        services.AddScoped<AiSuggestionService>();

        return services;
    }
}
