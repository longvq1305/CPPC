using PolygonAiBuilder.Infrastructure;
using PolygonAiBuilder.Web.Components;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

var configuredRoot = builder.Configuration["Storage:RootPath"];
var runtimeRoot = string.IsNullOrWhiteSpace(configuredRoot)
    ? builder.Environment.IsDevelopment()
        ? Path.GetFullPath(Path.Combine(builder.Environment.ContentRootPath, "..", ".."))
        : builder.Environment.ContentRootPath
    : Path.GetFullPath(configuredRoot, builder.Environment.ContentRootPath);
builder.Services.AddPolygonAiBuilderInfrastructure(RuntimePaths.Create(runtimeRoot));

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
app.Run();

public partial class Program;
