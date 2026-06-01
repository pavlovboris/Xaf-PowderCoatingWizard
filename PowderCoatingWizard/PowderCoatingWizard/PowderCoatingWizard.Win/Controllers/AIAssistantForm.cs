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
        private readonly string? _connectionString;

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


        // Status label shown in the toolbar -- updated when tools are invoked.
        private LabelControl _toolStatusLabel = null!;
        // Persistent right-side history of tool calls.
        private ListBoxControl _toolHistoryList = null!;
        private System.Windows.Forms.SplitContainer _mainSplitContainer = null!;
        private string? _activeToolName;
        private AssistantWindowState? _windowState;
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
            AIChatSession? session = null,
            string? connectionString = null)
        {
            _os = os;
            _osFactory = osFactory;
            _stage = stage;
            _chatClient = chatClient;
            _connectionString = connectionString;

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
                    var sqlSchema = new SqlServerSchemaProvider(_connectionString, schema.Schema.Entities.SelectMany(e => new[] { e.Name, e.ClrType.FullName ?? e.Name, e.TableName }));
                    var sqlExecutor = new SafeSqlExecutor(_connectionString);
                    var enumLookupService = new EnumLookupToolService(schema);
                    var dbChatService = new DatabaseChatInsightService(chatClient, sqlSchema, sqlExecutor, dbMaxRecords);
                    toolList.Add(Microsoft.Extensions.AI.AIFunctionFactory.Create(dbChatService.GetDatabaseInsight, "get_database_insight"));
                    toolList.Add(Microsoft.Extensions.AI.AIFunctionFactory.Create(dbChatService.GetNextDatabaseInsightPage, "get_next_database_insight_page"));
                    toolList.Add(Microsoft.Extensions.AI.AIFunctionFactory.Create(enumLookupService.GetEnumMappings, "get_enum_mappings"));
                    AILogger.LogEvent("DBTOOLS", "Legacy database query tools archived: list_entities, describe_entity, and query_entity are not registered.");
                }

                // Per-agent model parameters (temperature excluded — not injected to stay compatible with all models)
                int maxTokens = _agent?.MaxTokens ?? 0;
                int? tokens = maxTokens > 0 ? maxTokens : null;
                var tools = toolList.AsReadOnly();
                var allowedSkills = _agent?.EnabledSkills.Select(s => s.SkillName).ToList();

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
                    _agentOid,
                    allowedSkills,
                    AddRagHistoryEntry);

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
            _windowState = AssistantWindowState.Load();
            Size = _windowState.Size ?? new System.Drawing.Size(1100, 700);
            StartPosition = System.Windows.Forms.FormStartPosition.CenterParent;
            MinimizeBox = true;
            MaximizeBox = true;
            MinimumSize = new System.Drawing.Size(850, 520);

            if (_windowState.Location.HasValue)
            {
                StartPosition = System.Windows.Forms.FormStartPosition.Manual;
                Location = _windowState.Location.Value;
            }

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

            _mainSplitContainer = new System.Windows.Forms.SplitContainer
            {
                Dock = System.Windows.Forms.DockStyle.Fill,
                Orientation = System.Windows.Forms.Orientation.Vertical,
                FixedPanel = System.Windows.Forms.FixedPanel.Panel2
            };

            var toolHistoryPanel = new DevExpress.XtraEditors.GroupControl
            {
                Text = "Tool History",
                Dock = System.Windows.Forms.DockStyle.Fill
            };

            _toolHistoryList = new DevExpress.XtraEditors.ListBoxControl
            {
                Dock = System.Windows.Forms.DockStyle.Fill,
                HorizontalScrollbar = true
            };

            toolHistoryPanel.Controls.Add(_toolHistoryList);
            _mainSplitContainer.Panel1.Controls.Add(_chat);
            _mainSplitContainer.Panel2.Controls.Add(toolHistoryPanel);
            Shown += (_, _) => ConfigureMainSplitter();

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

            _toolStatusLabel = new DevExpress.XtraEditors.LabelControl
            {
                Dock = System.Windows.Forms.DockStyle.Right,
                Text = "No tool running",
                AutoSizeMode = DevExpress.XtraEditors.LabelAutoSizeMode.None,
                Width = 260,
                Appearance =
                {
                    TextOptions = { HAlignment = DevExpress.Utils.HorzAlignment.Center }
                },
                Padding = new System.Windows.Forms.Padding(8, 6, 8, 0),
                BorderStyle = DevExpress.XtraEditors.Controls.BorderStyles.Simple
            };

            toolbar.Controls.Add(createCaseStudyButton);
            toolbar.Controls.Add(shapeButton);
            toolbar.Controls.Add(_toolStatusLabel);

            if (_sessionOs != null)
                FormClosing += OnFormClosing;
            FormClosing += (_, _) => SaveAssistantWindowState();

            Controls.Add(_mainSplitContainer);
            Controls.Add(toolbar);
        }

        private void ConfigureMainSplitter()
        {
            const int chatMinWidth = 520;
            const int historyMinWidth = 220;
            const int defaultHistoryWidth = 300;

            if (!_mainSplitContainer.IsHandleCreated)
                return;

            _mainSplitContainer.Panel1MinSize = chatMinWidth;
            _mainSplitContainer.Panel2MinSize = historyMinWidth;

            var historyWidth = _windowState?.ToolHistoryWidth ?? defaultHistoryWidth;
            historyWidth = Math.Max(historyMinWidth, Math.Min(historyWidth, Math.Max(historyMinWidth, _mainSplitContainer.Width - chatMinWidth)));
            _mainSplitContainer.SplitterDistance = Math.Max(chatMinWidth, _mainSplitContainer.Width - historyWidth);
        }

        private void SaveAssistantWindowState()
        {
            var state = _windowState ?? new AssistantWindowState();
            if (WindowState == System.Windows.Forms.FormWindowState.Normal)
            {
                state.Size = Size;
                state.Location = Location;
            }

            if (_mainSplitContainer?.IsHandleCreated == true)
                state.ToolHistoryWidth = _mainSplitContainer.Panel2.Width;

            state.Save();
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


        private void SetToolStatus(string? toolName)
        {
            if (_toolStatusLabel == null) return;
            if (InvokeRequired) { Invoke(() => SetToolStatus(toolName)); return; }

            bool running = !string.IsNullOrWhiteSpace(toolName);
            _toolStatusLabel.Text = running ? $"Tool running: {toolName}" : "No tool running";
            _toolStatusLabel.Appearance.BackColor = running
                ? System.Drawing.Color.FromArgb(255, 244, 204)
                : System.Drawing.Color.FromArgb(235, 245, 235);
            _toolStatusLabel.Appearance.ForeColor = running
                ? System.Drawing.Color.FromArgb(120, 80, 0)
                : System.Drawing.Color.FromArgb(30, 100, 30);

            AddToolHistoryEntry(toolName);
        }

        private void AddToolHistoryEntry(string? toolName)
        {
            if (_toolHistoryList == null) return;

            var now = DateTime.Now.ToString("HH:mm:ss");
            if (!string.IsNullOrWhiteSpace(toolName))
            {
                _activeToolName = toolName;
                _toolHistoryList.Items.Insert(0, $"{now}  START  {toolName}");
                return;
            }

            if (!string.IsNullOrWhiteSpace(_activeToolName))
            {
                _toolHistoryList.Items.Insert(0, $"{now}  END    {_activeToolName}");
                _activeToolName = null;
            }
        }

        private void AddRagHistoryEntry(string message)
        {
            if (_toolHistoryList == null) return;
            if (InvokeRequired) { Invoke(() => AddRagHistoryEntry(message)); return; }

            var now = DateTime.Now.ToString("HH:mm:ss");
            _toolHistoryList.Items.Insert(0, $"{now}  RAG    {message}");
        }

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

        private sealed class AssistantWindowState
        {
            private static readonly string FilePath = System.IO.Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "PowderCoatingWizard",
                "AIAssistantWindowState.txt");

            public System.Drawing.Size? Size { get; set; }
            public System.Drawing.Point? Location { get; set; }
            public int? ToolHistoryWidth { get; set; }

            public static AssistantWindowState Load()
            {
                var state = new AssistantWindowState();
                try
                {
                    if (!System.IO.File.Exists(FilePath))
                        return state;

                    foreach (var line in System.IO.File.ReadAllLines(FilePath))
                    {
                        var parts = line.Split('=', 2);
                        if (parts.Length != 2) continue;

                        if (parts[0] == "Size" && TryReadPair(parts[1], out var width, out var height))
                            state.Size = new System.Drawing.Size(width, height);
                        else if (parts[0] == "Location" && TryReadPair(parts[1], out var x, out var y))
                            state.Location = new System.Drawing.Point(x, y);
                        else if (parts[0] == "ToolHistoryWidth" && int.TryParse(parts[1], out var toolHistoryWidth))
                            state.ToolHistoryWidth = toolHistoryWidth;
                    }
                }
                catch (Exception ex)
                {
                    DevExpress.Persistent.Base.Tracing.Tracer.LogError(ex);
                }

                return state;
            }

            public void Save()
            {
                try
                {
                    var directory = System.IO.Path.GetDirectoryName(FilePath);
                    if (!string.IsNullOrWhiteSpace(directory))
                        System.IO.Directory.CreateDirectory(directory);

                    var lines = new List<string>();
                    if (Size.HasValue) lines.Add($"Size={Size.Value.Width},{Size.Value.Height}");
                    if (Location.HasValue) lines.Add($"Location={Location.Value.X},{Location.Value.Y}");
                    if (ToolHistoryWidth.HasValue) lines.Add($"ToolHistoryWidth={ToolHistoryWidth.Value}");
                    System.IO.File.WriteAllLines(FilePath, lines);
                }
                catch (Exception ex)
                {
                    DevExpress.Persistent.Base.Tracing.Tracer.LogError(ex);
                }
            }

            private static bool TryReadPair(string value, out int first, out int second)
            {
                first = 0;
                second = 0;
                var parts = value.Split(',', 2);
                return parts.Length == 2
                    && int.TryParse(parts[0], out first)
                    && int.TryParse(parts[1], out second);
            }
        }
    }
}
