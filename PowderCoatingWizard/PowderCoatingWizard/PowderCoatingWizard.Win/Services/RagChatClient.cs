using Microsoft.Extensions.AI;
using PowderCoatingWizard.Module.Services.AI;
using System.Runtime.CompilerServices;

namespace PowderCoatingWizard.Win.Services
{
    /// <summary>
    /// IChatClient decorator that intercepts every request and:
    /// 1. Prepends a RAG context system message built from the user's last question.
    /// 2. Registers the BathDataTool so the LLM can dynamically query live bath data.
    /// 3. Handles the tool-call execution loop so the model never gets an unanswered tool_call_id.
    /// Streaming is fully forwarded to the inner client so the AIChatControl
    /// typing indicator and token-by-token rendering work natively.
    /// </summary>
    public sealed class RagChatClient : IChatClient
    {
        private readonly IChatClient _inner;
        private readonly RagSearchService _ragSearch;
        private readonly Guid? _agentOid;
        private readonly IReadOnlyList<AITool> _tools;

        public RagChatClient(IChatClient inner, RagSearchService ragSearch, Guid? agentOid,
            IReadOnlyList<AITool>? tools = null)
        {
            _inner = inner;
            _ragSearch = ragSearch;
            _agentOid = agentOid;
            _tools = tools ?? [];
        }

        public async Task<ChatResponse> GetResponseAsync(
            IEnumerable<ChatMessage> messages,
            ChatOptions? options = null,
            CancellationToken cancellationToken = default)
        {
            var augmented = await AugmentWithRagAsync(messages, cancellationToken);
            return await RunWithToolLoopAsync(augmented, MergeOptions(options), cancellationToken);
        }

        public async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
            IEnumerable<ChatMessage> messages,
            ChatOptions? options = null,
            [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            var augmented = await AugmentWithRagAsync(messages, cancellationToken);
            var mergedOptions = MergeOptions(options);

            // Execute any tool-call rounds synchronously (non-streaming) first,
            // then stream only the final text response to the UI.
            var (finalMessages, finalOptions) = await ExecuteToolRoundsAsync(
                augmented, mergedOptions, cancellationToken);

            await foreach (var update in _inner.GetStreamingResponseAsync(
                finalMessages, finalOptions, cancellationToken))
                yield return update;
        }

        public object? GetService(Type serviceType, object? serviceKey = null)
            => _inner.GetService(serviceType, serviceKey);

        public void Dispose() => _inner.Dispose();

        // ── Tool call loop ───────────────────────────────────────────────────

        /// <summary>
        /// Runs non-streaming rounds until there are no more tool calls, then returns
        /// the final ChatResponse. Used by GetResponseAsync.
        /// </summary>
        private async Task<ChatResponse> RunWithToolLoopAsync(
            IList<ChatMessage> messages,
            ChatOptions? options,
            CancellationToken ct)
        {
            var (finalMessages, finalOptions) = await ExecuteToolRoundsAsync(messages, options, ct);
            return await _inner.GetResponseAsync(finalMessages, finalOptions, ct);
        }

        /// <summary>
        /// Executes all tool-call rounds (without streaming) and returns the message list
        /// and options ready for the final response call. If no tools are registered, or the
        /// model does not invoke any tools, returns the inputs unchanged.
        /// </summary>
        private async Task<(IList<ChatMessage> messages, ChatOptions? options)> ExecuteToolRoundsAsync(
            IList<ChatMessage> messages,
            ChatOptions? options,
            CancellationToken ct)
        {
            if (_tools.Count == 0)
                return (messages, options);

            const int maxRounds = 5; // safety cap — prevent infinite loops
            var current = messages.ToList();

            for (int round = 0; round < maxRounds; round++)
            {
                var response = await _inner.GetResponseAsync(current, options, ct);

                // Collect tool calls from the response
                var toolCalls = response.Messages
                    .SelectMany(m => m.Contents.OfType<FunctionCallContent>())
                    .ToList();

                if (toolCalls.Count == 0)
                {
                    // No tool calls — add the assistant messages and stop
                    current.AddRange(response.Messages);
                    break;
                }

                // Add the assistant message(s) that contain the tool_calls
                current.AddRange(response.Messages);

                // Execute each tool call and add a tool-result message for each
                foreach (var call in toolCalls)
                {
                    string result;
                    try
                    {
                        result = await InvokeToolAsync(call, ct);
                    }
                    catch (Exception ex)
                    {
                        result = $"Tool error: {ex.Message}";
                    }

                    var toolResultMessage = new ChatMessage(ChatRole.Tool,
                    [
                        new FunctionResultContent(call.CallId, result)
                    ]);
                    current.Add(toolResultMessage);
                }

                // Continue — model may want to call more tools or produce the final answer
            }

            // For the final streaming/non-streaming call we do NOT include Tools in options
            // so the model produces a plain text answer without trying to call more tools.
            var finalOptions = options != null
                ? new ChatOptions
                {
                    Temperature = options.Temperature,
                    MaxOutputTokens = options.MaxOutputTokens,
                    TopP = options.TopP,
                    FrequencyPenalty = options.FrequencyPenalty,
                    PresencePenalty = options.PresencePenalty,
                    StopSequences = options.StopSequences,
                    ModelId = options.ModelId
                    // Tools intentionally omitted
                }
                : null;

            return (current, finalOptions);
        }

        /// <summary>Dispatches a single tool call to the matching registered AIFunction.</summary>
        private async Task<string> InvokeToolAsync(FunctionCallContent call, CancellationToken ct)
        {
            var fn = _tools.OfType<AIFunction>().FirstOrDefault(f => f.Name == call.Name);
            if (fn != null)
            {
                AIFunctionArguments? args = call.Arguments != null
                    ? new AIFunctionArguments(call.Arguments)
                    : null;
                var result = await fn.InvokeAsync(args, ct);
                return result?.ToString() ?? string.Empty;
            }
            return $"Unknown tool: {call.Name}";
        }

        // ── ChatOptions merge ────────────────────────────────────────────────

        private ChatOptions? MergeOptions(ChatOptions? options)
        {
            if (_tools.Count == 0) return options;

            return options != null
                ? new ChatOptions
                {
                    Temperature = options.Temperature,
                    MaxOutputTokens = options.MaxOutputTokens,
                    TopP = options.TopP,
                    FrequencyPenalty = options.FrequencyPenalty,
                    PresencePenalty = options.PresencePenalty,
                    StopSequences = options.StopSequences,
                    ModelId = options.ModelId,
                    Tools = options.Tools != null
                        ? [.. options.Tools, .. _tools]
                        : [.. _tools],
                    ToolMode = options.ToolMode ?? ChatToolMode.Auto
                }
                : new ChatOptions
                {
                    Tools = [.. _tools],
                    ToolMode = ChatToolMode.Auto
                };
        }

        // ── RAG augmentation ─────────────────────────────────────────────────

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
