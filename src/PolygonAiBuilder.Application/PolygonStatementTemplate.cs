using System.Text;
using System.Text.RegularExpressions;

namespace PolygonAiBuilder.Application;

public static partial class PolygonStatementTemplate
{
    public static bool NeedsVietnameseFontEncoding(StatementContent content) =>
        ContainsVietnamese(content.Title)
        || ContainsVietnamese(content.Legend)
        || ContainsVietnamese(content.Input)
        || ContainsVietnamese(content.Output)
        || ContainsVietnamese(content.Note);

    public static string EnableVietnameseFontEncoding(string template)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(template);
        var match = FontEncodingRegex().Match(template);
        if (!match.Success)
        {
            throw new ExternalServiceException(
                "Polygon",
                "statement_template_unsupported",
                "Không tìm thấy cấu hình fontenc trong statements.ftl của Polygon để bật hỗ trợ tiếng Việt.");
        }

        var encodings = match.Groups[1].Value
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (encodings.Contains("T5", StringComparer.OrdinalIgnoreCase))
        {
            return template;
        }

        var replacement = match.Value.Replace(
            match.Groups[1].Value,
            $"{match.Groups[1].Value.Trim()},T5",
            StringComparison.Ordinal);
        return string.Concat(template.AsSpan(0, match.Index), replacement, template.AsSpan(match.Index + match.Length));
    }

    private static bool ContainsVietnamese(string value) => value
        .Normalize(NormalizationForm.FormC)
        .Any(character =>
            character is '\u0102' or '\u0103' or '\u0110' or '\u0111'
                or '\u01A0' or '\u01A1' or '\u01AF' or '\u01B0'
            || character is >= '\u1EA0' and <= '\u1EF9');

    [GeneratedRegex(@"\\usepackage\s*\[\s*([^\]]+)\s*\]\s*\{\s*fontenc\s*\}", RegexOptions.CultureInvariant)]
    private static partial Regex FontEncodingRegex();
}
