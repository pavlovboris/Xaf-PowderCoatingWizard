using Microsoft.Extensions.AI;
using System.IO;

namespace PowderCoatingWizard.Win.Services
{
    /// <summary>
    /// Writes a detailed plain-text log of every AI pipeline event to a file
    /// in %LOCALAPPDATA%\PowderCoatingWizard\ai_pipeline.log.
    /// Open the file in any text editor (or tail it in PowerShell) to trace what
    /// the pipeline sends and receives in real time.
    /// </summary>
    public static class AILogger
    {
        private static readonly string LogPath;
        private static readonly object _lock = new();

        static AILogger()
        {
            var dir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "PowderCoatingWizard");
            Directory.CreateDirectory(dir);
            LogPath = Path.Combine(dir, "ai_pipeline.log");

            // Write header so it's obvious when the app started.
            WriteRaw($"{'=',60}");
            WriteRaw($"AI Pipeline Log — session started {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
            WriteRaw($"Log file: {LogPath}");
            WriteRaw($"{'=',60}");
        }

        // ── Public helpers ────────────────────────────────────────────────────

        public static void LogEvent(string category, string message)
        {
            WriteRaw($"[{DateTime.Now:HH:mm:ss.fff}] [{category}] {message}");
        }

        public static void LogMessages(string category, IEnumerable<ChatMessage> messages)
        {
            var list = messages.ToList();
            WriteRaw($"[{DateTime.Now:HH:mm:ss.fff}] [{category}] ── {list.Count} message(s) ──");
            for (int i = 0; i < list.Count; i++)
            {
                var m = list[i];
                var role = m.Role.Value;
                var text = string.Concat(m.Contents.OfType<TextContent>().Select(t => t.Text));
                var fcalls = m.Contents.OfType<FunctionCallContent>().ToList();
                var fresults = m.Contents.OfType<FunctionResultContent>().ToList();

                if (fcalls.Count > 0)
                {
                    var calls = string.Join(", ", fcalls.Select(f => $"{f.Name}(callId={f.CallId})"));
                    WriteRaw($"  [{i}] {role} → TOOL_CALLS: {calls}");
                }
                else if (fresults.Count > 0)
                {
                    var results = string.Join(", ", fresults.Select(f => $"callId={f.CallId}"));
                    WriteRaw($"  [{i}] {role} → TOOL_RESULTS: {results}  result={Truncate(fresults[0].Result?.ToString())}");
                }
                else
                {
                    WriteRaw($"  [{i}] {role}: {Truncate(text)}");
                }
            }
        }

        public static void LogOptions(string category, ChatOptions? options)
        {
            if (options == null)
            {
                WriteRaw($"[{DateTime.Now:HH:mm:ss.fff}] [{category}] options=null");
                return;
            }
            var tools = options.Tools?.Select(t => t is AIFunction f ? f.Name : t.GetType().Name).ToList() ?? [];
            WriteRaw($"[{DateTime.Now:HH:mm:ss.fff}] [{category}] options: " +
                     $"maxTokens={options.MaxOutputTokens} " +
                     $"temp={options.Temperature} " +
                     $"toolMode={options.ToolMode} " +
                     $"tools=[{string.Join(", ", tools)}]");
        }

        public static void LogError(string category, Exception ex)
        {
            WriteRaw($"[{DateTime.Now:HH:mm:ss.fff}] [{category}] ERROR: {ex.GetType().Name}: {ex.Message}");
            if (ex.InnerException != null)
                WriteRaw($"  Inner: {ex.InnerException.GetType().Name}: {ex.InnerException.Message}");
        }

        public static string GetLogPath() => LogPath;

        // ── Private ───────────────────────────────────────────────────────────

        private static void WriteRaw(string line)
        {
            try
            {
                lock (_lock)
                    File.AppendAllText(LogPath, line + Environment.NewLine);
            }
            catch { /* never crash the app because of logging */ }
        }

        private static string Truncate(string? s, int max = 200)
        {
            if (s == null) return "(null)";
            s = s.Replace("\n", " ").Replace("\r", "");
            return s.Length <= max ? s : s[..max] + "…";
        }
    }
}
