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
            client.Timeout = TimeSpan.FromMinutes(5);
            client.DefaultRequestHeaders.UserAgent.ParseAdd("PolygonAiBuilder/0.2");
        });
        services.AddScoped<IPolygonClient>(provider => provider.GetRequiredService<PolygonClient>());
        services.AddHttpClient<OpenAiProvider>(client =>
        {
            client.BaseAddress = new Uri("https://api.openai.com/", UriKind.Absolute);
            client.Timeout = TimeSpan.FromMinutes(5);
            client.DefaultRequestHeaders.UserAgent.ParseAdd("PolygonAiBuilder/0.3");
        });
        services.AddHttpClient<GeminiProvider>(client =>
        {
            client.BaseAddress = new Uri("https://generativelanguage.googleapis.com/", UriKind.Absolute);
            client.Timeout = TimeSpan.FromMinutes(5);
            client.DefaultRequestHeaders.UserAgent.ParseAdd("PolygonAiBuilder/0.3");
        });
        services.AddScoped<IAiProvider>(provider => provider.GetRequiredService<OpenAiProvider>());
        services.AddScoped<IAiProvider>(provider => provider.GetRequiredService<GeminiProvider>());
        return services;
    }
}
