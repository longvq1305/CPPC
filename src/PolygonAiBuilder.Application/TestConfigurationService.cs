namespace PolygonAiBuilder.Application;

public sealed class TestConfigurationService(ITestConfigurationRepository repository) : ITestConfigurationService
{
    public Task<TestConfigurationSnapshot?> LoadAsync(Guid projectId, CancellationToken cancellationToken = default) =>
        repository.GetAsync(projectId, cancellationToken);

    public Task<TestConfigurationSnapshot> SaveAsync(
        Guid projectId,
        TestConfigurationUpdate update,
        CancellationToken cancellationToken = default)
    {
        Validate(update);
        return repository.SaveAsync(projectId, update, cancellationToken);
    }

    public Task<TestConfigurationSnapshot> RegenerateScriptAsync(
        Guid projectId,
        TestConfigurationUpdate update,
        CancellationToken cancellationToken = default)
    {
        var regenerated = update with { Script = CreateDefaultScript(update.TestCount) };
        Validate(regenerated);
        return repository.SaveAsync(projectId, regenerated, cancellationToken);
    }

    public string CreateDefaultScript(int testCount)
    {
        if (testCount is < 1 or > 1000) throw new ArgumentOutOfRangeException(nameof(testCount));
        return $"<#list 1..{testCount} as i>\n    gen ${{i}} > $\n</#list>";
    }

    private static void Validate(TestConfigurationUpdate update)
    {
        if (string.IsNullOrWhiteSpace(update.TestsetName) || update.TestsetName.Trim().Length > 64)
            throw new ArgumentException("Testset name phải có 1–64 ký tự.");
        if (update.TestCount is < 1 or > 1000)
            throw new ArgumentOutOfRangeException(nameof(update), "Number of tests phải trong khoảng 1–1000.");
        if (update.ScorePerTest < 0 || update.ScorePerTest > 1_000_000)
            throw new ArgumentOutOfRangeException(nameof(update), "Score per test không hợp lệ.");
        if (update.Checker is not ("ncmp.cpp" or "wcmp.cpp"))
            throw new ArgumentException("Checker chỉ có thể là ncmp.cpp hoặc wcmp.cpp.");
        if (string.IsNullOrWhiteSpace(update.Script) || update.Script.Length > 200_000)
            throw new ArgumentException("Test script rỗng hoặc vượt quá 200 KB.");
        if (update.CommitMessage.Length > 500)
            throw new ArgumentException("Commit message không được vượt quá 500 ký tự.");
    }
}
