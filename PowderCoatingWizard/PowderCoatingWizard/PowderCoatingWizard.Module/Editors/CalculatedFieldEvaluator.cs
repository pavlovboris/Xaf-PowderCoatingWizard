using System.Data;

namespace PowderCoatingWizard.Module.Editors
{
    /// <summary>
    /// Evaluates a formula against a per-row value dictionary using
    /// <see cref="DataTable"/> computed expressions (.NET built-in).
    ///
    /// Supported syntax (standard DataTable Expression language):
    ///   IIF([pH] > 7, 'Alkaline', IIF([pH] &lt; 6, 'Acid', 'OK'))
    ///   [Conductivity] * 0.001
    ///   IIF([Vид_дефект_Status] = 'Alarm', 'CHECK', '')
    ///
    /// Column names (keys) must be valid DataTable column names.
    /// Spaces and special characters in parameter/criterion names are replaced with _.
    /// </summary>
    public static class CalculatedFieldEvaluator
    {
        /// <summary>
        /// Evaluates <paramref name="formula"/> using <paramref name="context"/>
        /// as column name → value pairs.
        /// Returns "#ERR" on any parse or evaluation error.
        /// </summary>
        public static string Evaluate(
            string formula,
            IReadOnlyDictionary<string, object?> context)
        {
            if (string.IsNullOrWhiteSpace(formula)) return string.Empty;
            try
            {
                using var table = new DataTable();

                // Add one column per context key, typed as object so any value fits
                foreach (var (key, _) in context)
                    table.Columns.Add(key, typeof(object));

                // Add the computed column
                const string ResultCol = "__result__";
                table.Columns.Add(new DataColumn(ResultCol, typeof(object))
                {
                    Expression = formula
                });

                // Add a single row with context values
                var row = table.NewRow();
                foreach (var (key, value) in context)
                    row[key] = value ?? DBNull.Value;
                table.Rows.Add(row);

                var result = row[ResultCol];
                return result == DBNull.Value || result is null
                    ? string.Empty
                    : Convert.ToString(result) ?? string.Empty;
            }
            catch
            {
                return "#ERR";
            }
        }

        /// <summary>
        /// Builds the per-row context dictionary from parameter measurements and criterion results.
        /// Keys are sanitised names (non-word characters → _) so they are valid DataTable column names.
        /// </summary>
        public static Dictionary<string, object?> BuildContext(
            IEnumerable<(string ParameterName, object? Value, string Status)> parameterValues,
            IEnumerable<(string CriterionName, string Message, string Status)> criterionValues)
        {
            var ctx = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);

            foreach (var (name, value, status) in parameterValues)
            {
                var key = Sanitise(name);
                ctx[key]              = value;
                ctx[key + "_Status"]  = status;
            }

            foreach (var (name, message, status) in criterionValues)
            {
                var key = Sanitise(name);
                ctx[key]              = message;
                ctx[key + "_Status"]  = status;
            }

            return ctx;
        }

        /// <summary>Replaces any non-word character with _ to produce a valid column name.</summary>
        public static string Sanitise(string name)
            => System.Text.RegularExpressions.Regex.Replace(name ?? string.Empty, @"\W", "_");
    }
}
