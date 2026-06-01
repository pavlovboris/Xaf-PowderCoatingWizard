using DevExpress.ExpressApp;
using PowderCoatingWizard.Module.BusinessObjects;
using System.ComponentModel;
using System.Text;

namespace PowderCoatingWizard.Module.Services.AI
{
    /// <summary>
    /// AI tool that analyses historical measurement trends for a given stage and parameter.
    /// Reports direction (rising/falling/stable), average, min/max, and threshold breach count.
    /// </summary>
    public sealed class MeasurementTrendToolService
    {
        private readonly IObjectSpace _os;

        public MeasurementTrendToolService(IObjectSpace os)
        {
            _os = os;
        }

        /// <summary>
        /// Analyses the measurement trend for a stage/parameter over a date range.
        /// Returns statistics (avg, min, max, direction) and threshold breach count.
        /// </summary>
        [Description(
            "Analyse the historical measurement trend for a specific bath stage and parameter. " +
            "Returns average, min, max, trend direction (rising/falling/stable), and how many readings were out of threshold. " +
            "Use this when the user asks about trends, historical data, or whether a parameter is getting better or worse.")]
        public string GetMeasurementTrend(
            [Description("Stage name (partial, case-insensitive). Leave empty to analyse all stages.")] string stageNameFilter,
            [Description("Parameter name (partial, case-insensitive). Leave empty to analyse all parameters.")] string parameterNameFilter,
            [Description("Number of days to look back from today. Default is 30.")] int daysBack = 30)
        {
            if (daysBack <= 0) daysBack = 30;
            var since = DateTime.Today.AddDays(-daysBack);

            var allMeasurements = _os.GetObjects<ParameterMeasurement>()
                .Where(m => m.MeasurementSession != null
                         && m.MeasurementSession.MeasuredOn >= since
                         && m.NumericValue.HasValue
                         && m.Stage != null
                         && m.Parameter != null)
                .ToList();

            if (!string.IsNullOrWhiteSpace(stageNameFilter))
                allMeasurements = allMeasurements
                    .Where(m => (m.Stage.Name ?? string.Empty).Contains(stageNameFilter, StringComparison.OrdinalIgnoreCase)
                             || (m.Stage.Line?.Name ?? string.Empty).Contains(stageNameFilter, StringComparison.OrdinalIgnoreCase))
                    .ToList();

            if (!string.IsNullOrWhiteSpace(parameterNameFilter))
                allMeasurements = allMeasurements
                    .Where(m => (m.Parameter.Name ?? string.Empty).Contains(parameterNameFilter, StringComparison.OrdinalIgnoreCase))
                    .ToList();

            if (allMeasurements.Count == 0)
                return $"No numeric measurements found for the given filters in the last {daysBack} days.";

            // Group by (stage, parameter)
            var groups = allMeasurements
                .GroupBy(m => (StageName: m.Stage.Name ?? "?", LineN: m.Stage.Line?.Name ?? "?",
                               ParamName: m.Parameter.Name ?? "?",
                               Unit: m.Parameter.Unit?.Symbol ?? string.Empty))
                .OrderBy(g => g.Key.LineN)
                .ThenBy(g => g.Key.StageName)
                .ThenBy(g => g.Key.ParamName);

            // Load active thresholds once
            var allThresholds = _os.GetObjects<ParameterThreshold>()
                .Where(t => t.IsActive)
                .ToList();

            var sb = new StringBuilder();
            sb.AppendLine($"## Measurement Trend Report — last {daysBack} days");
            sb.AppendLine();

            foreach (var g in groups)
            {
                var ordered = g.OrderBy(m => m.MeasurementSession.MeasuredOn).ToList();
                var values = ordered.Select(m => m.NumericValue!.Value).ToList();

                double avg = values.Average();
                double min = values.Min();
                double max = values.Max();
                int count = values.Count;

                // Simple linear trend: compare first-third vs last-third
                string direction = "stable";
                if (count >= 3)
                {
                    int chunk = Math.Max(1, count / 3);
                    double firstAvg = values.Take(chunk).Average();
                    double lastAvg = values.TakeLast(chunk).Average();
                    double delta = lastAvg - firstAvg;
                    double threshold = avg * 0.03; // 3% change = significant
                    if (delta > threshold) direction = "📈 rising";
                    else if (delta < -threshold) direction = "📉 falling";
                    else direction = "➡️ stable";
                }

                // Count out-of-threshold readings
                int outOfRange = ordered.Count(m =>
                    m.EvaluatedStatus == ParameterStatus.Warning ||
                    m.EvaluatedStatus == ParameterStatus.Alarm);

                var unitLabel = string.IsNullOrEmpty(g.Key.Unit) ? string.Empty : $" {g.Key.Unit}";
                sb.AppendLine($"### {g.Key.LineN} › {g.Key.StageName} — `{g.Key.ParamName}`");
                sb.AppendLine($"- Readings: **{count}** over {daysBack} days");
                sb.AppendLine($"- Average: **{avg:G4}{unitLabel}**  |  Min: {min:G4}{unitLabel}  |  Max: {max:G4}{unitLabel}");
                sb.AppendLine($"- Trend: **{direction}**");
                if (outOfRange > 0)
                    sb.AppendLine($"- ⚠️ Out-of-threshold readings: **{outOfRange}** of {count}");
                sb.AppendLine();
            }

            return sb.ToString();
        }
    }
}
