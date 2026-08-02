using System.Diagnostics;
using System.Text;
using PolygonAiBuilder.Application;

namespace PolygonAiBuilder.Infrastructure;

public sealed class ProcessRunner : IProcessRunner
{
    public async Task<ProcessExecutionResult> RunAsync(
        ProcessExecutionRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(request.ExecutablePath);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.WorkingDirectory);
        if (request.Timeout <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(request), "Process timeout must be positive.");
        }

        if (request.OutputLimitBytes <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(request), "Output limit must be positive.");
        }

        Directory.CreateDirectory(request.WorkingDirectory);
        var startInfo = new ProcessStartInfo
        {
            FileName = request.ExecutablePath,
            WorkingDirectory = request.WorkingDirectory,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            RedirectStandardInput = request.StandardInput is not null,
            CreateNoWindow = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8,
        };
        foreach (var argument in request.Arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        if (request.Environment is not null)
        {
            foreach (var pair in request.Environment)
            {
                startInfo.Environment[pair.Key] = pair.Value;
            }
        }

        using var process = new Process { StartInfo = startInfo, EnableRaisingEvents = true };
        var stopwatch = Stopwatch.StartNew();
        try
        {
            if (!process.Start())
            {
                return FailedToStart(stopwatch.Elapsed, "Process did not start.");
            }
        }
        catch (Exception exception) when (exception is InvalidOperationException or System.ComponentModel.Win32Exception or IOException)
        {
            return FailedToStart(stopwatch.Elapsed, exception.Message);
        }

        var budget = new OutputBudget(request.OutputLimitBytes, () => TryKill(process));
        using var cancellationRegistration = cancellationToken.Register(() => TryKill(process));
        var stdoutTask = DrainAsync(process.StandardOutput, budget, cancellationToken);
        var stderrTask = DrainAsync(process.StandardError, budget, cancellationToken);
        if (request.StandardInput is not null)
        {
            await process.StandardInput.WriteAsync(request.StandardInput.AsMemory(), cancellationToken);
            await process.StandardInput.FlushAsync(cancellationToken);
            process.StandardInput.Close();
        }

        var timedOut = false;
        var cancelled = false;
        using var timeout = new CancellationTokenSource(request.Timeout);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeout.Token);
        try
        {
            await process.WaitForExitAsync(linked.Token);
        }
        catch (OperationCanceledException)
        {
            timedOut = timeout.IsCancellationRequested && !cancellationToken.IsCancellationRequested;
            cancelled = cancellationToken.IsCancellationRequested;
            TryKill(process);
            await process.WaitForExitAsync(CancellationToken.None);
        }

        string stdout;
        string stderr;
        try
        {
            (stdout, stderr) = (await stdoutTask, await stderrTask);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            cancelled = true;
            stdout = string.Empty;
            stderr = "Đã hủy thao tác.";
        }

        stopwatch.Stop();
        return new(
            true,
            process.HasExited ? process.ExitCode : null,
            stdout,
            stderr,
            timedOut,
            cancelled,
            budget.Truncated,
            stopwatch.Elapsed,
            null);
    }

    private static async Task<string> DrainAsync(
        StreamReader reader,
        OutputBudget budget,
        CancellationToken cancellationToken)
    {
        var output = new StringBuilder();
        var buffer = new char[4096];
        while (true)
        {
            var read = await reader.ReadAsync(buffer.AsMemory(), cancellationToken);
            if (read == 0)
            {
                break;
            }

            var accepted = budget.Take(buffer.AsSpan(0, read));
            if (accepted.Length > 0)
            {
                output.Append(accepted);
            }
        }

        return output.ToString();
    }

    private static void TryKill(Process process)
    {
        try
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
            }
        }
        catch (InvalidOperationException)
        {
            // The process exited between the state check and Kill.
        }
        catch (System.ComponentModel.Win32Exception)
        {
            // The process already exited or Windows denied a redundant kill.
        }
    }

    private static ProcessExecutionResult FailedToStart(TimeSpan duration, string error) =>
        new(false, null, string.Empty, string.Empty, false, false, false, duration, error);

    private sealed class OutputBudget(int maximumBytes, Action onExceeded)
    {
        private readonly object gate = new();
        private int remaining = maximumBytes;
        private bool notified;

        public bool Truncated { get; private set; }

        public string Take(ReadOnlySpan<char> value)
        {
            lock (gate)
            {
                if (remaining <= 0)
                {
                    Exceeded();
                    return string.Empty;
                }

                var bytes = Encoding.UTF8.GetByteCount(value);
                if (bytes <= remaining)
                {
                    remaining -= bytes;
                    return value.ToString();
                }

                var low = 0;
                var high = value.Length;
                while (low < high)
                {
                    var middle = (low + high + 1) / 2;
                    if (Encoding.UTF8.GetByteCount(value[..middle]) <= remaining)
                    {
                        low = middle;
                    }
                    else
                    {
                        high = middle - 1;
                    }
                }

                var accepted = value[..low].ToString();
                remaining = 0;
                Exceeded();
                return accepted;
            }
        }

        private void Exceeded()
        {
            Truncated = true;
            if (!notified)
            {
                notified = true;
                onExceeded();
            }
        }
    }
}
