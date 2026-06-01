using DevExpress.ExpressApp;
using DevExpress.ExpressApp.Actions;
using DevExpress.ExpressApp.Win;
using DevExpress.Persistent.Base;
using DevExpress.XtraEditors;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.AI;
using PowderCoatingWizard.Module.BusinessObjects.AI;
using PowderCoatingWizard.Module.Services.AI;
using System.Text.Json;

namespace PowderCoatingWizard.Win.Controllers
{
    /// <summary>
    /// DetailView controller for <see cref="AICaseStudy"/>.
    /// Provides an "Embed Now" action and automatically re-embeds the case study
    /// whenever it is saved with <see cref="CaseStudyStatus.Approved"/> status.
    /// Removes chunks when the status is changed to <see cref="CaseStudyStatus.Archived"/>.
    /// </summary>
    public class CaseStudyController : ObjectViewController<DetailView, AICaseStudy>
    {
        private readonly SimpleAction _embedAction;
        private readonly SimpleAction _cleanAction;
        private bool _isCleaning;

        public CaseStudyController()
        {
            _cleanAction = new SimpleAction(this, "CleanCaseStudyWithAI", PredefinedCategory.Edit)
            {
                Caption = "AI Clean / Extract",
                ToolTip = "Uses AI to extract the final verified facts from the draft/transcript and populate the structured case study fields. The case remains Draft.",
                ImageName = "Action_Refresh",
                ConfirmationMessage = "This will replace the structured case study fields with AI-extracted content from the current draft/transcript. Continue?"
            };
            _cleanAction.Execute += OnCleanExecute;

            _embedAction = new SimpleAction(this, "EmbedCaseStudy", PredefinedCategory.Edit)
            {
                Caption        = "Embed into Knowledge Base",
                ToolTip        = "Generates or regenerates vector embeddings for this case study so it becomes searchable by AI agents.",
                ImageName      = "Action_Grant",
                ConfirmationMessage = "This will (re-)generate the vector embeddings for this case study. Continue?"
            };
            _embedAction.Execute += OnEmbedExecute;
        }

        protected override void OnActivated()
        {
            base.OnActivated();
            ObjectSpace.ObjectSaved += OnObjectSaved;
            UpdateActionState();
        }

        protected override void OnDeactivated()
        {
            ObjectSpace.ObjectSaved -= OnObjectSaved;
            base.OnDeactivated();
        }

        protected override void OnViewControlsCreated()
        {
            base.OnViewControlsCreated();
            UpdateActionState();
        }

        private void UpdateActionState()
        {
            var cs = View?.CurrentObject as AICaseStudy;
            _cleanAction.Enabled["HasObject"] = cs != null && cs.Status != CaseStudyStatus.Archived;
            _cleanAction.Enabled["NotBusy"] = !_isCleaning;
            _embedAction.Enabled["NotApproved"] = cs?.Status == CaseStudyStatus.Approved;
            _embedAction.Enabled["NotBusy"] = !_isCleaning;
        }

        // ── Auto-embed on Save ───────────────────────────────────────────────

        private void OnObjectSaved(object? sender, ObjectManipulatingEventArgs e)
        {
            if (e.Object is not AICaseStudy cs) return;

            UpdateActionState();

            if (cs.Status == CaseStudyStatus.Approved)
                _ = EmbedAsync(cs);
            else if (cs.Status == CaseStudyStatus.Archived)
                RemoveChunksAndCommit(cs);
        }

        // ── Manual "Embed Now" action ────────────────────────────────────────

        private void OnEmbedExecute(object sender, SimpleActionExecuteEventArgs e)
        {
            if (e.CurrentObject is AICaseStudy cs)
                _ = EmbedAsync(cs);
        }

        // ── AI cleanup / extraction ──────────────────────────────────────────

        private void OnCleanExecute(object sender, SimpleActionExecuteEventArgs e)
        {
            if (e.CurrentObject is AICaseStudy cs)
                _ = CleanWithAIAsync(cs);
        }

        private async Task CleanWithAIAsync(AICaseStudy caseStudy)
        {
            var chatClient = BuildChatClient();
            if (chatClient == null)
            {
                XtraMessageBox.Show(
                    "No chat model is configured.\n\nGo to AI → AI Provider Settings and configure a chat model first.",
                    "AI Case Study",
                    System.Windows.Forms.MessageBoxButtons.OK,
                    System.Windows.Forms.MessageBoxIcon.Warning);
                return;
            }

            var source = BuildCaseStudySourceText(caseStudy);
            if (string.IsNullOrWhiteSpace(source))
            {
                XtraMessageBox.Show(
                    "There is no draft content to clean.",
                    "AI Case Study",
                    System.Windows.Forms.MessageBoxButtons.OK,
                    System.Windows.Forms.MessageBoxIcon.Information);
                return;
            }

            try
            {
                _isCleaning = true;
                UpdateActionState();
                View.Caption = "AI Case Study - cleaning...";
                const string systemPrompt =
                    "You clean noisy AI assistant conversations into structured industrial powder-coating case studies. " +
                    "Remove false starts, failed attempts, speculation, UI/debug noise, and any claims that were not confirmed. " +
                    "Keep only the final verified facts, practical conclusions, and useful lessons. " +
                    "Return only valid JSON with these exact properties: " +
                    "title, tags, problemDescription, rootCause, resolution, outcome, lessonsLearned. " +
                    "The tags property must be a single comma-separated string, not an array. " +
                    "Use empty strings for unknown fields. Do not include markdown.";

                var messages = new List<ChatMessage>
                {
                    new(ChatRole.System, systemPrompt),
                    new(ChatRole.User, source)
                };

                var response = await chatClient.GetResponseAsync(messages, new ChatOptions { MaxOutputTokens = 1500 });
                var cleaned = ParseCleanedCaseStudy(response.Text);
                if (cleaned == null)
                {
                    XtraMessageBox.Show(
                        "AI returned an invalid cleanup result. No changes were applied.",
                        "AI Case Study",
                        System.Windows.Forms.MessageBoxButtons.OK,
                        System.Windows.Forms.MessageBoxIcon.Warning);
                    return;
                }

                if (!string.IsNullOrWhiteSpace(cleaned.Title)) caseStudy.Title = cleaned.Title.Trim();
                if (!string.IsNullOrWhiteSpace(cleaned.Tags)) caseStudy.Tags = cleaned.Tags.Trim();
                caseStudy.ProblemDescription = cleaned.ProblemDescription?.Trim() ?? string.Empty;
                caseStudy.RootCause = cleaned.RootCause?.Trim() ?? string.Empty;
                caseStudy.Resolution = cleaned.Resolution?.Trim() ?? string.Empty;
                caseStudy.Outcome = cleaned.Outcome?.Trim() ?? string.Empty;
                caseStudy.LessonsLearned = cleaned.LessonsLearned?.Trim() ?? string.Empty;
                caseStudy.Status = CaseStudyStatus.Draft;
                caseStudy.IsEmbedded = false;

                ObjectSpace.CommitChanges();
                View.ObjectSpace.Refresh();

                XtraMessageBox.Show(
                    "Case study cleaned and structured successfully.\n\nReview the fields and set Status to Approved only after manual validation.",
                    "AI Case Study",
                    System.Windows.Forms.MessageBoxButtons.OK,
                    System.Windows.Forms.MessageBoxIcon.Information);
            }
            catch (Exception ex)
            {
                DevExpress.Persistent.Base.Tracing.Tracer.LogError(ex);
                XtraMessageBox.Show(
                    $"AI cleanup failed:\n\n{ex.Message}",
                    "AI Case Study",
                    System.Windows.Forms.MessageBoxButtons.OK,
                    System.Windows.Forms.MessageBoxIcon.Error);
            }
            finally
            {
                _isCleaning = false;
                UpdateActionState();
                View.Caption = string.Empty;
            }
        }

        // ── Embedding logic ───────────────────────────────────────────────────

        private async Task EmbedAsync(AICaseStudy caseStudy)
        {
            var embService = BuildEmbeddingService();
            if (embService == null)
            {
                XtraMessageBox.Show(
                    "No embedding model is configured.\n\nGo to AI → AI Provider Settings and set an Embedding Model ID.",
                    "AI Knowledge Base",
                    System.Windows.Forms.MessageBoxButtons.OK,
                    System.Windows.Forms.MessageBoxIcon.Warning);
                return;
            }

            var svc = new CaseStudyEmbeddingService(embService);
            int count = await svc.EmbedAsync(caseStudy, ObjectSpace);

            if (count < 0)
            {
                XtraMessageBox.Show(
                    "Embedding was skipped (case study is not Approved, or no embedding model is available).",
                    "AI Knowledge Base",
                    System.Windows.Forms.MessageBoxButtons.OK,
                    System.Windows.Forms.MessageBoxIcon.Information);
                return;
            }

            ObjectSpace.CommitChanges();

            XtraMessageBox.Show(
                $"Case study embedded successfully ({count} chunk{(count == 1 ? "" : "s")}).\n\nIt is now searchable by AI agents.",
                "AI Knowledge Base",
                System.Windows.Forms.MessageBoxButtons.OK,
                System.Windows.Forms.MessageBoxIcon.Information);
        }

        private void RemoveChunksAndCommit(AICaseStudy caseStudy)
        {
            CaseStudyEmbeddingService.RemoveChunks(caseStudy, ObjectSpace);
            ObjectSpace.CommitChanges();
        }

        private EmbeddingService? BuildEmbeddingService()
        {
            var winApp = Application as WinApplication;
            var sp = winApp?.ServiceProvider;
            var settingsService = sp?.GetService<AISettingsService>();
            var settings = settingsService?.LoadSettings();
            var embGen = AISettingsService.BuildEmbeddingGenerator(settings);
            return embGen != null ? new EmbeddingService(embGen) : null;
        }

        private IChatClient? BuildChatClient()
        {
            var winApp = Application as WinApplication;
            var sp = winApp?.ServiceProvider;
            var settingsService = sp?.GetService<AISettingsService>();
            var settings = settingsService?.LoadSettings();
            return AISettingsService.BuildChatClient(settings);
        }

        private static string BuildCaseStudySourceText(AICaseStudy caseStudy)
        {
            var sb = new System.Text.StringBuilder();
            if (!string.IsNullOrWhiteSpace(caseStudy.Title)) sb.AppendLine($"Title: {caseStudy.Title}");
            if (!string.IsNullOrWhiteSpace(caseStudy.Tags)) sb.AppendLine($"Tags: {caseStudy.Tags}");
            if (caseStudy.Stage != null) sb.AppendLine($"Stage: {caseStudy.Stage.Line?.Name} > {caseStudy.Stage.Name}");
            sb.AppendLine();
            sb.AppendLine("Problem / transcript / draft:");
            sb.AppendLine(caseStudy.ProblemDescription ?? string.Empty);
            sb.AppendLine();
            if (!string.IsNullOrWhiteSpace(caseStudy.RootCause)) sb.AppendLine($"Root cause draft:\n{caseStudy.RootCause}");
            if (!string.IsNullOrWhiteSpace(caseStudy.Resolution)) sb.AppendLine($"Resolution draft:\n{caseStudy.Resolution}");
            if (!string.IsNullOrWhiteSpace(caseStudy.Outcome)) sb.AppendLine($"Outcome draft:\n{caseStudy.Outcome}");
            if (!string.IsNullOrWhiteSpace(caseStudy.LessonsLearned)) sb.AppendLine($"Lessons draft:\n{caseStudy.LessonsLearned}");
            return sb.ToString();
        }

        private static CleanedCaseStudy? ParseCleanedCaseStudy(string? text)
        {
            if (string.IsNullOrWhiteSpace(text)) return null;
            var json = ExtractJsonObject(text.Trim());
            using var document = JsonDocument.Parse(json);
            var root = document.RootElement;

            return new CleanedCaseStudy
            {
                Title = ReadString(root, "title"),
                Tags = ReadString(root, "tags"),
                ProblemDescription = ReadString(root, "problemDescription"),
                RootCause = ReadString(root, "rootCause"),
                Resolution = ReadString(root, "resolution"),
                Outcome = ReadString(root, "outcome"),
                LessonsLearned = ReadString(root, "lessonsLearned")
            };
        }

        private static string? ReadString(JsonElement root, string propertyName)
        {
            if (!root.TryGetProperty(propertyName, out var value)) return null;
            return value.ValueKind switch
            {
                JsonValueKind.String => value.GetString(),
                JsonValueKind.Array => string.Join(", ", value.EnumerateArray().Select(x => x.ValueKind == JsonValueKind.String ? x.GetString() : x.ToString()).Where(x => !string.IsNullOrWhiteSpace(x))),
                JsonValueKind.Null => null,
                JsonValueKind.Undefined => null,
                _ => value.ToString()
            };
        }

        private static string ExtractJsonObject(string text)
        {
            var start = text.IndexOf('{');
            var end = text.LastIndexOf('}');
            return start >= 0 && end > start ? text[start..(end + 1)] : text;
        }

        private sealed class CleanedCaseStudy
        {
            public string? Title { get; set; }
            public string? Tags { get; set; }
            public string? ProblemDescription { get; set; }
            public string? RootCause { get; set; }
            public string? Resolution { get; set; }
            public string? Outcome { get; set; }
            public string? LessonsLearned { get; set; }
        }
    }
}
