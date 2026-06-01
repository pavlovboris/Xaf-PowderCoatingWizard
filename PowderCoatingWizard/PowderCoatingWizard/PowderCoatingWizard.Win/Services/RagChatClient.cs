using DevExpress.Persistent.Base;
using Microsoft.Extensions.AI;
using PowderCoatingWizard.Module.Services.AI;
using System.Runtime.CompilerServices;
using System.Threading.Channels;

namespace PowderCoatingWizard.Win.Services
{
    /// <summary>
    /// IChatClient decorator that prepends a RAG context system message before every user turn.
    /// Inherits <see cref="DelegatingChatClient"/> so that <see cref="GetService"/> correctly
    /// returns <c>this</c> for the outer wrapper type. DevExpress <c>AIChatControl</c> probes
    /// <see cref="IChatClient.GetService"/> to discover streaming capabilities; a plain
    /// <c>IChatClient</c> implementation that delegates straight to <c>_inner</c> exposes the
    /// inner LLM client directly and bypasses the RAG augmentation for the streaming path.
    ///
    /// STREAMING / THINKING INDICATOR STRATEGY
    /// AIChatControl shows the thinking indicator only while GetStreamingResponseAsync is actively
    /// yielding. If we await RAG preprocessing (2-3 LLM calls) BEFORE the first yield the control
    /// sees a silent gap and never shows the indicator. To fix this we run RAG in a background Task
    /// and bridge results through a Channel so the thinking indicator appears immediately while RAG
    /// works in the background. Once RAG finishes we restart the inner stream with augmented messages.
    /// </summary>
    public sealed class RagChatClient : DelegatingChatClient
    {
        // Raw LLM provider client — used only for query planning (HyDE / decompose / classify).
        // Must NOT go through FunctionInvokingChatClient or ConfigureOptions, because those
        // layers inject tools into every ChatOptions, which causes FunctionInvokingChatClient
        // to attempt tool calls during RAG preprocessing and blocks / corrupts the pipeline.
        private readonly IChatClient _plannerClient;
        private readonly RagSearchService _ragSearch;
        private readonly Guid? _agentOid;

        public RagChatClient(IChatClient inner, IChatClient plannerClient, RagSearchService ragSearch, Guid? agentOid)
            : base(inner)
        {
            _plannerClient = plannerClient;
            _ragSearch = ragSearch;
            _agentOid = agentOid;
        }

        public override async Task<ChatResponse> GetResponseAsync(
            IEnumerable<ChatMessage> messages,
            ChatOptions? options = null,
            CancellationToken cancellationToken = default)
        {
            AILogger.LogEvent("RAG", "GetResponseAsync called");
            AILogger.LogMessages("RAG:IN", messages);
            AILogger.LogOptions("RAG:OPTIONS", options);
            try
            {
                var augmented = await AugmentWithRagAsync(messages, cancellationToken);
                AILogger.LogMessages("RAG:AUGMENTED", augmented);
                var response = await base.GetResponseAsync(augmented, options, cancellationToken);
                AILogger.LogEvent("RAG", $"GetResponseAsync completed — {response.Messages.Count} response message(s)");
                return response;
            }
            catch (Exception ex)
            {
                AILogger.LogError("RAG", ex);
                Tracing.Tracer.LogError(ex);
                throw;
            }
        }

        public override async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
            IEnumerable<ChatMessage> messages,
            ChatOptions? options = null,
            [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            // Run RAG augmentation in a background task so the Channel starts filling immediately.
            // This lets the caller (AIChatControl) enter the await-foreach loop right away, which
            // is what triggers the thinking indicator — the indicator disappears the moment the
            // enumerator returns without yielding anything.
            var channel = Channel.CreateUnbounded<ChatResponseUpdate>(
                new UnboundedChannelOptions { SingleWriter = true, SingleReader = true });

            AILogger.LogEvent("RAG", "GetStreamingResponseAsync called");
            AILogger.LogMessages("RAG:IN", messages);
            AILogger.LogOptions("RAG:OPTIONS", options);

            _ = Task.Run(async () =>
            {
                try
                {
                    var augmented = await AugmentWithRagAsync(messages, cancellationToken);
                    AILogger.LogMessages("RAG:AUGMENTED", augmented);
                    AILogger.LogEvent("RAG", "Starting inner streaming call");
                    int updateCount = 0;
                    await foreach (var update in base.GetStreamingResponseAsync(augmented, options, cancellationToken))
                    {
                        updateCount++;
                        // Log tool-call and finish updates so we can see what the model is doing.
                        if (update.Contents.OfType<FunctionCallContent>().Any())
                        {
                            var calls = string.Join(", ", update.Contents.OfType<FunctionCallContent>().Select(f => f.Name));
                            AILogger.LogEvent("RAG:STREAM", $"update[{updateCount}] TOOL_CALL: {calls}");
                        }
                        else if (update.FinishReason != null)
                        {
                            AILogger.LogEvent("RAG:STREAM", $"update[{updateCount}] FINISH reason={update.FinishReason}");
                        }
                        await channel.Writer.WriteAsync(update, cancellationToken);
                    }
                    AILogger.LogEvent("RAG", $"Inner streaming complete — {updateCount} update(s) forwarded");
                }
                catch (Exception ex)
                {
                    AILogger.LogError("RAG", ex);
                    Tracing.Tracer.LogError(ex);
                    channel.Writer.TryComplete(ex);
                    return;
                }
                channel.Writer.TryComplete();
            }, cancellationToken);

            await foreach (var update in channel.Reader.ReadAllAsync(cancellationToken))
                yield return update;
        }

        // ── RAG augmentation ─────────────────────────────────────────────────

        private async Task<IList<ChatMessage>> AugmentWithRagAsync(
            IEnumerable<ChatMessage> messages,
            CancellationToken ct)
        {
            var rawList = messages.ToList();
            AILogger.LogEvent("RAG:SANITIZE", $"Before sanitize: {rawList.Count} message(s)");
            var list = SanitizeToolCallHistory(rawList);
            list = SanitizeHistory(list);
            if (list.Count != rawList.Count)
                AILogger.LogEvent("RAG:SANITIZE", $"After full sanitize: {rawList.Count} → {list.Count} message(s)");

            var lastUser = list.LastOrDefault(m => m.Role == ChatRole.User);
            if (lastUser == null) { AILogger.LogEvent("RAG", "No user message found — skipping RAG"); return list; }

            var userText = string.Concat(lastUser.Contents.OfType<TextContent>().Select(t => t.Text));
            if (string.IsNullOrWhiteSpace(userText)) { AILogger.LogEvent("RAG", "User message is empty — skipping RAG"); return list; }

            AILogger.LogEvent("RAG", $"Searching RAG for: {userText[..Math.Min(120, userText.Length)]}");

            IReadOnlyList<string> chunks;
            try
            {
                // Use the raw provider client (no tools, no function-invoking middleware) for
                // query planning so that classify / HyDE / decompose calls are plain LLM calls.
                chunks = await _ragSearch.SearchAsync(userText, agentOid: _agentOid, chatClient: _plannerClient, ct: ct);
            }
            catch (Exception ex)
            {
                AILogger.LogError("RAG:SEARCH", ex);
                return list; // RAG failure is non-fatal
            }

            AILogger.LogEvent("RAG", $"RAG returned {chunks.Count} chunk(s)");
            if (chunks.Count == 0) return list;

            var sb = new System.Text.StringBuilder();
            sb.AppendLine("## Relevant Knowledge Base Excerpts");
            sb.AppendLine("The following excerpts from uploaded documents are relevant to the current question.");
            sb.AppendLine("Prioritise this information when formulating your answer.");
            foreach (var chunk in chunks)
            {
                sb.AppendLine();
                sb.AppendLine(chunk);
            }

            int insertAt = list.LastIndexOf(lastUser);
            list.Insert(insertAt, new ChatMessage(ChatRole.System, sb.ToString()));
            return list;
        }

        /// <summary>
        /// Removes assistant messages that contain tool_calls but are not immediately followed
        /// by the corresponding tool-role response messages. OpenAI rejects such sequences with
        /// HTTP 400. This can happen when AIChatControl does not persist the full tool exchange
        /// in its conversation history across turns.
        /// </summary>
        /// <summary>
        /// Removes messages that would cause an OpenAI HTTP 400:
        /// - User messages whose text is the AIChatControl error sentinel
        ///   "Something went wrong. Please try again in a few moments."
        ///   (these are injected by DevExpress into the history after a failed turn).
        /// - Empty assistant messages (blank content with no tool calls).
        /// Both patterns corrupt the conversation history and confuse the model.
        /// </summary>
        private static List<ChatMessage> SanitizeHistory(List<ChatMessage> messages)
        {
            const string errorSentinel = "Something went wrong. Please try again in a few moments.";

            var result = new List<ChatMessage>(messages.Count);
            for (int i = 0; i < messages.Count; i++)
            {
                var msg = messages[i];

                // Drop AIChatControl error sentinel user messages.
                if (msg.Role == ChatRole.User)
                {
                    var text = string.Concat(msg.Contents.OfType<TextContent>().Select(t => t.Text)).Trim();
                    if (text == errorSentinel)
                    {
                        AILogger.LogEvent("RAG:SANITIZE", $"Dropping error-sentinel user message at [{i}]");
                        continue;
                    }
                }

                // Drop empty assistant messages (no text, no tool calls).
                if (msg.Role == ChatRole.Assistant)
                {
                    var text = string.Concat(msg.Contents.OfType<TextContent>().Select(t => t.Text)).Trim();
                    bool hasToolCalls = msg.Contents.OfType<FunctionCallContent>().Any();
                    if (string.IsNullOrEmpty(text) && !hasToolCalls)
                    {
                        AILogger.LogEvent("RAG:SANITIZE", $"Dropping empty assistant message at [{i}]");
                        continue;
                    }
                }

                result.Add(msg);
            }
            return result;
        }

        private static List<ChatMessage> SanitizeToolCallHistory(List<ChatMessage> messages)
        {
            var result = new List<ChatMessage>(messages.Count);
            for (int i = 0; i < messages.Count; i++)
            {
                var msg = messages[i];
                if (msg.Role == ChatRole.Assistant)
                {
                    bool hasFunctionCall = msg.Contents.OfType<FunctionCallContent>().Any();
                    if (hasFunctionCall)
                    {
                        var callIds = msg.Contents.OfType<FunctionCallContent>()
                            .Select(f => f.CallId)
                            .Where(id => id != null)
                            .ToHashSet();

                        int j = i + 1;
                        var respondedIds = new HashSet<string?>();
                        while (j < messages.Count && messages[j].Role == ChatRole.Tool)
                        {
                            foreach (var fc in messages[j].Contents.OfType<FunctionResultContent>())
                                respondedIds.Add(fc.CallId);
                            j++;
                        }

                        if (callIds.Any(id => !respondedIds.Contains(id)))
                        {
                            var missing = string.Join(", ", callIds.Where(id => !respondedIds.Contains(id)));
                            AILogger.LogEvent("RAG:SANITIZE", $"Dropping orphaned tool_call at [{i}], missing responses for: {missing}");
                            i = j - 1;
                            continue;
                        }
                    }
                }
                result.Add(msg);
            }
            return result;
        }
    }
}
