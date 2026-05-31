using DevExpress.ExpressApp;
using System.ComponentModel;
using System.Text;

namespace PowderCoatingWizard.Module.Services.AI
{
    /// <summary>
    /// Exposes three read-only AI tools that allow the assistant to explore
    /// and query persistent entities in the database.
    ///
    /// All access is strictly read-only: no CreateObject, CommitChanges, or Delete calls.
    /// </summary>
    public sealed class DatabaseQueryToolService
    {
        private readonly IObjectSpace _os;
        private readonly SchemaDiscoveryService _schema;
        private readonly int _maxRecords;

        public DatabaseQueryToolService(IObjectSpace os, SchemaDiscoveryService schema, int maxRecords = 50)
        {
            _os = os;
            _schema = schema;
            _maxRecords = maxRecords > 0 ? maxRecords : 50;
        }

        // ── Tool: list_entities ──────────────────────────────────────────────

        [Description("Returns the list of all database entities (tables) the assistant can query. Call this first to see what data is available.")]
        public string ListEntities()
        {
            var entities = _schema.Schema.Entities;
            if (entities.Count == 0)
                return "No queryable entities are available.";

            var sb = new StringBuilder();
            sb.AppendLine("Available entities:");
            foreach (var e in entities)
            {
                var desc = string.IsNullOrWhiteSpace(e.Description) ? string.Empty : $" — {e.Description}";
                sb.AppendLine($"  - {e.Name}{desc}");
            }
            return sb.ToString();
        }

        // ── Tool: describe_entity ────────────────────────────────────────────

        [Description("Returns full schema details for a single entity: properties, types, relationships, and enum values. Call this before querying an unfamiliar entity.")]
        public string DescribeEntity(
            [Description("Entity name to describe. Use list_entities to see available names.")] string entityName)
        {
            if (string.IsNullOrWhiteSpace(entityName))
                return $"Entity name is required. Available: {GetEntityNameList()}";

            var entity = _schema.Schema.FindEntity(entityName);
            if (entity == null)
                return $"Entity '{entityName}' not found. Available: {GetEntityNameList()}";

            var sb = new StringBuilder();
            sb.AppendLine($"**{entity.Name}**");
            if (!string.IsNullOrWhiteSpace(entity.Description))
                sb.AppendLine(entity.Description);
            sb.AppendLine();

            sb.AppendLine("Properties:");
            foreach (var p in entity.Properties)
            {
                var req = p.IsRequired ? " (required)" : string.Empty;
                var desc = string.IsNullOrWhiteSpace(p.Description) ? string.Empty : $" — {p.Description}";
                sb.AppendLine($"  - {p.Name}: {p.TypeName}{req}{desc}");
                if (p.EnumValues.Count > 0)
                    sb.AppendLine($"    Values: {string.Join(", ", p.EnumValues)}");
            }

            if (entity.Relationships.Count > 0)
            {
                sb.AppendLine();
                sb.AppendLine("Relationships:");
                foreach (var r in entity.Relationships)
                {
                    var kind = r.IsCollection ? "has many" : "belongs to";
                    sb.AppendLine($"  - {r.PropertyName}: {kind} {r.TargetEntity}");
                }
            }

            return sb.ToString();
        }

        // ── Tool: query_entity ───────────────────────────────────────────────

        [Description("Queries records of any available database entity. Returns up to the configured max records. Use describe_entity first if you are unsure about property names.")]
        public string QueryEntity(
            [Description("Entity name to query. Use list_entities to see available names.")] string entityName,
            [Description("Optional filter as semicolon-separated 'PropertyName=Value' pairs. Example: 'IsActive=True;Name=Zinc'. Omit for no filter.")] string filter = "",
            [Description("Maximum number of records to return. Capped by the system limit.")] int top = 25)
        {
            if (string.IsNullOrWhiteSpace(entityName))
                return $"Entity name is required. Available: {GetEntityNameList()}";

            var entity = _schema.Schema.FindEntity(entityName);
            if (entity == null)
                return $"Entity '{entityName}' not found. Available: {GetEntityNameList()}";

            int limit = Math.Min(top, _maxRecords);

            try
            {
                var objects = _os.GetObjects(entity.ClrType);
                var results = new List<string>();

                foreach (var obj in objects)
                {
                    if (results.Count >= limit) break;

                    // Apply filter if provided
                    if (!string.IsNullOrWhiteSpace(filter) && !MatchesFilter(obj, entity, filter))
                        continue;

                    results.Add(FormatObject(obj, entity));
                }

                if (results.Count == 0)
                    return $"No records found for {entityName}" +
                        (string.IsNullOrWhiteSpace(filter) ? "." : $" with filter '{filter}'.");

                var sb = new StringBuilder();
                sb.AppendLine($"{entityName} — {results.Count} record(s):");
                foreach (var r in results)
                    sb.AppendLine(r);
                if (objects.Count > limit)
                    sb.AppendLine($"(Results capped at {limit}. Use a filter to narrow down.)");

                return sb.ToString();
            }
            catch (Exception ex)
            {
                return $"Error querying {entityName}: {ex.Message}";
            }
        }

        // ── Helpers ──────────────────────────────────────────────────────────

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

        private static string FormatObject(object obj, EntityInfo entity)
        {
            var parts = new List<string>();

            foreach (var prop in entity.Properties)
            {
                var clrProp = obj.GetType().GetProperty(prop.Name);
                if (clrProp == null) continue;
                var val = clrProp.GetValue(obj);
                parts.Add($"{prop.Name}: {FormatValue(val)}");
            }

            foreach (var rel in entity.Relationships.Where(r => !r.IsCollection))
            {
                var clrProp = obj.GetType().GetProperty(rel.PropertyName);
                if (clrProp == null) continue;
                var refObj = clrProp.GetValue(obj);
                if (refObj != null)
                    parts.Add($"{rel.PropertyName}: {GetDisplayText(refObj)}");
            }

            return "  • " + string.Join(" | ", parts);
        }

        private static string GetDisplayText(object obj)
        {
            if (obj == null) return "null";
            var type = obj.GetType();
            foreach (var name in new[] { "Name", "DisplayName", "Title", "FullName", "Description" })
            {
                var p = type.GetProperty(name);
                if (p != null)
                {
                    var v = p.GetValue(obj);
                    if (v != null) return v.ToString()!;
                }
            }
            return obj.ToString()!;
        }

        private static string FormatValue(object? val)
        {
            if (val == null) return "N/A";
            if (val is DateTime dt) return dt.ToString("yyyy-MM-dd HH:mm");
            if (val is decimal d) return d.ToString("F2");
            if (val is double db) return db.ToString("F2");
            if (val is float f) return f.ToString("F2");
            return val.ToString()!;
        }
    }
}
