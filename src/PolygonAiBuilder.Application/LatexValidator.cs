using System.Text.RegularExpressions;

namespace PolygonAiBuilder.Application;

public sealed partial class LatexValidator : ILatexValidator
{
    private static readonly HashSet<string> KnownCommands = new(StringComparer.Ordinal)
    {
        "begin", "end", "textbf", "textit", "texttt", "emph", "item", "hline",
        "frac", "sqrt", "sum", "min", "max", "left", "right", "cdot", "times",
        "le", "leq", "ge", "geq", "neq", "in", "ldots", "dots", "mod", "bmod",
        "mathrm", "mathbf", "mathit", "operatorname", "log", "ln", "gcd", "infty",
        "quad", "qquad", "newline", "\\",
    };

    public IReadOnlyList<LatexIssue> Validate(StatementContent content)
    {
        var issues = new List<LatexIssue>();
        ValidateField("Title", content.Title, issues);
        ValidateField("Legend", content.Legend, issues);
        ValidateField("Input", content.Input, issues);
        ValidateField("Output", content.Output, issues);
        ValidateField("Note", content.Note, issues);
        return issues;
    }

    private static void ValidateField(string field, string value, ICollection<LatexIssue> issues)
    {
        var braceDepth = 0;
        for (var index = 0; index < value.Length; index++)
        {
            if (IsEscaped(value, index))
            {
                continue;
            }

            if (value[index] == '{')
            {
                braceDepth++;
            }
            else if (value[index] == '}')
            {
                braceDepth--;
                if (braceDepth < 0)
                {
                    issues.Add(new(field, LatexIssueSeverity.Error, "Có dấu } không có dấu { tương ứng."));
                    break;
                }
            }
        }

        if (braceDepth > 0)
        {
            issues.Add(new(field, LatexIssueSeverity.Error, "Dấu ngoặc nhọn { } chưa cân bằng."));
        }

        var environmentStack = new Stack<string>();
        foreach (Match match in EnvironmentRegex().Matches(value))
        {
            var action = match.Groups[1].Value;
            var environment = match.Groups[2].Value;
            if (action == "begin")
            {
                environmentStack.Push(environment);
            }
            else if (environmentStack.Count == 0 || environmentStack.Pop() != environment)
            {
                issues.Add(new(field, LatexIssueSeverity.Error,
                    $"Môi trường \\end{{{environment}}} không khớp với \\begin gần nhất."));
            }
        }

        if (environmentStack.Count > 0)
        {
            issues.Add(new(field, LatexIssueSeverity.Error,
                $"Thiếu \\end{{{environmentStack.Peek()}}}."));
        }

        var dollarCount = Enumerable.Range(0, value.Length)
            .Count(index => value[index] == '$' && !IsEscaped(value, index));
        if (dollarCount % 2 != 0)
        {
            issues.Add(new(field, LatexIssueSeverity.Error, "Delimiter toán học $ chưa đóng."));
        }

        foreach (Match match in CommandRegex().Matches(value))
        {
            var command = match.Groups[1].Value;
            if (!KnownCommands.Contains(command))
            {
                issues.Add(new(field, LatexIssueSeverity.Warning,
                    $"Lệnh LaTeX \\{command} chưa có trong danh sách preview thông dụng; Polygon có thể vẫn hỗ trợ."));
            }
        }
    }

    private static bool IsEscaped(string value, int index)
    {
        var slashCount = 0;
        for (var current = index - 1; current >= 0 && value[current] == '\\'; current--)
        {
            slashCount++;
        }

        return slashCount % 2 != 0;
    }

    [GeneratedRegex(@"\\(begin|end)\{([A-Za-z*]+)\}", RegexOptions.CultureInvariant)]
    private static partial Regex EnvironmentRegex();

    [GeneratedRegex(@"\\([A-Za-z]+|\\)", RegexOptions.CultureInvariant)]
    private static partial Regex CommandRegex();
}
