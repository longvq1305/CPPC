using PolygonAiBuilder.Application;

namespace PolygonAiBuilder.Infrastructure;

public sealed class CheckerSourceStore(RuntimePaths paths) : ICheckerSourceStore
{
    public async Task<string> ReadAsync(string checkerName, CancellationToken cancellationToken = default)
    {
        if (checkerName is not ("ncmp.cpp" or "wcmp.cpp"))
            throw new ArgumentException("Checker không được hỗ trợ.", nameof(checkerName));
        var path = Path.Combine(paths.ToolchainPath, "checkers", checkerName);
        if (!File.Exists(path)) throw new FileNotFoundException("Thiếu bundled checker source.", path);
        return await File.ReadAllTextAsync(path, cancellationToken);
    }
}
