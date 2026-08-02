namespace PolygonAiBuilder.Application;

public static class GeneralInfoValidator
{
    public const int MinimumTimeLimitMs = 250;
    public const int MaximumTimeLimitMs = 15_000;
    public const int TimeLimitIncrementMs = 50;
    public const int MinimumMemoryLimitMb = 4;
    public const int MaximumMemoryLimitMb = 1_024;
    public const int MaximumFileNameLength = 64;

    public static IReadOnlyList<ValidationIssue> Validate(
        string? internalName,
        string? inputFile,
        string? outputFile,
        int timeLimitMs,
        int memoryLimitMb)
    {
        var issues = new List<ValidationIssue>();
        if (string.IsNullOrWhiteSpace(internalName))
        {
            issues.Add(new("InternalName", "Tên nội bộ Polygon là bắt buộc."));
        }

        ValidateFileName("InputFile", "Input file", inputFile, issues);
        ValidateFileName("OutputFile", "Output file", outputFile, issues);

        if (!string.IsNullOrWhiteSpace(inputFile)
            && !string.IsNullOrWhiteSpace(outputFile)
            && string.Equals(inputFile.Trim(), outputFile.Trim(), StringComparison.OrdinalIgnoreCase))
        {
            issues.Add(new("OutputFile", "Input file và Output file không được giống nhau."));
        }

        if (timeLimitMs is < MinimumTimeLimitMs or > MaximumTimeLimitMs
            || timeLimitMs % TimeLimitIncrementMs != 0)
        {
            issues.Add(new(
                "TimeLimitMs",
                $"Time limit phải từ {MinimumTimeLimitMs} đến {MaximumTimeLimitMs} ms và chia hết cho {TimeLimitIncrementMs}."));
        }

        if (memoryLimitMb is < MinimumMemoryLimitMb or > MaximumMemoryLimitMb)
        {
            issues.Add(new(
                "MemoryLimitMb",
                $"Memory limit phải từ {MinimumMemoryLimitMb} đến {MaximumMemoryLimitMb} MB."));
        }

        return issues;
    }

    private static void ValidateFileName(
        string field,
        string label,
        string? value,
        ICollection<ValidationIssue> issues)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            issues.Add(new(field, $"{label} là bắt buộc."));
            return;
        }

        if (value.Trim().Length > MaximumFileNameLength)
        {
            issues.Add(new(field, $"{label} không được vượt quá {MaximumFileNameLength} ký tự."));
        }

        if (value.Any(char.IsControl))
        {
            issues.Add(new(field, $"{label} chứa ký tự điều khiển không hợp lệ."));
        }
    }
}
