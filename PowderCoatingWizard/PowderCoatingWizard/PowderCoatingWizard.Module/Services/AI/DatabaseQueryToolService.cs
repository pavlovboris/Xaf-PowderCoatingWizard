using DevExpress.ExpressApp;
using DevExpress.Data.Filtering;
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

        [Description("Returns the list of XAF application entities available to the assistant. Use for application-model/entity discovery before selecting a database evidence tool; do not use for aggregates, trends, or analytical answers.")]
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

        [Description("Returns XAF application-model details for a single entity: properties, types, relationships, and enum values. Use before querying unfamiliar entities or when display/object semantics matter; use get_database_insight for set-based SQL evidence.")]
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

        [Description("Queries a small XAF ObjectSpace-level sample of records for an available entity. Use for application-level object semantics, display values, relationships, enum/display formatting, or small examples. Supports semicolon-separated safe filters: Property=Value (contains/default), Property==Value, Property!=Value, Property>=Value, Property<=Value, Property>Value, Property<Value, Property~Value, PropertyFrom=yyyy-MM-dd, PropertyTo=yyyy-MM-dd, PropertyBetween=A..B, PropertyIn=A,B, PropertyAny=A,B, Property=null, Property!=null, safe reference paths like Stage.Name~Degreaser, and SortBy=Property or SortBy=-Property. Do not use for counts, aggregates, broad filtering, joins, trends, comparisons, or analytical summaries; use get_database_insight for those.")]
        public string QueryEntity(
            [Description("Entity name to query. Use list_entities to see available names.")] string entityName,
            [Description("Optional semicolon-separated filters. Examples: 'IsActive==True;Name~Zinc;CreatedOnBetween=2025-01-01..2025-12-31;StatusIn=Warning,Alarm;Stage.Name~Degreaser;SortBy=-Name'. Omit for no filter.")] string filter = "",
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
                var queryOptions = ParseQueryOptions(filter, entity, _schema.Schema);
                var objects = _os.GetObjects(entity.ClrType, queryOptions.Criteria).Cast<object>();
                objects = ApplySort(objects, queryOptions.SortProperty, queryOptions.SortDescending);
                var results = new List<string>();
                var matchedCount = 0;

                foreach (var obj in objects)
                {
                    matchedCount++;
                    if (results.Count < limit)
                        results.Add(FormatObject(obj, entity));
                }

                if (results.Count == 0)
                    return $"No records found for {entityName}" +
                        (string.IsNullOrWhiteSpace(filter) ? "." : $" with filter '{filter}'.");

                var sb = new StringBuilder();
                sb.AppendLine($"{entityName} — {results.Count} of {matchedCount} matching record(s):");
                foreach (var r in results)
                    sb.AppendLine(r);
                if (matchedCount > limit)
                    sb.AppendLine($"(Results capped at {limit}. Use a filter to narrow down.)");
                if (queryOptions.IgnoredSegments.Count > 0)
                    sb.AppendLine($"(Ignored unsupported filter segment(s): {string.Join("; ", queryOptions.IgnoredSegments)})");

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

        private static QueryOptions ParseQueryOptions(string filter, EntityInfo entity, SchemaInfo schema)
        {
            var options = new QueryOptions();
            var criteria = new List<CriteriaOperator>();
            foreach (var segment in (filter ?? string.Empty).Split(';', StringSplitOptions.RemoveEmptyEntries))
            {
                var trimmed = segment.Trim();
                if (string.IsNullOrWhiteSpace(trimmed)) continue;

                var eq = trimmed.IndexOf('=');
                var tilde = trimmed.IndexOf('~');
                if (eq < 0 && tilde < 0)
                {
                    options.IgnoredSegments.Add(trimmed);
                    continue;
                }

                var propName = trimmed[..(eq >= 0 ? eq : tilde)].Trim();
                var filterValue = trimmed[((eq >= 0 ? eq : tilde) + 1)..].Trim();
                if (!propName.Equals("SortBy", StringComparison.OrdinalIgnoreCase))
                {
                    var criterion = BuildCriterion(trimmed, entity, schema);
                    if (criterion != null)
                        criteria.Add(criterion);
                    else
                        options.IgnoredSegments.Add(trimmed);
                    continue;
                }

                options.SortDescending = filterValue.StartsWith("-", StringComparison.Ordinal);
                var sortProperty = options.SortDescending ? filterValue[1..].Trim() : filterValue;
                if (TryResolvePropertyPath(sortProperty, entity, schema, out var sortPath, out _))
                {
                    options.SortProperty = sortPath;
                }
                else if (entity.Properties.Any(p => p.Name.Equals(sortProperty, StringComparison.OrdinalIgnoreCase)))
                    options.SortProperty = sortProperty;
            }

            options.Criteria = criteria.Count switch
            {
                0 => null,
                1 => criteria[0],
                _ => CriteriaOperator.And(criteria)
            };

            return options;
        }

        private static CriteriaOperator? BuildCriterion(string segment, EntityInfo entity, SchemaInfo schema)
        {
            var operators = new[] { "!=", ">=", "<=", "==", "~", ">", "<", "=" };
            var op = operators.FirstOrDefault(segment.Contains);
            if (op == null) return null;

            var split = segment.Split([op], 2, StringSplitOptions.None);
            if (split.Length != 2) return null;

            var propertyName = split[0].Trim();
            var valueText = split[1].Trim();
            var rangeMode = string.Empty;
            var setMode = string.Empty;
            if (propertyName.EndsWith("From", StringComparison.OrdinalIgnoreCase))
            {
                rangeMode = "from";
                propertyName = propertyName[..^4];
            }
            else if (propertyName.EndsWith("To", StringComparison.OrdinalIgnoreCase))
            {
                rangeMode = "to";
                propertyName = propertyName[..^2];
            }
            else if (propertyName.EndsWith("Between", StringComparison.OrdinalIgnoreCase))
            {
                rangeMode = "between";
                propertyName = propertyName[..^7];
            }
            else if (propertyName.EndsWith("In", StringComparison.OrdinalIgnoreCase))
            {
                setMode = "in";
                propertyName = propertyName[..^2];
            }
            else if (propertyName.EndsWith("Any", StringComparison.OrdinalIgnoreCase))
            {
                setMode = "any";
                propertyName = propertyName[..^3];
            }

            if (!TryResolvePropertyPath(propertyName, entity, schema, out var criteriaPath, out var property))
                return null;
            if (property == null || string.IsNullOrWhiteSpace(valueText)) return null;

            if (IsNullToken(valueText))
            {
                var isNull = new FunctionOperator(FunctionOperatorType.IsNull, new OperandProperty(criteriaPath));
                return op == "!="
                    ? new UnaryOperator(UnaryOperatorType.Not, isNull)
                    : isNull;
            }

            if (rangeMode == "between")
            {
                var rangeParts = valueText.Split("..", 2, StringSplitOptions.TrimEntries);
                if (rangeParts.Length != 2 || string.IsNullOrWhiteSpace(rangeParts[0]) || string.IsNullOrWhiteSpace(rangeParts[1]))
                    return null;

                return new BetweenOperator(criteriaPath, ConvertFilterValue(rangeParts[0], property), ConvertFilterValue(rangeParts[1], property));
            }

            if (setMode == "in")
            {
                var values = SplitListValues(valueText).Select(v => ConvertFilterValue(v, property)).ToList();
                return values.Count == 0 ? null : new InOperator(criteriaPath, values);
            }

            if (setMode == "any")
            {
                var values = SplitListValues(valueText)
                    .Select(v => BuildContainsCriterion(criteriaPath, v))
                    .Cast<CriteriaOperator>()
                    .ToList();
                return values.Count switch
                {
                    0 => null,
                    1 => values[0],
                    _ => CriteriaOperator.Or(values)
                };
            }

            if (rangeMode == "from")
                return new BinaryOperator(criteriaPath, ConvertFilterValue(valueText, property), BinaryOperatorType.GreaterOrEqual);
            if (rangeMode == "to")
                return new BinaryOperator(criteriaPath, ConvertFilterValue(valueText, property), BinaryOperatorType.LessOrEqual);

            return op switch
            {
                "==" => new BinaryOperator(criteriaPath, ConvertFilterValue(valueText, property), BinaryOperatorType.Equal),
                "!=" => new BinaryOperator(criteriaPath, ConvertFilterValue(valueText, property), BinaryOperatorType.NotEqual),
                ">=" => new BinaryOperator(criteriaPath, ConvertFilterValue(valueText, property), BinaryOperatorType.GreaterOrEqual),
                "<=" => new BinaryOperator(criteriaPath, ConvertFilterValue(valueText, property), BinaryOperatorType.LessOrEqual),
                ">" => new BinaryOperator(criteriaPath, ConvertFilterValue(valueText, property), BinaryOperatorType.Greater),
                "<" => new BinaryOperator(criteriaPath, ConvertFilterValue(valueText, property), BinaryOperatorType.Less),
                "~" or "=" => BuildContainsCriterion(criteriaPath, valueText),
                _ => null
            };
        }

        private static bool TryResolvePropertyPath(string path, EntityInfo entity, SchemaInfo schema, out string criteriaPath, out PropertyInfo? property)
        {
            criteriaPath = string.Empty;
            property = null;
            if (string.IsNullOrWhiteSpace(path)) return false;

            var parts = path.Split('.', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            if (parts.Length == 1)
            {
                property = entity.Properties.FirstOrDefault(p => p.Name.Equals(parts[0], StringComparison.OrdinalIgnoreCase));
                if (property == null) return false;
                criteriaPath = property.Name;
                return true;
            }

            if (parts.Length is < 2 or > 3)
                return false;

            var currentEntity = entity;
            var resolvedParts = new List<string>();
            for (var i = 0; i < parts.Length - 1; i++)
            {
                var relationship = currentEntity.Relationships.FirstOrDefault(r =>
                    !r.IsCollection && r.PropertyName.Equals(parts[i], StringComparison.OrdinalIgnoreCase));
                if (relationship == null) return false;

                resolvedParts.Add(relationship.PropertyName);
                var nextEntity = schema.FindEntity(relationship.TargetEntity);
                if (nextEntity == null) return false;
                currentEntity = nextEntity;
            }

            property = currentEntity.Properties.FirstOrDefault(p => p.Name.Equals(parts[^1], StringComparison.OrdinalIgnoreCase));
            if (property == null) return false;

            resolvedParts.Add(property.Name);
            criteriaPath = string.Join('.', resolvedParts);
            return true;
        }

        private static IEnumerable<string> SplitListValues(string valueText) =>
            valueText.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Where(v => !string.IsNullOrWhiteSpace(v));

        private static bool IsNullToken(string valueText) =>
            valueText.Equals("null", StringComparison.OrdinalIgnoreCase) ||
            valueText.Equals("isnull", StringComparison.OrdinalIgnoreCase);

        private static CriteriaOperator BuildContainsCriterion(string propertyName, string valueText) =>
            new FunctionOperator(FunctionOperatorType.Contains, new OperandProperty(propertyName), new OperandValue(valueText));

        private static object ConvertFilterValue(string valueText, PropertyInfo property)
        {
            var typeName = property.TypeName;
            if (typeName.Equals("Boolean", StringComparison.OrdinalIgnoreCase) || typeName.Equals("bool", StringComparison.OrdinalIgnoreCase))
                return bool.Parse(valueText);
            if (typeName.Equals("Int32", StringComparison.OrdinalIgnoreCase) || typeName.Equals("int", StringComparison.OrdinalIgnoreCase))
                return int.Parse(valueText);
            if (typeName.Equals("Int64", StringComparison.OrdinalIgnoreCase) || typeName.Equals("long", StringComparison.OrdinalIgnoreCase))
                return long.Parse(valueText);
            if (typeName.Equals("Decimal", StringComparison.OrdinalIgnoreCase))
                return decimal.Parse(valueText);
            if (typeName.Equals("Double", StringComparison.OrdinalIgnoreCase))
                return double.Parse(valueText);
            if (typeName.Equals("Single", StringComparison.OrdinalIgnoreCase) || typeName.Equals("float", StringComparison.OrdinalIgnoreCase))
                return float.Parse(valueText);
            if (typeName.Equals("DateTime", StringComparison.OrdinalIgnoreCase))
                return DateTime.Parse(valueText);

            return valueText;
        }

        private static IEnumerable<object> ApplySort(IEnumerable<object> objects, string? sortProperty, bool descending)
        {
            if (string.IsNullOrWhiteSpace(sortProperty))
                return objects;

            object? GetValue(object obj) => obj.GetType().GetProperty(sortProperty)?.GetValue(obj);
            return descending
                ? objects.OrderByDescending(GetValue)
                : objects.OrderBy(GetValue);
        }

        private sealed class QueryOptions
        {
            public CriteriaOperator? Criteria { get; set; }
            public List<string> IgnoredSegments { get; } = [];
            public string? SortProperty { get; set; }
            public bool SortDescending { get; set; }
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
