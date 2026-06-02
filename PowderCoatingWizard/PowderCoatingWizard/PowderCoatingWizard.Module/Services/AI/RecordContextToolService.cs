using DevExpress.ExpressApp;
using DevExpress.Persistent.Base;
using System.ComponentModel;
using System.Text;

namespace PowderCoatingWizard.Module.Services.AI
{
    /// <summary>
    /// Provides compact XAF ObjectSpace-level context for one specific business object.
    /// </summary>
    public sealed class RecordContextToolService
    {
        private readonly IObjectSpace _os;
        private readonly SchemaDiscoveryService _schema;
        private readonly int _maxFields;
        private readonly int _maxRelationships;

        public RecordContextToolService(IObjectSpace os, SchemaDiscoveryService schema, int maxFields = 16, int maxRelationships = 8)
        {
            _os = os;
            _schema = schema;
            _maxFields = Math.Clamp(maxFields, 4, 40);
            _maxRelationships = Math.Clamp(maxRelationships, 2, 20);
        }

        [Description("Returns compact XAF ObjectSpace-level context for a specific record, including scalar values, display text, enum/display formatting, and reference relationships. Use when the user asks about one known object or when application-level object semantics matter.")]
        public string GetRecordContext(
            [Description("AI-queryable entity name, for example LineStage, ParameterMeasurement, ParameterThreshold, ChemicalProduct, or AnalysisRecord.")] string entityName,
            [Description("Object key/Oid value as text. Use get_current_context first when the user refers to the current or selected object.")] string objectKey,
            [Description("True to include reference relationship display values. Default true.")] bool includeRelationships = true)
        {
            if (string.IsNullOrWhiteSpace(entityName))
                return $"Record context skipped: entity name is required. Available entities: {GetEntityNameList()}.";

            if (string.IsNullOrWhiteSpace(objectKey))
                return "Record context skipped: object key is required.";

            var entity = _schema.Schema.FindEntity(entityName);
            if (entity == null)
                return $"Record context skipped: entity '{entityName}' is not available. Available entities: {GetEntityNameList()}.";

            try
            {
                var key = ConvertKey(objectKey.Trim(), entity.ClrType);
                var obj = _os.GetObjectByKey(entity.ClrType, key);
                if (obj == null)
                    return $"Record context skipped: {entity.Name} record with key '{objectKey}' was not found in the current XAF ObjectSpace.";

                var sb = new StringBuilder();
                sb.AppendLine("Record context for internal assistant reasoning.");
                sb.AppendLine("Source: XAF ObjectSpace. Use this for application-level display values and object semantics.");
                sb.AppendLine($"Entity: {entity.Name}");
                sb.AppendLine($"Key: {objectKey.Trim()}");
                sb.AppendLine($"Display: {Safe(GetDisplayText(obj))}");
                sb.AppendLine();

                sb.AppendLine("Properties:");
                foreach (var prop in entity.Properties.Take(_maxFields))
                {
                    var clrProp = obj.GetType().GetProperty(prop.Name);
                    if (clrProp == null) continue;

                    var val = clrProp.GetValue(obj);
                    sb.AppendLine($"- {prop.Name}: {Safe(FormatValue(val))}");
                }

                if (includeRelationships)
                {
                    var relationships = entity.Relationships.Where(r => !r.IsCollection).Take(_maxRelationships).ToList();
                    if (relationships.Count > 0)
                    {
                        sb.AppendLine();
                        sb.AppendLine("Reference relationships:");
                        foreach (var rel in relationships)
                        {
                            var clrProp = obj.GetType().GetProperty(rel.PropertyName);
                            if (clrProp == null) continue;

                            var refObj = clrProp.GetValue(obj);
                            sb.AppendLine($"- {rel.PropertyName} ({rel.TargetEntity}): {Safe(refObj == null ? "N/A" : GetDisplayText(refObj))}");
                        }
                    }
                }

                return sb.ToString();
            }
            catch (Exception ex)
            {
                Tracing.Tracer.LogError(ex);
                return $"Record context failed safely for {entity.Name}: {ex.Message}";
            }
        }

        private string GetEntityNameList() =>
            string.Join(", ", _schema.Schema.Entities.Select(e => e.Name));

        private static object ConvertKey(string keyText, Type entityType)
        {
            var keyProperty = entityType.GetProperty("Oid") ?? entityType.GetProperty("ID") ?? entityType.GetProperty("Id");
            var keyType = Nullable.GetUnderlyingType(keyProperty?.PropertyType ?? typeof(Guid)) ?? keyProperty?.PropertyType ?? typeof(Guid);

            if (keyType == typeof(Guid)) return Guid.Parse(keyText);
            if (keyType == typeof(int)) return int.Parse(keyText);
            if (keyType == typeof(long)) return long.Parse(keyText);
            if (keyType == typeof(string)) return keyText;

            return Convert.ChangeType(keyText, keyType);
        }

        private static string GetDisplayText(object obj)
        {
            var type = obj.GetType();
            foreach (var name in new[] { "DisplayName", "Name", "Title", "FullName", "Description" })
            {
                var p = type.GetProperty(name);
                if (p == null) continue;

                var v = p.GetValue(obj);
                if (v != null) return v.ToString()!;
            }

            return obj.ToString() ?? type.Name;
        }

        private static string FormatValue(object? value)
        {
            if (value == null) return "N/A";
            if (value is DateTime dt) return dt.ToString("yyyy-MM-dd HH:mm");
            if (value is decimal d) return d.ToString("G6");
            if (value is double db) return db.ToString("G6");
            if (value is float f) return f.ToString("G6");
            return value.ToString() ?? string.Empty;
        }

        private static string Safe(string? value)
        {
            var text = value?.Replace("\r", " ").Replace("\n", " ").Trim() ?? string.Empty;
            return text.Length <= 300 ? text : text[..300] + "...";
        }
    }
}
