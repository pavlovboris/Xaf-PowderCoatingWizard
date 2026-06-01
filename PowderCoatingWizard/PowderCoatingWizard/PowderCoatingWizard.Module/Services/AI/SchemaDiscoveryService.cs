using DevExpress.ExpressApp.DC;
using DevExpress.ExpressApp;
using PowderCoatingWizard.Module.Attributes;
using System.ComponentModel;
using DevExpress.Xpo.Metadata;
using DevExpress.Xpo;

namespace PowderCoatingWizard.Module.Services.AI
{
    /// <summary>
    /// Discovers persistent entity metadata at runtime via XAF <see cref="ITypesInfo"/>.
    /// Only classes decorated with <see cref="AIQueryableAttribute"/> are included.
    /// The result is cached after the first discovery call.
    /// </summary>
    public sealed class SchemaDiscoveryService
    {
        private readonly object _lock = new();
        private SchemaInfo? _cached;

        public SchemaInfo Schema
        {
            get
            {
                if (_cached != null) return _cached;
                lock (_lock)
                {
                    return _cached ??= Discover();
                }
            }
        }

        /// <summary>
        /// Generates a compact Markdown listing of all AI-queryable entities
        /// for inclusion in the system prompt.
        /// </summary>
        public string GenerateSystemPromptSection()
        {
            var schema = Schema;
            if (schema.Entities.Count == 0) return string.Empty;

            var sb = new System.Text.StringBuilder();
            sb.AppendLine("## Queryable Database Entities");
            sb.AppendLine("Use `get_database_insight` as the primary database evidence tool when live database facts may help answer the user's domain question.");
            sb.AppendLine("The database insight tool may generate and execute safe read-only SELECT statements internally, but generated SQL and raw results must not be exposed unless explicitly requested.");
            sb.AppendLine("Use `get_next_database_insight_page` only when the user explicitly asks for more rows/next page or confirms that additional pages are needed.");
            sb.AppendLine("Do not expose raw records, SQL, or tabular analysis unless explicitly requested.");
            sb.AppendLine();
            foreach (var e in schema.Entities)
            {
                var desc = string.IsNullOrWhiteSpace(e.Description) ? string.Empty : $" — {e.Description}";
                sb.AppendLine($"- **{e.Name}**{desc}");
            }
            return sb.ToString();
        }

        public string GenerateEnumPromptSection()
        {
            var schema = Schema;
            var enumProperties = schema.Entities
                .SelectMany(entity => entity.Properties
                    .Where(property => property.EnumValues.Count > 0)
                    .Select(property => new { Entity = entity, Property = property }))
                .ToList();

            if (enumProperties.Count == 0)
                return string.Empty;

            var sb = new System.Text.StringBuilder();
            sb.AppendLine("Enum value mappings for SQL result interpretation:");
            sb.AppendLine("When a SQL column stores one of these enum integer values, use the mapped enum name in the evidence summary.");
            sb.AppendLine();

            foreach (var item in enumProperties)
            {
                var tableName = string.IsNullOrWhiteSpace(item.Entity.TableName) ? item.Entity.Name : item.Entity.TableName;
                sb.AppendLine($"{tableName}.{item.Property.Name} ({item.Property.TypeName}): {string.Join(", ", item.Property.EnumValues)}");
            }

            return sb.ToString();
        }

        private static SchemaInfo Discover()
        {
            var entities = new List<EntityInfo>();

            foreach (var typeInfo in XafTypesInfo.Instance.PersistentTypes)
            {
                if (typeInfo.Type == null) continue;
                var attr = typeInfo.Type.GetCustomAttributes(typeof(AIQueryableAttribute), false)
                    .OfType<AIQueryableAttribute>()
                    .FirstOrDefault();
                if (attr == null) continue;

                var entityInfo = new EntityInfo
                {
                    Name = typeInfo.Type.Name,
                    ClrType = typeInfo.Type,
                    Description = attr.Description,
                    TableName = GetPersistentTableName(typeInfo.Type)
                };

                foreach (var member in typeInfo.Members)
                {
                    if (member.IsPublic == false) continue;
                    if (member.Name is "Oid" or "OptimisticLockField" or "GCRecord") continue;
                    if (member.MemberType == null) continue;

                    var memberType = member.MemberType;

                    // Detect collection / association
                    if (memberType.IsGenericType &&
                        (memberType.GetGenericTypeDefinition() == typeof(IList<>) ||
                         memberType.GetGenericTypeDefinition().Name.StartsWith("XPCollection")))
                    {
                        var targetType = memberType.GetGenericArguments().FirstOrDefault();
                        if (targetType != null)
                        {
                            entityInfo.Relationships.Add(new RelationshipInfo
                            {
                                PropertyName = member.Name,
                                TargetEntity = targetType.Name,
                                TargetClrType = targetType,
                                IsCollection = true
                            });
                        }
                        continue;
                    }

                    // Detect to-one reference (persistent class, not primitive)
                    if (!memberType.IsPrimitive && memberType != typeof(string) &&
                        memberType != typeof(DateTime) && memberType != typeof(Guid) &&
                        !memberType.IsValueType && memberType != typeof(object) &&
                        XafTypesInfo.Instance.FindTypeInfo(memberType)?.IsPersistent == true)
                    {
                        entityInfo.Relationships.Add(new RelationshipInfo
                        {
                            PropertyName = member.Name,
                            TargetEntity = memberType.Name,
                            TargetClrType = memberType,
                            IsCollection = false
                        });
                        continue;
                    }

                    // Scalar property
                    var propInfo = new PropertyInfo
                    {
                        Name = member.Name,
                        ClrType = memberType,
                        TypeName = GetFriendlyTypeName(memberType),
                        IsRequired = member.FindAttribute<DevExpress.Persistent.Validation.RuleRequiredFieldAttribute>() != null
                    };

                    // Collect enum values
                    if (memberType.IsEnum)
                    {
                        foreach (var value in Enum.GetValues(memberType))
                        {
                            var numericValue = Convert.ToInt64(value);
                            propInfo.EnumValues.Add($"{numericValue}={value}");
                        }
                    }

                    // Description from ToolTip attribute (XAF) or standard DescriptionAttribute.
                    // ToolTipAttribute stores its text in the first constructor argument;
                    // access it via CustomAttributeData to avoid a direct type reference.
                    var toolTipData = typeInfo.Type
                        .GetProperty(member.Name)?
                        .GetCustomAttributesData()
                        .FirstOrDefault(a => a.AttributeType.Name == "ToolTipAttribute");
                    if (toolTipData?.ConstructorArguments.Count > 0)
                    {
                        propInfo.Description = toolTipData.ConstructorArguments[0].Value?.ToString() ?? string.Empty;
                    }
                    else
                    {
                        var desc = member.FindAttribute<DescriptionAttribute>();
                        if (desc != null) propInfo.Description = desc.Description;
                    }

                    entityInfo.Properties.Add(propInfo);
                }

                entities.Add(entityInfo);
            }

            return new SchemaInfo { Entities = entities };
        }

        private static string GetFriendlyTypeName(Type t)
        {
            if (t == typeof(string)) return "string";
            if (t == typeof(int)) return "int";
            if (t == typeof(long)) return "long";
            if (t == typeof(double) || t == typeof(float) || t == typeof(decimal)) return "decimal";
            if (t == typeof(bool)) return "bool";
            if (t == typeof(DateTime)) return "DateTime";
            if (t == typeof(Guid)) return "Guid";
            if (t.IsEnum) return $"enum({t.Name})";
            return t.Name;
        }

        private static string GetPersistentTableName(Type type)
        {
            try
            {
                return XpoDefault.Session?.GetClassInfo(type)?.TableName ?? type.FullName ?? type.Name;
            }
            catch
            {
                return type.FullName ?? type.Name;
            }
        }
    }

    // ── Data transfer objects ────────────────────────────────────────────────

    public sealed class SchemaInfo
    {
        public List<EntityInfo> Entities { get; set; } = [];

        public EntityInfo? FindEntity(string name) =>
            Entities.FirstOrDefault(e => e.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
    }

    public sealed class EntityInfo
    {
        public string Name { get; set; } = string.Empty;
        public Type ClrType { get; set; } = typeof(object);
        public string Description { get; set; } = string.Empty;
        public string TableName { get; set; } = string.Empty;
        public List<PropertyInfo> Properties { get; set; } = [];
        public List<RelationshipInfo> Relationships { get; set; } = [];
    }

    public sealed class PropertyInfo
    {
        public string Name { get; set; } = string.Empty;
        public Type ClrType { get; set; } = typeof(object);
        public string TypeName { get; set; } = string.Empty;
        public bool IsRequired { get; set; }
        public string Description { get; set; } = string.Empty;
        public List<string> EnumValues { get; set; } = [];
    }

    public sealed class RelationshipInfo
    {
        public string PropertyName { get; set; } = string.Empty;
        public string TargetEntity { get; set; } = string.Empty;
        public Type TargetClrType { get; set; } = typeof(object);
        public bool IsCollection { get; set; }
    }
}
