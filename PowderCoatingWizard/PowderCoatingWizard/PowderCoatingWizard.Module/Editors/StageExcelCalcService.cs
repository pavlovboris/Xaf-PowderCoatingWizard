using DevExpress.Spreadsheet;
using PowderCoatingWizard.Module.BusinessObjects;
using System.Text.RegularExpressions;
using SpreadsheetDocumentFormat = DevExpress.Spreadsheet.DocumentFormat;

namespace PowderCoatingWizard.Module.Editors
{
    /// <summary>
    /// Headless Excel-formula calculation engine for the Bath Stage Sheet.
    ///
    /// Per row in the sheet:
    ///   1. Load the .xlsx template into a headless DevExpress Workbook.
    ///   2. Write INPUT values (from the row context dictionary) into configured cells
    ///      (via explicit InputMap entries).
    ///   3. Auto-fill any cell placeholders found in the template:
    ///        {{Prop:fieldName}}  — compatible with the DSERPEvo naming convention
    ///        {{Key:fieldName}}   — alternative prefix, identical behaviour
    ///      In both cases <c>fieldName</c> is looked up in the context dictionary which
    ///      contains every value visible in the Bath Stage Sheet grid:
    ///        • Sanitised parameter name          → measured value     e.g. {{Prop:pH}}
    ///        • Sanitised parameter name + _Status → status string     e.g. {{Prop:pH_Status}}
    ///        • Sanitised criterion name           → result message    e.g. {{Prop:Criterion_A}}
    ///        • Sanitised criterion name + _Status → status string     e.g. {{Prop:Criterion_A_Status}}
    ///        • Sanitised calculated-field name    → computed string   e.g. {{Prop:My_Formula}}
    ///   4. Workbook.Calculate()
    ///   5. Read each OUTPUT cell → return as (ColumnName, Value) pairs.
    ///
    /// No UI dependency — works identically in WinForms and Blazor.
    /// </summary>
    public static class StageExcelCalcService
    {
        // Matches {{Prop:Name}} or {{Key:Name}} — case-insensitive prefix
        private static readonly Regex PlaceholderRegex =
            new(@"\{\{(?:Prop|Key):(?<key>[A-Za-z_][A-Za-z0-9_]*)\}\}",
                RegexOptions.Compiled | RegexOptions.IgnoreCase);

        /// <summary>
        /// Evaluates <paramref name="template"/> for every context in <paramref name="contexts"/>
        /// using a single workbook load — O(n) writes/reads instead of O(n) LoadDocument calls.
        ///
        /// Returns one result list per context, in the same order.
        /// Each inner list contains (ColumnName, Value) pairs ordered by SortOrder.
        /// </summary>
        public static IReadOnlyList<IReadOnlyList<(string ColumnName, string Value)>> EvaluateBatch(
            StageExcelTemplate template,
            IReadOnlyList<IReadOnlyDictionary<string, object?>> contexts)
        {
            var outputMaps = template.OutputMaps
                .OrderBy(m => m.SortOrder).ThenBy(m => m.ColumnName)
                .ToList();

            var empty = (IReadOnlyList<(string, string)>)[];

            if (outputMaps.Count == 0 || contexts.Count == 0)
                return Enumerable.Repeat(empty, contexts.Count).ToList();

            var templateBytes = template.TemplateData;
            if (templateBytes is not { Length: > 0 })
                return Enumerable.Repeat(empty, contexts.Count).ToList();

            var inputMaps = template.InputMaps
                .OrderBy(m => m.SortOrder)
                .Where(m => !string.IsNullOrWhiteSpace(m.ContextKey)
                         && !string.IsNullOrWhiteSpace(m.InputCellAddress))
                .ToList();

            var results = new List<IReadOnlyList<(string, string)>>(contexts.Count);

            try
            {
                using var workbook = new Workbook();
                workbook.LoadDocument(templateBytes, SpreadsheetDocumentFormat.Xlsx);

                // Pre-resolve cell references so we don't re-parse the address string per row
                var inputCells = inputMaps
                    .Select(m => (Map: m, Cell: ResolveCell(workbook, m.InputCellAddress)))
                    .Where(x => x.Cell is not null)
                    .ToList();

                var outputCells = outputMaps
                    .Select(m => (Map: m, Cell: ResolveCell(workbook, m.ResultCellAddress)))
                    .ToList();

                // Snapshot the template cell values so we can restore between rows
                // (avoids re-loading the file for each row)
                var snapshot = TakeSnapshot(workbook);

                foreach (var context in contexts)
                {
                    // Restore template state
                    RestoreSnapshot(workbook, snapshot);

                    // STEP 1a: Placeholders (text cells containing {{Prop:...}} / {{Key:...}})
                    ApplyPlaceholders(workbook, context);

                    // STEP 1b: Explicit input cell mappings
                    foreach (var (map, cell) in inputCells)
                    {
                        if (context.TryGetValue(map.ContextKey, out var value))
                            SetCellValue(cell!, value);
                    }

                    // STEP 2: Recalculate
                    workbook.Calculate();

                    // STEP 3: Read outputs
                    var rowResults = new List<(string, string)>(outputMaps.Count);
                    foreach (var (outMap, cell) in outputCells)
                    {
                        if (string.IsNullOrWhiteSpace(outMap.ColumnName)) continue;
                        var text = cell is null ? "#ERR" : ReadCellText(cell);
                        rowResults.Add((outMap.ColumnName, text));
                    }
                    results.Add(rowResults);
                }
            }
            catch
            {
                // If workbook fails to load entirely, fill remaining rows with #ERR
                var errRow = outputMaps
                    .Where(m => !string.IsNullOrWhiteSpace(m.ColumnName))
                    .Select(m => (m.ColumnName, "#ERR"))
                    .ToList();
                while (results.Count < contexts.Count)
                    results.Add(errRow);
            }

            return results;
        }

        // ── Snapshot helpers ─────────────────────────────────────────────────

        /// <summary>
        /// Records the value of every non-formula cell in the workbook.
        /// Only values are captured — formulas stay as-is because Calculate() overwrites their cached value.
        /// </summary>
        private static Dictionary<(int SheetIndex, int Row, int Col), CellValue> TakeSnapshot(Workbook workbook)
        {
            var snap = new Dictionary<(int, int, int), CellValue>();
            for (int si = 0; si < workbook.Worksheets.Count; si++)
            {
                var sheet = workbook.Worksheets[si];
                var used  = sheet.GetUsedRange();
                if (used is null) continue;
                foreach (var cell in used)
                    if (!cell.HasFormula)
                        snap[(si, cell.RowIndex, cell.ColumnIndex)] = cell.Value;
            }
            return snap;
        }

        private static void RestoreSnapshot(
            Workbook workbook,
            Dictionary<(int SheetIndex, int Row, int Col), CellValue> snapshot)
        {
            for (int si = 0; si < workbook.Worksheets.Count; si++)
            {
                var sheet = workbook.Worksheets[si];
                var used  = sheet.GetUsedRange();
                if (used is null) continue;
                foreach (var cell in used)
                {
                    if (cell.HasFormula) continue;
                    var key = (si, cell.RowIndex, cell.ColumnIndex);
                    if (snapshot.TryGetValue(key, out var original))
                        cell.Value = original;
                    else
                        cell.ClearContents();
                }
            }
        }

        /// <summary>
        /// Evaluates all OUTPUT mappings of <paramref name="template"/> for a single row.
        /// Returns a list of (ColumnName, CellValue) pairs in <see cref="StageExcelOutputMap.SortOrder"/> order.
        /// Returns an empty list if the template has no data or no output maps.
        /// Any per-cell error is reported as the string "#ERR".
        /// </summary>
        public static IReadOnlyList<(string ColumnName, string Value)> Evaluate(
            StageExcelTemplate template,
            IReadOnlyDictionary<string, object?> context)
        {
            var outputMaps = template.OutputMaps
                .OrderBy(m => m.SortOrder).ThenBy(m => m.ColumnName)
                .ToList();

            if (outputMaps.Count == 0) return [];

            var templateBytes = template.TemplateData;
            if (templateBytes is not { Length: > 0 }) return [];

            try
            {
                using var workbook = new Workbook();
                workbook.LoadDocument(templateBytes, SpreadsheetDocumentFormat.Xlsx);

                // STEP 1a: Auto-fill {{Key:…}} placeholders
                ApplyPlaceholders(workbook, context);

                // STEP 1b: Write explicit INPUT cell mappings
                var inputMaps = template.InputMaps
                    .OrderBy(m => m.SortOrder)
                    .ToList();

                foreach (var map in inputMaps)
                {
                    if (string.IsNullOrWhiteSpace(map.ContextKey)
                        || string.IsNullOrWhiteSpace(map.InputCellAddress))
                        continue;

                    if (!context.TryGetValue(map.ContextKey, out var value)) continue;

                    var cell = ResolveCell(workbook, map.InputCellAddress);
                    if (cell is null) continue;

                    SetCellValue(cell, value);
                }

                // STEP 2: Recalculate
                workbook.Calculate();

                // STEP 3: Read OUTPUT cells
                var results = new List<(string, string)>(outputMaps.Count);
                foreach (var outMap in outputMaps)
                {
                    if (string.IsNullOrWhiteSpace(outMap.ResultCellAddress)
                        || string.IsNullOrWhiteSpace(outMap.ColumnName))
                        continue;

                    var cell = ResolveCell(workbook, outMap.ResultCellAddress);
                    var text = cell is null ? "#ERR" : ReadCellText(cell);
                    results.Add((outMap.ColumnName, text));
                }

                return results;
            }
            catch
            {
                // If the whole workbook fails, report #ERR for every output map
                return outputMaps
                    .Where(m => !string.IsNullOrWhiteSpace(m.ColumnName))
                    .Select(m => (m.ColumnName, "#ERR"))
                    .ToList();
            }
        }

        // ── Placeholder engine ───────────────────────────────────────────────

        private static void ApplyPlaceholders(
            Workbook workbook,
            IReadOnlyDictionary<string, object?> context)
        {
            foreach (var sheet in workbook.Worksheets)
            {
                var used = sheet.GetUsedRange();
                if (used is null) continue;

                foreach (var cell in used)
                {
                    if (cell.HasFormula) continue;
                    var text = cell.DisplayText;
                    if (string.IsNullOrEmpty(text)) continue;

                    var matches = PlaceholderRegex.Matches(text);
                    if (matches.Count == 0) continue;

                    if (matches.Count == 1 && text.Trim() == matches[0].Value)
                    {
                        // Entire cell is a single placeholder → set typed value
                        var key = matches[0].Groups["key"].Value;
                        if (context.TryGetValue(key, out var val))
                            SetCellValue(cell, val);
                    }
                    else
                    {
                        // Mixed text → string replacement
                        var result = PlaceholderRegex.Replace(text, m =>
                        {
                            var key = m.Groups["key"].Value;
                            return context.TryGetValue(key, out var val)
                                ? val?.ToString() ?? string.Empty
                                : m.Value;
                        });
                        cell.Value = result;
                    }
                }
            }
        }

        // ── Cell helpers ─────────────────────────────────────────────────────

        private static Cell? ResolveCell(Workbook workbook, string address)
        {
            try
            {
                int bang = address.IndexOf('!');
                if (bang > 0)
                {
                    var sheetName = address[..bang].Trim('\'');
                    var cellRef   = address[(bang + 1)..];
                    return workbook.Worksheets[sheetName]?.Cells[cellRef];
                }
                return workbook.Worksheets[0].Cells[address];
            }
            catch { return null; }
        }

        private static void SetCellValue(Cell cell, object? value)
        {
            switch (value)
            {
                case null:                                          cell.ClearContents(); break;
                case bool b:                                        cell.Value = b;       break;
                case DateTime dt:                                   cell.Value = dt;      break;
                case double d:                                      cell.Value = d;       break;
                case float f:                                       cell.Value = (double)f; break;
                case decimal dec:                                   cell.Value = (double)dec; break;
                case int or long or short or byte or sbyte
                        or uint or ulong or ushort:                 cell.Value = Convert.ToDouble(value); break;
                default:                                            cell.Value = value.ToString(); break;
            }
        }

        private static string ReadCellText(Cell cell)
        {
            var v = cell.Value;
            if (v.IsEmpty || v.IsError) return v.IsError ? "#ERR" : string.Empty;
            if (v.IsText)    return v.TextValue;
            if (v.IsNumeric) return v.NumericValue.ToString(System.Globalization.CultureInfo.InvariantCulture);
            if (v.IsBoolean) return v.BooleanValue.ToString();
            if (v.IsDateTime) return v.DateTimeValue.ToString("dd.MM.yyyy HH:mm");
            return cell.DisplayText;
        }
    }
}
