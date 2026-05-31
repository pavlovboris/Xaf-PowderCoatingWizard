using DevExpress.AIIntegration;
using DevExpress.AIIntegration.Blazor.Chat;
using DevExpress.AIIntegration.Blazor.Chat.WebView;
using DevExpress.AIIntegration.WinForms.Chat;
using Markdig;
using Microsoft.AspNetCore.Components;
using DevExpress.ExpressApp;
using DevExpress.XtraEditors;
using Microsoft.Extensions.AI;
using PowderCoatingWizard.Module.BusinessObjects;
using PowderCoatingWizard.Module.BusinessObjects.AI;
using PowderCoatingWizard.Module.Services.AI;
using PowderCoatingWizard.Win.Services;

namespace PowderCoatingWizard.Win.Controllers
{
    /// <summary>
    /// Floating AI assistant window containing the DevExpress AIChatControl.
    /// RagChatClient is registered in AIExtensionsContainerDesktop.Default so the
    /// control uses it natively — full streaming with thinking indicator and RAG grounding.
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
        // Registered into AIExtensionsContainerDesktop.Default so the AIChatControl uses it
        // natively — no MessageSent buffering, real streaming + thinking indicator.
        private readonly RagChatClient? _ragClient;

        // Saved system prompt reused for context loading
        private string _systemPrompt = string.Empty;

        // Whether we registered the client — tracked so Dispose can unregister.
        private bool _clientRegistered;

        public AIAssistantForm(
            IChatClient? chatClient,
            IEmbeddingGenerator<string, Embedding<float>>? embeddingGenerator,
            IObjectSpace os,
            IObjectSpaceFactory? osFactory,
            LineStage? stage,
            AIAgent? agent = null)
        {
            _os = os;
            _osFactory = osFactory;
            _stage = stage;

            // Store only the Oid — the caller's object space may be disposed before
            // OnChatInitialized runs. We reload the agent from our own _os below.
            _agentOid = agent?.Oid;
            _agent = _agentOid.HasValue ? _os.GetObjectByKey<AIAgent>(_agentOid.Value) : null;

            if (chatClient != null && embeddingGenerator != null && osFactory != null)
            {
                _ragClient = new RagChatClient(chatClient,
                    new RagSearchService(osFactory, new EmbeddingService(embeddingGenerator)),
                    _agentOid);

                // Register our RAG-augmented client as the global provider so AIChatControl
                // picks it up natively with streaming and the thinking indicator.
                // Unregister any previously registered client first (e.g. from a prior session).
                try { AIExtensionsContainerDesktop.Default.UnregisterChatClient(); } catch { /* none registered */ }
                AIExtensionsContainerDesktop.Default.RegisterChatClient(_ragClient);
                _clientRegistered = true;
            }

            InitializeForm();
        }

        private void InitializeForm()
        {
            var agentLabel = _agent != null ? $" [{_agent.Name}]" : string.Empty;
            Text = _stage != null
                ? $"AI Assistant{agentLabel} — {_stage.Name}"
                : $"AI Assistant{agentLabel} — Powder Coating Wizard";
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
                ContentFormat = ResponseContentFormat.Markdown
            };
            _chat.MarkdownConvert += OnMarkdownConvert;

            if (!_clientRegistered)
            {
                _chat.EmptyStateText =
                    "⚠️ AI is not configured.\n\n" +
                    "Go to AI → AI Provider Settings and fill in Provider / Endpoint / API Key / Model ID, then restart the application.";
            }
            else
            {
                _chat.EmptyStateText = _stage != null
                    ? $"Ready to analyse {_stage.Name}. Ask a question."
                    : "Ready to analyse production line data. Ask a question.";

                // OnChatInitialized injects the domain system prompt; no MessageSent override
                // needed — streaming is handled natively by the registered RagChatClient.
                _chat.Initialized += OnChatInitialized;
            }

            Controls.Add(_chat);
        }

        private static void OnMarkdownConvert(object? sender, AIChatControlMarkdownConvertEventArgs e)
        {
            var html = Markdown.ToHtml(e.MarkdownText ?? string.Empty, new MarkdownPipelineBuilder().UseAdvancedExtensions().Build());
            e.HtmlText = new MarkupString(html);
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

                // Formatting instruction — the chat control renders Markdown natively.
                sb.AppendLine();
                sb.AppendLine("Always format your responses using Markdown: use **bold** for important values, `code` for parameter names, ## headings for sections, bullet lists for recommendations, and tables where appropriate.");

                // 2. Standards / SOPs — scoped to agent or global
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

                // 3. Live database data — HIGHEST PRIORITY
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
                _chat.LoadMessages(new[]
                {
                    new BlazorChatMessage(ChatRole.System, _systemPrompt)
                });
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
                _os?.Dispose();
                _chat?.Dispose();
            }
            base.Dispose(disposing);
        }
    }
}
