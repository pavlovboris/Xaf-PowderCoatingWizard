using DevExpress.Persistent.Base;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace PowderCoatingWizard.Module.Services.AI
{
    /// <summary>
    /// Lightweight wrapper for OpenAI Vector Store search.
    /// Fails closed so local RAG remains the primary source when OpenAI storage is unavailable.
    /// </summary>
    public sealed class OpenAIVectorStoreSearchService
    {
        private const string SearchEndpointFormat = "https://api.openai.com/v1/vector_stores/{0}/search";
        private static readonly HttpClient HttpClient = new();

        private readonly string _apiKey;
        private readonly string _vectorStoreId;
        private readonly int _maxResults;

        public OpenAIVectorStoreSearchService(string apiKey, string vectorStoreId, int maxResults)
        {
            _apiKey = apiKey;
            _vectorStoreId = vectorStoreId;
            _maxResults = Math.Clamp(maxResults, 1, 50);
        }

        public async Task<IReadOnlyList<RagSearchResult>> SearchAsync(string query, CancellationToken ct = default)
        {
            if (string.IsNullOrWhiteSpace(_apiKey) || string.IsNullOrWhiteSpace(_vectorStoreId) || string.IsNullOrWhiteSpace(query))
            {
                Tracing.Tracer.LogText($"OPENAI_VECTOR Skipped apiKeySet={!string.IsNullOrWhiteSpace(_apiKey)} vectorStoreIdSet={!string.IsNullOrWhiteSpace(_vectorStoreId)} querySet={!string.IsNullOrWhiteSpace(query)}");
                return [];
            }

            try
            {
                Tracing.Tracer.LogText($"OPENAI_VECTOR Searching vector store {_vectorStoreId} maxResults={_maxResults} query={query[..Math.Min(query.Length, 120)]}");
                using var request = new HttpRequestMessage(HttpMethod.Post, string.Format(SearchEndpointFormat, Uri.EscapeDataString(_vectorStoreId)));
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _apiKey);
                request.Headers.Add("OpenAI-Beta", "assistants=v2");

                var payload = JsonSerializer.Serialize(new
                {
                    query,
                    max_num_results = _maxResults
                });
                request.Content = new StringContent(payload, Encoding.UTF8, "application/json");

                using var response = await HttpClient.SendAsync(request, ct);
                if (!response.IsSuccessStatusCode)
                {
                    var errorBody = await response.Content.ReadAsStringAsync(ct);
                    Tracing.Tracer.LogText($"OPENAI_VECTOR Search failed {(int)response.StatusCode} {response.ReasonPhrase}: {errorBody[..Math.Min(errorBody.Length, 500)]}");
                    Tracing.Tracer.LogText($"OpenAI vector store search failed: {(int)response.StatusCode} {response.ReasonPhrase}");
                    return [];
                }

                var json = await response.Content.ReadAsStringAsync(ct);
                var parsed = JsonSerializer.Deserialize<VectorStoreSearchResponse>(json, new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true
                });

                var results = parsed?.Data?
                    .Select((hit, index) => ToResult(hit, index))
                    .Where(r => !string.IsNullOrWhiteSpace(r.Text))
                    .ToList() ?? [];
                Tracing.Tracer.LogText($"OPENAI_VECTOR Search returned {results.Count} result(s)");
                return results;
            }
            catch (Exception ex)
            {
                Tracing.Tracer.LogError(ex);
                return [];
            }
        }

        private static RagSearchResult ToResult(VectorStoreSearchHit hit, int index)
        {
            var text = ExtractText(hit);
            var title = hit.FileName ?? hit.FileId ?? hit.Id ?? "OpenAI Vector Store";
            return new RagSearchResult(
                text,
                "OpenAI Vector Store",
                title,
                index,
                hit.Score ?? 0f);
        }

        private static string ExtractText(VectorStoreSearchHit hit)
        {
            if (hit.Content == null || hit.Content.Count == 0)
                return string.Empty;

            return string.Join("\n\n", hit.Content
                .Select(c => c.Text)
                .Where(t => !string.IsNullOrWhiteSpace(t)));
        }

        private sealed class VectorStoreSearchResponse
        {
            [JsonPropertyName("data")]
            public List<VectorStoreSearchHit>? Data { get; set; }
        }

        private sealed class VectorStoreSearchHit
        {
            [JsonPropertyName("id")]
            public string? Id { get; set; }

            [JsonPropertyName("file_id")]
            public string? FileId { get; set; }

            [JsonPropertyName("filename")]
            public string? FileName { get; set; }

            [JsonPropertyName("score")]
            public float? Score { get; set; }

            [JsonPropertyName("content")]
            public List<VectorStoreSearchContent>? Content { get; set; }
        }

        private sealed class VectorStoreSearchContent
        {
            [JsonPropertyName("text")]
            public string? Text { get; set; }
        }
    }
}
