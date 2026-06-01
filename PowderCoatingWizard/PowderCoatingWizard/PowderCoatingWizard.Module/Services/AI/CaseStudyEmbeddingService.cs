using DevExpress.ExpressApp;
using PowderCoatingWizard.Module.BusinessObjects.AI;

namespace PowderCoatingWizard.Module.Services.AI
{
    /// <summary>
    /// Embeds an <see cref="AICaseStudy"/> into <see cref="KnowledgeChunk"/> records
    /// so the RAG pipeline can retrieve it during agent conversations.
    ///
    /// Only <see cref="CaseStudyStatus.Approved"/> case studies are embedded.
    /// Calling <see cref="EmbedAsync"/> on a Draft or Archived study is a no-op.
    /// Re-running on an already-embedded study replaces the existing chunks.
    /// </summary>
    public sealed class CaseStudyEmbeddingService
    {
        private readonly EmbeddingService _embedding;

        public CaseStudyEmbeddingService(EmbeddingService embedding)
        {
            _embedding = embedding;
        }

        /// <summary>
        /// Generates (or regenerates) <see cref="KnowledgeChunk"/> records for the given case study.
        /// The <paramref name="os"/> must already contain the case study object and must be
        /// committed by the caller after this method returns.
        /// </summary>
        /// <returns>Number of chunks written, or -1 if embedding is unavailable / study not approved.</returns>
        public async Task<int> EmbedAsync(AICaseStudy caseStudy, IObjectSpace os, CancellationToken ct = default)
        {
            if (!_embedding.IsAvailable) return -1;
            if (caseStudy.Status != CaseStudyStatus.Approved) return -1;

            // Remove stale chunks first (idempotent re-run)
            var stale = caseStudy.KnowledgeChunks.ToList();
            foreach (var old in stale)
                os.Delete(old);

            string fullText = caseStudy.BuildEmbeddingText();

            // Split into chunks of ~1000 chars with ~100-char overlap so context isn't lost at boundaries.
            var chunks = SplitIntoChunks(fullText, chunkSize: 1000, overlap: 100);
            var texts = chunks.ToList();

            // Batch-embed all chunks in one API call
            float[][]? vectors = await _embedding.EmbedBatchAsync(texts, ct);
            if (vectors == null || vectors.Length != texts.Count) return -1;

            for (int i = 0; i < texts.Count; i++)
            {
                var chunk = os.CreateObject<KnowledgeChunk>();
                chunk.CaseStudy   = caseStudy;
                chunk.ChunkIndex  = i;
                chunk.ChunkText   = texts[i];
                chunk.CreatedAt   = DateTime.UtcNow;
                chunk.SetEmbedding(vectors[i]);
            }

            caseStudy.IsEmbedded = true;
            return texts.Count;
        }

        /// <summary>
        /// Deletes all <see cref="KnowledgeChunk"/> records for a case study
        /// (called when status changes to Archived or the record is deleted).
        /// </summary>
        public static void RemoveChunks(AICaseStudy caseStudy, IObjectSpace os)
        {
            foreach (var chunk in caseStudy.KnowledgeChunks.ToList())
                os.Delete(chunk);

            caseStudy.IsEmbedded = false;
        }

        // ── Text splitting ────────────────────────────────────────────────────

        private static IEnumerable<string> SplitIntoChunks(string text, int chunkSize, int overlap)
        {
            if (string.IsNullOrWhiteSpace(text)) yield break;

            // Prefer paragraph breaks; fall back to character boundary.
            var paragraphs = text.Split(["\n\n"], StringSplitOptions.RemoveEmptyEntries);

            var buffer = new System.Text.StringBuilder();

            foreach (var para in paragraphs)
            {
                // If adding this paragraph would exceed chunkSize, flush current buffer first.
                if (buffer.Length > 0 && buffer.Length + para.Length + 2 > chunkSize)
                {
                    string chunk = buffer.ToString().Trim();
                    if (chunk.Length > 0) yield return chunk;

                    // Keep the overlap tail for context continuity.
                    string tail = chunk.Length > overlap ? chunk[^overlap..] : chunk;
                    buffer.Clear();
                    buffer.AppendLine(tail);
                }

                buffer.AppendLine(para);
            }

            if (buffer.Length > 0)
            {
                string last = buffer.ToString().Trim();
                if (last.Length > 0) yield return last;
            }
        }
    }
}
