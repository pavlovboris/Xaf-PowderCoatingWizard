using DevExpress.ExpressApp;
using DevExpress.Persistent.Base;
using Microsoft.Extensions.AI;
using PowderCoatingWizard.Module.BusinessObjects.AI;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace PowderCoatingWizard.Module.Services.AI
{
    /// <summary>
    /// Multi-step RAG retrieval:
    ///   1. <see cref="QueryPlannerService"/> expands the query via HyDE or decomposition.
    ///   2. Parallel embedding similarity retrieval against a candidate pool (default 15).
    ///   3. LLM-based reranker scores each candidate and selects the best topK.
    /// Falls back gracefully if no LLM client is provided (single-pass cosine only).
    /// </summary>
    public partial class RagSearchService
    {
        private readonly IObjectSpaceFactory _osFactory;
        private readonly EmbeddingService _embedding;

        public RagSearchService(IObjectSpaceFactory osFactory, EmbeddingService embedding)
        {
            _osFactory = osFactory;
            _embedding = embedding;
        }

        /// <summary>
        /// Returns the most relevant chunk texts for the given query.
        /// When <paramref name="chatClient"/> is supplied the full multi-step pipeline runs:
        /// query planning → candidate retrieval → LLM reranking.
        /// Without a client only cosine similarity is used (legacy behaviour).
        /// </summary>
        public async Task<IReadOnlyList<string>> SearchAsync(
            string query,
            int? topK = null,
            float? minScore = null,
            Guid? agentOid = null,
            IChatClient? chatClient = null,
            CancellationToken ct = default)
        {
            var results = await SearchDetailedAsync(query, topK, minScore, agentOid, chatClient, ct);
            return results.Select(r => r.Text).ToList();
        }

        public async Task<IReadOnlyList<RagSearchResult>> SearchDetailedAsync(
            string query,
            int? topK = null,
            float? minScore = null,
            Guid? agentOid = null,
            IChatClient? chatClient = null,
            CancellationToken ct = default)
        {
            if (string.IsNullOrWhiteSpace(query))
                return [];

            int finalTopK;
            float finalMinScore;
            int candidatePool;
            bool rerankerEnabled;
            int maxSubqueries;
            int decomposeMaxTokens;
            int hydeMaxTokens;
            bool openAIVectorStoreEnabled;
            string? openAIVectorStoreId;
            int openAIVectorStoreMaxResults;
            string? openAIApiKeyEncrypted;
            AIProviderType providerType;

            using (var settingsOs = _osFactory.CreateObjectSpace(typeof(AIProviderSettings)))
            {
                var s = settingsOs.FirstOrDefault<AIProviderSettings>(_ => true);
                finalTopK = topK ?? s?.RagTopK ?? 5;
                finalMinScore = minScore ?? s?.RagMinScore ?? 0.4f;
                candidatePool = s?.RagCandidatePool ?? 15;
                rerankerEnabled = s?.RagRerankerEnabled ?? true;
                maxSubqueries = s?.RagMaxSubqueries ?? 4;
                decomposeMaxTokens = s?.PlannerDecomposeMaxTokens ?? 300;
                hydeMaxTokens = s?.PlannerHyDEMaxTokens ?? 200;
                openAIVectorStoreEnabled = s?.OpenAIVectorStoreEnabled ?? false;
                openAIVectorStoreId = s?.OpenAIVectorStoreId;
                openAIVectorStoreMaxResults = s?.OpenAIVectorStoreMaxResults ?? 8;
                openAIApiKeyEncrypted = s?.ApiKeyEncrypted;
                providerType = s?.ProviderType ?? AIProviderType.OpenAI;
            }

            // Ensure candidate pool is at least topK
            if (candidatePool < finalTopK) candidatePool = finalTopK;

            // ── Step 1: Query planning (requires LLM) ────────────────────────
            IReadOnlyList<string> searchQueries;
            if (chatClient != null)
            {
                var planner = new QueryPlannerService(chatClient, maxSubqueries)
                {
                    DecomposeOptions = new Microsoft.Extensions.AI.ChatOptions { MaxOutputTokens = decomposeMaxTokens },
                    HyDEOptions      = new Microsoft.Extensions.AI.ChatOptions { MaxOutputTokens = hydeMaxTokens }
                };
                searchQueries = await planner.PlanAsync(query, ct);
            }
            else
            {
                searchQueries = [query];
            }

            var externalSearchTask = SearchOpenAIVectorStoreAsync(
                query,
                providerType,
                openAIVectorStoreEnabled,
                openAIVectorStoreId,
                openAIVectorStoreMaxResults,
                openAIApiKeyEncrypted,
                ct);

            if (!_embedding.IsAvailable)
            {
                var externalOnlyResults = await externalSearchTask;
                Tracing.Tracer.LogText($"RAG:OPENAI_VECTOR Local embedding unavailable — OpenAI Vector Store returned {externalOnlyResults.Count} result(s)");
                return externalOnlyResults.Take(finalTopK).ToList();
            }

            // ── Step 2: Load chunks and resolve agent doc filter ─────────────
            HashSet<Guid>? allowedDocOids = null;
            if (agentOid.HasValue)
            {
                using var agentOs = _osFactory.CreateObjectSpace(typeof(AIAgent));
                var agent = agentOs.GetObjectByKey<AIAgent>(agentOid.Value);
                if (agent?.Documents.Count > 0)
                    allowedDocOids = agent.Documents.Select(d => d.Oid).ToHashSet();
            }

            List<KnowledgeChunk> allChunks;
            using (var os = _osFactory.CreateObjectSpace(typeof(KnowledgeChunk)))
            {
                allChunks = os.GetObjects<KnowledgeChunk>()
                              .Where(c => !string.IsNullOrEmpty(c.EmbeddingJson)
                                       && ((c.CaseStudy != null && c.CaseStudy.Status == CaseStudyStatus.Approved)
                                           || allowedDocOids == null
                                           || allowedDocOids.Contains(c.Document?.Oid ?? Guid.Empty)))
                              .ToList();
            }

            if (allChunks.Count == 0)
            {
                var externalOnlyResults = await externalSearchTask;
                Tracing.Tracer.LogText($"RAG:OPENAI_VECTOR No local chunks — OpenAI Vector Store returned {externalOnlyResults.Count} result(s)");
                return externalOnlyResults.Take(finalTopK).ToList();
            }

            // ── Step 3: Parallel embedding similarity per subquery ───────────
            // Embed all search queries in parallel
            var embeddingTasks = searchQueries
                .Select(q => _embedding.EmbedAsync(q, ct))
                .ToArray();
            float[]?[] queryVecs = await Task.WhenAll(embeddingTasks);
            var lexicalTerms = ExtractLexicalTerms(searchQueries.Prepend(query));

            // Score each chunk: best score across all subquery vectors
            var scored = allChunks
                .Select(c =>
                {
                    var chunkVec = c.GetEmbedding();
                    float vectorScore = queryVecs
                        .Where(v => v != null)
                        .Select(v => Similarity(v!, chunkVec))
                        .DefaultIfEmpty(0f)
                        .Max();
                    float lexicalBoost = CalculateLexicalBoost(c.ChunkText, lexicalTerms);
                    return (Chunk: c, Score: vectorScore + lexicalBoost);
                })
                .Where(x => x.Score >= finalMinScore)
                .OrderByDescending(x => x.Score)
                .Take(candidatePool)
                .ToList();

            if (scored.Count == 0)
            {
                var externalOnlyResults = await externalSearchTask;
                Tracing.Tracer.LogText($"RAG:OPENAI_VECTOR No local matches — OpenAI Vector Store returned {externalOnlyResults.Count} result(s)");
                return externalOnlyResults.Take(finalTopK).ToList();
            }

            // ── Step 4: LLM Reranking ────────────────────────────────────────
            if (chatClient != null && rerankerEnabled && scored.Count > finalTopK)
            {
                scored = await ReRankAsync(query, scored, finalTopK, chatClient, ct);
            }

            var localResults = scored
                .Take(finalTopK)
                .Select(x => ToResult(x.Chunk, x.Score))
                .ToList();

            var externalResults = await externalSearchTask;
            Tracing.Tracer.LogText($"RAG:OPENAI_VECTOR Local returned {localResults.Count}; OpenAI Vector Store returned {externalResults.Count}");

            return MergeResults(localResults, externalResults, finalTopK);
        }

        private static IReadOnlyList<RagSearchResult> MergeResults(
            IReadOnlyList<RagSearchResult> localResults,
            IReadOnlyList<RagSearchResult> externalResults,
            int topK)
        {
            if (externalResults.Count == 0)
                return localResults;

            return localResults
                .Concat(externalResults)
                .GroupBy(r => r.Citation, StringComparer.OrdinalIgnoreCase)
                .Select(g => g.OrderByDescending(r => r.Score).First())
                .OrderByDescending(r => r.Score)
                .Take(Math.Max(topK, localResults.Count))
                .ToList();
        }

        private static async Task<IReadOnlyList<RagSearchResult>> SearchOpenAIVectorStoreAsync(
            string query,
            AIProviderType providerType,
            bool enabled,
            string? vectorStoreId,
            int maxResults,
            string? apiKeyEncrypted,
            CancellationToken ct)
        {
            if (!enabled || providerType != AIProviderType.OpenAI || string.IsNullOrWhiteSpace(vectorStoreId))
            {
                Tracing.Tracer.LogText($"RAG:OPENAI_VECTOR Skipped enabled={enabled} provider={providerType} vectorStoreIdSet={!string.IsNullOrWhiteSpace(vectorStoreId)}");
                return [];
            }

            try
            {
                var apiKey = AICredentialProtector.Decrypt(apiKeyEncrypted ?? string.Empty);
                var service = new OpenAIVectorStoreSearchService(apiKey, vectorStoreId, maxResults);
                var results = await service.SearchAsync(query, ct);
                Tracing.Tracer.LogText($"RAG:OPENAI_VECTOR Search completed — {results.Count} result(s)");
                return results;
            }
            catch (Exception ex)
            {
                Tracing.Tracer.LogError(ex);
                return [];
            }
        }

        private static RagSearchResult ToResult(KnowledgeChunk chunk, float score)
        {
            var sourceType = chunk.CaseStudy != null ? "Case Study" : "Document";
            var sourceTitle = chunk.CaseStudy?.Title
                ?? chunk.Document?.Title
                ?? chunk.Document?.File?.FileName
                ?? "Unknown Source";

            return new RagSearchResult(
                chunk.ChunkText ?? string.Empty,
                sourceType,
                sourceTitle,
                chunk.ChunkIndex,
                score);
        }

        // ── LLM Reranker ─────────────────────────────────────────────────────

        private static async Task<List<(KnowledgeChunk Chunk, float Score)>> ReRankAsync(
            string originalQuery,
            List<(KnowledgeChunk Chunk, float Score)> candidates,
            int topK,
            IChatClient llm,
            CancellationToken ct)
        {
            // Build the reranking prompt with indexed candidates
            var sb = new System.Text.StringBuilder();
            sb.AppendLine($"Original question: {originalQuery}");
            sb.AppendLine();
            sb.AppendLine("Rate each passage's relevance to the question on a scale of 0-10.");
            sb.AppendLine("Return a JSON array of objects: [{\"index\":0,\"score\":8},{\"index\":1,\"score\":3},...]");
            sb.AppendLine("Include ALL indices. Higher score = more relevant. No other text.");
            sb.AppendLine();

            for (int i = 0; i < candidates.Count; i++)
            {
                var text = candidates[i].Chunk.ChunkText ?? string.Empty;
                // Truncate long chunks for the reranker to stay within token limits
                var snippet = text.Length > 400 ? text[..400] + "…" : text;
                sb.AppendLine($"[{i}] {snippet}");
            }

            var messages = new List<ChatMessage>
            {
                new(ChatRole.System,
                    "You are a relevance judge. Output only valid JSON. No markdown, no explanation."),
                new(ChatRole.User, sb.ToString())
            };

            try
            {
                var response = await llm.GetResponseAsync(messages,
                    new ChatOptions { MaxOutputTokens = 500 }, ct);

                var json = ExtractJsonArray(response.Text?.Trim() ?? "[]");

                var scores = JsonSerializer.Deserialize<List<RerankerScore>>(json,
                    new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

                if (scores != null && scores.Count > 0)
                {
                    var scoreMap = scores.ToDictionary(s => s.Index, s => (float)s.Score);
                    return candidates
                        .Select((c, i) => (c.Chunk, Score: scoreMap.GetValueOrDefault(i, c.Score)))
                        .OrderByDescending(x => x.Score)
                        .Take(topK)
                        .ToList();
                }
            }
            catch (Exception ex)
            {
                Tracing.Tracer.LogError(ex);
            }

            return candidates.Take(topK).ToList();
        }

        private static string ExtractJsonArray(string text)
        {
            if (string.IsNullOrWhiteSpace(text)) return "[]";

            if (text.StartsWith("```"))
                text = string.Join("\n", text.Split('\n').Skip(1).TakeWhile(l => !l.StartsWith("```")));

            var start = text.IndexOf('[');
            var end = text.LastIndexOf(']');
            return start >= 0 && end > start ? text[start..(end + 1)] : text;
        }

        private sealed class RerankerScore
        {
            public int Index { get; set; }
            public float Score { get; set; }
        }

        private static IReadOnlySet<string> ExtractLexicalTerms(IEnumerable<string> queries)
        {
            var terms = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var query in queries)
            {
                foreach (Match match in LexicalTermRegex().Matches(query ?? string.Empty))
                {
                    var term = match.Value.Trim();
                    if (term.Length >= 3 || term.Any(char.IsDigit))
                        terms.Add(term);
                }
            }
            return terms;
        }

        private static float CalculateLexicalBoost(string? chunkText, IReadOnlySet<string> terms)
        {
            if (string.IsNullOrWhiteSpace(chunkText) || terms.Count == 0)
                return 0f;

            int hits = 0;
            foreach (var term in terms)
            {
                if (chunkText.Contains(term, StringComparison.OrdinalIgnoreCase))
                    hits++;
            }

            if (hits == 0) return 0f;

            var coverage = (float)hits / terms.Count;
            return MathF.Min(0.08f, coverage * 0.08f);
        }

        // ── Cosine similarity ─────────────────────────────────────────────────

        private static float Similarity(float[] a, float[]? b)
        {
            if (b == null || a.Length != b.Length) return 0f;
            float dot = 0, normA = 0, normB = 0;
            for (int i = 0; i < a.Length; i++)
            {
                dot += a[i] * b[i];
                normA += a[i] * a[i];
                normB += b[i] * b[i];
            }
            if (normA == 0 || normB == 0) return 0f;
            return dot / (MathF.Sqrt(normA) * MathF.Sqrt(normB));
        }

        [GeneratedRegex(@"[\p{L}\p{N}][\p{L}\p{N}_\-\.]*")]
        private static partial Regex LexicalTermRegex();
    }
}
