using Microsoft.Extensions.DependencyInjection;
using PolygonAiBuilder.Application;

namespace PolygonAiBuilder.Integrations;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddPolygonAiBuilderIntegrations(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        services.AddHttpClient<PolygonClient>(client =>
        {
            client.BaseAddress = new Uri("https://polygon.codeforces.com/api/", UriKind.Absolute);
            client.Timeout = TimeSpan.FromSeconds(30);
            client.DefaultRequestHeaders.UserAgent.ParseAdd("PolygonAiBuilder/0.2");
        });
        services.AddScoped<IPolygonClient>(provider => provider.GetRequiredService<PolygonClient>());
        return services;
    }
}
