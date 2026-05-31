using Microsoft.Extensions.AI;

namespace PowderCoatingWizard.Module.Services.AI
{
    /// <summary>
    /// Wraps <see cref="IEmbeddingGenerator{TInput,TEmbedding}"/> to produce float vectors.
    /// Falls back gracefully when no generator is available (no AI provider configured).
    /// </summary>
    public class EmbeddingService
    {
        private readonly IEmbeddingGenerator<string, Embedding<float>>? _generator;

        public bool IsAvailable => _generator != null;

        public EmbeddingService(IEmbeddingGenerator<string, Embedding<float>>? generator = null)
        {
            _generator = generator;
        }

        /// <summary>
        /// Generates an embedding vector for the given text.
        /// Returns null when no generator is configured.
        /// </summary>
        public async Task<float[]?> EmbedAsync(string text, CancellationToken ct = default)
        {
            if (_generator == null || string.IsNullOrWhiteSpace(text)) return null;
            var result = await _generator.GenerateAsync([text], cancellationToken: ct);
            return result[0].Vector.ToArray();
        }

        /// <summary>Generates embeddings for multiple texts in one call.</summary>
        public async Task<float[][]?> EmbedBatchAsync(IList<string> texts, CancellationToken ct = default)
        {
            if (_generator == null || texts.Count == 0) return null;
            var result = await _generator.GenerateAsync(texts, cancellationToken: ct);
            return result.Select(e => e.Vector.ToArray()).ToArray();
        }
    }
}
