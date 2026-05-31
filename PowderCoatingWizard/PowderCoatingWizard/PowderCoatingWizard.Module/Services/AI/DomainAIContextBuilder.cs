using DevExpress.ExpressApp;
using PowderCoatingWizard.Module.BusinessObjects;
using PowderCoatingWizard.Module.BusinessObjects.AI;
using System.Text;

namespace PowderCoatingWizard.Module.Services.AI
{
    /// <summary>
    /// Builds structured system-prompt text and context summaries that
    /// the AI assistant uses to understand the powder-coating domain.
    /// </summary>
    public static class DomainAIContextBuilder
    {
        // ── Instruction sets (standards, SOPs, reference URLs) ───────────────

        /// <summary>
        /// Loads all active <see cref="AIInstructionSet"/> records that apply globally
        /// or to the given line, and returns them as a system-prompt block.
        /// URL content is fetched asynchronously when <see cref="AIReferenceUrl.FetchAtRuntime"/> is true.
        /// </summary>
        public static async Task<string> BuildInstructionsContextAsync(
            IObjectSpace os,
            ProductionLine? line,
            CancellationToken ct = default)
        {
            var sets = os.GetObjects<AIInstructionSet>()
                .Where(s => s.IsActive &&
                            (s.ProductionLine == null || s.ProductionLine.Oid == line?.Oid))
                .OrderByDescending(s => s.Priority)
                .ToList();

            return await BuildInstructionSetsPromptAsync(sets, ct);
        }

        /// <summary>
        /// Builds a system-prompt block from a specific list of instruction-set OIDs.
        /// Used when an <see cref="AIAgent"/> defines its own instruction set selection.
        /// </summary>
        public static async Task<string> BuildInstructionsFromSetsAsync(
            IList<Guid> setOids,
            IObjectSpace os,
            CancellationToken ct = default)
        {
            if (setOids == null || setOids.Count == 0) return string.Empty;

            var sets = os.GetObjects<AIInstructionSet>()
                .Where(s => s.IsActive && setOids.Contains(s.Oid))
                .OrderByDescending(s => s.Priority)
                .ToList();

            return await BuildInstructionSetsPromptAsync(sets, ct);
        }

        private static async Task<string> BuildInstructionSetsPromptAsync(
            IList<AIInstructionSet> sets,
            CancellationToken ct = default)
        {
            if (sets.Count == 0) return string.Empty;

            var sb = new StringBuilder();
            sb.AppendLine("## Active Standards & Instructions");
            sb.AppendLine();
            sb.AppendLine("> The following standards and instructions are **authoritative**.");
            sb.AppendLine("> Always comply with them. They override general knowledge.");
            sb.AppendLine();

            using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(10) };

            foreach (var set in sets)
            {
                sb.AppendLine($"### {set.Name}");
                if (!string.IsNullOrWhiteSpace(set.Instructions))
                    sb.AppendLine(set.Instructions.Trim());

                foreach (var urlRef in set.ReferenceUrls.Where(r => !string.IsNullOrWhiteSpace(r.Url)))
                {
                    if (urlRef.FetchAtRuntime)
                    {
                        string fetched = await FetchUrlTextAsync(http, urlRef.Url, ct);
                        if (!string.IsNullOrWhiteSpace(fetched))
                        {
                            sb.AppendLine();
                            sb.AppendLine($"#### Reference: {urlRef.Label ?? urlRef.Url}");
                            sb.AppendLine(fetched.Length > 4000
                                ? fetched[..4000] + "\n… [truncated]"
                                : fetched);
                        }
                    }
                    else
                    {
                        sb.AppendLine($"- Reference: [{urlRef.Label ?? urlRef.Url}]({urlRef.Url})");
                    }
                }

                sb.AppendLine();
            }

            return sb.ToString();
        }

        private static async Task<string> FetchUrlTextAsync(HttpClient http, string url, CancellationToken ct)
        {
            try
            {
                var html = await http.GetStringAsync(url, ct);
                // Strip HTML tags for a clean text feed to the AI
                return System.Text.RegularExpressions.Regex.Replace(html, "<[^>]+>", " ")
                    .Replace("&nbsp;", " ").Replace("&amp;", "&").Replace("&lt;", "<").Replace("&gt;", ">")
                    .Split('\n')
                    .Select(l => l.Trim())
                    .Where(l => l.Length > 0)
                    .Aggregate(new StringBuilder(), (a, l) => a.AppendLine(l))
                    .ToString();
            }
            catch
            {
                return string.Empty;
            }
        }

        // ── System prompt ────────────────────────────────────────────────────

        public static string BuildSystemPrompt()
        {
            return """
                You are an expert industrial process assistant for a powder-coating production facility.

                ## Knowledge priority — follow this order strictly:

                1. **Database thresholds and measurements** (provided in this prompt under "Current Stage" and "Historical Analysis")
                   → These are live, plant-specific values. They are ALWAYS correct for THIS facility.
                   → Never contradict or override them, even if a standard or general knowledge suggests otherwise.

                2. **Active standards and SOPs** (provided under "Active Standards & Instructions")
                   → Authoritative written rules for this facility. Comply fully.
                   → When a threshold from the database and a standard differ, flag the discrepancy but follow the database value.

                3. **Uploaded knowledge base excerpts** (provided under "Knowledge Base")
                   → Relevant documents uploaded by the team. Use as supporting context.

                4. **Your general knowledge**
                   → Use only when the above sources do not cover the question.
                   → Always make clear when you are drawing on general knowledge vs. plant data.

                ## Your role:
                - Analyze measurement data (pH, temperature, conductivity, concentration, etc.) for each bath/tank stage.
                - Compare measurements against their defined thresholds (min/max limits and target values).
                - Identify deviations, out-of-spec conditions, and trends.
                - Suggest concrete corrective actions (dosing, temperature adjustment, bath replacement, etc.).
                - Provide historical analysis when historical data is supplied.
                - Communicate clearly and concisely; use tables when comparing multiple parameters.
                - Always state the units and threshold limits when discussing a measurement.
                - If data is missing or unclear, ask clarifying questions.

                ## Domain vocabulary:
                - "Stage" = a bath or tank on the production line (e.g., Degreaser, Rinse 1, Phosphate, etc.)
                - "Parameter" = a measurable property of a stage (e.g., pH, Temperature, Concentration)
                - "Threshold" = the acceptable min/max/target range for a parameter
                - "Session" = one complete measurement round by an operator across all stages
                - "EvaluatedStatus" = OK | Warning | OutOfSpec — the result of comparing a value against its thresholds
                - "Criterion" = a pass/fail rule evaluated against one or more parameters
                """;
        }

        // ── Current stage context ────────────────────────────────────────────

        /// <summary>
        /// Produces a Markdown summary of the latest measurement session(s)
        /// for a specific stage, suitable for injection into the AI chat.
        /// </summary>
        public static string BuildStageContext(IObjectSpace os, LineStage stage, int lastSessionCount = 3)
        {
            if (stage == null) return string.Empty;

            var sb = new StringBuilder();
            sb.AppendLine($"## Current Stage: {stage.Name}");
            if (!string.IsNullOrWhiteSpace(stage.Description))
                sb.AppendLine($"_{stage.Description}_");
            sb.AppendLine();

            // Latest sessions for this stage
            var sessions = os.GetObjects<MeasurementSession>(
                    DevExpress.Data.Filtering.CriteriaOperator.Parse(
                        "Measurements[Stage.Oid = ?].Count() > 0", stage.Oid))
                .OrderByDescending(s => s.MeasuredOn)
                .Take(lastSessionCount)
                .ToList();

            if (sessions.Count == 0)
            {
                sb.AppendLine("No measurement sessions found for this stage.");
                return sb.ToString();
            }

            sb.AppendLine($"### Last {sessions.Count} session(s)");
            sb.AppendLine();

            foreach (var session in sessions)
            {
                sb.AppendLine($"**Session**: {session.MeasuredOn:yyyy-MM-dd HH:mm}  |  Operator: {session.OperatorName}");
                if (!string.IsNullOrWhiteSpace(session.Notes))
                    sb.AppendLine($"Notes: _{session.Notes}_");

                var measurements = session.Measurements
                    .Where(m => m.Stage?.Oid == stage.Oid)
                    .OrderBy(m => m.Parameter?.Name)
                    .ToList();

                if (measurements.Count > 0)
                {
                    sb.AppendLine();
                    sb.AppendLine("| Parameter | Value | Unit | Status | Threshold (Min / Target / Max) |");
                    sb.AppendLine("|-----------|-------|------|--------|-------------------------------|");

                    foreach (var m in measurements)
                    {
                        string value = m.NumericValue.HasValue
                            ? m.NumericValue.Value.ToString("G6")
                            : m.SelectedValue?.Name ?? m.TextValue ?? "-";

                        string unit = m.Parameter?.Unit?.Symbol ?? "";
                        string status = m.EvaluatedStatus.ToString();

                        // Build threshold summary from snapshots
                        string thresholds = BuildThresholdSummary(m);

                        sb.AppendLine($"| {m.Parameter?.Name ?? "?"} | {value} | {unit} | **{status}** | {thresholds} |");
                    }
                }
                sb.AppendLine();
            }

            return sb.ToString();
        }

        // ── Historical context ────────────────────────────────────────────────

        /// <summary>
        /// Returns a statistical summary (min, max, avg, out-of-spec count)
        /// per parameter for a stage over the given date range.
        /// </summary>
        public static string BuildHistoricalContext(
            IObjectSpace os,
            LineStage stage,
            DateTime from,
            DateTime to)
        {
            if (stage == null) return string.Empty;

            var measurements = os.GetObjects<ParameterMeasurement>(
                    DevExpress.Data.Filtering.CriteriaOperator.Parse(
                        "Stage.Oid = ? AND MeasurementSession.MeasuredOn >= ? AND MeasurementSession.MeasuredOn <= ?",
                        stage.Oid, from, to))
                .Where(m => m.NumericValue.HasValue)
                .ToList();

            if (measurements.Count == 0)
                return $"No numeric measurements found for stage **{stage.Name}** between {from:yyyy-MM-dd} and {to:yyyy-MM-dd}.";

            var sb = new StringBuilder();
            sb.AppendLine($"## Historical Analysis: {stage.Name}");
            sb.AppendLine($"Period: {from:yyyy-MM-dd} → {to:yyyy-MM-dd}  |  Total readings: {measurements.Count}");
            sb.AppendLine();
            sb.AppendLine("| Parameter | Unit | Min | Avg | Max | Out-of-Spec | Warning |");
            sb.AppendLine("|-----------|------|-----|-----|-----|-------------|---------|");

            foreach (var grp in measurements.GroupBy(m => m.Parameter?.Name ?? "?").OrderBy(g => g.Key))
            {
                var vals = grp.Where(m => m.NumericValue.HasValue).Select(m => m.NumericValue!.Value).ToList();
                if (vals.Count == 0) continue;

                string unit = grp.First().Parameter?.Unit?.Symbol ?? "";
                int outOfSpec = grp.Count(m => m.EvaluatedStatus == ParameterStatus.Alarm);
                int warning = grp.Count(m => m.EvaluatedStatus == ParameterStatus.Warning);

                sb.AppendLine($"| {grp.Key} | {unit} | {vals.Min():G5} | {vals.Average():G5} | {vals.Max():G5} | {outOfSpec} | {warning} |");
            }

            return sb.ToString();
        }

        // ── Full line snapshot ───────────────────────────────────────────────

        /// <summary>
        /// Builds a full summary of the latest session for every stage on a line.
        /// Used when the user asks "how is the whole line doing?"
        /// </summary>
        public static string BuildLineSnapshot(IObjectSpace os, ProductionLine line)
        {
            if (line == null) return string.Empty;

            var latestSession = os.GetObjects<MeasurementSession>(
                    DevExpress.Data.Filtering.CriteriaOperator.Parse("Line.Oid = ?", line.Oid))
                .OrderByDescending(s => s.MeasuredOn)
                .FirstOrDefault();

            if (latestSession == null)
                return $"No sessions found for line **{line.Name}**.";

            var sb = new StringBuilder();
            sb.AppendLine($"## Line Snapshot: {line.Name}");
            sb.AppendLine($"Latest session: {latestSession.MeasuredOn:yyyy-MM-dd HH:mm}  |  Operator: {latestSession.OperatorName}");
            sb.AppendLine();

            var byStage = latestSession.Measurements
                .GroupBy(m => m.Stage?.Name ?? "Unknown")
                .OrderBy(g => g.Key);

            foreach (var stageGrp in byStage)
            {
                int outOfSpec = stageGrp.Count(m => m.EvaluatedStatus == ParameterStatus.Alarm);
                int warning = stageGrp.Count(m => m.EvaluatedStatus == ParameterStatus.Warning);
                int ok = stageGrp.Count(m => m.EvaluatedStatus == ParameterStatus.OK);
                string badge = outOfSpec > 0 ? "🔴 ALARM" : warning > 0 ? "🟡 WARNING" : "🟢 OK";

                sb.AppendLine($"### {stageGrp.Key} — {badge}");
                sb.AppendLine($"OK: {ok} | Warning: {warning} | Out-of-spec: {outOfSpec}");

                foreach (var m in stageGrp.OrderBy(m => m.Parameter?.Name))
                {
                    if (m.EvaluatedStatus == ParameterStatus.OK) continue;
                    string val = m.NumericValue.HasValue
                        ? $"{m.NumericValue.Value:G5} {m.Parameter?.Unit?.Symbol}"
                        : m.SelectedValue?.Name ?? m.TextValue ?? "-";
                    sb.AppendLine($"  - **{m.Parameter?.Name}**: {val} [{m.EvaluatedStatus}]  Thresholds: {BuildThresholdSummary(m)}");
                }
                sb.AppendLine();
            }

            return sb.ToString();
        }

        // ── Helpers ──────────────────────────────────────────────────────────

        private static string BuildThresholdSummary(ParameterMeasurement m)
        {
            var snaps = m.ThresholdSnapshots?.ToList();
            if (snaps == null || snaps.Count == 0) return "-";

            // Each snapshot is one threshold limit (e.g. AlarmLow < 4.5, WarningHigh > 6.0)
            // Format: "AlarmLow Below 4.5 ⚠" etc.
            var parts = snaps.OrderBy(s => s.ThresholdType).Select(s =>
            {
                string breached = s.WasBreached ? " ⚠" : "";
                return $"{s.ThresholdType} {s.Direction} {s.ThresholdValue:G5}{breached}";
            });

            return string.Join(" | ", parts);
        }
    }
}
