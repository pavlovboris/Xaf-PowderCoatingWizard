using DevExpress.Persistent.Base;
using Microsoft.Data.SqlClient;
using System.Collections;
using System.Data.Common;

namespace PowderCoatingWizard.Module.Services.AI
{
    internal static class SqlClientConnectionStringSanitizer
    {
        public static string? Sanitize(string? connectionString)
        {
            if (string.IsNullOrWhiteSpace(connectionString))
                return null;

            try
            {
                _ = new SqlConnectionStringBuilder(connectionString);
                return connectionString;
            }
            catch (ArgumentException)
            {
                // XPO connection strings can include provider-specific keywords that SqlClient does not support.
            }

            try
            {
                var source = new DbConnectionStringBuilder { ConnectionString = connectionString };
                var target = new SqlConnectionStringBuilder();

                foreach (DictionaryEntry entry in source)
                {
                    if (entry.Key is not string key)
                        continue;

                    try
                    {
                        target[key] = entry.Value;
                    }
                    catch (ArgumentException)
                    {
                        Tracing.Tracer.LogText($"DBCHAT_SQL Ignored non-SqlClient connection string keyword: {key}");
                    }
                }

                return target.Count == 0 ? null : target.ConnectionString;
            }
            catch (Exception ex)
            {
                Tracing.Tracer.LogError(ex);
                return null;
            }
        }
    }
}
