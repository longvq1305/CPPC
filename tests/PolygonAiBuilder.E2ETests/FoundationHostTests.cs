using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.Sqlite;

namespace PolygonAiBuilder.E2ETests;

public sealed class FoundationHostTests
{
    [Fact]
    public async Task Host_StartsMigratesDatabaseAndServesDashboard()
    {
        var rootPath = Path.Combine(
            Path.GetTempPath(),
            "PolygonAiBuilder.E2E",
            Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(rootPath);

        try
        {
            using var baseFactory = new WebApplicationFactory<Program>();
            using var factory = baseFactory.WithWebHostBuilder(builder =>
            {
                builder.UseSetting("Storage:RootPath", rootPath);
                builder.UseSetting("Browser:OpenOnStartup", "false");
            });
            using var client = factory.CreateClient();

            var health = await client.GetStringAsync("/health", CancellationToken.None);
            using var dashboardResponse = await client.GetAsync("/", CancellationToken.None);
            var dashboard = await dashboardResponse.Content.ReadAsStringAsync(CancellationToken.None);

            Assert.Contains("healthy", health, StringComparison.Ordinal);
            Assert.True(dashboardResponse.IsSuccessStatusCode, dashboard);
            Assert.Contains("Dự án lập trình", dashboard, StringComparison.Ordinal);
            Assert.True(File.Exists(Path.Combine(rootPath, "data", "polygon-builder.db")));
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            if (Directory.Exists(rootPath))
            {
                Directory.Delete(rootPath, recursive: true);
            }
        }
    }
}
