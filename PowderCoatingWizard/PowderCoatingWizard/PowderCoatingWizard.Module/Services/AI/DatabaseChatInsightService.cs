using DevExpress.Persistent.Base;
using Microsoft.Extensions.AI;
using System.ComponentModel;
using System.Data;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace PowderCoatingWizard.Module.Services.AI
{
    /// <summary>
    /// DSERPEvo-style internal DBChat tool: generate safe SELECT SQL from schema context,
    /// execute it read-only, and return concise database evidence for assistant reasoning.
    /// </summary>
    public sealed class DatabaseChatInsightService
    {
        private readonly IChatClient _chatClient;
        private readonly SqlServerSchemaProvider _schemaProvider;
        private readonly SafeSqlExecutor _sqlExecutor;
        private readonly int _maxRows;
        private readonly int _maxColumns;
        private string? _lastSql;

        public DatabaseChatInsightService(
            IChatClient chatClient,
            SqlServerSchemaProvider schemaProvider,
            SafeSqlExecutor sqlExecutor,
            int maxRows = 50,
            int maxColumns = 20)
        {
            _chatClient = chatClient;
            _schemaProvider = schemaProvider;
            _sqlExecutor = sqlExecutor;
            _maxRows = maxRows > 0 ? maxRows : 50;
            _maxColumns = Math.Clamp(maxColumns, 3, 40);
        }

        [Description(
            "Primary database evidence tool. Generates and executes a safe read-only SELECT using the allowed SQL Server schema, then returns concise internal evidence for the assistant. " +
            "Use this for database facts, summaries, comparisons, counts, aggregates, trends, and analysis. Do not expose SQL, raw records, or tables unless the user explicitly requested table/list/report output.")]
        public async Task<string> GetDatabaseInsight(
            [Description("The user's database-related question or the specific evidence needed for the assistant's answer.")] string question,
            [Description("True only when the user explicitly asked for a table, list, report, records, or raw database output. Otherwise false.")] bool explicitTabularOutput = false,
            CancellationToken ct = default)
        {
            if (string.IsNullOrWhiteSpace(question))
                return "Database insight skipped: question is required.";

            var schemaSummary = _schemaProvider.GetSchemaSummary();
            if (string.IsNullOrWhiteSpace(schemaSummary))
                return "Database insight skipped: allowed SQL schema is unavailable.";

            try
            {
                var response = await GenerateSqlAsync(question, schemaSummary, ct);
                if (string.IsNullOrWhiteSpace(response.Sql))
                    return string.IsNullOrWhiteSpace(response.Answer)
                        ? "Database insight skipped: no safe SQL was generated."
                        : response.Answer;

                var safeSql = SafeSqlExecutor.NormalizeAndValidate(response.Sql);
                ValidateSoftDeleteFilters(safeSql, schemaSummary);
                LogGeneratedSql("generated", safeSql, question);
                var table = await _sqlExecutor.ExecuteSafeSelectAsync(safeSql, _maxRows, ct);
                _lastSql = safeSql;

                return BuildEvidence(question, response.Answer, safeSql, table, explicitTabularOutput);
            }
            catch (Exception ex)
            {
                Tracing.Tracer.LogError(ex);
                return $"Database insight failed safely: {ex.Message}";
            }
        }

        [Description(
            "Fetches the next page for the last DBChat database insight query. Use only if the user explicitly asks for more rows/next page and the previous database insight was insufficient or tabular output was explicitly requested.")]
        public async Task<string> GetNextDatabaseInsightPage(
            [Description("Number of additional rows requested. Use the configured default if unsure.")] int rows = 0,
            [Description("True only when the user explicitly confirmed or requested more rows/page output.")] bool confirmed = false,
            CancellationToken ct = default)
        {
            if (!confirmed)
                return "Additional database pages require explicit user confirmation or an explicit request for more rows.";

            if (string.IsNullOrWhiteSpace(_lastSql))
                return "No previous database insight query is available for paging.";

            var fetch = rows > 0 ? rows : _maxRows;
            try
            {
                var pagedSql = ApplyNextPage(_lastSql, fetch);
                LogGeneratedSql("next-page", pagedSql, "Next database insight page");
                var table = await _sqlExecutor.ExecuteSafeSelectAsync(pagedSql, fetch, ct);
                _lastSql = pagedSql;
                return BuildEvidence("Next database insight page", string.Empty, pagedSql, table, explicitTabularOutput: true);
            }
            catch (Exception ex)
            {
                Tracing.Tracer.LogError(ex);
                return $"Additional database page failed safely: {ex.Message}";
            }
        }

        private async Task<DbChatSqlResponse> GenerateSqlAsync(string question, string schemaSummary, CancellationToken ct)
        {
            var systemPrompt =
                "You are an internal database evidence planner for a powder-coating domain assistant. " +
                "Return exactly one valid JSON object with properties: answer and sql. " +
                "Generate only read-only SQL Server SELECT statements. Never generate INSERT, UPDATE, DELETE, MERGE, DROP, ALTER, TRUNCATE, CREATE, EXEC, DBCC, or multiple statements. " +
                "Use only the allowed schema. " +
                "Use LIKE '%value%' for text search unless exact matching is clearly required. " +
                "For every table that has GCRecord, exclude XPO soft-deleted rows with GCRecord IS NULL. " +
                "Do not add markdown or code fences. If the schema is insufficient, set sql to an empty string and explain the missing data in answer. " +
                "The final assistant may use your result internally; do not optimize for table display.\n\n" +
                schemaSummary;

            var userPrompt =
                "Create one safe SQL Server SELECT statement for this database evidence request. " +
                "Return only JSON in this exact shape: {\"answer\":\"short planner note\",\"sql\":\"SELECT ...\"}. " +
                "Request: " + question;

            var messages = new List<ChatMessage>
            {
                new(ChatRole.System, systemPrompt),
                new(ChatRole.User, userPrompt)
            };

            var chatResponse = await _chatClient.GetResponseAsync(messages, new ChatOptions { MaxOutputTokens = 800 }, ct);
            var text = chatResponse.Text?.Trim() ?? string.Empty;
            LogPlannerResponse(text, chatResponse);

            if (TryParsePlannerResponse(text, out var parsed))
                return parsed;

            var retryPrompt =
                "Return only one JSON object now. No prose. No markdown. Shape: {\"answer\":\"note\",\"sql\":\"SELECT ...\"}. " +
                "Use only allowed schema and SELECT only. Database request: " + question;
            var retryResponse = await _chatClient.GetResponseAsync([
                new ChatMessage(ChatRole.System, systemPrompt),
                new ChatMessage(ChatRole.User, retryPrompt)
            ], new ChatOptions { MaxOutputTokens = 800 }, ct);
            var retryText = retryResponse.Text?.Trim() ?? string.Empty;
            LogPlannerResponse("retry: " + retryText, retryResponse);

            if (TryParsePlannerResponse(retryText, out parsed))
                return parsed;

            if (IsCountLikeQuestion(question) && TryBuildFallbackAggregateSql(question, schemaSummary, out var fallbackSql))
            {
                Tracing.Tracer.LogText("DBCHAT_SQL planner_fallback=aggregate-sql");
                return new DbChatSqlResponse
                {
                    Answer = "Planner returned an empty response; deterministic aggregate SQL fallback was used.",
                    Sql = fallbackSql
                };
            }

            return new DbChatSqlResponse
            {
                Answer = "The database planner did not return valid JSON or a direct SELECT statement.",
                Sql = string.Empty
            };
        }

        private static bool TryParsePlannerResponse(string text, out DbChatSqlResponse response)
        {
            response = new DbChatSqlResponse();
            if (string.IsNullOrWhiteSpace(text))
                return false;

            var json = ExtractJson(text);
            if (!string.IsNullOrWhiteSpace(json))
            {
                try
                {
                    var parsed = JsonSerializer.Deserialize<DbChatSqlResponse>(json, new JsonSerializerOptions
                    {
                        PropertyNameCaseInsensitive = true
                    });

                    if (parsed != null)
                    {
                        response = parsed;
                        return true;
                    }
                }
                catch (JsonException ex)
                {
                    Tracing.Tracer.LogText($"DBCHAT_SQL Planner JSON parse failed: {ex.Message}");
                }
            }

            var sql = ExtractSelectStatement(text);
            if (!string.IsNullOrWhiteSpace(sql))
            {
                response = new DbChatSqlResponse
                {
                    Answer = "Planner returned a direct SELECT statement.",
                    Sql = sql
                };
                return true;
            }

            return false;
        }

        private static void LogPlannerResponse(string text, ChatResponse response)
        {
            var safeText = text.Replace("\r", " ").Replace("\n", " ").Trim();
            if (safeText.Length > 1200)
                safeText = safeText[..1200] + "...";

            Tracing.Tracer.LogText($"DBCHAT_SQL planner_raw length={text.Length} messageCount={response.Messages.Count} text={safeText}");
        }

        private static bool TryBuildFallbackAggregateSql(string question, string schemaSummary, out string sql)
        {
            sql = string.Empty;
            var requestedTables = new List<string>();

            foreach (Match match in Regex.Matches(schemaSummary, @"^Table:\s+(?<table>[\w\[\]\.]+)\s*$", RegexOptions.Multiline | RegexOptions.IgnoreCase))
            {
                var tableName = match.Groups["table"].Value;
                var simpleName = tableName.Split('.').Last().Trim('[', ']');
                if (question.Contains(simpleName, StringComparison.OrdinalIgnoreCase))
                    requestedTables.Add(tableName);
            }

            requestedTables = requestedTables.Distinct(StringComparer.OrdinalIgnoreCase).Take(8).ToList();
            if (requestedTables.Count == 0)
                return false;

            var selects = requestedTables.Select(tableName =>
            {
                var safeLabel = tableName.Replace("'", "''");
                var filter = TableHasGcRecord(schemaSummary, tableName) ? " WHERE GCRecord IS NULL" : string.Empty;
                return $"SELECT '{safeLabel}' AS EntityName, COUNT_BIG(*) AS RecordCount FROM {tableName}{filter}";
            });

            sql = string.Join(" UNION ALL ", selects);
            return true;
        }

        private static void ValidateSoftDeleteFilters(string sql, string schemaSummary)
        {
            foreach (var tableName in GetSoftDeleteTableNames(schemaSummary))
            {
                if (!SqlReferencesTable(sql, tableName))
                    continue;

                if (!Regex.IsMatch(sql, @"\bGCRecord\s+IS\s+NULL\b", RegexOptions.IgnoreCase))
                    throw new InvalidOperationException($"The SQL statement must exclude XPO soft-deleted rows from {tableName} with GCRecord IS NULL.");
            }
        }

        private static bool TableHasGcRecord(string schemaSummary, string tableName) =>
            GetSoftDeleteTableNames(schemaSummary).Any(t => TableNamesEqual(t, tableName));

        private static IEnumerable<string> GetSoftDeleteTableNames(string schemaSummary)
        {
            var currentTable = string.Empty;
            foreach (var line in schemaSummary.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries))
            {
                var tableMatch = Regex.Match(line, @"^Table:\s+(?<table>[\w\[\]\.]+)\s*$", RegexOptions.IgnoreCase);
                if (tableMatch.Success)
                {
                    currentTable = tableMatch.Groups["table"].Value;
                    continue;
                }

                if (!string.IsNullOrWhiteSpace(currentTable) &&
                    line.Contains("GCRecord", StringComparison.OrdinalIgnoreCase) &&
                    line.Contains("XPO soft delete", StringComparison.OrdinalIgnoreCase))
                {
                    yield return currentTable;
                }
            }
        }

        private static bool SqlReferencesTable(string sql, string tableName)
        {
            var simpleName = tableName.Split('.').Last().Trim('[', ']');
            return Regex.IsMatch(sql, $@"\b(from|join)\s+(?:\[[^\]]+\]\.)?\[?{Regex.Escape(simpleName)}\]?\b", RegexOptions.IgnoreCase) ||
                Regex.IsMatch(sql, $@"\b(from|join)\s+{Regex.Escape(tableName)}\b", RegexOptions.IgnoreCase);
        }

        private static bool TableNamesEqual(string left, string right) =>
            NormalizeTableName(left).Equals(NormalizeTableName(right), StringComparison.OrdinalIgnoreCase);

        private static string NormalizeTableName(string tableName) =>
            new(tableName.Where(char.IsLetterOrDigit).Select(char.ToLowerInvariant).ToArray());

        private static bool IsCountLikeQuestion(string question)
        {
            var text = question.ToLowerInvariant();
            return ContainsAny(text, "брой", "колко", "общо", "count", "total", "aggregate", "aggregated", "record count", "records count");
        }

        private static bool ContainsAny(string text, params string[] terms) =>
            terms.Any(text.Contains);

        private static void LogGeneratedSql(string kind, string sql, string question)
        {
            var safeQuestion = question.Replace("\r", " ").Replace("\n", " ").Trim();
            if (safeQuestion.Length > 300)
                safeQuestion = safeQuestion[..300] + "...";

            Tracing.Tracer.LogText($"DBCHAT_SQL {kind} question={safeQuestion}");
            Tracing.Tracer.LogText($"DBCHAT_SQL {kind} sql={sql}");
        }

        private string BuildEvidence(string question, string answer, string sql, DataTable table, bool explicitTabularOutput)
        {
            var sb = new StringBuilder();
            sb.AppendLine("Database evidence for internal assistant reasoning.");
            sb.AppendLine("Use this to answer the user's domain question. Do not expose SQL, raw records, or tables unless explicitly requested.");
            sb.AppendLine($"Question: {question.Trim()}");
            if (!string.IsNullOrWhiteSpace(answer))
                sb.AppendLine($"Planner note: {answer.Trim()}");
            sb.AppendLine($"Rows returned: {table.Rows.Count} (limited to {_maxRows})");
            sb.AppendLine($"Columns returned: {table.Columns.Count} (display limited to {_maxColumns})");
            AppendQualityHints(sb, table, explicitTabularOutput);
            sb.AppendLine();

            if (table.Rows.Count == 0 || table.Columns.Count == 0)
            {
                sb.AppendLine("No database rows were returned.");
                return sb.ToString();
            }

            if (explicitTabularOutput)
            {
                sb.AppendLine("Tabular result was explicitly requested:");
                sb.Append(BuildMarkdownTable(table));
            }
            else
            {
                sb.AppendLine("Evidence summary:");
                sb.Append(BuildCompactSummary(table));
            }

            return sb.ToString();
        }

        private void AppendQualityHints(StringBuilder sb, DataTable table, bool explicitTabularOutput)
        {
            var hints = new List<string>();
            if (table.Rows.Count >= _maxRows)
                hints.Add("Result reached the row cap; answer only from the returned evidence or ask for a narrower filter/next page when appropriate.");

            if (table.Columns.Count > _maxColumns)
                hints.Add("Result has more columns than the display limit; use only visible evidence unless additional detail is explicitly needed.");

            var enumLikeColumns = table.Columns.Cast<DataColumn>()
                .Where(c => IsEnumLikeColumn(c.ColumnName, table))
                .Select(c => c.ColumnName)
                .Take(6)
                .ToList();
            if (enumLikeColumns.Count > 0)
                hints.Add($"Potential enum/status integer columns detected ({string.Join(", ", enumLikeColumns)}); call get_enum_mappings when human-readable names are needed.");

            if (!explicitTabularOutput)
                hints.Add("Keep this evidence internal; do not present raw rows or a table unless the user explicitly requested that output.");

            if (hints.Count == 0)
                return;

            sb.AppendLine("Quality hints:");
            foreach (var hint in hints)
                sb.AppendLine("- " + hint);
        }

        private static bool IsEnumLikeColumn(string columnName, DataTable table)
        {
            if (!Regex.IsMatch(columnName, @"(status|state|type|kind|mode|category|function)$", RegexOptions.IgnoreCase))
                return false;

            var values = table.Rows.Cast<DataRow>()
                .Select(row => row[columnName])
                .Where(value => value != null && value != DBNull.Value)
                .Take(20)
                .ToList();

            return values.Count > 0 && values.All(value =>
                value is byte or short or int or long || int.TryParse(value.ToString(), out _));
        }

        private string BuildCompactSummary(DataTable table)
        {
            var sb = new StringBuilder();
            var visibleColumns = table.Columns.Cast<DataColumn>().Take(_maxColumns).ToList();
            var visibleRows = table.Rows.Cast<DataRow>().Take(_maxRows).ToList();

            foreach (var row in visibleRows)
            {
                var values = visibleColumns
                    .Select(column => $"{column.ColumnName}: {FormatValue(row[column])}");
                sb.AppendLine("- " + string.Join(" | ", values));
            }

            return sb.ToString();
        }

        private string BuildMarkdownTable(DataTable table)
        {
            var sb = new StringBuilder();
            var visibleColumns = table.Columns.Cast<DataColumn>().Take(_maxColumns).ToList();
            var visibleRows = table.Rows.Cast<DataRow>().Take(_maxRows).ToList();

            sb.Append("| ");
            sb.Append(string.Join(" | ", visibleColumns.Select(c => EscapeMarkdown(c.ColumnName))));
            sb.AppendLine(" |");
            sb.Append("| ");
            sb.Append(string.Join(" | ", visibleColumns.Select(_ => "---")));
            sb.AppendLine(" |");

            foreach (var row in visibleRows)
            {
                sb.Append("| ");
                sb.Append(string.Join(" | ", visibleColumns.Select(c => EscapeMarkdown(FormatValue(row[c])))));
                sb.AppendLine(" |");
            }

            return sb.ToString();
        }

        private static string ApplyNextPage(string sql, int fetch)
        {
            var normalized = SafeSqlExecutor.NormalizeAndValidate(sql);
            var offsetMatch = Regex.Match(normalized, @"\boffset\s+(\d+)\s+rows\s+fetch\s+next\s+(\d+)\s+rows\s+only\b", RegexOptions.IgnoreCase);
            if (offsetMatch.Success && int.TryParse(offsetMatch.Groups[1].Value, out var offset) && int.TryParse(offsetMatch.Groups[2].Value, out var previousFetch))
            {
                var nextOffset = offset + previousFetch;
                return Regex.Replace(normalized, @"\boffset\s+\d+\s+rows\s+fetch\s+next\s+\d+\s+rows\s+only\b", $"OFFSET {nextOffset} ROWS FETCH NEXT {fetch} ROWS ONLY", RegexOptions.IgnoreCase);
            }

            var withoutTop = Regex.Replace(normalized, @"^select\s+(distinct\s+)?top\s*(\(\d+\)|\d+)\s+", match =>
                match.Groups[1].Success ? "SELECT DISTINCT " : "SELECT ", RegexOptions.IgnoreCase);

            if (!Regex.IsMatch(withoutTop, @"\border\s+by\b", RegexOptions.IgnoreCase))
                withoutTop += " ORDER BY (SELECT NULL)";

            return withoutTop + $" OFFSET {fetch} ROWS FETCH NEXT {fetch} ROWS ONLY";
        }

        private static string ExtractJson(string text)
        {
            text = text.Trim();
            var firstBrace = text.IndexOf('{');
            var lastBrace = text.LastIndexOf('}');
            if (firstBrace >= 0 && lastBrace > firstBrace)
                return text[firstBrace..(lastBrace + 1)];

            return string.Empty;
        }

        private static string ExtractSelectStatement(string text)
        {
            var cleaned = text.Trim();
            if (cleaned.StartsWith("```", StringComparison.Ordinal))
                cleaned = string.Join(Environment.NewLine, cleaned.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries)
                    .Where(line => !line.Trim().StartsWith("```", StringComparison.Ordinal)));

            var selectIndex = cleaned.IndexOf("select", StringComparison.OrdinalIgnoreCase);
            if (selectIndex < 0)
                return string.Empty;

            var sql = cleaned[selectIndex..].Trim().TrimEnd(';').Trim();
            return sql;
        }

        private static string FormatValue(object? value)
        {
            if (value == null || value == DBNull.Value) return "N/A";
            if (value is DateTime dt) return dt.ToString("yyyy-MM-dd HH:mm");
            if (value is decimal d) return d.ToString("G6");
            if (value is double db) return db.ToString("G6");
            if (value is float f) return f.ToString("G6");
            var text = value.ToString() ?? string.Empty;
            return text.Length <= 160 ? text : text[..160] + "...";
        }

        private static string EscapeMarkdown(string value) =>
            value.Replace("|", "\\|").Replace("\r", " ").Replace("\n", " ");

        private sealed class DbChatSqlResponse
        {
            [JsonPropertyName("answer")]
            public string Answer { get; set; } = string.Empty;

            [JsonPropertyName("sql")]
            public string Sql { get; set; } = string.Empty;
        }
    }
}
