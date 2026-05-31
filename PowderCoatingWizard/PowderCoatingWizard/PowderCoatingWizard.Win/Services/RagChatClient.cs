using Microsoft.Extensions.AI;
using PowderCoatingWizard.Module.BusinessObjects.AI;
using PowderCoatingWizard.Module.Services.AI;
using System.Runtime.CompilerServices;

namespace PowderCoatingWizard.Win.Services
{
    /// <summary>
    /// IChatClient decorator that intercepts every request and prepends
    /// a RAG context system message built from the user's last question.
    /// Streaming is fully forwarded to the inner client so the AIChatControl
    /// typing indicator and token-by-token rendering work natively.
    /// </summary>
    public sealed class RagChatClient : IChatClient
    {
        private readonly IChatClient _inner;
        private readonly RagSearchService _ragSearch;
        private readonly Guid? _agentOid;

        public RagChatClient(IChatClient inner, RagSearchService ragSearch, Guid? agentOid)
        {
            _inner = inner;
            _ragSearch = ragSearch;
            _agentOid = agentOid;
        }

        public Task<ChatResponse> GetResponseAsync(
            IEnumerable<ChatMessage> messages,
            ChatOptions? options = null,
            CancellationToken cancellationToken = default)
        {
            return AugmentWithRagAsync(messages, cancellationToken)
                .ContinueWith(t => _inner.GetResponseAsync(t.Result, options, cancellationToken),
                    cancellationToken,
                    System.Threading.Tasks.TaskContinuationOptions.None,
                    System.Threading.Tasks.TaskScheduler.Default)
                .Unwrap();
        }

        public async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
            IEnumerable<ChatMessage> messages,
            ChatOptions? options = null,
            [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            var augmented = await AugmentWithRagAsync(messages, cancellationToken);
            await foreach (var update in _inner.GetStreamingResponseAsync(augmented, options, cancellationToken))
                yield return update;
        }

        public object? GetService(Type serviceType, object? serviceKey = null)
            => _inner.GetService(serviceType, serviceKey);

        public void Dispose() => _inner.Dispose();

        // ── Helpers ─────────────────────────────────────────────────────────

        private async Task<IList<ChatMessage>> AugmentWithRagAsync(
            IEnumerable<ChatMessage> messages,
            CancellationToken ct)
        {
            var list = messages.ToList();

            var lastUser = list.LastOrDefault(m => m.Role == ChatRole.User);
            if (lastUser == null) return list;

            var userText = string.Concat(lastUser.Contents.OfType<TextContent>().Select(t => t.Text));
            if (string.IsNullOrWhiteSpace(userText)) return list;

            IReadOnlyList<string> chunks;
            try
            {
                chunks = await _ragSearch.SearchAsync(userText, agentOid: _agentOid, ct: ct);
            }
            catch
            {
                return list; // RAG failure is non-fatal — fall back to plain chat
            }

            if (chunks.Count == 0) return list;

            var ragSb = new System.Text.StringBuilder();
            ragSb.AppendLine("## Relevant Knowledge Base Excerpts");
            ragSb.AppendLine("The following excerpts from uploaded documents are relevant to the current question.");
            ragSb.AppendLine("Prioritise this information when formulating your answer.");
            foreach (var chunk in chunks)
            {
                ragSb.AppendLine();
                ragSb.AppendLine(chunk);
            }

            int insertAt = list.LastIndexOf(lastUser);
            list.Insert(insertAt, new ChatMessage(ChatRole.System, ragSb.ToString()));
            return list;
        }
    }
}
