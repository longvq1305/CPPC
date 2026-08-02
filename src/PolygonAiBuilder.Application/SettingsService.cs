namespace PolygonAiBuilder.Application;

public sealed class SettingsService(
    ISecretStore secretStore,
    IApplicationSettingsRepository settingsRepository) : ISettingsService
{
    public const string OpenAiDefaultModelKey = "OpenAI.DefaultModel";
    public const string GeminiDefaultModelKey = "Gemini.DefaultModel";
    private const string Mask = "••••••••";

    public async Task<SettingsSnapshot> LoadAsync(CancellationToken cancellationToken = default)
    {
        var secretsTask = secretStore.LoadAsync(cancellationToken);
        var settingsTask = settingsRepository.GetAllAsync(cancellationToken);
        await Task.WhenAll(secretsTask, settingsTask);

        var secrets = await secretsTask;
        var settings = await settingsTask;
        return new(
            HasValue(secrets.OpenAiApiKey),
            HasValue(secrets.GeminiApiKey),
            HasValue(secrets.PolygonApiKey),
            HasValue(secrets.PolygonApiSecret),
            ToMask(secrets.OpenAiApiKey),
            ToMask(secrets.GeminiApiKey),
            ToMask(secrets.PolygonApiKey),
            ToMask(secrets.PolygonApiSecret),
            settings.GetValueOrDefault(OpenAiDefaultModelKey, string.Empty),
            settings.GetValueOrDefault(GeminiDefaultModelKey, string.Empty));
    }

    public async Task SaveAsync(SettingsUpdate update, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(update);
        ValidateModelId(update.OpenAiDefaultModel, nameof(update.OpenAiDefaultModel));
        ValidateModelId(update.GeminiDefaultModel, nameof(update.GeminiDefaultModel));

        var current = await secretStore.LoadAsync(cancellationToken);
        var updated = new SecretBundle(
            SelectValue(current.OpenAiApiKey, update.OpenAiApiKey, update.ClearOpenAiApiKey),
            SelectValue(current.GeminiApiKey, update.GeminiApiKey, update.ClearGeminiApiKey),
            SelectValue(current.PolygonApiKey, update.PolygonApiKey, update.ClearPolygonApiKey),
            SelectValue(current.PolygonApiSecret, update.PolygonApiSecret, update.ClearPolygonApiSecret));

        await secretStore.SaveAsync(updated, cancellationToken);
        await settingsRepository.SetManyAsync(
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                [OpenAiDefaultModelKey] = update.OpenAiDefaultModel.Trim(),
                [GeminiDefaultModelKey] = update.GeminiDefaultModel.Trim(),
            },
            cancellationToken);
    }

    private static string SelectValue(string current, string? replacement, bool clear)
    {
        if (clear)
        {
            return string.Empty;
        }

        return string.IsNullOrWhiteSpace(replacement) ? current : replacement.Trim();
    }

    private static bool HasValue(string value) => !string.IsNullOrEmpty(value);
    private static string ToMask(string value) => HasValue(value) ? Mask : string.Empty;

    private static void ValidateModelId(string modelId, string parameterName)
    {
        if (modelId.Length > 200 || modelId.Any(char.IsControl))
        {
            throw new ArgumentException("Model ID is invalid.", parameterName);
        }
    }
}
