using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using PolygonAiBuilder.Application;
using PolygonAiBuilder.Domain;
using PolygonAiBuilder.Infrastructure;

namespace PolygonAiBuilder.IntegrationTests;

public sealed class CodePersistenceTests
{
    [Fact]
    public async Task GeneratedCode_IsVersionedMirroredMarkedStaleAndRestorable()
    {
        using var temporary = new TemporaryDirectory();
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddPolygonAiBuilderInfrastructure(RuntimePaths.Create(temporary.Path));
        await using var provider = services.BuildServiceProvider();
        await provider.MigratePolygonAiBuilderDatabaseAsync();

        await using var scope = provider.CreateAsyncScope();
        var projects = scope.ServiceProvider.GetRequiredService<IProjectService>();
        var statements = scope.ServiceProvider.GetRequiredService<IStatementRepository>();
        var codes = scope.ServiceProvider.GetRequiredService<ICodeRepository>();
        var project = await projects.CreateAsync("code-version-test");
        var statement = await statements.SaveAsync(project.Id,
            new("Sum", "Add two integers.", "Two integers.", "Their sum.", ""),
            ChangeSource.User, null, null, null, CancellationToken.None);

        var generated = await codes.SaveGeneratedAsync(project.Id,
            new(Solution("return 0;"), Generator("cout << \"1 2\\n\";"), "summary", "O(1)", "O(1)", "ncmp.cpp", []),
            statement.CurrentVersion, "Gemini", "gemini-test", CancellationToken.None);

        Assert.Equal(1, generated.Solution!.Version);
        Assert.Equal(1, generated.Generator!.Version);
        Assert.False(generated.HasStaleCode);
        var codeDirectory = Path.Combine(temporary.Path, "projects", project.Id.ToString("N"), "code");
        Assert.Equal(generated.Solution.Content, await File.ReadAllTextAsync(Path.Combine(codeDirectory, "solution.cpp")));
        Assert.Equal(generated.Generator.Content, await File.ReadAllTextAsync(Path.Combine(codeDirectory, "generate.cpp")));

        var edited = await codes.SaveArtifactAsync(project.Id, CodeArtifactType.Solution, Solution("return 7;"),
            ChangeSource.User, statement.CurrentVersion, null, null, CancellationToken.None);
        Assert.Equal(2, edited.Solution!.Version);
        Assert.Equal([2, 1], edited.Solution.History.Select(item => item.VersionNumber));
        Assert.Equal(Solution("return 0;"), edited.Solution.History.Single(item => item.VersionNumber == 1).Content);

        await codes.MarkCompileAsync(project.Id, CodeArtifactType.Solution, CompileStatus.Failed, "compiler error", CancellationToken.None);
        var compiled = await codes.GetAsync(project.Id, CancellationToken.None);
        Assert.Equal(CompileStatus.Failed, compiled!.Solution!.LastCompileStatus);
        Assert.Equal("compiler error", compiled.Solution.History[0].CompilerOutput);

        await statements.SaveAsync(project.Id,
            new("Sum changed", "Add two integers.", "Two integers.", "Their sum.", ""),
            ChangeSource.User, null, null, null, CancellationToken.None);
        var stale = await codes.GetAsync(project.Id, CancellationToken.None);
        Assert.True(stale!.HasStaleCode);

        var restored = await codes.RestoreAsync(project.Id, CodeArtifactType.Solution, 1, CancellationToken.None);
        Assert.Equal(3, restored.Solution!.Version);
        Assert.Equal(Solution("return 0;"), restored.Solution.Content);
        Assert.Equal(CompileStatus.NotCompiled, restored.Solution.LastCompileStatus);
    }

    [Fact]
    public async Task SaveGenerated_RejectsWhenStatementChangedDuringGeneration()
    {
        using var temporary = new TemporaryDirectory();
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddPolygonAiBuilderInfrastructure(RuntimePaths.Create(temporary.Path));
        await using var provider = services.BuildServiceProvider();
        await provider.MigratePolygonAiBuilderDatabaseAsync();
        await using var scope = provider.CreateAsyncScope();
        var projects = scope.ServiceProvider.GetRequiredService<IProjectService>();
        var statements = scope.ServiceProvider.GetRequiredService<IStatementRepository>();
        var codes = scope.ServiceProvider.GetRequiredService<ICodeRepository>();
        var project = await projects.CreateAsync("statement-race-test");
        await statements.SaveAsync(project.Id, new("T", "L", "I", "O", ""),
            ChangeSource.User, null, null, null, CancellationToken.None);
        await statements.SaveAsync(project.Id, new("T2", "L", "I", "O", ""),
            ChangeSource.User, null, null, null, CancellationToken.None);

        await Assert.ThrowsAsync<InvalidOperationException>(() => codes.SaveGeneratedAsync(project.Id,
            new(Solution("return 0;"), Generator("cout << \"x\\n\";"), "", "O(1)", "O(1)", "wcmp.cpp", []),
            1, "OpenAI", "gpt-test", CancellationToken.None));
    }

    private static string Solution(string body) => $"#include <bits/stdc++.h>\nusing namespace std;\nint main(){{ios_base::sync_with_stdio(false);cin.tie(NULL);{body}}}\n";
    private static string Generator(string body) => $"#include <bits/stdc++.h>\n#include \"testlib.h\"\nusing namespace std;\nint main(int argc,char** argv){{registerGen(argc,argv,1); int test_id=stoi(argv[1]); mt19937_64 gen(test_id); (void)gen; {body}}}\n";
}
