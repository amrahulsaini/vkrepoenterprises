using System;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace VRASDesktopApp.Data;

internal static class Diagnostics
{
    private static readonly string LogDir = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "CRMRS", "logs");

    internal static string AppVersion =>
        System.Reflection.Assembly.GetExecutingAssembly().GetName().Version?.ToString() ?? "?";

    internal static string LogFilePath =>
        Path.Combine(LogDir, $"errors-{DateTime.Now:yyyy-MM-dd}.log");

    internal static string LogError(string operation, Exception ex, string? context = null)
    {
        string detail  = Describe(ex);
        string summary = detail.Split('\n').FirstOrDefault()?.Trim() ?? ex.Message;
        string report  = BuildReport(operation, detail, context, ex.StackTrace);

        try
        {
            Directory.CreateDirectory(LogDir);
            File.AppendAllText(LogFilePath, report + Environment.NewLine + new string('-', 72) + Environment.NewLine);
        }
        catch { }

        _ = ReportToServerAsync(operation, summary, report, context);
        return report;
    }

    internal static string Describe(Exception ex)
    {
        var sb = new StringBuilder();
        Exception? e = ex;
        int depth = 0;
        while (e != null)
        {
            string indent = depth == 0 ? "" : new string(' ', (depth - 1) * 2) + "caused by -> ";
            sb.AppendLine($"{indent}{e.GetType().Name}: {e.Message}");
            if (e is HttpRequestException hre && hre.StatusCode != null)
                sb.AppendLine($"    HTTP status: {(int)hre.StatusCode} {hre.StatusCode}");
            if (e is System.Net.Sockets.SocketException se)
                sb.AppendLine($"    Socket error: {se.SocketErrorCode}");
            if (e is TaskCanceledException || e is OperationCanceledException)
                sb.AppendLine("    (the request timed out or was cancelled — usually a slow/dropped connection)");
            e = e.InnerException;
            depth++;
        }
        return sb.ToString().TrimEnd();
    }

    private static string BuildReport(string op, string detail, string? context, string? stack)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"Operation : {op}");
        sb.AppendLine($"When      : {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
        sb.AppendLine($"Agency    : {SafeBrand()}");
        sb.AppendLine($"App       : CRMRS v{AppVersion}");
        sb.AppendLine($"Machine   : {Environment.MachineName}   ({Environment.OSVersion})");
        if (!string.IsNullOrWhiteSpace(context)) sb.AppendLine($"Context   : {context}");
        sb.AppendLine();
        sb.AppendLine("What went wrong:");
        sb.AppendLine(detail);
        if (!string.IsNullOrWhiteSpace(stack))
        {
            sb.AppendLine();
            sb.AppendLine("Where (top of stack):");
            sb.AppendLine(string.Join(Environment.NewLine, stack!.Split('\n').Take(6)).TrimEnd());
        }
        return sb.ToString().TrimEnd();
    }

    private static string SafeBrand()
    {
        try { return App.BrandName; } catch { return "CRMRS"; }
    }

    private static async Task ReportToServerAsync(string op, string summary, string detail, string? context)
    {
        try
        {
            var url = $"{App.ApiBaseUrl.TrimEnd('/')}/api/agency/desktop/client-error";
            var payload = new
            {
                operation   = op,
                summary,
                detail,
                context     = context ?? "",
                appVersion  = AppVersion,
                machineName = Environment.MachineName,
                os          = Environment.OSVersion.ToString(),
                occurredAt  = DateTime.UtcNow.ToString("o"),
            };
            var json = System.Text.Json.JsonSerializer.Serialize(payload);
            using var req = new HttpRequestMessage(HttpMethod.Post, url)
            {
                Content = new StringContent(json, Encoding.UTF8, "application/json"),
            };
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(15));
            await App.HttpClient.SendAsync(req, cts.Token);
        }
        catch { }
    }
}
