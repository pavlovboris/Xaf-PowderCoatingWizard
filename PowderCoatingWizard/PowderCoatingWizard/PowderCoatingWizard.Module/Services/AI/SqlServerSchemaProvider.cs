using DevExpress.Persistent.Base;
using Microsoft.Data.SqlClient;
using System.Text;

namespace PowderCoatingWizard.Module.Services.AI
{
    /// <summary>
    /// Read-only SQL Server catalog metadata provider for internal AI database context.
    /// It does not execute user data queries and fails closed when the connection or table match is unavailable.
    /// </summary>
    public sealed class SqlServerSchemaProvider
    {
        private readonly string? _connectionString;
        private readonly HashSet<string> _allowedTableNames;
        private readonly object _lock = new();
        private Dictionary<string, SqlTableSchema>? _cache;

        public SqlServerSchemaProvider(string? connectionString, IEnumerable<string> allowedEntityNames)
        {
            _connectionString = SqlClientConnectionStringSanitizer.Sanitize(connectionString);
            _allowedTableNames = allowedEntityNames
                .Where(n => !string.IsNullOrWhiteSpace(n))
                .Select(NormalizeName)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
        }

        public string GetTableSummary(string entityName)
        {
            if (string.IsNullOrWhiteSpace(entityName) || string.IsNullOrWhiteSpace(_connectionString))
                return string.Empty;

            try
            {
                var schema = LoadSchema();
                var normalized = NormalizeName(entityName);
                var table = schema.Values.FirstOrDefault(t => NormalizeName(t.TableName) == normalized);
                if (table == null)
                    return string.Empty;

                var sb = new StringBuilder();
                sb.AppendLine("SQL Server schema context for internal assistant reasoning only:");
                sb.AppendLine($"Table: {table.SchemaName}.{table.TableName}");
                if (!string.IsNullOrWhiteSpace(table.Description))
                    sb.AppendLine($"Description: {table.Description}");

                var primaryKeys = table.Columns.Where(c => c.IsPrimaryKey).Select(c => c.Name).ToList();
                if (primaryKeys.Count > 0)
                    sb.AppendLine($"Primary key: {string.Join(", ", primaryKeys)}");

                var identityColumns = table.Columns.Where(c => c.IsIdentity).Select(c => c.Name).ToList();
                if (identityColumns.Count > 0)
                    sb.AppendLine($"Identity: {string.Join(", ", identityColumns)}");

                if (HasXpoSoftDeleteColumn(table))
                    sb.AppendLine("XPO soft delete: exclude deleted records with GCRecord IS NULL.");

                sb.AppendLine("Columns:");
                foreach (var column in table.Columns.OrderBy(c => c.Ordinal))
                {
                    var nullable = column.IsNullable ? "NULL" : "NOT NULL";
                    var flags = new List<string>();
                    if (column.IsPrimaryKey) flags.Add("PK");
                    if (column.IsIdentity) flags.Add("identity");
                    var flagText = flags.Count > 0 ? $" [{string.Join(", ", flags)}]" : string.Empty;
                    var desc = string.IsNullOrWhiteSpace(column.Description) ? string.Empty : $" — {column.Description}";
                    sb.AppendLine($"- {column.Name}: {column.DataType} {nullable}{flagText}{desc}");
                }

                if (table.ForeignKeys.Count > 0)
                {
                    sb.AppendLine("Foreign keys:");
                    foreach (var fk in table.ForeignKeys)
                        sb.AppendLine($"- {fk.ColumnName} -> {fk.ReferencedSchema}.{fk.ReferencedTable}.{fk.ReferencedColumn} ({fk.Name})");
                }

                return sb.ToString();
            }
            catch (Exception ex)
            {
                Tracing.Tracer.LogError(ex);
                return string.Empty;
            }
        }

        public string GetSchemaSummary()
        {
            if (string.IsNullOrWhiteSpace(_connectionString))
                return string.Empty;

            try
            {
                var schema = LoadSchema();
                if (schema.Count == 0)
                    return string.Empty;

                var sb = new StringBuilder();
                sb.AppendLine("Allowed SQL Server schema for internal database reasoning:");
                sb.AppendLine("Only generate SELECT statements against the tables and columns listed below.");
                sb.AppendLine("Use LIKE for text search unless exact matching is clearly required.");
                sb.AppendLine("For tables with XPO soft delete, always exclude deleted records with GCRecord IS NULL.");
                sb.AppendLine();

                foreach (var table in schema.Values.OrderBy(t => t.SchemaName).ThenBy(t => t.TableName))
                {
                    sb.AppendLine($"Table: {table.SchemaName}.{table.TableName}");
                    if (!string.IsNullOrWhiteSpace(table.Description))
                        sb.AppendLine($"Description: {table.Description}");

                    var columns = table.Columns
                        .OrderBy(c => c.Ordinal)
                        .Select(FormatColumnForSummary);
                    sb.AppendLine($"Columns: {string.Join(", ", columns)}");

                    if (HasXpoSoftDeleteColumn(table))
                        sb.AppendLine("XPO soft delete: add GCRecord IS NULL for this table.");

                    if (table.ForeignKeys.Count > 0)
                    {
                        var fks = table.ForeignKeys
                            .Select(fk => $"{fk.ColumnName}->{fk.ReferencedSchema}.{fk.ReferencedTable}.{fk.ReferencedColumn}");
                        sb.AppendLine($"Foreign keys: {string.Join(", ", fks)}");
                    }

                    sb.AppendLine();
                }

                return sb.ToString();
            }
            catch (Exception ex)
            {
                Tracing.Tracer.LogError(ex);
                return string.Empty;
            }
        }

        private Dictionary<string, SqlTableSchema> LoadSchema()
        {
            if (_cache != null) return _cache;
            lock (_lock)
            {
                if (_cache != null) return _cache;
                return _cache = ReadSchema();
            }
        }

        private Dictionary<string, SqlTableSchema> ReadSchema()
        {
            var tables = new Dictionary<int, SqlTableSchema>();
            var allTables = new Dictionary<int, SqlTableSchema>();
            var skippedTableNames = new List<string>();

            using var connection = new SqlConnection(_connectionString);
            connection.Open();

            using (var command = connection.CreateCommand())
            {
                command.CommandText = @"
SELECT
    t.object_id,
    s.name AS schema_name,
    t.name AS table_name,
    CAST(ep.value AS nvarchar(max)) AS description
FROM sys.tables t
INNER JOIN sys.schemas s ON s.schema_id = t.schema_id
LEFT JOIN sys.extended_properties ep
    ON ep.major_id = t.object_id
   AND ep.minor_id = 0
   AND ep.name = N'MS_Description'
WHERE t.is_ms_shipped = 0
ORDER BY s.name, t.name;";

                using var reader = command.ExecuteReader();
                while (reader.Read())
                {
                    var tableName = reader.GetString(reader.GetOrdinal("table_name"));
                    var objectId = reader.GetInt32(reader.GetOrdinal("object_id"));

                    var table = new SqlTableSchema
                    {
                        ObjectId = objectId,
                        SchemaName = reader.GetString(reader.GetOrdinal("schema_name")),
                        TableName = tableName,
                        Description = reader.IsDBNull(reader.GetOrdinal("description")) ? string.Empty : reader.GetString(reader.GetOrdinal("description"))
                    };

                    allTables[objectId] = table;
                    if (IsAllowedTable(tableName))
                        tables[objectId] = table;
                    else
                        skippedTableNames.Add(tableName);
                }
            }

            if (tables.Count == 0)
            {
                Tracing.Tracer.LogText("DBCHAT_SCHEMA No SQL tables matched the AI-queryable allowlist.");
                Tracing.Tracer.LogText($"DBCHAT_SCHEMA Allowlist={string.Join(", ", _allowedTableNames.Take(50))}");
                Tracing.Tracer.LogText($"DBCHAT_SCHEMA AvailableTables={string.Join(", ", allTables.Values.Select(t => t.TableName).Take(100))}");
                return [];
            }
            else if (skippedTableNames.Count > 0)
            {
                Tracing.Tracer.LogText($"DBCHAT_SCHEMA Matched {tables.Count} table(s); skipped {skippedTableNames.Count} non-allowlisted table(s).");
            }

            ReadColumns(connection, tables);
            ReadPrimaryKeys(connection, tables);
            ReadForeignKeys(connection, tables);

            return tables.Values.ToDictionary(t => NormalizeName(t.TableName), StringComparer.OrdinalIgnoreCase);
        }

        private static void ReadColumns(SqlConnection connection, Dictionary<int, SqlTableSchema> tables)
        {
            using var command = connection.CreateCommand();
            command.CommandText = @"
SELECT
    c.object_id,
    c.column_id,
    c.name AS column_name,
    typ.name AS type_name,
    c.max_length,
    c.precision,
    c.scale,
    c.is_nullable,
    c.is_identity,
    CAST(ep.value AS nvarchar(max)) AS description
FROM sys.columns c
INNER JOIN sys.types typ ON typ.user_type_id = c.user_type_id
LEFT JOIN sys.extended_properties ep
    ON ep.major_id = c.object_id
   AND ep.minor_id = c.column_id
   AND ep.name = N'MS_Description'
ORDER BY c.object_id, c.column_id;";

            using var reader = command.ExecuteReader();
            while (reader.Read())
            {
                var objectId = reader.GetInt32(reader.GetOrdinal("object_id"));
                if (!tables.TryGetValue(objectId, out var table))
                    continue;

                table.Columns.Add(new SqlColumnSchema
                {
                    Name = reader.GetString(reader.GetOrdinal("column_name")),
                    Ordinal = reader.GetInt32(reader.GetOrdinal("column_id")),
                    DataType = FormatDataType(reader),
                    IsNullable = reader.GetBoolean(reader.GetOrdinal("is_nullable")),
                    IsIdentity = reader.GetBoolean(reader.GetOrdinal("is_identity")),
                    Description = reader.IsDBNull(reader.GetOrdinal("description")) ? string.Empty : reader.GetString(reader.GetOrdinal("description"))
                });
            }
        }

        private static void ReadPrimaryKeys(SqlConnection connection, Dictionary<int, SqlTableSchema> tables)
        {
            using var command = connection.CreateCommand();
            command.CommandText = @"
SELECT ic.object_id, c.name AS column_name
FROM sys.indexes i
INNER JOIN sys.index_columns ic
    ON ic.object_id = i.object_id
   AND ic.index_id = i.index_id
INNER JOIN sys.columns c
    ON c.object_id = ic.object_id
   AND c.column_id = ic.column_id
WHERE i.is_primary_key = 1;";

            using var reader = command.ExecuteReader();
            while (reader.Read())
            {
                var objectId = reader.GetInt32(reader.GetOrdinal("object_id"));
                if (!tables.TryGetValue(objectId, out var table))
                    continue;

                var columnName = reader.GetString(reader.GetOrdinal("column_name"));
                var column = table.Columns.FirstOrDefault(c => c.Name.Equals(columnName, StringComparison.OrdinalIgnoreCase));
                if (column != null)
                    column.IsPrimaryKey = true;
            }
        }

        private static void ReadForeignKeys(SqlConnection connection, Dictionary<int, SqlTableSchema> tables)
        {
            using var command = connection.CreateCommand();
            command.CommandText = @"
SELECT
    fk.name AS fk_name,
    parent.object_id AS parent_object_id,
    pc.name AS parent_column,
    rs.name AS referenced_schema,
    rt.name AS referenced_table,
    rc.name AS referenced_column
FROM sys.foreign_keys fk
INNER JOIN sys.foreign_key_columns fkc ON fkc.constraint_object_id = fk.object_id
INNER JOIN sys.tables parent ON parent.object_id = fk.parent_object_id
INNER JOIN sys.columns pc ON pc.object_id = parent.object_id AND pc.column_id = fkc.parent_column_id
INNER JOIN sys.tables rt ON rt.object_id = fk.referenced_object_id
INNER JOIN sys.schemas rs ON rs.schema_id = rt.schema_id
INNER JOIN sys.columns rc ON rc.object_id = rt.object_id AND rc.column_id = fkc.referenced_column_id;";

            using var reader = command.ExecuteReader();
            while (reader.Read())
            {
                var objectId = reader.GetInt32(reader.GetOrdinal("parent_object_id"));
                if (!tables.TryGetValue(objectId, out var table))
                    continue;

                table.ForeignKeys.Add(new SqlForeignKeySchema
                {
                    Name = reader.GetString(reader.GetOrdinal("fk_name")),
                    ColumnName = reader.GetString(reader.GetOrdinal("parent_column")),
                    ReferencedSchema = reader.GetString(reader.GetOrdinal("referenced_schema")),
                    ReferencedTable = reader.GetString(reader.GetOrdinal("referenced_table")),
                    ReferencedColumn = reader.GetString(reader.GetOrdinal("referenced_column"))
                });
            }
        }

        private bool IsAllowedTable(string tableName)
        {
            var normalized = NormalizeName(tableName);
            return _allowedTableNames.Contains(normalized) ||
                _allowedTableNames.Any(allowed =>
                    normalized.EndsWith(allowed, StringComparison.OrdinalIgnoreCase) ||
                    allowed.EndsWith(normalized, StringComparison.OrdinalIgnoreCase));
        }

        private static string NormalizeName(string name) =>
            new(name.Where(char.IsLetterOrDigit).Select(char.ToLowerInvariant).ToArray());

        private static string FormatDataType(SqlDataReader reader)
        {
            var typeName = reader.GetString(reader.GetOrdinal("type_name"));
            var maxLength = reader.GetInt16(reader.GetOrdinal("max_length"));
            var precision = reader.GetByte(reader.GetOrdinal("precision"));
            var scale = reader.GetByte(reader.GetOrdinal("scale"));

            return typeName switch
            {
                "varchar" or "char" or "varbinary" or "binary" => maxLength == -1 ? $"{typeName}(max)" : $"{typeName}({maxLength})",
                "nvarchar" or "nchar" => maxLength == -1 ? $"{typeName}(max)" : $"{typeName}({maxLength / 2})",
                "decimal" or "numeric" => $"{typeName}({precision},{scale})",
                "datetime2" or "datetimeoffset" or "time" => $"{typeName}({scale})",
                _ => typeName
            };
        }

        private static string FormatColumnForSummary(SqlColumnSchema column)
        {
            var flags = new List<string>();
            if (column.IsPrimaryKey) flags.Add("PK");
            if (column.IsIdentity) flags.Add("identity");
            if (!column.IsNullable) flags.Add("required");
            var flagText = flags.Count > 0 ? $" [{string.Join(", ", flags)}]" : string.Empty;
            var desc = string.IsNullOrWhiteSpace(column.Description) ? string.Empty : $" ({column.Description})";
            return $"{column.Name} {column.DataType}{flagText}{desc}";
        }

        private static bool HasXpoSoftDeleteColumn(SqlTableSchema table) =>
            table.Columns.Any(c => c.Name.Equals("GCRecord", StringComparison.OrdinalIgnoreCase));

        private sealed class SqlTableSchema
        {
            public int ObjectId { get; set; }
            public string SchemaName { get; set; } = string.Empty;
            public string TableName { get; set; } = string.Empty;
            public string Description { get; set; } = string.Empty;
            public List<SqlColumnSchema> Columns { get; } = [];
            public List<SqlForeignKeySchema> ForeignKeys { get; } = [];
        }

        private sealed class SqlColumnSchema
        {
            public string Name { get; set; } = string.Empty;
            public int Ordinal { get; set; }
            public string DataType { get; set; } = string.Empty;
            public bool IsNullable { get; set; }
            public bool IsIdentity { get; set; }
            public bool IsPrimaryKey { get; set; }
            public string Description { get; set; } = string.Empty;
        }

        private sealed class SqlForeignKeySchema
        {
            public string Name { get; set; } = string.Empty;
            public string ColumnName { get; set; } = string.Empty;
            public string ReferencedSchema { get; set; } = string.Empty;
            public string ReferencedTable { get; set; } = string.Empty;
            public string ReferencedColumn { get; set; } = string.Empty;
        }
    }
}
