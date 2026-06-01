using Microsoft.Extensions.AI;
using DevExpress.Persistent.Base;
using System.Text.Json;

namespace PowderCoatingWizard.Module.Services.AI
{
    /// <summary>
    /// Analyses an incoming user query and produces an optimal set of retrieval subqueries.
    /// Strategy:
    ///   1. Classify: simple (single intent) vs complex (multiple intents / broad topic).
    ///   2a. Complex → Decompose into up to <see cref="MaxSubqueries"/> focused subqueries.
    ///   2b. Simple  → HyDE: generate a hypothetical answer paragraph; embed that instead.
    /// This two-path approach maximises recall for complex queries and precision for simple ones.
    /// </summary>
    public class QueryPlannerService
    {
        private readonly IChatClient _llm;

        /// <summary>Maximum number of decomposed subqueries for a complex query.</summary>
        public int MaxSubqueries { get; set; }

        /// <summary>ChatOptions used for the Decompose LLM call.</summary>
        public ChatOptions DecomposeOptions { get; set; } = new ChatOptions { MaxOutputTokens = 300 };

        /// <summary>ChatOptions used for the HyDE LLM call.</summary>
        public ChatOptions HyDEOptions { get; set; } = new ChatOptions { MaxOutputTokens = 200 };

        public QueryPlannerService(IChatClient llm, int maxSubqueries = 4)
        {
            _llm = llm;
            MaxSubqueries = maxSubqueries > 0 ? maxSubqueries : 4;
        }

        /// <summary>
        /// Returns one or more search strings to use for embedding retrieval.
        /// For a simple query returns the original query plus a HyDE expansion.
        /// For a complex query returns the original query plus multiple focused decompositions.
        /// </summary>
        public async Task<IReadOnlyList<string>> PlanAsync(string userQuery, CancellationToken ct = default)
        {
            if (string.IsNullOrWhiteSpace(userQuery))
                return [userQuery];

            bool isComplex = await ClassifyAsync(userQuery, ct);

            if (isComplex)
            {
                var decomposed = await DecomposeAsync(userQuery, ct);
                return AddOriginalQuery(userQuery, decomposed);
            }

            var hyde = await HyDEAsync(userQuery, ct);
            return AddOriginalQuery(userQuery, [hyde]);
        }

        // ── Private helpers ─────────────────────────────────────────────────

        private static IReadOnlyList<string> AddOriginalQuery(string originalQuery, IEnumerable<string> plannedQueries)
        {
            var result = new List<string>();
            if (!string.IsNullOrWhiteSpace(originalQuery))
                result.Add(originalQuery.Trim());

            foreach (var query in plannedQueries)
            {
                if (string.IsNullOrWhiteSpace(query)) continue;

                var trimmed = query.Trim();
                if (!result.Any(existing => string.Equals(existing, trimmed, StringComparison.OrdinalIgnoreCase)))
                    result.Add(trimmed);
            }

            return result;
        }

        private async Task<bool> ClassifyAsync(string query, CancellationToken ct)
        {
            const string systemPrompt =
                "You are a query classifier. Respond with exactly one word: SIMPLE or COMPLEX.\n" +
                "SIMPLE  = single, specific question that has a direct answer.\n" +
                "COMPLEX = broad topic, multiple sub-questions, or requires gathering several pieces of information.\n" +
                "Do not explain. Output only the word.";

            var messages = new List<ChatMessage>
            {
                new(ChatRole.System, systemPrompt),
                new(ChatRole.User, query)
            };

            try
            {
                var response = await _llm.GetResponseAsync(messages,
                    new ChatOptions { MaxOutputTokens = 5 }, ct);
                var text = response.Text?.Trim().ToUpperInvariant() ?? string.Empty;
                return text.StartsWith("COMPLEX");
            }
            catch (Exception ex)
            {
                Tracing.Tracer.LogError(ex);
                return false; // Default to simple on failure — HyDE is safe
            }
        }

        private async Task<IReadOnlyList<string>> DecomposeAsync(string query, CancellationToken ct)
        {
            string systemPrompt =
                $"You are a retrieval query decomposer. Break the user question into at most {MaxSubqueries} " +
                "focused, self-contained subqueries that together cover the full intent.\n" +
                "Return a JSON array of strings only. No other text.\n" +
                "Example: [\"What is the zinc phosphate bath temperature range?\", \"What happens when pH is too low?\"]";

            var messages = new List<ChatMessage>
            {
                new(ChatRole.System, systemPrompt),
                new(ChatRole.User, query)
            };

            try
            {
                var response = await _llm.GetResponseAsync(messages, DecomposeOptions, ct);
                var json = response.Text?.Trim() ?? "[]";
                // Strip possible markdown code fences
                if (json.StartsWith("```")) json = json.Split('\n').Skip(1).TakeWhile(l => !l.StartsWith("```")).Aggregate((a, b) => a + "\n" + b);
                var subqueries = JsonSerializer.Deserialize<List<string>>(json);
                if (subqueries is { Count: > 0 })
                    return subqueries.Take(MaxSubqueries).ToList();
            }
            catch (Exception ex)
            {
                Tracing.Tracer.LogError(ex);
            }

            // Fallback: return original query as-is
            return [query];
        }

        private async Task<string> HyDEAsync(string query, CancellationToken ct)
        {
            const string systemPrompt =
                "You are a domain expert in industrial surface treatment and powder coating.\n" +
                "Write a short, dense paragraph (3-5 sentences) that would be the ideal answer to the user's question.\n" +
                "Do not say 'I don't know'. Write as if the answer is known. Be specific and technical.\n" +
                "This text will be used for semantic similarity search, not shown to the user.";

            var messages = new List<ChatMessage>
            {
                new(ChatRole.System, systemPrompt),
                new(ChatRole.User, query)
            };

            try
            {
                var response = await _llm.GetResponseAsync(messages, HyDEOptions, ct);
                var hydeText = response.Text?.Trim();
                if (!string.IsNullOrEmpty(hydeText))
                    return hydeText;
            }
            catch (Exception ex)
            {
                Tracing.Tracer.LogError(ex);
            }

            return query; // Fallback to original
        }
    }
}
