using DevExpress.ExpressApp;
using PowderCoatingWizard.Module.BusinessObjects;
using System.Data;

namespace PowderCoatingWizard.Module.Editors
{
    /// <summary>
    /// Builds a pivot DataTable for one LineStage.
    /// Rows   = all MeasurementSession records for the stage's line (including empty new sessions).
    /// Columns = Date + Operator  +  one Value/Status pair per StageParameter
    ///                             +  one Result column per StageCriterion (if any configured).
    /// </summary>
    public static class BathStageSheetService
    {
        public const string ColOid      = "_Oid";
        public const string ColDate     = "Date";
        public const string ColOperator = "Operator";
        public const string ColNotes    = "Notes";

        /// <summary>
        /// Creates a new <see cref="MeasurementSession"/> for <paramref name="stage"/>'s line
        /// and immediately seeds one empty <see cref="ParameterMeasurement"/> for every
        /// <see cref="StageParameter"/> of <paramref name="stage"/> so the session is
        /// associated with this stage from the start.
        /// Changes are committed before returning.
        /// </summary>
        public static MeasurementSession CreateSessionForStage(IObjectSpace os, LineStage stage)
        {
            var session          = os.CreateObject<MeasurementSession>();
            session.Line         = stage.Line;
            session.MeasuredOn   = DateTime.Now;
            session.OperatorName = string.Empty;

            // Seed an empty measurement for every parameter on this stage
            foreach (var sp in stage.Parameters)
            {
                if (sp.Parameter == null) continue;
                var m = os.CreateObject<ParameterMeasurement>();
                m.MeasurementSession = session;
                m.Stage              = stage;
                m.Parameter          = sp.Parameter;
                // leave NumericValue / TextValue / SelectedValue null → empty cell
            }

            os.CommitChanges();
            return session;
        }

        /// <summary>
        /// Returns sessions that belong to <paramref name="stage"/>'s line but do NOT yet
        /// contain any measurement for <paramref name="stage"/>.  These are candidate sessions
        /// that the operator can "append" the stage's measurements to.
        /// </summary>
        public static List<MeasurementSession> GetSessionsMissingStage(IObjectSpace os, LineStage stage)
        {
            if (stage.Line == null) return [];

            var lineOid  = stage.Line.Oid;
            var stageOid = stage.Oid;

            var sessionOidsWithStage = os.GetObjects<ParameterMeasurement>()
                .Where(m => m.Stage?.Oid == stageOid)
                .Select(m => m.MeasurementSession?.Oid)
                .Where(id => id.HasValue)
                .Select(id => id!.Value)
                .ToHashSet();

            return os.GetObjects<MeasurementSession>()
                .Where(s => s.Line?.Oid == lineOid && !sessionOidsWithStage.Contains(s.Oid))
                .OrderByDescending(s => s.MeasuredOn)
                .ToList();
        }

        /// <summary>
        /// Appends empty <see cref="ParameterMeasurement"/> rows for <paramref name="stage"/> to
        /// an <paramref name="existingSession"/> that does not yet have measurements for that stage.
        /// Already-existing stage/parameter combinations are skipped (uniqueness guard).
        /// Changes are committed before returning.
        /// </summary>
        public static void AppendStageToSession(IObjectSpace os, LineStage stage, MeasurementSession existingSession)
        {
            var alreadyMeasuredParamOids = os.GetObjects<ParameterMeasurement>()
                .Where(m => m.MeasurementSession?.Oid == existingSession.Oid && m.Stage?.Oid == stage.Oid)
                .Select(m => m.Parameter?.Oid)
                .Where(id => id.HasValue)
                .Select(id => id!.Value)
                .ToHashSet();

            foreach (var sp in stage.Parameters)
            {
                if (sp.Parameter == null) continue;
                if (alreadyMeasuredParamOids.Contains(sp.Parameter.Oid)) continue; // uniqueness guard

                var m = os.CreateObject<ParameterMeasurement>();
                m.MeasurementSession = existingSession;
                m.Stage              = stage;
                m.Parameter          = sp.Parameter;
            }

            os.CommitChanges();
        }

        public static (DataTable Table, List<BathStageColumnMeta> Columns, List<StageCriterion> Criteria, List<StageCalculatedField> CalculatedFields, List<StageExcelTemplate> ExcelTemplates)
                Build(IObjectSpace objectSpace, LineStage stage,
                    DateTime? dateFrom = null, DateTime? dateTo = null)
        {
            var (table, columnMetas, criteria, calculatedFields, excelTemplates) =
                BuildColumnStructure(objectSpace, stage);

            // ── Sessions for this stage: those with measurements here + brand-new empty ones ──
            var allMeasurements = objectSpace.GetObjects<ParameterMeasurement>()
                .Where(m => m.Stage?.Oid == stage.Oid)
                .ToList();

            // OIDs of sessions that have measurements for THIS stage
            var stageSessionOids = allMeasurements
                .Select(m => m.MeasurementSession?.Oid)
                .Where(id => id.HasValue)
                .Select(id => id!.Value)
                .ToHashSet();

            // OIDs of sessions that have ANY measurement (for any stage) on this line
            var anyMeasSessionOids = objectSpace.GetObjects<ParameterMeasurement>()
                .Where(m => m.MeasurementSession?.Line?.Oid == stage.Line?.Oid)
                .Select(m => m.MeasurementSession?.Oid)
                .Where(id => id.HasValue)
                .Select(id => id!.Value)
                .ToHashSet();

            var sessions = objectSpace.GetObjects<MeasurementSession>()
                .Where(s => s.Line?.Oid == stage.Line?.Oid)
                // Show if: has measurements for THIS stage, OR is brand-new (no measurements on any stage yet)
                .Where(s => stageSessionOids.Contains(s.Oid) || !anyMeasSessionOids.Contains(s.Oid))
                .Where(s => dateFrom == null || s.MeasuredOn >= dateFrom.Value)
                .Where(s => dateTo   == null || s.MeasuredOn <= dateTo.Value)
                .OrderByDescending(s => s.MeasuredOn)
                .ToList();

            // ── Populate rows ──────────────────────────────────────────────
            // We do two passes so Excel templates are batch-evaluated once per template
            // instead of once per row — critical when sessions number in the thousands.

            // Pass 1: parameters, criteria, calculated fields + collect per-row Excel context
            var rowContexts = new List<IReadOnlyDictionary<string, object?>>(sessions.Count);

            foreach (var session in sessions)
            {
                var row = table.NewRow();
                row[ColOid]      = session.Oid;
                row[ColDate]     = session.MeasuredOn;
                row[ColOperator] = session.OperatorName ?? string.Empty;
                row[ColNotes]    = session.Notes ?? string.Empty;

                var measByParam = allMeasurements
                    .Where(m => m.MeasurementSession?.Oid == session.Oid)
                    .GroupBy(m => m.Parameter.Oid)
                    .ToDictionary(g => g.Key, g => g.Last());

                foreach (var meta in columnMetas)
                {
                    if (!measByParam.TryGetValue(meta.ParameterOid, out var m)) continue;

                    if (m.NumericValue.HasValue)
                    {
                        row[meta.ValueKey]  = $"{m.NumericValue} {m.Parameter.Unit?.Symbol}";
                        row[meta.StatusKey] = m.EvaluatedStatus;
                    }
                    else if (m.SelectedValue != null)
                    {
                        row[meta.ValueKey]  = m.SelectedValue.Name;
                        row[meta.StatusKey] = BathParameter.EvaluateStatus(m.SelectedValue);
                    }
                    else
                    {
                        row[meta.ValueKey] = m.TextValue ?? string.Empty;
                    }
                }

                var criterionResults = new Dictionary<Guid, ParameterStatus>(criteria.Count);
                foreach (var criterion in TopologicalSort(criteria))
                {
                    var (status, message) = criterion.Evaluate(measByParam, criterionResults);
                    criterionResults[criterion.Oid] = status;
                    row[CriterionColKey(criterion)] = new CriterionCellValue(status, message);
                }

                IReadOnlyDictionary<string, object?> context = new Dictionary<string, object?>();

                if (calculatedFields.Count > 0 || excelTemplates.Count > 0)
                {
                    var paramCtx = columnMetas.Select(meta =>
                    {
                        measByParam.TryGetValue(meta.ParameterOid, out var m);
                        object? rawVal = m?.NumericValue.HasValue == true
                            ? (object?)m.NumericValue.Value
                            : m?.SelectedValue != null
                                ? m.SelectedValue.Name
                                : m?.TextValue;
                        string statusStr = m != null ? m.EvaluatedStatus.ToString() : "OK";
                        return (meta.ParameterName, Value: rawVal, Status: statusStr);
                    });

                    var critCtx = criteria.Select(c =>
                    {
                        criterionResults.TryGetValue(c.Oid, out var st);
                        var cell = row[CriterionColKey(c)] as CriterionCellValue;
                        return (c.Name, Message: cell?.Message ?? string.Empty, Status: st.ToString());
                    });

                    context = CalculatedFieldEvaluator.BuildContext(paramCtx, critCtx);

                    foreach (var field in calculatedFields)
                        row[CalcFieldColKey(field)] = CalculatedFieldEvaluator.Evaluate(field.Formula, context);
                }

                rowContexts.Add(context);
                table.Rows.Add(row);
            }

            // Pass 2: Excel templates — one workbook load per template, all rows at once
            if (excelTemplates.Count > 0 && rowContexts.Count > 0)
            {
                foreach (var tmpl in excelTemplates)
                {
                    var batchResults = StageExcelCalcService.EvaluateBatch(tmpl, rowContexts);
                    var outMaps = tmpl.OutputMaps
                        .OrderBy(m => m.SortOrder).ThenBy(m => m.ColumnName)
                        .ToList();

                    for (int i = 0; i < sessions.Count; i++)
                    {
                        var row     = table.Rows[i];
                        var rowRes  = batchResults[i];

                        foreach (var outMap in outMaps)
                        {
                            var colKey = ExcelOutputColKey(tmpl, outMap);
                            if (!table.Columns.Contains(colKey)) continue;
                            var match = rowRes.FirstOrDefault(r => r.ColumnName == outMap.ColumnName);
                            row[colKey] = match.ColumnName != null ? match.Value : string.Empty;
                        }
                    }
                }
            }

            return (table, columnMetas, criteria, calculatedFields, excelTemplates);
        }

        /// <summary>
        /// Builds an empty DataTable with all columns for the stage (no rows)
        /// and returns the associated metadata lists.
        /// Shared by <see cref="Build"/> and <see cref="BuildFromArchive"/>.
        /// </summary>
        private static (DataTable Table,
            List<BathStageColumnMeta>  ColumnMetas,
            List<StageCriterion>       Criteria,
            List<StageCalculatedField> CalculatedFields,
            List<StageExcelTemplate>   ExcelTemplates)
            BuildColumnStructure(IObjectSpace objectSpace, LineStage stage)
        {
            var table = new DataTable();
            table.Columns.Add(ColOid,      typeof(Guid));
            table.Columns.Add(ColDate,     typeof(DateTime));
            table.Columns.Add(ColOperator, typeof(string));
            table.Columns.Add(ColNotes,    typeof(string));

            var stageParams = objectSpace.GetObjects<StageParameter>()
                .Where(sp => sp.Stage?.Oid == stage.Oid && sp.Parameter != null)
                .OrderBy(sp => sp.Parameter.Name)
                .ToList();

            var columnMetas = stageParams
                .Select(sp => new BathStageColumnMeta(sp.Parameter))
                .ToList();

            foreach (var meta in columnMetas)
            {
                table.Columns.Add(meta.ValueKey,  typeof(string));
                table.Columns.Add(meta.StatusKey, typeof(ParameterStatus));
            }

            var criteria = objectSpace.GetObjects<StageCriterion>()
                .Where(c => c.Stage?.Oid == stage.Oid)
                .OrderBy(c => c.SortOrder).ThenBy(c => c.Name)
                .ToList();

            foreach (var criterion in criteria)
                table.Columns.Add(CriterionColKey(criterion), typeof(CriterionCellValue));

            var calculatedFields = objectSpace.GetObjects<StageCalculatedField>()
                .Where(f => f.Stage?.Oid == stage.Oid)
                .OrderBy(f => f.SortOrder).ThenBy(f => f.Name)
                .ToList();

            foreach (var field in calculatedFields)
                table.Columns.Add(CalcFieldColKey(field), typeof(string));

            var excelTemplates = objectSpace.GetObjects<StageExcelTemplate>()
                .Where(t => t.Stage?.Oid == stage.Oid && t.TemplateData is { Length: > 0 })
                .OrderBy(t => t.Name)
                .ToList();

            foreach (var tmpl in excelTemplates)
                foreach (var outMap in tmpl.OutputMaps.OrderBy(m => m.SortOrder).ThenBy(m => m.ColumnName))
                    if (!string.IsNullOrWhiteSpace(outMap.ColumnName))
                        table.Columns.Add(ExcelOutputColKey(tmpl, outMap), typeof(string));

            return (table, columnMetas, criteria, calculatedFields, excelTemplates);
        }

        /// <summary>
        /// Reads the Bath Stage Sheet from the pre-computed archive — no calculation.
        /// Returns the same tuple shape as <see cref="Build"/> so callers are interchangeable.
        /// </summary>
        public static (DataTable Table, List<BathStageColumnMeta> Columns, List<StageCriterion> Criteria, List<StageCalculatedField> CalculatedFields, List<StageExcelTemplate> ExcelTemplates)
                BuildFromArchive(IObjectSpace objectSpace, LineStage stage,
                    DateTime? dateFrom = null, DateTime? dateTo = null)
        {
            var (table, columnMetas, criteria, calculatedFields, excelTemplates) =
                BuildColumnStructure(objectSpace, stage);

            var archiveRows = objectSpace.GetObjects<BathStageSheetArchiveRow>()
                .Where(r => r.Stage?.Oid == stage.Oid)
                .Where(r => dateFrom == null || r.SessionDate >= dateFrom.Value)
                .Where(r => dateTo   == null || r.SessionDate <= dateTo.Value)
                .OrderByDescending(r => r.SessionDate)
                .ToList();

            foreach (var archRow in archiveRows)
                AppendArchiveRow(table, columnMetas, criteria, calculatedFields, excelTemplates, archRow);

            return (table, columnMetas, criteria, calculatedFields, excelTemplates);
        }

        /// <summary>
        /// Hybrid mode: archived sessions are read from the archive (fast),
        /// unarchived sessions are calculated live and appended after.
        /// The result is sorted by date descending so the two sets merge naturally.
        /// </summary>
        public static (DataTable Table, List<BathStageColumnMeta> Columns, List<StageCriterion> Criteria, List<StageCalculatedField> CalculatedFields, List<StageExcelTemplate> ExcelTemplates)
                BuildHybrid(IObjectSpace objectSpace, LineStage stage,
                    DateTime? dateFrom = null, DateTime? dateTo = null)
        {
            var (table, columnMetas, criteria, calculatedFields, excelTemplates) =
                BuildColumnStructure(objectSpace, stage);

            // ── Archived sessions ─────────────────────────────────────────
            var archiveRows = objectSpace.GetObjects<BathStageSheetArchiveRow>()
                .Where(r => r.Stage?.Oid == stage.Oid)
                .Where(r => dateFrom == null || r.SessionDate >= dateFrom.Value)
                .Where(r => dateTo   == null || r.SessionDate <= dateTo.Value)
                .ToList();

            var archivedSessionOids = archiveRows
                .Select(r => r.MeasurementSession?.Oid)
                .Where(id => id.HasValue)
                .Select(id => id!.Value)
                .ToHashSet();

            foreach (var archRow in archiveRows.OrderByDescending(r => r.SessionDate))
                AppendArchiveRow(table, columnMetas, criteria, calculatedFields, excelTemplates, archRow);

            // ── Live sessions (with measurements for THIS stage, + brand-new empty ones) ──
            var allMeasurements = objectSpace.GetObjects<ParameterMeasurement>()
                .Where(m => m.Stage?.Oid == stage.Oid)
                .ToList();

            var stageSessionOidsLive = allMeasurements
                .Select(m => m.MeasurementSession?.Oid)
                .Where(id => id.HasValue)
                .Select(id => id!.Value)
                .ToHashSet();

            var anyMeasSessionOidsLive = objectSpace.GetObjects<ParameterMeasurement>()
                .Where(m => m.MeasurementSession?.Line?.Oid == stage.Line?.Oid)
                .Select(m => m.MeasurementSession?.Oid)
                .Where(id => id.HasValue)
                .Select(id => id!.Value)
                .ToHashSet();

            var liveSessions = objectSpace.GetObjects<MeasurementSession>()
                .Where(s => s.Line?.Oid == stage.Line?.Oid)
                .Where(s => stageSessionOidsLive.Contains(s.Oid) || !anyMeasSessionOidsLive.Contains(s.Oid))
                .Where(s => !archivedSessionOids.Contains(s.Oid))
                .Where(s => dateFrom == null || s.MeasuredOn >= dateFrom.Value)
                .Where(s => dateTo   == null || s.MeasuredOn <= dateTo.Value)
                .OrderByDescending(s => s.MeasuredOn)
                .ToList();

            if (liveSessions.Count > 0)
                AppendLiveRows(table, columnMetas, criteria, calculatedFields, excelTemplates,
                               allMeasurements, liveSessions);

            // ── Sort combined result by date descending ────────────────────
            if (table.Rows.Count > 0)
            {
                var sorted = table.AsEnumerable()
                    .OrderByDescending(r => r.Field<DateTime>(ColDate))
                    .CopyToDataTable();
                table.Clear();
                foreach (DataRow r in sorted.Rows)
                    table.ImportRow(r);
            }

            return (table, columnMetas, criteria, calculatedFields, excelTemplates);
        }

        // ── Shared row-building helpers ───────────────────────────────────

        private static void AppendArchiveRow(
            DataTable                  table,
            List<BathStageColumnMeta>  columnMetas,
            List<StageCriterion>       criteria,
            List<StageCalculatedField> calculatedFields,
            List<StageExcelTemplate>   excelTemplates,
            BathStageSheetArchiveRow   archRow)
        {
            var row = table.NewRow();
            row[ColOid]      = archRow.MeasurementSession?.Oid ?? Guid.Empty;
            row[ColDate]     = archRow.SessionDate;
            row[ColOperator] = archRow.OperatorName ?? string.Empty;
            row[ColNotes]    = archRow.MeasurementSession?.Notes ?? string.Empty;

            var cellsByKey = archRow.Cells
                .ToDictionary(c => c.ColumnKey, StringComparer.Ordinal);

            foreach (var meta in columnMetas)
            {
                if (cellsByKey.TryGetValue(meta.ValueKey, out var vc) && vc.TextValue != null)
                    row[meta.ValueKey] = vc.TextValue;
                if (cellsByKey.TryGetValue(meta.StatusKey, out var sc))
                {
                    ParameterStatus ps;
                    if (sc.StatusValue.HasValue)
                        ps = (ParameterStatus)sc.StatusValue.Value;
                    else if (Enum.TryParse<ParameterStatus>(sc.TextValue, out var parsed))
                        ps = parsed;
                    else
                        continue;
                    row[meta.StatusKey] = ps;
                }
            }

            foreach (var criterion in criteria)
            {
                var key = CriterionColKey(criterion);
                if (!cellsByKey.TryGetValue(key, out var cc)) continue;
                ParameterStatus status;
                if (cc.StatusValue.HasValue)
                    status = (ParameterStatus)cc.StatusValue.Value;
                else if (Enum.TryParse<ParameterStatus>(cc.TextValue, out var parsed))
                    status = parsed;
                else
                    status = ParameterStatus.OK;
                row[key] = new CriterionCellValue(status, cc.TextValue ?? string.Empty);
            }

            foreach (var field in calculatedFields)
            {
                var key = CalcFieldColKey(field);
                if (cellsByKey.TryGetValue(key, out var fc) && fc.TextValue != null)
                    row[key] = fc.TextValue;
            }

            foreach (var tmpl in excelTemplates)
                foreach (var outMap in tmpl.OutputMaps.OrderBy(m => m.SortOrder).ThenBy(m => m.ColumnName))
                {
                    var key = ExcelOutputColKey(tmpl, outMap);
                    if (cellsByKey.TryGetValue(key, out var ec) && ec.TextValue != null)
                        row[key] = ec.TextValue;
                }

            table.Rows.Add(row);
        }

        /// <summary>
        /// Calculates and appends live rows for the given sessions into <paramref name="table"/>.
        /// Reuses the same logic as <see cref="Build"/> but operates on a pre-built table
        /// and a pre-filtered measurements list.
        /// </summary>
        private static void AppendLiveRows(
            DataTable                  table,
            List<BathStageColumnMeta>  columnMetas,
            List<StageCriterion>       criteria,
            List<StageCalculatedField> calculatedFields,
            List<StageExcelTemplate>   excelTemplates,
            List<ParameterMeasurement> allMeasurements,
            List<MeasurementSession>   sessions)
        {
            var rowContexts = new List<IReadOnlyDictionary<string, object?>>(sessions.Count);

            foreach (var session in sessions)
            {
                var row = table.NewRow();
                row[ColOid]      = session.Oid;
                row[ColDate]     = session.MeasuredOn;
                row[ColOperator] = session.OperatorName ?? string.Empty;
                row[ColNotes]    = session.Notes ?? string.Empty;

                var measByParam = allMeasurements
                    .Where(m => m.MeasurementSession?.Oid == session.Oid)
                    .GroupBy(m => m.Parameter.Oid)
                    .ToDictionary(g => g.Key, g => g.Last());

                foreach (var meta in columnMetas)
                {
                    if (!measByParam.TryGetValue(meta.ParameterOid, out var m)) continue;
                    if (m.NumericValue.HasValue)
                    {
                        row[meta.ValueKey]  = $"{m.NumericValue} {m.Parameter.Unit?.Symbol}";
                        row[meta.StatusKey] = m.EvaluatedStatus;
                    }
                    else if (m.SelectedValue != null)
                    {
                        row[meta.ValueKey]  = m.SelectedValue.Name;
                        row[meta.StatusKey] = BathParameter.EvaluateStatus(m.SelectedValue);
                    }
                    else
                    {
                        row[meta.ValueKey] = m.TextValue ?? string.Empty;
                    }
                }

                var criterionResults = new Dictionary<Guid, ParameterStatus>(criteria.Count);
                foreach (var criterion in TopologicalSort(criteria))
                {
                    var (status, message) = criterion.Evaluate(measByParam, criterionResults);
                    criterionResults[criterion.Oid] = status;
                    row[CriterionColKey(criterion)] = new CriterionCellValue(status, message);
                }

                IReadOnlyDictionary<string, object?> context = new Dictionary<string, object?>();
                if (calculatedFields.Count > 0 || excelTemplates.Count > 0)
                {
                    var paramCtx = columnMetas.Select(meta =>
                    {
                        measByParam.TryGetValue(meta.ParameterOid, out var m);
                        object? rawVal = m?.NumericValue.HasValue == true
                            ? (object?)m.NumericValue.Value
                            : m?.SelectedValue != null ? m.SelectedValue.Name : m?.TextValue;
                        string statusStr = m != null ? m.EvaluatedStatus.ToString() : "OK";
                        return (meta.ParameterName, Value: rawVal, Status: statusStr);
                    });
                    var critCtx = criteria.Select(c =>
                    {
                        criterionResults.TryGetValue(c.Oid, out var st);
                        var cell = row[CriterionColKey(c)] as CriterionCellValue;
                        return (c.Name, Message: cell?.Message ?? string.Empty, Status: st.ToString());
                    });
                    context = CalculatedFieldEvaluator.BuildContext(paramCtx, critCtx);
                    foreach (var field in calculatedFields)
                        row[CalcFieldColKey(field)] = CalculatedFieldEvaluator.Evaluate(field.Formula, context);
                }

                rowContexts.Add(context);
                table.Rows.Add(row);
            }

            // Batch Excel evaluation for live rows
            if (excelTemplates.Count > 0 && rowContexts.Count > 0)
            {
                foreach (var tmpl in excelTemplates)
                {
                    var batchResults = StageExcelCalcService.EvaluateBatch(tmpl, rowContexts);
                    var outMaps = tmpl.OutputMaps
                        .OrderBy(m => m.SortOrder).ThenBy(m => m.ColumnName).ToList();
                    // rows were appended starting at table.Rows.Count - sessions.Count
                    int startIdx = table.Rows.Count - sessions.Count;
                    for (int i = 0; i < sessions.Count; i++)
                    {
                        var row    = table.Rows[startIdx + i];
                        var rowRes = batchResults[i];
                        foreach (var outMap in outMaps)
                        {
                            var colKey = ExcelOutputColKey(tmpl, outMap);
                            if (!table.Columns.Contains(colKey)) continue;
                            var match = rowRes.FirstOrDefault(r => r.ColumnName == outMap.ColumnName);
                            row[colKey] = match.ColumnName != null ? match.Value : string.Empty;
                        }
                    }
                }
            }
        }

        public static string CriterionColKey(StageCriterion criterion)
            => $"C__{criterion.Oid}";

        public static string CalcFieldColKey(StageCalculatedField field)
            => $"F__{field.Oid}";

        public static string ExcelOutputColKey(StageExcelTemplate template, StageExcelOutputMap outMap)
            => $"X__{template.Oid}__{outMap.Oid}";

        /// <summary>
        /// Returns <paramref name="criteria"/> in an order where every criterion
        /// that is referenced by a CriterionIs/CriterionIsNot condition appears
        /// before the criterion that references it (Kahn's algorithm).
        /// Cycles are broken by keeping the original sort order as a fallback.
        /// </summary>
        private static IEnumerable<StageCriterion> TopologicalSort(
            IReadOnlyList<StageCriterion> criteria)
        {
            // Build dependency map: criterion → set of criteria it depends on
            var deps = criteria.ToDictionary(
                c => c.Oid,
                c => c.AllConditions
                    .Where(cond => cond.CriterionRef != null
                                && (cond.Operator == CriterionOperator.CriterionIs
                                    || cond.Operator == CriterionOperator.CriterionIsNot))
                    .Select(cond => cond.CriterionRef!.Oid)
                    .Where(oid => criteria.Any(x => x.Oid == oid))
                    .ToHashSet());

            var sorted  = new List<StageCriterion>(criteria.Count);
            var visited = new HashSet<Guid>();

            void Visit(StageCriterion c)
            {
                if (!visited.Add(c.Oid)) return;
                foreach (var depOid in deps[c.Oid])
                {
                    var dep = criteria.FirstOrDefault(x => x.Oid == depOid);
                    if (dep != null) Visit(dep);
                }
                sorted.Add(c);
            }

            foreach (var c in criteria)
                Visit(c);

            return sorted;
        }
    }

    /// <summary>Metadata for one dynamic parameter column in the Bath Stage Sheet.</summary>
    public sealed class BathStageColumnMeta
    {
        public Guid          ParameterOid   { get; }
        public string        ParameterName  { get; }
        public BathParameter Parameter      { get; }
        public bool          IsPredefined   => Parameter.ValueType == ParameterValueType.Predefined;

        public string ValueKey  => $"V__{ParameterOid}";
        public string StatusKey => $"S__{ParameterOid}";

        public BathStageColumnMeta(BathParameter param)
        {
            ParameterOid  = param.Oid;
            ParameterName = param.Name ?? string.Empty;
            Parameter     = param;
        }
    }

    /// <summary>Cell value for a criterion column — carries both status and the message text.</summary>
    public sealed record CriterionCellValue(ParameterStatus Status, string Message);
}
