using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace PolygonAiBuilder.Infrastructure;

public sealed class BuilderDbContextDesignFactory : IDesignTimeDbContextFactory<BuilderDbContext>
{
    public BuilderDbContext CreateDbContext(string[] args)
    {
        var options = new DbContextOptionsBuilder<BuilderDbContext>()
            .UseSqlite("Data Source=polygon-builder.design.db")
            .Options;
        return new BuilderDbContext(options);
    }
}
