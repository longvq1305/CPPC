namespace PolygonAiBuilder.Infrastructure;

public sealed record RuntimePaths(
    string RootPath,
    string DataPath,
    string ProjectsPath,
    string LogsPath,
    string ToolchainPath)
{
    public string DatabasePath => Path.Combine(DataPath, "polygon-builder.db");
    public string SecretsPath => Path.Combine(DataPath, "secrets.local.json");

    public static RuntimePaths Create(string rootPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(rootPath);
        var fullRoot = Path.GetFullPath(rootPath);
        return new(
            fullRoot,
            Path.Combine(fullRoot, "data"),
            Path.Combine(fullRoot, "projects"),
            Path.Combine(fullRoot, "logs"),
            Path.Combine(fullRoot, "toolchain"));
    }

    public void EnsureDirectories()
    {
        Directory.CreateDirectory(RootPath);
        Directory.CreateDirectory(DataPath);
        Directory.CreateDirectory(ProjectsPath);
        Directory.CreateDirectory(LogsPath);
        Directory.CreateDirectory(ToolchainPath);
    }
}
