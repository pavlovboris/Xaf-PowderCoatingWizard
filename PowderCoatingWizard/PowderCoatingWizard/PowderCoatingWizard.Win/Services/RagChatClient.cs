using DevExpress.Persistent.Base;
using Microsoft.Extensions.AI;
using PowderCoatingWizard.Module.BusinessObjects.AI;
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
        private readonly AIIntentRouterService _intentRouter;
        private readonly AISkillRouterService _skillRouter;
        private readonly AutomaticRagRoutingPolicyService _routingPolicy;
        private readonly Guid? _agentOid;
        private readonly Action<string>? _onRagStatus;

        public RagChatClient(
            IChatClient inner,
            IChatClient plannerClient,
            RagSearchService ragSearch,
            AutomaticRagRoutingPolicyService routingPolicy,
            Guid? agentOid,
            IEnumerable<AgentSkill>? allowedSkills = null,
            Action<string>? onRagStatus = null)
            : base(inner)
        {
            _plannerClient = plannerClient;
            _ragSearch = ragSearch;
            _intentRouter = new AIIntentRouterService(plannerClient);
            _skillRouter = new AISkillRouterService(allowedSkills);
            _routingPolicy = routingPolicy;
            _agentOid = agentOid;
            _onRagStatus = onRagStatus;
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

            var routingDecision = _routingPolicy.Classify(userText);
            if (routingDecision == AutomaticRagRoutingDecision.Context)
            {
                AILogger.LogEvent("RAG", "Skipping intent classification and automatic RAG for context/tool/capability request");
                NotifyRagStatus("SKIP   context/tool request");
                AddContextToolInstruction(list, lastUser);
                return list;
            }
            if (routingDecision == AutomaticRagRoutingDecision.Database)
            {
                AILogger.LogEvent("RAG", "Skipping automatic RAG for database-preferred request");
                NotifyRagStatus("SKIP   database preferred");
                AddDatabaseToolInstruction(list, lastUser);
                return list;
            }

            NotifyRagStatus("START  Classify intent");
            var intent = routingDecision switch
            {
                AutomaticRagRoutingDecision.Knowledge => AIQueryIntent.DocumentQuestion,
                AutomaticRagRoutingDecision.Hybrid => AIQueryIntent.HybridInvestigation,
                _ => await _intentRouter.ClassifyAsync(userText, ct)
            };
            AILogger.LogEvent("INTENT", $"Classified as {intent}: {userText[..Math.Min(120, userText.Length)]}");
            NotifyRagStatus($"INTENT {intent}");

            var skill = _skillRouter.Route(userText, intent);
            AILogger.LogEvent("SKILL", $"Selected {skill} for intent {intent}");
            NotifyRagStatus($"SKILL  {skill}");

            AddIntentInstruction(list, lastUser, intent);
            AddSkillInstruction(list, lastUser, skill);

            if (intent == AIQueryIntent.GeneralChat || intent == AIQueryIntent.DatabaseQuestion)
            {
                AILogger.LogEvent("RAG", $"Skipping RAG for intent {intent}");
                NotifyRagStatus($"SKIP   {intent}");
                return list;
            }

            AILogger.LogEvent("RAG", $"Searching RAG for: {userText[..Math.Min(120, userText.Length)]}");
            NotifyRagStatus("START  Search knowledge base");

            IReadOnlyList<RagSearchResult> results;
            try
            {
                // Use the raw provider client (no tools, no function-invoking middleware) for
                // query planning so that classify / HyDE / decompose calls are plain LLM calls.
                results = await _ragSearch.SearchDetailedAsync(userText, agentOid: _agentOid, chatClient: _plannerClient, ct: ct);
            }
            catch (Exception ex)
            {
                AILogger.LogError("RAG:SEARCH", ex);
                NotifyRagStatus("ERROR  Search failed");
                return list; // RAG failure is non-fatal
            }

            AILogger.LogEvent("RAG", $"RAG returned {results.Count} chunk(s)");
            NotifyRagSourceSummary(results);
            NotifyRagStatus($"END    {results.Count} chunk(s)");
            if (results.Count == 0) return list;

            var sb = new System.Text.StringBuilder();
            sb.AppendLine("## Retrieved Evidence");
            sb.AppendLine("Use the following retrieved evidence only when relevant. Cite sources using the provided citation label.");
            sb.AppendLine("If evidence is insufficient, explicitly say what is missing instead of inventing facts.");
            foreach (var result in results)
            {
                sb.AppendLine();
                sb.AppendLine($"### {result.Citation} (score: {result.Score:F3})");
                sb.AppendLine(result.Text);
            }

            int insertAt = list.LastIndexOf(lastUser);
            list.Insert(insertAt, new ChatMessage(ChatRole.System, sb.ToString()));
            return list;
        }

        private static void AddContextToolInstruction(List<ChatMessage> messages, ChatMessage lastUser)
        {
            const string instruction =
                "The user's request is about the current application context, selected object, active screen, or available tools. " +
                "Do not use document retrieval or database analysis first. " +
                "Call get_current_context for current/selected/context requests. " +
                "Call get_tool_policy for tool/capability requests.";

            int insertAt = messages.LastIndexOf(lastUser);
            messages.Insert(insertAt, new ChatMessage(ChatRole.System, instruction));
        }

        private static void AddDatabaseToolInstruction(List<ChatMessage> messages, ChatMessage lastUser)
        {
            const string instruction =
                "The user's request matches this agent's database-preferred routing terms. " +
                "Do not use document retrieval first. Use current context, domain tools, get_database_insight, query_entity, or get_record_context as appropriate. " +
                "Only produce tabular analysis if the user explicitly requested tables or lists.";

            int insertAt = messages.LastIndexOf(lastUser);
            messages.Insert(insertAt, new ChatMessage(ChatRole.System, instruction));
        }

        private void NotifyRagStatus(string message)
        {
            try { _onRagStatus?.Invoke(message); }
            catch { /* UI status is non-critical */ }
        }

        private void NotifyRagSourceSummary(IReadOnlyList<RagSearchResult> results)
        {
            int localDocumentCount = results.Count(r => r.SourceType == "Document");
            int localCaseStudyCount = results.Count(r => r.SourceType == "Case Study");
            int openAiCount = results.Count(r => r.SourceType == "OpenAI Vector Store");

            if (localDocumentCount + localCaseStudyCount > 0)
                NotifyRagStatus($"LOCAL  {localDocumentCount} doc, {localCaseStudyCount} case");

            if (openAiCount > 0)
                NotifyRagStatus($"OPENAI {openAiCount} vector hit(s)");

            if (results.Count > 0)
            {
                var sources = results
                    .GroupBy(r => r.SourceType)
                    .Select(g => $"{g.Key}: {g.Count()}");
                NotifyRagStatus($"MIX    {string.Join(", ", sources)}");
            }
        }

        private static void AddIntentInstruction(List<ChatMessage> messages, ChatMessage lastUser, AIQueryIntent intent)
        {
            var intentInstruction = intent switch
            {
                AIQueryIntent.DatabaseQuestion =>
                    "Intent: DatabaseQuestion. Prefer database/tool evidence over general knowledge, but keep the assistant domain-focused rather than query-focused. Use list_entities and describe_entity for XAF application-model discovery, entity relationships, properties, and enum/display semantics. Use query_entity only for small XAF ObjectSpace-level samples, display values, relationships, or application object semantics. Use get_database_insight for set-based database evidence: counts, aggregates, joins, broad filtering, trends, comparisons, time windows, summaries, and analysis. If database evidence contains enum integer values that need decoding, call get_enum_mappings for the relevant entity/property. Do not expose generated SQL, raw records, or tabular output unless the user explicitly asks for a table, list, report, or record-level output. Use get_next_database_insight_page only when the user explicitly asks for more rows/next page or confirms that additional pages are needed. If required data is missing, say exactly what is missing. Do not invent measurements or limits.",
                AIQueryIntent.DocumentQuestion =>
                    "Intent: DocumentQuestion. Prefer uploaded documents, standards, certificates, SOPs, and retrieved knowledge-base excerpts. If document evidence is insufficient, say so. Do not invent certificate dates, limits, or compliance status.",
                AIQueryIntent.CaseStudyQuestion =>
                    "Intent: CaseStudyQuestion. Prefer approved case studies and lessons learned. Distinguish prior examples from current measured facts. If no similar case is found, say so.",
                AIQueryIntent.HybridInvestigation =>
                    "Intent: HybridInvestigation. Combine database/tool evidence with documents and approved case studies. Clearly separate observed data, document/case-study evidence, inferred cause, uncertainty, and recommended next actions. Do not invent measurements or causes.",
                _ =>
                    "Intent: GeneralChat. Answer conversationally and do not perform document/database analysis unless the user asks for it."
            };

            var instruction = intentInstruction + Environment.NewLine + Environment.NewLine +
                "Professional answer contract: " +
                "Base the answer on explicit evidence from database tools, retrieved documents, or approved case studies. " +
                "Clearly distinguish observed data, retrieved evidence, inference, uncertainty, and recommended next actions. " +
                "When using retrieved evidence, include its citation label. " +
                "When the user refers to this, current, selected, here, active screen, active stage, or similar Bulgarian references such as tuk or tova, call get_current_context before choosing database or document tools. " +
                "Use get_record_context when a specific business object key is known and XAF display values or relationships matter. " +
                "If tool choice is unclear, call get_tool_policy. " +
                "For database work, use XAF entity tools for application-model discovery and small object samples, and use get_database_insight for set-based SQL evidence such as counts, aggregates, joins, broad filtering, trends, comparisons, and summaries. " +
                "Use search_knowledge for targeted document, standard, SOP, certificate, vector-store, or approved case-study evidence, especially after database facts clarify the issue. " +
                "Use investigate_process_issue for coating defects, bath chemistry issues, abnormal measurements, or production-quality investigations that need current bath data, threshold alerts, and trends together. " +
                "Use database evidence internally to complete the user's task; do not expose generated SQL, raw records, or tabular analysis unless the user explicitly asks for a table, list, report, export, or record-level output. " +
                "Use get_enum_mappings only on demand when integer enum values from database evidence need human-readable names. " +
                "Call additional database pages only in exceptional cases when the user explicitly asks for more rows or confirms that more pages are needed. " +
                "If the evidence is missing or insufficient, state what data is needed. " +
                "Do not invent measurements, dates, thresholds, certificate validity, causes, or compliance conclusions.";

            int insertAt = messages.LastIndexOf(lastUser);
            messages.Insert(insertAt, new ChatMessage(ChatRole.System, instruction));
        }

        private static void AddSkillInstruction(List<ChatMessage> messages, ChatMessage lastUser, AgentSkill skill)
        {
            var instruction = skill switch
            {
                AgentSkill.CoatingDefectInvestigation =>
                    "Skill: CoatingDefectInvestigation. Use document and case-study evidence first for coating defects, surface appearance, adhesion, gloss, contamination, stains, corrosion, orange peel, pinholes, craters, and similar quality issues. Use get_database_insight when the user asks about a specific stage, bath, measurement, threshold, trend, time period, or current production data. Keep database output internal unless table/list output is explicitly requested. Structure the answer as: Observed facts, Evidence, Likely causes, Missing data, Next checks, Recommended actions.",
                AgentSkill.ChemicalBathAnalysis =>
                    "Skill: ChemicalBathAnalysis. Focus on bath chemistry, concentration, pH, temperature, dosing, limits, active chemicals, and quality risk. Prefer get_database_insight and domain tools for current or historical bath data, including summaries and aggregate reasoning. Clearly state in-range/out-of-range status, missing measurements, likely process impact, and corrective checks. Do not output tables unless explicitly requested.",
                AgentSkill.ProcessTrendAnalysis =>
                    "Skill: ProcessTrendAnalysis. Prefer trend, measurement, and get_database_insight tools. Use database evidence for summaries, comparisons, aggregate reasoning, direction, drift, abnormal periods, correlations, and uncertainty. Do not infer root cause without supporting evidence. Include time window, parameter names, observed values, and recommended follow-up checks. Return tabular analysis only when the user explicitly asks for a table, list, report, or record-level output.",
                AgentSkill.DocumentCompliance =>
                    "Skill: DocumentCompliance. Prefer retrieved document evidence from local documents and OpenAI Vector Store. Cite sources. Explain what the document says and what it does not prove. Do not invent certificate validity, standard limits, compliance status, dates, or supplier claims.",
                AgentSkill.CaseStudyMatching =>
                    "Skill: CaseStudyMatching. Prefer approved case studies and lessons learned. Compare similarities and differences between prior cases and the user's situation. Distinguish previous examples from current measured facts. If no similar case is found, say so and list what data would help matching.",
                _ =>
                    "Skill: GeneralAnswer. Answer directly and avoid unnecessary database tools or document searches unless the user asks for data, documents, standards, case studies, measurements, or investigation."
            };

            int insertAt = messages.LastIndexOf(lastUser);
            messages.Insert(insertAt, new ChatMessage(ChatRole.System, instruction));
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
