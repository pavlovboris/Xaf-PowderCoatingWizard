using DevExpress.ExpressApp;
using PowderCoatingWizard.Module.BusinessObjects.AI;

namespace PowderCoatingWizard.Module.Services.AI
{
    /// <summary>
    /// Searches <see cref="KnowledgeChunk"/> rows by cosine similarity against a query embedding.
    /// topK and minScore defaults are read from <see cref="AIProviderSettings"/> when available.
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
        /// When <paramref name="topK"/> or <paramref name="minScore"/> are not supplied
        /// the values stored in <see cref="AIProviderSettings"/> are used as defaults.
        /// When <paramref name="agentOid"/> is supplied only chunks belonging to
        /// documents that are explicitly linked to that agent are considered.
        /// If the agent has no documents linked, all documents are searched (no restriction).
        /// </summary>
        public async Task<IReadOnlyList<string>> SearchAsync(
            string query,
            int? topK = null,
            float? minScore = null,
            Guid? agentOid = null,
            CancellationToken ct = default)
        {
            if (!_embedding.IsAvailable || string.IsNullOrWhiteSpace(query))
                return [];

            // Load RAG tuning values from persisted settings when not overridden by the caller
            if (topK == null || minScore == null)
            {
                using var settingsOs = _osFactory.CreateObjectSpace(typeof(AIProviderSettings));
                var settings = settingsOs.FirstOrDefault<AIProviderSettings>(s => true);
                if (settings != null)
                {
                    topK ??= settings.RagTopK;
                    minScore ??= settings.RagMinScore;
                }
            }

            // Final fallback if no settings record exists yet
            topK ??= 5;
            minScore ??= 0.4f;

            float[]? queryVec = await _embedding.EmbedAsync(query, ct);
            if (queryVec == null) return [];

            using var os = _osFactory.CreateObjectSpace(typeof(KnowledgeChunk));

            // Determine the allowed document OIDs for this agent (null = no restriction)
            HashSet<Guid>? allowedDocOids = null;
            if (agentOid.HasValue)
            {
                using var agentOs = _osFactory.CreateObjectSpace(typeof(AIAgent));
                var agent = agentOs.GetObjectByKey<AIAgent>(agentOid.Value);
                if (agent?.Documents.Count > 0)
                    allowedDocOids = agent.Documents.Select(d => d.Oid).ToHashSet();
            }

            var chunks = os.GetObjects<KnowledgeChunk>()
                           .Where(c => !string.IsNullOrEmpty(c.EmbeddingJson)
                                    && (allowedDocOids == null || allowedDocOids.Contains(c.Document?.Oid ?? Guid.Empty)))
                           .ToList();

            if (chunks.Count == 0) return [];

            return chunks
                .Select(c => (Text: c.ChunkText, Score: Similarity(queryVec, c.GetEmbedding())))
                .Where(x => x.Score >= minScore)
                .OrderByDescending(x => x.Score)
                .Take(topK.Value)
                .Select(x => x.Text)
                .ToList();
        }

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
