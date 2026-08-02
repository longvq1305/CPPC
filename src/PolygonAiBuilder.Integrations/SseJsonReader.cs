using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;

namespace PolygonAiBuilder.Integrations;

internal static class SseJsonReader
{
    public static async IAsyncEnumerable<JsonDocument> ReadAsync(
        HttpContent content,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        await using var stream = await content.ReadAsStreamAsync(cancellationToken);
        using var reader = new StreamReader(stream, Encoding.UTF8, true, 8192, leaveOpen: false);
        var data = new StringBuilder();
        while (true)
        {
            var line = await reader.ReadLineAsync(cancellationToken);
            if (line is null)
            {
                if (data.Length > 0 && TryParse(data.ToString(), out var finalDocument))
                {
                    yield return finalDocument;
                }

                yield break;
            }

            if (line.Length == 0)
            {
                if (data.Length > 0 && TryParse(data.ToString(), out var document))
                {
                    yield return document;
                }

                data.Clear();
                continue;
            }

            if (line.StartsWith("data:", StringComparison.Ordinal))
            {
                if (data.Length > 0)
                {
                    data.Append('\n');
                }

                data.Append(line.AsSpan(5).TrimStart());
            }
            else if (line[0] == '{')
            {
                if (TryParse(line, out var document))
                {
                    yield return document;
                }
            }
        }
    }

    private static bool TryParse(string data, out JsonDocument document)
    {
        document = null!;
        if (string.Equals(data.Trim(), "[DONE]", StringComparison.Ordinal))
        {
            return false;
        }

        try
        {
            document = JsonDocument.Parse(data);
            return true;
        }
        catch (JsonException)
        {
            return false;
        }
    }
}
