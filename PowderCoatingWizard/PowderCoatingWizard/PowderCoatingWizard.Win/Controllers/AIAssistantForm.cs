using DevExpress.AIIntegration;
using DevExpress.AIIntegration.Blazor.Chat;
using DevExpress.AIIntegration.Blazor.Chat.WebView;
using DevExpress.AIIntegration.WinForms.Chat;
using Markdig;
using Microsoft.AspNetCore.Components;
using DevExpress.ExpressApp;
using DevExpress.ExpressApp.Security;
using DevExpress.XtraEditors;
using Microsoft.Extensions.AI;
using PowderCoatingWizard.Module.BusinessObjects;
using PowderCoatingWizard.Module.BusinessObjects.AI;
using PowderCoatingWizard.Module.Services.AI;
using PowderCoatingWizard.Win.Forms;
using PowderCoatingWizard.Win.Services;
namespace PowderCoatingWizard.Win.Controllers
{
    /// <summary>
    /// Floating AI assistant window containing the DevExpress AIChatControl.
    /// RagChatClient is registered in AIExtensionsContainerDesktop.Default so the
    /// control uses it natively â€” full streaming with thinking indicator and RAG grounding.
    /// The system prompt is injected as a hidden System message after initialization.
    /// </summary>
    public class AIAssistantForm : XtraForm
    {
        private AIChatControl _chat = null!;
        private readonly IObjectSpace _os;
        private readonly IObjectSpaceFactory? _osFactory;
        private readonly LineStage? _stage;
        private readonly AIAgent? _agent;
        private readonly Guid? _agentOid;

        // RagChatClient wraps the real IChatClient and injects RAG context before every call.
        private readonly RagChatClient? _ragClient;
        // Keep original IChatClient for PromptShaperForm's AI Improve feature.
        private readonly IChatClient? _chatClient;
        // Prompt shaper trigger mode loaded from settings.
        private PromptShaperTriggerMode _promptShaperTrigger = PromptShaperTriggerMode.SlashCommand;

        // Saved system prompt reused for context loading
        private string _systemPrompt = string.Empty;

        // Whether we registered the client â€” tracked so Dispose can unregister.
        private bool _clientRegistered;


        // Status label shown at the bottom -- updated when tools are invoked.
        private LabelControl _toolStatusLabel = null!;
        // Persistent chat session â€” null means ephemeral (not saved).
        private AIChatSession? _session;
        // Separate ObjectSpace for session writes to avoid polluting the main _os.
        private readonly IObjectSpace? _sessionOs;

        public AIAssistantForm(
            IChatClient? chatClient,
            IEmbeddingGenerator<string, Embedding<float>>? embeddingGenerator,
            IObjectSpace os,
            IObjectSpaceFactory? osFactory,
            LineStage? stage,
            AIAgent? agent = null,
            AIChatSession? session = null)
        {
            _os = os;
            _osFactory = osFactory;
            _stage = stage;
            _chatClient = chatClient;

            // Store only the Oid
            // OnChatInitialized runs. We reload the agent from our own _os below.
            _agentOid = agent?.Oid;
            _agent = _agentOid.HasValue ? _os.GetObjectByKey<AIAgent>(_agentOid.Value) : null;

            // Session persistence: create a dedicated ObjectSpace for writing session data.
            if (osFactory != null)
            {
                _sessionOs = osFactory.CreateObjectSpace(typeof(AIChatSession));
                if (session != null)
                {
                    // Reload session in our own ObjectSpace so we can safely attach new messages.
                    _session = _sessionOs.GetObjectByKey<AIChatSession>(session.Oid);
                }
                else
                {
                    // A new session will be created lazily on the first message.
                    _session = null;
                }
            }

            if (chatClient != null && embeddingGenerator != null && osFactory != null)
            {
                // Load DB query settings from AIProviderSettings
                var settings = _os.FirstOrDefault<Module.BusinessObjects.AI.AIProviderSettings>(s => true);
                int dbMaxRecords = settings?.DbQueryMaxRecords ?? 50;
                bool dbQueryEnabled = settings?.DbQueryEnabled ?? true;
                _promptShaperTrigger = settings?.PromptShaperTrigger ?? PromptShaperTriggerMode.SlashCommand;

                // Determine which tools are enabled for the current agent (null agent = all enabled)
                bool IsTool(AgentTool tool) => _agent == null || _agent.HasTool(tool);

                // Build the tool list respecting per-agent enablement
                var toolList = new List<Microsoft.Extensions.AI.AITool>();

                if (IsTool(AgentTool.BathData))
                    toolList.Add(Microsoft.Extensions.AI.AIFunctionFactory.Create(new BathDataToolService(_os).GetBathData));

                if (IsTool(AgentTool.Trend))
                    toolList.Add(Microsoft.Extensions.AI.AIFunctionFactory.Create(new MeasurementTrendToolService(_os).GetMeasurementTrend));

                if (IsTool(AgentTool.ThresholdAlert))
                    toolList.Add(Microsoft.Extensions.AI.AIFunctionFactory.Create(new ThresholdAlertToolService(_os).GetThresholdAlerts));

                if (dbQueryEnabled && IsTool(AgentTool.DbQuery))
                {
                    var schema = new SchemaDiscoveryService();
                    var dbQueryService = new DatabaseQueryToolService(_os, schema, dbMaxRecords);
                    toolList.Add(Microsoft.Extensions.AI.AIFunctionFactory.Create(dbQueryService.ListEntities, "list_entities"));
                    toolList.Add(Microsoft.Extensions.AI.AIFunctionFactory.Create(dbQueryService.DescribeEntity, "describe_entity"));
                    toolList.Add(Microsoft.Extensions.AI.AIFunctionFactory.Create(dbQueryService.QueryEntity, "query_entity"));
                }

                // Per-agent model parameters (temperature excluded — not injected to stay compatible with all models)
                int maxTokens = _agent?.MaxTokens ?? 0;
                int? tokens = maxTokens > 0 ? maxTokens : null;
                var tools = toolList.AsReadOnly();

                // Build pipeline so ConfigureOptions runs before FunctionInvokingChatClient.
                // FunctionInvokingChatClient must see the injected tools; otherwise OpenAI returns
                // raw tool_calls to the UI and no local tool invocation happens.
                // RagChatClient wraps everything above so RAG runs first.
                IChatClient pipeline = chatClient
                    .AsBuilder()
                    .ConfigureOptions(o =>
                    {
                        if (tools.Count > 0)
                        {
                            o.Tools = o.Tools != null ? [.. o.Tools, .. tools] : [.. tools];
                            o.ToolMode ??= ChatToolMode.Auto;
                        }
                        if (tokens.HasValue && o.MaxOutputTokens == null) o.MaxOutputTokens = tokens;
                        var toolNames = o.Tools?.Select(t => t is Microsoft.Extensions.AI.AIFunction f ? f.Name : t.GetType().Name).ToList() ?? [];
                        AILogger.LogEvent("PIPELINE:OPTIONS", $"ConfigureOptions applied — tools=[{string.Join(", ", toolNames)}] maxTokens={o.MaxOutputTokens} toolMode={o.ToolMode}");
                    })
                    .UseFunctionInvocation()
                    .Build();
                pipeline = new ToolIndicatorChatClient(pipeline, name => SetToolStatus(name), () => SetToolStatus(null));

                _ragClient = new RagChatClient(pipeline,
                    chatClient,   // raw provider client — for query planning only (no tools injected)
                    new RagSearchService(osFactory, new EmbeddingService(embeddingGenerator)),
                    _agentOid);

                // Register the pipeline so AIChatControl picks it up with full streaming + thinking indicator.
                try { AIExtensionsContainerDesktop.Default.UnregisterChatClient(); } catch { /* none registered */ }
                AIExtensionsContainerDesktop.Default.RegisterChatClient(_ragClient);
                _clientRegistered = true;
                AILogger.LogEvent("INIT", $"AI pipeline registered. Log: {AILogger.GetLogPath()}");
            }

            InitializeForm();
        }


        private void InitializeForm()
        {
            var agentLabel = _agent != null ? $" [{_agent.Name}]" : string.Empty;
            Text = _stage != null
                ? $"AI Assistant{agentLabel} - {_stage.Name}"
                : $"AI Assistant{agentLabel} - Powder Coating Wizard";
            Size = new System.Drawing.Size(900, 700);
            StartPosition = System.Windows.Forms.FormStartPosition.CenterParent;
            MinimizeBox = true;
            MaximizeBox = true;

            _chat = new AIChatControl
            {
                Dock = System.Windows.Forms.DockStyle.Fill,
                ShowHeader = DevExpress.Utils.DefaultBoolean.True,
                HeaderText = "AI Assistant",
                UseStreaming = DevExpress.Utils.DefaultBoolean.True,
                FileUploadEnabled = DevExpress.Utils.DefaultBoolean.True,
                ContentFormat = ResponseContentFormat.Markdown
            };
            _chat.OptionsFileUpload.FileTypeFilter.AddRange([
                "text/plain",
                "application/pdf",
                "image/png",
                "image/jpeg",
                "image/webp",
                "application/vnd.openxmlformats-officedocument.wordprocessingml.document",
                "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet"
            ]);
            _chat.OptionsFileUpload.AllowedFileExtensions.AddRange([
                ".txt",
                ".pdf",
                ".png",
                ".jpg",
                ".jpeg",
                ".webp",
                ".docx",
                ".xlsx"
            ]);
            _chat.OptionsFileUpload.MaxFileCount = 5;
            _chat.OptionsFileUpload.MaxFileSize = 10 * 1024 * 1024;
            _chat.MarkdownConvert += OnMarkdownConvert;

            if (!_clientRegistered)
            {
                _chat.EmptyStateText =
                    "AI is not configured.\n\n" +
                    "Go to AI > AI Provider Settings and fill in Provider / Endpoint / API Key / Model ID, then restart the application.";
            }
            else
            {
                _chat.EmptyStateText = _stage != null
                    ? $"Ready to analyse {_stage.Name}. Ask a question."
                    : "Ready to analyse production line data. Ask a question.";

                _chat.Initialized += OnChatInitialized;
            }

            // Toolbar row.
            var toolbar = new System.Windows.Forms.Panel
            {
                Dock = System.Windows.Forms.DockStyle.Top,
                Height = 36,
                Padding = new System.Windows.Forms.Padding(4, 2, 4, 2)
            };

            var shapeButton = new SimpleButton
            {
                Text = "Shape Prompt",
                Height = 30,
                Width = 130,
                Dock = System.Windows.Forms.DockStyle.Left
            };
            shapeButton.Click += (_, _) => OpenPromptShaper(string.Empty);

            var createCaseStudyButton = new SimpleButton
            {
                Text = "Create Case Study",
                Height = 30,
                Width = 150,
                Dock = System.Windows.Forms.DockStyle.Left
            };
            createCaseStudyButton.Click += (_, _) => CreateCaseStudyFromConversation();

            toolbar.Controls.Add(createCaseStudyButton);
            toolbar.Controls.Add(shapeButton);

            if (_sessionOs != null)
                FormClosing += OnFormClosing;
            _toolStatusLabel = new DevExpress.XtraEditors.LabelControl { Dock = System.Windows.Forms.DockStyle.Bottom, Text = string.Empty, AutoSizeMode = DevExpress.XtraEditors.LabelAutoSizeMode.None, Height = 22 };

            Controls.Add(_chat);
            Controls.Add(_toolStatusLabel);
            Controls.Add(toolbar);
        }

        // Prompt Shaper

        private void OpenPromptShaper(string initialText)
        {
            using var shaper = new PromptShaperForm(initialText, _chatClient);
            if (shaper.ShowDialog(this) == System.Windows.Forms.DialogResult.OK
                && !string.IsNullOrWhiteSpace(shaper.FinalPrompt))
            {
                _ = _chat.SendMessage(shaper.FinalPrompt, ChatRole.User);
            }
        }

        private void CreateCaseStudyFromConversation()
        {
            if (_osFactory == null)
            {
                XtraMessageBox.Show(
                    "Cannot create a case study because ObjectSpaceFactory is not available.",
                    "AI Assistant",
                    System.Windows.Forms.MessageBoxButtons.OK,
                    System.Windows.Forms.MessageBoxIcon.Warning);
                return;
            }

            var messages = _chat.SaveMessages()?.OfType<BlazorChatMessage>().ToList();
            var visibleMessages = messages?
                .Where(m => m.Role != ChatMessageRole.System && !string.IsNullOrWhiteSpace(m.Content))
                .ToList();

            if (visibleMessages == null || visibleMessages.Count == 0)
            {
                XtraMessageBox.Show(
                    "There are no conversation messages to save as a case study.",
                    "AI Assistant",
                    System.Windows.Forms.MessageBoxButtons.OK,
                    System.Windows.Forms.MessageBoxIcon.Information);
                return;
            }

            try
            {
                using var caseOs = _osFactory.CreateObjectSpace(typeof(AICaseStudy));
                var caseStudy = caseOs.CreateObject<AICaseStudy>();
                caseStudy.Title = BuildCaseStudyTitle(visibleMessages);
                caseStudy.Tags = "ai chat, draft, case study";
                caseStudy.Status = CaseStudyStatus.Draft;
                caseStudy.CreatedAt = DateTime.UtcNow;
                caseStudy.OccurredOn = DateTime.Today;
                caseStudy.Stage = _stage != null ? caseOs.GetObjectByKey<LineStage>(_stage.Oid) : null;
                caseStudy.ProblemDescription = BuildConversationTranscript(visibleMessages);
                caseStudy.LessonsLearned = "Review and complete this draft case study before approving it for RAG embedding.";

                caseOs.CommitChanges();

                XtraMessageBox.Show(
                    $"Draft case study created:\n\n{caseStudy.Title}\n\nOpen AI > Case Studies to review, complete, and approve it.",
                    "AI Assistant",
                    System.Windows.Forms.MessageBoxButtons.OK,
                    System.Windows.Forms.MessageBoxIcon.Information);
            }
            catch (Exception ex)
            {
                DevExpress.Persistent.Base.Tracing.Tracer.LogError(ex);
                XtraMessageBox.Show(
                    $"Could not create case study:\n\n{ex.Message}",
                    "AI Assistant",
                    System.Windows.Forms.MessageBoxButtons.OK,
                    System.Windows.Forms.MessageBoxIcon.Error);
            }
        }

        private static string BuildCaseStudyTitle(IReadOnlyList<BlazorChatMessage> messages)
        {
            var firstUser = messages.FirstOrDefault(m => m.Role == ChatMessageRole.User)?.Content?.Trim();
            if (string.IsNullOrWhiteSpace(firstUser))
                return $"AI chat case study - {DateTime.Now:yyyy-MM-dd HH:mm}";

            firstUser = firstUser.ReplaceLineEndings(" ");
            return firstUser.Length <= 120 ? firstUser : firstUser[..120];
        }

        private static string BuildConversationTranscript(IEnumerable<BlazorChatMessage> messages)
        {
            var sb = new System.Text.StringBuilder();
            sb.AppendLine("Created from AI assistant conversation.");
            sb.AppendLine();
            sb.AppendLine("## Conversation Transcript");

            foreach (var message in messages)
            {
                var role = message.Role == ChatMessageRole.Assistant ? "Assistant" : "User";
                sb.AppendLine();
                sb.AppendLine($"### {role}");
                sb.AppendLine(message.Content?.Trim());
            }

            return sb.ToString().TrimEnd();
        }

        private static void OnMarkdownConvert(object? sender, AIChatControlMarkdownConvertEventArgs e)
        {
            var html = Markdown.ToHtml(e.MarkdownText ?? string.Empty, new MarkdownPipelineBuilder().UseAdvancedExtensions().Build());
            e.HtmlText = new MarkupString(html);
        }

        // ------------------------------------------------------------------
        // Session persistence helpers
        // ------------------------------------------------------------------

        private void OnFormClosing(object? sender, System.Windows.Forms.FormClosingEventArgs e)
        {
            // SaveMessages returns the full conversation (excluding system messages from LoadMessages).
            var messages = _chat.SaveMessages()?.OfType<BlazorChatMessage>().ToList();
            if (messages == null || messages.Count == 0) return;

            // Only persist non-system visible messages.
            var visible = messages.Where(m => m.Role != ChatMessageRole.System).ToList();
            if (visible.Count == 0) return;

            try
            {
                EnsureSessionCreated();

                // Remove old stored messages (re-save the full history on close).
                var existing = _session!.Messages.ToList();
                foreach (var old in existing)
                    _sessionOs!.Delete(old);

                int order = 0;
                foreach (var m in visible)
                {
                    var msg = _sessionOs!.CreateObject<AIChatSessionMessage>();
                    msg.ChatSession = _session;
                    msg.Role = m.Role == ChatMessageRole.Assistant ? "assistant" : "user";
                    msg.Content = m.Content ?? string.Empty;
                    msg.SentAt = DateTime.UtcNow;
                    msg.SortOrder = order++;
                }

                _session!.UpdatedAt = DateTime.UtcNow;
                if (string.IsNullOrWhiteSpace(_session.Title))
                {
                    var firstUser = visible.FirstOrDefault(m => m.Role == ChatMessageRole.User);
                    if (firstUser != null)
                    {
                        var text = firstUser.Content ?? string.Empty;
                        _session.Title = text.Length > 200 ? text[..200] : text;
                    }
                }

                _sessionOs!.CommitChanges();
            }
            catch (Exception ex)
            {
                DevExpress.Persistent.Base.Tracing.Tracer.LogError(ex);
            }
        }

        private void EnsureSessionCreated()
        {
            if (_session != null || _sessionOs == null) return;

            _session = _sessionOs.CreateObject<AIChatSession>();
            _session.CreatedAt = DateTime.UtcNow;
            _session.UpdatedAt = DateTime.UtcNow;
            _session.Agent = _agentOid.HasValue
                ? _sessionOs.GetObjectByKey<AIAgent>(_agentOid.Value)
                : null;
            _session.IsPublic = false;

            var currentUserName = SecuritySystem.CurrentUserName;
            if (!string.IsNullOrEmpty(currentUserName))
            {
                var user = _sessionOs.FirstOrDefault<ApplicationUser>(u => u.UserName == currentUserName);
                _session.Owner = user!;
            }
        }

        private async void OnChatInitialized(object? sender, AIChatControlInitializedEventArgs e)
        {
            try
            {
                var sb = new System.Text.StringBuilder();

                // 1. System identity + priority rules (use agent override when available)
                if (_agent != null && !string.IsNullOrWhiteSpace(_agent.SystemPromptOverride))
                    sb.AppendLine(_agent.SystemPromptOverride);
                else
                    sb.AppendLine(DomainAIContextBuilder.BuildSystemPrompt());

                // Formatting instruction â€” the chat control renders Markdown natively.
                sb.AppendLine();
                sb.AppendLine("Always format your responses using Markdown: use **bold** for important values, `code` for parameter names, ## headings for sections, bullet lists for recommendations, and tables where appropriate.");

                // 2. Standards / SOPs â€” scoped to agent or global
                if (_osFactory != null)
                {
                    using var instructionOs = _osFactory.CreateObjectSpace(typeof(AIInstructionSet));
                    string instructions = _agent != null && _agent.Instructions.Count > 0
                        ? await DomainAIContextBuilder.BuildInstructionsFromSetsAsync(
                            _agent.Instructions.Select(i => i.Oid).ToList(), instructionOs).ConfigureAwait(true)
                        : await DomainAIContextBuilder.BuildInstructionsContextAsync(
                            instructionOs, _stage?.Line).ConfigureAwait(true);
                    if (!string.IsNullOrWhiteSpace(instructions))
                    {
                        sb.AppendLine();
                        sb.AppendLine(instructions);
                    }
                }

                // 3. Live database data â€” HIGHEST PRIORITY
                if (_stage != null)
                {
                    sb.AppendLine();
                    sb.AppendLine(DomainAIContextBuilder.BuildStageContext(_os, _stage, 5));
                    sb.AppendLine();
                    sb.AppendLine(DomainAIContextBuilder.BuildHistoricalContext(
                        _os, _stage, DateTime.Today.AddDays(-90), DateTime.Today));
                }

                _systemPrompt = sb.ToString();

                // Seed the conversation with the system prompt as a hidden message.
                // The RagChatClient will receive this context on every subsequent turn.
                var seedMessages = new List<BlazorChatMessage>
                {
                    new BlazorChatMessage(ChatRole.System, _systemPrompt)
                };

                // If restoring an existing session, replay saved messages.
                if (_session != null && _session.Messages.Count > 0)
                {
                    foreach (var m in _session.Messages.OrderBy(m => m.SortOrder))
                    {
                        var role = m.Role switch
                        {
                            "assistant" => ChatRole.Assistant,
                            "system"    => ChatRole.System,
                            _           => ChatRole.User
                        };
                        seedMessages.Add(new BlazorChatMessage(role, m.Content ?? string.Empty));
                    }
                    Text = $"{Text} â€” (Restored)";
                }

                _chat.LoadMessages(seedMessages.ToArray());
            }
            catch (Exception ex)
            {
                DevExpress.Persistent.Base.Tracing.Tracer.LogError(ex);
                XtraMessageBox.Show(
                    $"AI Assistant initialization error:\n\n{ex.Message}\n\n{ex.InnerException?.Message}",
                    "AI Assistant",
                    System.Windows.Forms.MessageBoxButtons.OK,
                    System.Windows.Forms.MessageBoxIcon.Warning);
            }
        }


        private void SetToolStatus(string? toolName) { if (_toolStatusLabel == null) return; if (InvokeRequired) { Invoke(() => SetToolStatus(toolName)); return; } _toolStatusLabel.Text = toolName != null ? $"Working: {toolName}..." : string.Empty; }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                if (_clientRegistered)
                {
                    try { AIExtensionsContainerDesktop.Default.UnregisterChatClient(); }
                    catch { /* best-effort */ }
                    _clientRegistered = false;
                }
                _ragClient?.Dispose();
                _sessionOs?.Dispose();
                _os?.Dispose();
                _chat?.Dispose();
            }
            base.Dispose(disposing);
        }
    }
}
