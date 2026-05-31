using DevExpress.ExpressApp;
using PowderCoatingWizard.Module.BusinessObjects;
using System.Data;

namespace PowderCoatingWizard.Module.Editors
{
    /// <summary>
    /// Builds and updates the Bath Stage Sheet archive for a given LineStage.
    ///
    /// Two modes:
    ///   UpdateNew   — archives only sessions that have no existing archive row yet.
    ///                 Fast; use after new measurements are entered.
    ///   FullRebuild — recalculates every session for the stage.
    ///                 Slower; use after changing criteria / calculated field formulas.
    ///                 Also removes orphan cells whose column keys no longer exist.
    /// </summary>
    public static class BathStageSheetArchiveService
    {
        public enum ArchiveMode { UpdateNew, FullRebuild }

        /// <summary>
        /// Runs the archive operation and returns how many rows were written.
        /// Progress callback receives (current, total) for UI feedback.
        /// </summary>
        public static int Archive(
            IObjectSpace objectSpace,
            LineStage    stage,
            ArchiveMode  mode,
            DateTime?    dateFrom = null,
            DateTime?    dateTo   = null,
            Action<int, int>? progress = null)
        {
            // For FullRebuild with a range we build only the ranged live table.
            // For UpdateNew the range is ignored (archive whatever is missing).
            var (table, cols, criteria, calcFields, excelTmpls) = mode == ArchiveMode.FullRebuild
                ? BathStageSheetService.Build(objectSpace, stage, dateFrom, dateTo)
                : BathStageSheetService.Build(objectSpace, stage);

            // Collect current column keys so we can prune orphan cells in FullRebuild.
            var currentColumnKeys = BuildCurrentColumnKeys(table);

            // Existing archive rows for this stage, keyed by SessionOid for fast lookup.
            var existingRows = objectSpace.GetObjects<BathStageSheetArchiveRow>()
                .Where(r => r.Stage?.Oid == stage.Oid)
                .ToDictionary(r => r.MeasurementSession!.Oid);

            // Determine which DataTable rows to archive.
            var dataRows = table.Rows.Cast<DataRow>().ToList();

            var toProcess = mode == ArchiveMode.UpdateNew
                ? dataRows.Where(dr => !existingRows.ContainsKey((Guid)dr[BathStageSheetService.ColOid])).ToList()
                : dataRows;

            int total   = toProcess.Count;
            int written = 0;

            foreach (var dataRow in toProcess)
            {
                var sessionOid = (Guid)dataRow[BathStageSheetService.ColOid];

                // Get or create the archive row.
                if (!existingRows.TryGetValue(sessionOid, out var archiveRow))
                {
                    archiveRow = objectSpace.CreateObject<BathStageSheetArchiveRow>();
                    archiveRow.Stage             = stage;
                    archiveRow.MeasurementSession = objectSpace.GetObjectByKey<MeasurementSession>(sessionOid);
                }

                archiveRow.ArchivedOn   = DateTime.UtcNow;
                archiveRow.SessionDate  = (DateTime)dataRow[BathStageSheetService.ColDate];
                archiveRow.OperatorName = dataRow[BathStageSheetService.ColOperator]?.ToString() ?? string.Empty;

                // Build a lookup of existing cells for this row.
                var existingCells = archiveRow.Cells
                    .ToDictionary(c => c.ColumnKey, StringComparer.Ordinal);

                // In FullRebuild, remove cells whose column key no longer exists.
                if (mode == ArchiveMode.FullRebuild)
                {
                    foreach (var cell in archiveRow.Cells
                        .Where(c => !currentColumnKeys.Contains(c.ColumnKey))
                        .ToList())
                    {
                        objectSpace.Delete(cell);
                    }
                }

                // Write / update every current column.
                foreach (DataColumn col in table.Columns)
                {
                    if (col.ColumnName == BathStageSheetService.ColOid) continue;
                    if (col.ColumnName == BathStageSheetService.ColDate) continue;
                    if (col.ColumnName == BathStageSheetService.ColOperator) continue;

                    var (text, status) = ExtractCellValues(dataRow, col);

                    if (!existingCells.TryGetValue(col.ColumnName, out var archiveCell))
                    {
                        archiveCell           = objectSpace.CreateObject<BathStageSheetArchiveCell>();
                        archiveCell.ArchiveRow = archiveRow;
                        archiveCell.ColumnKey  = col.ColumnName;
                    }

                    archiveCell.TextValue   = text;
                    archiveCell.StatusValue = status;
                }

                written++;
                progress?.Invoke(written, total);
            }

            objectSpace.CommitChanges();
            return written;
        }

        // ── Helpers ──────────────────────────────────────────────────────────

        private static HashSet<string> BuildCurrentColumnKeys(DataTable table)
        {
            var keys = new HashSet<string>(StringComparer.Ordinal);
            foreach (DataColumn col in table.Columns)
                keys.Add(col.ColumnName);
            return keys;
        }

        private static (string text, int? status) ExtractCellValues(DataRow row, DataColumn col)
        {
            var raw = row[col];
            if (raw == DBNull.Value || raw == null) return (string.Empty, null);

            // Criterion cell — carries both status and message
            if (raw is CriterionCellValue crit)
                return (crit.Message ?? string.Empty, (int)crit.Status);

            // ParameterStatus enum — status column; store explicit int even for OK=0
            if (raw is ParameterStatus ps)
                return (ps.ToString(), (int)ps);   // OK=0, Warning=1, Alarm=2

            // Numeric or text value column
            if (raw is double d)
                return (d.ToString(System.Globalization.CultureInfo.InvariantCulture), null);
            if (raw is float f)
                return (f.ToString(System.Globalization.CultureInfo.InvariantCulture), null);
            if (raw is decimal dec)
                return (dec.ToString(System.Globalization.CultureInfo.InvariantCulture), null);

            return (raw.ToString() ?? string.Empty, null);
        }
    }
}
