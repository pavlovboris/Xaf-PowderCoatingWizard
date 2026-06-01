using System.ComponentModel;
using System.Text;

namespace PowderCoatingWizard.Module.Services.AI
{
    /// <summary>
    /// On-demand enum mapping lookup for database results that contain integer enum values.
    /// </summary>
    public sealed class EnumLookupToolService
    {
        private readonly SchemaDiscoveryService _schema;

        public EnumLookupToolService(SchemaDiscoveryService schema)
        {
            _schema = schema;
        }

        [Description(
            "Returns enum integer-to-name mappings for AI-queryable database entities. " +
            "Use only when database evidence contains enum integer values that need to be decoded for the final answer.")]
        public string GetEnumMappings(
            [Description("Optional entity name, for example LineCertification. Leave empty to search all AI-queryable entities.")] string entityName = "",
            [Description("Optional property/column name, for example Status or Standard. Leave empty to include all enum properties for the entity.")] string propertyName = "")
        {
            var entities = string.IsNullOrWhiteSpace(entityName)
                ? _schema.Schema.Entities
                : _schema.Schema.Entities
                    .Where(e => e.Name.Equals(entityName, StringComparison.OrdinalIgnoreCase) ||
                                e.TableName.Equals(entityName, StringComparison.OrdinalIgnoreCase) ||
                                e.TableName.EndsWith("." + entityName, StringComparison.OrdinalIgnoreCase))
                    .ToList();

            var sb = new StringBuilder();
            sb.AppendLine("Enum mappings for database result interpretation:");

            int count = 0;
            foreach (var entity in entities)
            {
                var properties = entity.Properties.Where(p => p.EnumValues.Count > 0);
                if (!string.IsNullOrWhiteSpace(propertyName))
                {
                    properties = properties.Where(p => p.Name.Equals(propertyName, StringComparison.OrdinalIgnoreCase));
                }

                foreach (var property in properties)
                {
                    count++;
                    var tableName = string.IsNullOrWhiteSpace(entity.TableName) ? entity.Name : entity.TableName;
                    sb.AppendLine($"- {tableName}.{property.Name} ({property.TypeName}): {string.Join(", ", property.EnumValues)}");
                }
            }

            if (count == 0)
                return "No enum mappings found for the requested entity/property.";

            return sb.ToString();
        }
    }
}
