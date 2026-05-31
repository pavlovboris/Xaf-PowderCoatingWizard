using DevExpress.AIIntegration;
using DevExpress.AIIntegration.Extensions;
using DevExpress.AIIntegration.Services.Chat;
using Microsoft.Extensions.AI;
using PowderCoatingWizard.Module.Services.AI;

namespace PowderCoatingWizard.Win.Services
{
    /// <summary>
    /// Implements IAIChatClientCustomizeMessageRequest to inject RAG context into
    /// every chat request before it reaches the AI model, while preserving native streaming.
    /// </summary>
    public sealed class RagMessageCustomizer : IAIChatClientCustomizeMessageRequest
    {
        private readonly RagSearchService _ragSearch;
        private readonly Guid? _agentOid;

        public RagMessageCustomizer(RagSearchService ragSearch, Guid? agentOid)
        {
            _ragSearch = ragSearch;
            _agentOid = agentOid;
        }

        public void Customize(ChatMessageRequest request, BaseRequest baseRequest, RequestContext context)
        {
            var messages = request.Messages;
            var lastUser = messages?.LastOrDefault(m => m.Role == ChatRole.User);
            if (lastUser == null) return;

            var userText = string.Concat(lastUser.Contents.OfType<TextContent>().Select(t => t.Text));
            if (string.IsNullOrWhiteSpace(userText)) return;

            IReadOnlyList<string> chunks;
            try
            {
                chunks = _ragSearch.SearchAsync(userText, agentOid: _agentOid)
                    .GetAwaiter().GetResult();
            }
            catch
            {
                return;
            }

            if (chunks.Count == 0) return;

            var sb = new System.Text.StringBuilder();
            sb.AppendLine("## Relevant Knowledge Base Excerpts");
            sb.AppendLine("The following excerpts from uploaded documents are relevant to the current question.");
            sb.AppendLine("Prioritise this information when formulating your answer.");
            foreach (var chunk in chunks)
            {
                sb.AppendLine();
                sb.AppendLine(chunk);
            }

            int insertAt = messages!.ToList().LastIndexOf(lastUser);
            messages.Insert(insertAt, new ChatMessage(ChatRole.System, sb.ToString()));
        }
    }
}
