using DevExpress.ExpressApp;
using DevExpress.Persistent.Base;
using System.ComponentModel;
using System.Text;

namespace PowderCoatingWizard.Module.Services.AI
{
    /// <summary>
    /// Read-only database evidence helper for the AI assistant.
    /// It returns compact context for the model to use internally instead of acting as a query/table helper.
    /// </summary>
    public sealed class DatabaseInsightService
    {
        private readonly IObjectSpace _os;
        private readonly SchemaDiscoveryService _schema;
        private readonly SqlServerSchemaProvider? _sqlSchema;
        private readonly int _maxRecords;
        private readonly int _maxFields;

        public DatabaseInsightService(IObjectSpace os, SchemaDiscoveryService schema, int maxRecords = 50, int maxFields = 8, SqlServerSchemaProvider? sqlSchema = null)
        {
            _os = os;
            _schema = schema;
            _sqlSchema = sqlSchema;
            _maxRecords = maxRecords > 0 ? maxRecords : 50;
            _maxFields = Math.Clamp(maxFields, 3, 12);
        }

        [Description(
            "Gets read-only database evidence for the assistant to use when answering a domain question, including SQL schema context when available. " +
            "Prefer this tool before generic query tools when database facts may help. It can provide enough evidence for summaries, comparisons, and aggregate reasoning, but do not expose raw records, SQL, or tables from this output unless the user explicitly asked for a table/report/list. " +
            "The entity name must come from the AI-queryable schema; filter is optional semicolon-separated PropertyName=Value pairs.")]
        public string GetDatabaseInsight(
            [Description("AI-queryable entity name to inspect, for example LineStage, ParameterMeasurement, ParameterThreshold, ChemicalProduct, or AnalysisRecord.")] string entityName,
            [Description("Optional semicolon-separated PropertyName=Value filters. Prefer narrow filters from the user's question. Omit for a small sample summary.")] string filter = "",
            [Description("Short reason why this evidence is needed for the assistant's answer. This is used only for traceability.")] string reason = "")
        {
            if (string.IsNullOrWhiteSpace(entityName))
                return $"Database insight skipped: entity name is required. Available entities: {GetEntityNameList()}.";

            var entity = _schema.Schema.FindEntity(entityName);
            if (entity == null)
                return $"Database insight skipped: entity '{entityName}' is not available. Available entities: {GetEntityNameList()}.";

            try
            {
                var objects = _os.GetObjects(entity.ClrType);
                var matched = new List<object>();

                foreach (var obj in objects)
                {
                    if (!string.IsNullOrWhiteSpace(filter) && !MatchesFilter(obj, entity, filter))
                        continue;

                    matched.Add(obj);
                    if (matched.Count >= _maxRecords)
                        break;
                }

                var sb = new StringBuilder();
                sb.AppendLine("Database evidence for internal assistant reasoning only.");
                sb.AppendLine("Do not present this as a table or raw query output unless the user explicitly requested tabular/list analysis.");
                sb.AppendLine($"Entity: {entity.Name}");
                var sqlSchemaSummary = _sqlSchema?.GetTableSummary(entity.Name);
                if (!string.IsNullOrWhiteSpace(sqlSchemaSummary))
                {
                    sb.AppendLine();
                    sb.AppendLine(sqlSchemaSummary.Trim());
                }
                if (!string.IsNullOrWhiteSpace(reason))
                    sb.AppendLine($"Reason: {reason.Trim()}");
                if (!string.IsNullOrWhiteSpace(filter))
                    sb.AppendLine($"Filter: {filter.Trim()}");
                sb.AppendLine($"Matched sample count: {matched.Count} (capped at {_maxRecords})");

                if (matched.Count == 0)
                {
                    sb.AppendLine("No matching records found in the allowed database scope.");
                    return sb.ToString();
                }

                sb.AppendLine();
                sb.AppendLine("Compact evidence sample:");
                foreach (var obj in matched)
                    sb.AppendLine(FormatEvidenceLine(obj, entity));

                return sb.ToString();
            }
            catch (Exception ex)
            {
                Tracing.Tracer.LogError(ex);
                return $"Database insight failed for {entity.Name}: {ex.Message}";
            }
        }

        private string GetEntityNameList() =>
            string.Join(", ", _schema.Schema.Entities.Select(e => e.Name));

        private static bool MatchesFilter(object obj, EntityInfo entity, string filter)
        {
            foreach (var segment in filter.Split(';', StringSplitOptions.RemoveEmptyEntries))
            {
                var eq = segment.IndexOf('=');
                if (eq < 0) continue;

                var propName = segment[..eq].Trim();
                var filterValue = segment[(eq + 1)..].Trim();
                if (string.IsNullOrWhiteSpace(propName) || string.IsNullOrWhiteSpace(filterValue))
                    continue;

                var prop = entity.Properties.FirstOrDefault(
                    p => p.Name.Equals(propName, StringComparison.OrdinalIgnoreCase));
                if (prop == null) continue;

                var clrProp = obj.GetType().GetProperty(prop.Name);
                if (clrProp == null) continue;

                var actualValue = clrProp.GetValue(obj);
                var actualStr = FormatValue(actualValue);

                if (!actualStr.Contains(filterValue, StringComparison.OrdinalIgnoreCase))
                    return false;
            }

            return true;
        }

        private string FormatEvidenceLine(object obj, EntityInfo entity)
        {
            var parts = new List<string>();

            foreach (var prop in entity.Properties.Take(_maxFields))
            {
                var clrProp = obj.GetType().GetProperty(prop.Name);
                if (clrProp == null) continue;

                var val = clrProp.GetValue(obj);
                parts.Add($"{prop.Name}: {FormatValue(val)}");
            }

            foreach (var rel in entity.Relationships.Where(r => !r.IsCollection).Take(3))
            {
                var clrProp = obj.GetType().GetProperty(rel.PropertyName);
                if (clrProp == null) continue;

                var refObj = clrProp.GetValue(obj);
                if (refObj != null)
                    parts.Add($"{rel.PropertyName}: {GetDisplayText(refObj)}");
            }

            return "- " + string.Join(" | ", parts);
        }

        private static string GetDisplayText(object obj)
        {
            var type = obj.GetType();
            foreach (var name in new[] { "Name", "DisplayName", "Title", "FullName", "Description" })
            {
                var p = type.GetProperty(name);
                if (p == null) continue;

                var v = p.GetValue(obj);
                if (v != null) return v.ToString()!;
            }

            return obj.ToString()!;
        }

        private static string FormatValue(object? val)
        {
            if (val == null) return "N/A";
            if (val is DateTime dt) return dt.ToString("yyyy-MM-dd HH:mm");
            if (val is decimal d) return d.ToString("G6");
            if (val is double db) return db.ToString("G6");
            if (val is float f) return f.ToString("G6");

            var text = val.ToString() ?? string.Empty;
            return text.Length <= 120 ? text : text[..120] + "...";
        }
    }
}
