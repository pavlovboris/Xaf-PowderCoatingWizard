using DevExpress.ExpressApp;
using PowderCoatingWizard.Module.BusinessObjects;
using System.ComponentModel;
using System.Text;

namespace PowderCoatingWizard.Module.Services.AI
{
    /// <summary>
    /// AI tool that returns a prioritised list of all currently out-of-threshold parameters
    /// across the entire production line or a specific stage/line.
    /// Critical issues are listed first, then warnings.
    /// </summary>
    public sealed class ThresholdAlertToolService
    {
        private readonly IObjectSpace _os;

        public ThresholdAlertToolService(IObjectSpace os)
        {
            _os = os;
        }

        /// <summary>
        /// Returns all parameters whose latest measurement is Warning or Critical.
        /// Results are sorted by severity (Critical first) then by stage position.
        /// </summary>
        [Description(
            "Returns a prioritised alert list of all bath parameters whose latest measurement is out of threshold (Warning or Critical). " +
            "Use this when the user asks 'what is wrong', 'show me alerts', 'any issues', 'what needs attention', or wants an overall status overview. " +
            "Pass an optional line or stage name to scope the results.")]
        public string GetThresholdAlerts(
            [Description("Production line or stage name filter (partial, case-insensitive). Leave empty to check ALL lines.")] string lineOrStageFilter = "")
        {
            var filter = (lineOrStageFilter ?? string.Empty).Trim();

            // Load all active stages
            var stages = _os.GetObjects<LineStage>()
                .Where(s => s.IsActive)
                .ToList();

            if (!string.IsNullOrEmpty(filter))
                stages = stages
                    .Where(s => (s.Name ?? string.Empty).Contains(filter, StringComparison.OrdinalIgnoreCase)
                             || (s.Line?.Name ?? string.Empty).Contains(filter, StringComparison.OrdinalIgnoreCase))
                    .ToList();

            if (stages.Count == 0)
                return string.IsNullOrEmpty(filter)
                    ? "No active stages found."
                    : $"No active stages found matching '{filter}'.";

            // Latest measurement per (stage, parameter)
            var allMeasurements = _os.GetObjects<ParameterMeasurement>()
                .Where(m => m.Stage != null && m.Parameter != null && m.MeasurementSession != null)
                .ToList();

            var latestByStageParam = allMeasurements
                .GroupBy(m => (StageOid: m.Stage.Oid, ParamOid: m.Parameter.Oid))
                .ToDictionary(
                    g => g.Key,
                    g => g.OrderByDescending(m => m.MeasurementSession.MeasuredOn).First());

            // Collect alerts
            var alerts = new List<(ParameterStatus Status, string Line, string Stage, int Position,
                                   string Param, string Unit, string Value, DateTime MeasuredOn)>();

            foreach (var stage in stages)
            {
                foreach (var sp in stage.Parameters)
                {
                    var param = sp.Parameter;
                    if (param == null) continue;

                    if (!latestByStageParam.TryGetValue((stage.Oid, param.Oid), out var m)) continue;

                    if (m.EvaluatedStatus != ParameterStatus.Warning &&
                        m.EvaluatedStatus != ParameterStatus.Alarm) continue;

                    var valueStr = m.NumericValue.HasValue
                        ? m.NumericValue.Value.ToString("G6")
                        : m.SelectedValue?.Name ?? m.TextValue ?? "–";

                    alerts.Add((m.EvaluatedStatus,
                        m.Stage.Line?.Name ?? "?",
                        m.Stage.Name ?? "?",
                        stage.Position,
                        param.Name ?? "?",
                        param.Unit?.Symbol ?? string.Empty,
                        valueStr,
                        m.MeasurementSession.MeasuredOn));
                }
            }

            if (alerts.Count == 0)
            {
                var scopeDesc = string.IsNullOrEmpty(filter) ? "all stages" : $"'{filter}'";
                return $"✅ No threshold alerts found for {scopeDesc}. All measured parameters are within normal limits.";
            }

            // Sort: Critical first, then Warning; within each group sort by line + position
            var sorted = alerts
                .OrderBy(a => a.Status == ParameterStatus.Alarm ? 0 : 1)
                .ThenBy(a => a.Line)
                .ThenBy(a => a.Position)
                .ThenBy(a => a.Param)
                .ToList();

            var sb = new StringBuilder();
            int critical = sorted.Count(a => a.Status == ParameterStatus.Alarm);
            int warning = sorted.Count(a => a.Status == ParameterStatus.Warning);

            sb.AppendLine($"## ⚠️ Threshold Alert Summary");
            sb.AppendLine($"**{critical} Alarm** | **{warning} Warning** | Total: {alerts.Count}");
            sb.AppendLine();

            ParameterStatus? lastStatus = null;
            foreach (var a in sorted)
            {
                if (a.Status != lastStatus)
                {
                    sb.AppendLine(a.Status == ParameterStatus.Alarm
                        ? "### 🔴 Alarm"
                        : "### 🟡 Warning");
                    lastStatus = a.Status;
                }

                var unitLabel = string.IsNullOrEmpty(a.Unit) ? string.Empty : $" {a.Unit}";
                sb.AppendLine($"- **{a.Line} › {a.Stage}** — `{a.Param}` = **{a.Value}{unitLabel}**  *(measured {a.MeasuredOn:dd MMM yyyy HH:mm})*");
            }

            return sb.ToString();
        }
    }
}
