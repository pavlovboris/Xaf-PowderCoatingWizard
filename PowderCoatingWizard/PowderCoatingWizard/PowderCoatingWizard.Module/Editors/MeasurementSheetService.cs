using DevExpress.ExpressApp;
using PowderCoatingWizard.Module.BusinessObjects;
using System.Data;

namespace PowderCoatingWizard.Module.Editors
{
    /// <summary>
    /// Builds a pivot DataTable from MeasurementSession records for a given ProductionLine.
    /// Rows   = one MeasurementSession (date + operator)
    /// Columns = fixed header cols + one col per Stage/Parameter combination
    /// This DataTable is platform-neutral and can be consumed by both Blazor and WinForms.
    /// </summary>
    public static class MeasurementSheetService
    {
        public const string ColOid      = "_Oid";
        public const string ColDate     = "Date";
        public const string ColOperator = "Operator";
        public const string ColNotes    = "Notes";

        /// <summary>
        /// Returns the pivot DataTable and the ordered list of dynamic column keys.
        /// Key format: "{stagePosition}|{stageName}|{parameterName}"
        /// </summary>
        public static (DataTable Table, List<ColumnMeta> Columns) Build(
            IObjectSpace objectSpace, ProductionLine line,
            DateTime? dateFrom = null, DateTime? dateTo = null)
        {
            var table = new DataTable();

            // Fixed columns
            table.Columns.Add(ColOid,      typeof(Guid));
            table.Columns.Add(ColDate,     typeof(DateTime));
            table.Columns.Add(ColOperator, typeof(string));
            table.Columns.Add(ColNotes,    typeof(string));

            // Load sessions for this line, respecting optional date range
            var sessions = objectSpace.GetObjects<MeasurementSession>()
                .Where(s => s.Line?.Oid == line.Oid)
                .Where(s => dateFrom == null || s.MeasuredOn >= dateFrom.Value)
                .Where(s => dateTo   == null || s.MeasuredOn <= dateTo.Value)
                .OrderByDescending(s => s.MeasuredOn)
                .ToList();

            // Build ordered column metadata from the line's configured stages/parameters (source of truth).
            // Query through objectSpace to avoid cross-session lazy-loading issues.
            var stages = objectSpace.GetObjects<LineStage>()
                .Where(s => s.Line != null && s.Line.Oid == line.Oid)
                .OrderBy(s => s.Position)
                .ToList();

            var stageOids = new HashSet<Guid>(stages.Select(s => s.Oid));

            var stageParams = objectSpace.GetObjects<StageParameter>()
                .Where(sp => sp.Stage != null && stageOids.Contains(sp.Stage.Oid) && sp.Parameter != null)
                .ToList();

            var configMetas = stages
                .SelectMany(s => stageParams
                    .Where(sp => sp.Stage.Oid == s.Oid)
                    .OrderBy(sp => sp.Parameter.Name)
                    .Select(sp => new ColumnMeta(s, sp.Parameter)))
                .ToList();

            var configKeys = new HashSet<string>(configMetas.Select(c => c.Key));

            var extraMetas = sessions
                .SelectMany(s => s.Measurements)
                .Where(m => m.Stage != null && m.Parameter != null)
                .Select(m => new ColumnMeta(m.Stage, m.Parameter))
                .Where(c => !configKeys.Contains(c.Key))
                .DistinctBy(c => c.Key)
                .OrderBy(c => c.StagePosition)
                .ThenBy(c => c.ParameterName)
                .ToList();

            var columnMetas = configMetas.Concat(extraMetas).ToList();

            // Add value + status column pair for each combination
            foreach (var meta in columnMetas)
            {
                table.Columns.Add(meta.ValueKey,  meta.IsNumeric ? typeof(double) : typeof(string));
                table.Columns.Add(meta.StatusKey, typeof(ParameterStatus));
            }

            // Load all measurements for these sessions in one query (avoids lazy-load issues
            // with navigation properties such as SelectedValue on XPCollections).
            var sessionOids = new HashSet<Guid>(sessions.Select(s => s.Oid));
            var allMeasurements = objectSpace.GetObjects<ParameterMeasurement>()
                .Where(m => m.MeasurementSession != null
                         && sessionOids.Contains(m.MeasurementSession.Oid)
                         && m.Stage     != null
                         && m.Parameter != null)
                .ToList();

            // Populate rows
            foreach (var session in sessions)
            {
                var row = table.NewRow();
                row[ColOid]      = session.Oid;
                row[ColDate]     = session.MeasuredOn;
                row[ColOperator] = session.OperatorName ?? string.Empty;
                row[ColNotes]    = session.Notes ?? string.Empty;

                var measForSession = allMeasurements
                    .Where(m => m.MeasurementSession!.Oid == session.Oid)
                    .ToList();

                foreach (var measurement in measForSession)
                {
                    var meta = columnMetas.FirstOrDefault(
                        c => c.Key == ColumnMeta.BuildKey(measurement.Stage, measurement.Parameter));

                    if (meta == null) continue;

                    if (measurement.NumericValue.HasValue)
                    {
                        row[meta.ValueKey]  = measurement.NumericValue.Value;
                        row[meta.StatusKey] = measurement.EvaluatedStatus;
                    }
                    else if (!meta.IsNumeric && measurement.SelectedValue != null)
                    {
                        row[meta.ValueKey]  = measurement.SelectedValue.Name;
                        row[meta.StatusKey] = BathParameter.EvaluateStatus(measurement.SelectedValue);
                    }
                    else if (!meta.IsNumeric)
                    {
                        row[meta.ValueKey]  = measurement.TextValue ?? string.Empty;
                        row[meta.StatusKey] = ParameterStatus.OK;
                    }
                }

                table.Rows.Add(row);
            }

            return (table, columnMetas);
        }
    }

    /// <summary>Metadata for one dynamic column (Stage + Parameter pair).</summary>
    public sealed class ColumnMeta
    {
        public int           StagePosition   { get; }
        public string        StageName       { get; }
        public string        ParameterName   { get; }
        public string        Key             { get; }
        public BathParameter Parameter       { get; }
        public bool          IsPredefined    => Parameter?.ValueType == ParameterValueType.Predefined;
        public bool          IsNumeric       => Parameter?.ValueType == ParameterValueType.Numeric;

        /// <summary>DataTable column name for the display value.</summary>
        public string ValueKey  => $"V__{Key}";

        /// <summary>DataTable column name for the evaluated status.</summary>
        public string StatusKey => $"S__{Key}";

        /// <summary>Header caption shown in the grid.</summary>
        public string Caption => $"{StageName} / {ParameterName}";

        public ColumnMeta(LineStage stage, BathParameter param)
        {
            StagePosition = stage.Position;
            StageName     = stage.Name ?? string.Empty;
            ParameterName = param.Name ?? string.Empty;
            Key           = BuildKey(stage, param);
            Parameter     = param;
        }

        public static string BuildKey(LineStage stage, BathParameter param)
            => $"{stage.Position}__{stage.Name}__{param.Name}";
    }
}
