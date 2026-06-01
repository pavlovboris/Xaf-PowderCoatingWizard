using DevExpress.ExpressApp;
using Microsoft.Extensions.AI;
using PowderCoatingWizard.Module.BusinessObjects.AI;
using System.Text.Json;

namespace PowderCoatingWizard.Module.Services.AI
{
    /// <summary>
    /// Multi-step RAG retrieval:
    ///   1. <see cref="QueryPlannerService"/> expands the query via HyDE or decomposition.
    ///   2. Parallel embedding similarity retrieval against a candidate pool (default 15).
    ///   3. LLM-based reranker scores each candidate and selects the best topK.
    /// Falls back gracefully if no LLM client is provided (single-pass cosine only).
    /// </summary>
    public class RagSearchService
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
            if (!_embedding.IsAvailable || string.IsNullOrWhiteSpace(query))
                return [];

            int finalTopK;
            float finalMinScore;
            int candidatePool;
            bool rerankerEnabled;
            int maxSubqueries;
            int decomposeMaxTokens;
            float decomposeTemperature;
            int hydeMaxTokens;
            float hydeTemperature;

            using (var settingsOs = _osFactory.CreateObjectSpace(typeof(AIProviderSettings)))
            {
                var s = settingsOs.FirstOrDefault<AIProviderSettings>(_ => true);
                finalTopK = topK ?? s?.RagTopK ?? 5;
                finalMinScore = minScore ?? s?.RagMinScore ?? 0.4f;
                candidatePool = s?.RagCandidatePool ?? 15;
                rerankerEnabled = s?.RagRerankerEnabled ?? true;
                maxSubqueries = s?.RagMaxSubqueries ?? 4;
                decomposeMaxTokens = s?.PlannerDecomposeMaxTokens ?? 300;
                decomposeTemperature = s?.PlannerDecomposeTemperature ?? 0.2f;
                hydeMaxTokens = s?.PlannerHyDEMaxTokens ?? 200;
                hydeTemperature = s?.PlannerHyDETemperature ?? 0.3f;
            }

            // Ensure candidate pool is at least topK
            if (candidatePool < finalTopK) candidatePool = finalTopK;

            // ── Step 1: Query planning (requires LLM) ────────────────────────
            IReadOnlyList<string> searchQueries;
            if (chatClient != null)
            {
                var planner = new QueryPlannerService(chatClient, maxSubqueries)
                {
                    DecomposeOptions = new Microsoft.Extensions.AI.ChatOptions { MaxOutputTokens = decomposeMaxTokens, Temperature = decomposeTemperature },
                    HyDEOptions      = new Microsoft.Extensions.AI.ChatOptions { MaxOutputTokens = hydeMaxTokens,      Temperature = hydeTemperature }
                };
                searchQueries = await planner.PlanAsync(query, ct);
            }
            else
            {
                searchQueries = [query];
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
                                       && (c.CaseStudy != null
                                           || allowedDocOids == null
                                           || allowedDocOids.Contains(c.Document?.Oid ?? Guid.Empty)))
                              .ToList();
            }

            if (allChunks.Count == 0) return [];

            // ── Step 3: Parallel embedding similarity per subquery ───────────
            // Embed all search queries in parallel
            var embeddingTasks = searchQueries
                .Select(q => _embedding.EmbedAsync(q, ct))
                .ToArray();
            float[]?[] queryVecs = await Task.WhenAll(embeddingTasks);

            // Score each chunk: best score across all subquery vectors
            var scored = allChunks
                .Select(c =>
                {
                    var chunkVec = c.GetEmbedding();
                    float bestScore = queryVecs
                        .Where(v => v != null)
                        .Select(v => Similarity(v!, chunkVec))
                        .DefaultIfEmpty(0f)
                        .Max();
                    return (Chunk: c, Score: bestScore);
                })
                .Where(x => x.Score >= finalMinScore)
                .OrderByDescending(x => x.Score)
                .Take(candidatePool)
                .ToList();

            if (scored.Count == 0) return [];

            // ── Step 4: LLM Reranking ────────────────────────────────────────
            if (chatClient != null && rerankerEnabled && scored.Count > finalTopK)
            {
                scored = await ReRankAsync(query, scored, finalTopK, chatClient, ct);
            }

            return scored
                .Take(finalTopK)
                .Select(x => x.Chunk.ChunkText)
                .ToList();
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
                    new ChatOptions { MaxOutputTokens = 500, Temperature = 0f }, ct);

                var json = response.Text?.Trim() ?? "[]";
                if (json.StartsWith("```"))
                    json = string.Join("\n", json.Split('\n').Skip(1).TakeWhile(l => !l.StartsWith("```")));

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
            catch { /* Reranker failure is non-fatal — use cosine ordering */ }

            return candidates.Take(topK).ToList();
        }

        private sealed class RerankerScore
        {
            public int Index { get; set; }
            public float Score { get; set; }
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
    }
}
