using DevExpress.ExpressApp;
using DevExpress.Xpo;
using PowderCoatingWizard.Module.BusinessObjects;
using System.ComponentModel;
using System.Text;

namespace PowderCoatingWizard.Module.Services.AI
{
    /// <summary>
    /// Provides an AI tool that lets the LLM dynamically fetch live bath/stage data
    /// from the database during a conversation.
    /// Call <see cref="GetBathData"/> via AIFunctionFactory to register it as a chat tool.
    /// </summary>
    public sealed class BathDataToolService
    {
        private readonly IObjectSpace _os;

        public BathDataToolService(IObjectSpace os)
        {
            _os = os;
        }

        /// <summary>
        /// Returns current parameter readings and threshold status for one or more bath stages.
        /// The <paramref name="stageNameFilter"/> is matched case-insensitively against stage names
        /// and production line names. Pass an empty string to get a summary of ALL active stages.
        /// </summary>
        [Description(
            "Retrieve live bath/stage parameter readings and threshold status from the production database. " +
            "Use this tool whenever the user asks about current bath values, concentrations, pH, temperatures, " +
            "or any measured parameter for a specific tank or production line. " +
            "Pass the stage or line name as 'stageNameFilter', or an empty string to get all stages.")]
        public string GetBathData(
            [Description("Stage name or production line name to filter by. Case-insensitive partial match. Pass empty string to get all active stages.")]
            string stageNameFilter)
        {
            var filter = (stageNameFilter ?? string.Empty).Trim();

            var allStages = _os.GetObjects<LineStage>()
                .Where(s => s.IsActive)
                .ToList();

            var stages = string.IsNullOrEmpty(filter)
                ? allStages
                : allStages.Where(s =>
                    (s.Name != null && s.Name.Contains(filter, StringComparison.OrdinalIgnoreCase)) ||
                    (s.Line?.Name != null && s.Line.Name.Contains(filter, StringComparison.OrdinalIgnoreCase)))
                .ToList();

            if (stages.Count == 0)
                return $"No active stages found matching '{filter}'.";

            // Load all measurements once — pick the latest per (stage, parameter)
            var allMeasurements = _os.GetObjects<ParameterMeasurement>().ToList();
            var latestByStageParam = allMeasurements
                .Where(m => m.Stage != null && m.Parameter != null && m.MeasurementSession != null)
                .GroupBy(m => (StageOid: m.Stage.Oid, ParamOid: m.Parameter.Oid))
                .ToDictionary(
                    g => g.Key,
                    g => g.OrderByDescending(m => m.MeasurementSession.MeasuredOn).First());

            // Load active thresholds
            var allThresholds = _os.GetObjects<ParameterThreshold>()
                .Where(t => t.IsActive)
                .ToList();

            var sb = new StringBuilder();

            foreach (var stage in stages.OrderBy(s => s.Line?.Name).ThenBy(s => s.Position))
            {
                sb.AppendLine($"### Stage: {stage.Line?.Name ?? "?"} › {stage.Name} (pos {stage.Position})");
                sb.AppendLine($"Chemistry: {stage.ChemistryType}  |  Function: {stage.StageFunction}");

                if (!stage.Parameters.Any())
                {
                    sb.AppendLine("  No parameters configured.");
                    sb.AppendLine();
                    continue;
                }

                foreach (var sp in stage.Parameters.OrderBy(p => p.Parameter?.Name))
                {
                    var param = sp.Parameter;
                    if (param == null) continue;

                    sb.Append($"  • {param.Name}");
                    if (param.Unit != null) sb.Append($" [{param.Unit.Symbol}]");

                    // Latest measurement
                    if (latestByStageParam.TryGetValue((stage.Oid, param.Oid), out var m))
                    {
                        var val = m.NumericValue.HasValue
                            ? m.NumericValue.Value.ToString("G6")
                            : m.SelectedValue?.Name ?? m.TextValue ?? "–";
                        sb.Append($" = {val}");
                        sb.Append($"  (measured {m.MeasurementSession.MeasuredOn:dd MMM yyyy HH:mm})");
                        sb.Append($"  status: {m.EvaluatedStatus}");
                    }
                    else
                    {
                        sb.Append("  = no recent measurement");
                    }

                    // Thresholds for this stage+param
                    var thresholds = allThresholds
                        .Where(t => t.Parameter?.Oid == param.Oid &&
                                    (t.Stage == null || t.Stage.Oid == stage.Oid))
                        .OrderBy(t => t.ThresholdType)
                        .ToList();

                    if (thresholds.Count > 0)
                    {
                        sb.Append("  [limits: ");
                        sb.Append(string.Join(", ", thresholds.Select(t =>
                            $"{t.ThresholdType} {t.Direction} {t.Value:G6}")));
                        sb.Append("]");
                    }

                    sb.AppendLine();
                }

                sb.AppendLine();
            }

            return sb.ToString();
        }
    }
}
