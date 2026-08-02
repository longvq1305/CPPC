using PolygonAiBuilder.Infrastructure;
using PolygonAiBuilder.Integrations;
using PolygonAiBuilder.Web;
using PolygonAiBuilder.Web.Components;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();
builder.Services.AddSingleton<StartupBrowserLauncher>();
builder.Services.AddPolygonAiBuilderIntegrations();

var configuredRoot = builder.Configuration["Storage:RootPath"];
var runtimeRoot = string.IsNullOrWhiteSpace(configuredRoot)
    ? builder.Environment.IsDevelopment()
        ? Path.GetFullPath(Path.Combine(builder.Environment.ContentRootPath, "..", ".."))
        : builder.Environment.ContentRootPath
    : Path.GetFullPath(configuredRoot, builder.Environment.ContentRootPath);
var runtimePaths = RuntimePaths.Create(runtimeRoot);
runtimePaths.EnsureDirectories();
builder.Logging.AddProvider(new DailyFileLoggerProvider(runtimePaths.LogsPath));
builder.Services.AddPolygonAiBuilderInfrastructure(runtimePaths);

var app = builder.Build();

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
}
app.UseStatusCodePagesWithReExecute("/not-found", createScopeForStatusCodePages: true);
app.UseAntiforgery();
app.MapStaticAssets();
app.MapGet("/health", () => Results.Ok(new { status = "healthy" })).DisableAntiforgery();
app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

await app.Services.MigratePolygonAiBuilderDatabaseAsync();
app.Services.GetRequiredService<StartupBrowserLauncher>().Register(app.Lifetime);
app.Run();

public partial class Program;
