using System.ComponentModel;
using System.Text;

namespace PowderCoatingWizard.Module.Services.AI
{
    /// <summary>
    /// Composes existing read-only domain tools into a structured process investigation package.
    /// </summary>
    public sealed class ProcessInvestigationToolService
    {
        private readonly BathDataToolService _bathData;
        private readonly ThresholdAlertToolService _thresholdAlerts;
        private readonly MeasurementTrendToolService _trends;

        public ProcessInvestigationToolService(
            BathDataToolService bathData,
            ThresholdAlertToolService thresholdAlerts,
            MeasurementTrendToolService trends)
        {
            _bathData = bathData;
            _thresholdAlerts = thresholdAlerts;
            _trends = trends;
        }

        [Description("Builds a structured read-only process investigation package from current bath data, threshold alerts, and measurement trends. Use for coating defects, bath chemistry issues, abnormal measurements, and production-quality investigations.")]
        public string InvestigateProcessIssue(
            [Description("Short description of the issue or question, for traceability in the evidence package.")] string issueDescription,
            [Description("Stage or production line filter. Use current context first when the user refers to this/current stage.")] string stageOrLineFilter = "",
            [Description("Parameter filter for trend analysis, for example pH, conductivity, temperature, concentration. Leave empty if unknown.")] string parameterFilter = "",
            [Description("Number of days to analyze for trends. Default 30.")] int daysBack = 30)
        {
            if (daysBack <= 0) daysBack = 30;

            var sb = new StringBuilder();
            sb.AppendLine("Process investigation evidence package for internal assistant reasoning.");
            sb.AppendLine("Use this package to structure the final answer as observed facts, evidence, likely causes, missing data, next checks, and recommended actions.");
            if (!string.IsNullOrWhiteSpace(issueDescription))
                sb.AppendLine($"Issue: {Safe(issueDescription)}");
            if (!string.IsNullOrWhiteSpace(stageOrLineFilter))
                sb.AppendLine($"Scope: {Safe(stageOrLineFilter)}");
            if (!string.IsNullOrWhiteSpace(parameterFilter))
                sb.AppendLine($"Trend parameter filter: {Safe(parameterFilter)}");
            sb.AppendLine($"Trend window: {daysBack} day(s)");
            sb.AppendLine();

            sb.AppendLine("## Current bath/stage data");
            sb.AppendLine(_bathData.GetBathData(stageOrLineFilter ?? string.Empty).Trim());
            sb.AppendLine();

            sb.AppendLine("## Threshold alerts");
            sb.AppendLine(_thresholdAlerts.GetThresholdAlerts(stageOrLineFilter ?? string.Empty).Trim());
            sb.AppendLine();

            sb.AppendLine("## Measurement trends");
            sb.AppendLine(_trends.GetMeasurementTrend(stageOrLineFilter ?? string.Empty, parameterFilter ?? string.Empty, daysBack).Trim());

            return sb.ToString();
        }

        private static string Safe(string value)
        {
            var text = value.Replace("\r", " ").Replace("\n", " ").Trim();
            return text.Length <= 300 ? text : text[..300] + "...";
        }
    }
}
