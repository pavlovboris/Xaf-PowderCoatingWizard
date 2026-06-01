using DevExpress.XtraEditors;
using DevExpress.XtraLayout;
using Microsoft.Extensions.AI;

namespace PowderCoatingWizard.Win.Forms
{
    /// <summary>
    /// A two-panel dialog for shaping an AI assistant prompt before sending.
    /// Left panel: structured input fields (topic, analysis type, stage, parameter, date range, free text).
    /// Right panel: live preview of the generated prompt, editable, with an "AI Improve" button.
    /// DialogResult.OK → <see cref="FinalPrompt"/> contains the ready-to-send text.
    /// </summary>
    internal sealed class PromptShaperForm : XtraForm
    {
        // ── Left panel controls ──────────────────────────────────────────────
        private TextEdit _topicEdit = null!;
        private ComboBoxEdit _analysisTypeCombo = null!;
        private TextEdit _stageEdit = null!;
        private TextEdit _parameterEdit = null!;
        private DateEdit _dateFromEdit = null!;
        private DateEdit _dateToEdit = null!;
        private MemoEdit _freeTextEdit = null!;
        private SimpleButton _buildPromptButton = null!;

        // ── Right panel controls ─────────────────────────────────────────────
        private MemoEdit _previewEdit = null!;
        private SimpleButton _aiImproveButton = null!;
        private SimpleButton _okButton = null!;
        private SimpleButton _cancelButton = null!;
        private LabelControl _statusLabel = null!;
        private System.Windows.Forms.SplitContainer _splitContainer = null!;

        private readonly IChatClient? _chatClient;
        private static System.Drawing.Size? _lastSize;
        private static System.Drawing.Point? _lastLocation;
        private static int? _lastSplitterDistance;

        /// <summary>Initial text placed into the Free Text field (e.g. whatever the user typed in chat).</summary>
        private readonly string _initialText;

        /// <summary>The final prompt ready to be sent to the AI assistant.</summary>
        public string FinalPrompt { get; private set; } = string.Empty;

        public PromptShaperForm(string initialText = "", IChatClient? chatClient = null)
        {
            _initialText = initialText;
            _chatClient = chatClient;
            BuildUI();
            _freeTextEdit.Text = _initialText;
            BuildPrompt(); // pre-populate preview
        }

        // ── UI Construction ──────────────────────────────────────────────────

        private void BuildUI()
        {
            Text = "Prompt Shaper";
            Size = _lastSize ?? new System.Drawing.Size(1180, 760);
            StartPosition = System.Windows.Forms.FormStartPosition.CenterParent;
            MinimumSize = new System.Drawing.Size(900, 620);
            FormBorderStyle = System.Windows.Forms.FormBorderStyle.Sizable;

            if (_lastLocation.HasValue)
            {
                StartPosition = System.Windows.Forms.FormStartPosition.Manual;
                Location = _lastLocation.Value;
            }

            _splitContainer = new System.Windows.Forms.SplitContainer
            {
                Dock = System.Windows.Forms.DockStyle.Fill
            };
            Controls.Add(_splitContainer);

            BuildLeftPanel(_splitContainer.Panel1);
            BuildRightPanel(_splitContainer.Panel2);

            Shown += (_, _) => ConfigureSplitter(_splitContainer);
            FormClosing += (_, _) => SaveWindowState();
        }

        private static void ConfigureSplitter(System.Windows.Forms.SplitContainer splitContainer)
        {
            const int panel1Min = 260;
            const int panel2Min = 240;

            var maxDistance = splitContainer.Width - panel2Min;
            if (maxDistance <= panel1Min)
                return;

            var desiredDistance = _lastSplitterDistance ?? 460;
            splitContainer.SplitterDistance = Math.Min(desiredDistance, maxDistance);
            splitContainer.Panel1MinSize = panel1Min;
            splitContainer.Panel2MinSize = panel2Min;
        }

        private void SaveWindowState()
        {
            if (WindowState == System.Windows.Forms.FormWindowState.Normal)
            {
                _lastSize = Size;
                _lastLocation = Location;
            }

            if (_splitContainer.IsHandleCreated)
                _lastSplitterDistance = _splitContainer.SplitterDistance;
        }

        private void BuildLeftPanel(System.Windows.Forms.Panel panel)
        {
            var layout = new LayoutControl { Dock = System.Windows.Forms.DockStyle.Fill, Parent = panel };
            var root = layout.Root;
            root.GroupBordersVisible = false;

            _topicEdit = new TextEdit();
            _analysisTypeCombo = new ComboBoxEdit();
            _analysisTypeCombo.Properties.Items.AddRange([
                "General Analysis",
                "Threshold / Alarm Review",
                "Trend Analysis",
                "Root Cause Investigation",
                "Optimisation Suggestion",
                "Comparison with Case Studies",
                "Custom"
            ]);
            _analysisTypeCombo.SelectedIndex = 0;

            _stageEdit = new TextEdit();
            _parameterEdit = new TextEdit();
            _dateFromEdit = new DateEdit { EditValue = DateTime.Today.AddDays(-7) };
            _dateToEdit = new DateEdit { EditValue = DateTime.Today };
            _freeTextEdit = new MemoEdit { MinimumSize = new System.Drawing.Size(0, 80) };
            _buildPromptButton = new SimpleButton { Text = "▶  Build Preview" };
            _buildPromptButton.Click += (_, _) => BuildPrompt();

            layout.AddItem("Topic / Subject", _topicEdit);
            layout.AddItem("Analysis Type", _analysisTypeCombo);
            layout.AddItem("Stage (optional)", _stageEdit);
            layout.AddItem("Parameter (optional)", _parameterEdit);
            layout.AddItem("Date From", _dateFromEdit);
            layout.AddItem("Date To", _dateToEdit);
            layout.AddItem("Additional Context / Notes", _freeTextEdit);
            layout.AddItem(string.Empty, _buildPromptButton).TextVisible = false;

            layout.BestFit();
        }

        private void BuildRightPanel(System.Windows.Forms.Panel panel)
        {
            var table = new System.Windows.Forms.TableLayoutPanel
            {
                Dock = System.Windows.Forms.DockStyle.Fill,
                ColumnCount = 1,
                RowCount = 4,
                Padding = new System.Windows.Forms.Padding(8)
            };
            table.RowStyles.Add(new System.Windows.Forms.RowStyle(System.Windows.Forms.SizeType.AutoSize));
            table.RowStyles.Add(new System.Windows.Forms.RowStyle(System.Windows.Forms.SizeType.Percent, 100));
            table.RowStyles.Add(new System.Windows.Forms.RowStyle(System.Windows.Forms.SizeType.AutoSize));
            table.RowStyles.Add(new System.Windows.Forms.RowStyle(System.Windows.Forms.SizeType.AutoSize));
            panel.Controls.Add(table);

            var previewLabel = new LabelControl
            {
                Text = "Prompt Preview (editable)",
                AutoSizeMode = LabelAutoSizeMode.Vertical
            };

            _previewEdit = new MemoEdit { Dock = System.Windows.Forms.DockStyle.Fill };
            _aiImproveButton = new SimpleButton { Text = "AI Improve", Width = 120, Height = 30 };
            _aiImproveButton.Enabled = _chatClient != null;
            _aiImproveButton.Click += async (_, _) => await AiImproveAsync();

            _okButton = new SimpleButton { Text = "Send", Width = 90, Height = 30, DialogResult = System.Windows.Forms.DialogResult.None };
            _okButton.Click += (_, _) =>
            {
                FinalPrompt = _previewEdit.Text.Trim();
                if (string.IsNullOrWhiteSpace(FinalPrompt)) return;
                DialogResult = System.Windows.Forms.DialogResult.OK;
                Close();
            };
            _cancelButton = new SimpleButton { Text = "Cancel", Width = 90, Height = 30, DialogResult = System.Windows.Forms.DialogResult.Cancel };
            _cancelButton.Click += (_, _) => { DialogResult = System.Windows.Forms.DialogResult.Cancel; Close(); };

            _statusLabel = new LabelControl
            {
                Text = string.Empty,
                AutoSizeMode = LabelAutoSizeMode.Vertical,
                Dock = System.Windows.Forms.DockStyle.Fill
            };

            var buttonPanel = new System.Windows.Forms.FlowLayoutPanel
            {
                Dock = System.Windows.Forms.DockStyle.Fill,
                FlowDirection = System.Windows.Forms.FlowDirection.RightToLeft,
                Height = 38,
                WrapContents = false,
                Padding = new System.Windows.Forms.Padding(0, 4, 0, 0)
            };
            buttonPanel.Controls.Add(_cancelButton);
            buttonPanel.Controls.Add(_okButton);
            buttonPanel.Controls.Add(_aiImproveButton);

            table.Controls.Add(previewLabel, 0, 0);
            table.Controls.Add(_previewEdit, 0, 1);
            table.Controls.Add(_statusLabel, 0, 2);
            table.Controls.Add(buttonPanel, 0, 3);
        }

        // ── Prompt builder ───────────────────────────────────────────────────

        private void BuildPrompt()
        {
            var sb = new System.Text.StringBuilder();

            var topic = _topicEdit.Text.Trim();
            var analysisType = _analysisTypeCombo.Text.Trim();
            var stage = _stageEdit.Text.Trim();
            var parameter = _parameterEdit.Text.Trim();
            var dateFrom = _dateFromEdit.DateTime;
            var dateTo = _dateToEdit.DateTime;
            var notes = _freeTextEdit.Text.Trim();

            if (!string.IsNullOrEmpty(topic))
                sb.AppendLine($"Topic: {topic}");

            sb.AppendLine($"Analysis type: {analysisType}");

            if (!string.IsNullOrEmpty(stage))
                sb.AppendLine($"Stage: {stage}");

            if (!string.IsNullOrEmpty(parameter))
                sb.AppendLine($"Parameter: {parameter}");

            if (dateFrom != DateTime.MinValue || dateTo != DateTime.MinValue)
                sb.AppendLine($"Date range: {dateFrom:dd.MM.yyyy} – {dateTo:dd.MM.yyyy}");

            if (!string.IsNullOrEmpty(notes))
            {
                sb.AppendLine();
                sb.AppendLine(notes);
            }

            _previewEdit.Text = sb.ToString().TrimEnd();
        }

        // ── AI Improve ───────────────────────────────────────────────────────

        private async Task AiImproveAsync()
        {
            if (_chatClient == null) return;

            var currentPrompt = _previewEdit.Text.Trim();
            if (string.IsNullOrEmpty(currentPrompt)) return;

            SetStatus("Improving with AI…", busy: true);

            try
            {
                const string systemInstruction =
                    "You are a prompt editor, not an analyst. Improve the user's rough question into one clear, concise question for an industrial powder-coating AI assistant. " +
                    "Preserve the user's language, intent, stage, dates, and technical terms. Do not answer the question. Do not add analysis, hypotheses, risks, recommendations, checklists, or many new requirements. " +
                    "Only fix grammar, clarify ambiguous wording, and make the request explicit enough for the assistant to answer. " +
                    "Keep roughly the same level of detail as the original. If the original is short, keep it short; if it is long, preserve the important details. " +
                    "Do not expand the prompt by more than about 25 percent. Return only the improved prompt text.";

                var messages = new List<ChatMessage>
                {
                    new(ChatRole.System, systemInstruction),
                    new(ChatRole.User, currentPrompt)
                };

                var response = await _chatClient.GetResponseAsync(messages);
                var improved = ExtractResponseText(response);

                if (string.Equals(improved, currentPrompt, StringComparison.OrdinalIgnoreCase))
                {
                    messages.Add(new ChatMessage(ChatRole.Assistant, improved ?? string.Empty));
                    messages.Add(new ChatMessage(ChatRole.User,
                        "Improve it only slightly more. Keep roughly the same length and do not add checklist items or analysis."));
                    response = await _chatClient.GetResponseAsync(messages);
                    improved = ExtractResponseText(response);
                }

                if (string.IsNullOrWhiteSpace(improved))
                {
                    PowderCoatingWizard.Win.Services.AILogger.LogEvent("PROMPT_SHAPER", "AI Improve returned an empty result.");
                    PowderCoatingWizard.Win.Services.AILogger.LogEvent("PROMPT_SHAPER", $"Response type: {response.GetType().FullName}; message count: {response.Messages.Count}");
                    for (int i = 0; i < response.Messages.Count; i++)
                    {
                        var contentTypes = string.Join(", ", response.Messages[i].Contents.Select(c => c.GetType().Name));
                        PowderCoatingWizard.Win.Services.AILogger.LogEvent("PROMPT_SHAPER", $"Message[{i}] role={response.Messages[i].Role.Value} contentTypes=[{contentTypes}]");
                    }
                    SetStatus("AI returned an empty result. Check ai_pipeline.log.", busy: false);
                    return;
                }

                _previewEdit.Text = NormalizePromptText(improved);
                SetStatus("Improved", busy: false);
            }
            catch (Exception ex)
            {
                SetStatus($"Error: {ex.Message}", busy: false);
            }
        }

        private void SetStatus(string text, bool busy)
        {
            if (InvokeRequired)
            {
                Invoke(() => SetStatus(text, busy));
                return;
            }
            _statusLabel.Text = text;
            _aiImproveButton.Enabled = !busy && _chatClient != null;
            _okButton.Enabled = !busy;
        }

        private static string? ExtractResponseText(ChatResponse response)
        {
            var text = response.Text?.Trim();
            if (!string.IsNullOrWhiteSpace(text)) return text;

            text = string.Join(Environment.NewLine,
                    response.Messages.SelectMany(m => m.Contents.OfType<TextContent>().Select(c => c.Text)))
                .Trim();

            return string.IsNullOrWhiteSpace(text) ? null : text;
        }

        private static string NormalizePromptText(string text)
        {
            var lines = text.Replace("**", string.Empty)
                .ReplaceLineEndings("\n")
                .Split('\n')
                .Select(line => line.Trim())
                .Where(line => !string.IsNullOrWhiteSpace(line));

            return string.Join(Environment.NewLine, lines);
        }
    }
}
