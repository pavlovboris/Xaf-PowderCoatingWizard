using DevExpress.ExpressApp;
using DevExpress.ExpressApp.Actions;
using DevExpress.ExpressApp.Win;
using DevExpress.Persistent.Base;
using DevExpress.XtraEditors;
using Microsoft.Extensions.DependencyInjection;
using PowderCoatingWizard.Module.BusinessObjects.AI;
using PowderCoatingWizard.Module.Services.AI;

namespace PowderCoatingWizard.Win.Controllers
{
    /// <summary>
    /// Adds a "Learn from Session" action to the <see cref="AIChatSession"/> DetailView and ListView.
    /// The action creates a draft <see cref="AICaseStudy"/> pre-populated from the session's
    /// messages, so a domain expert can review, edit, approve, and embed it in one workflow.
    /// </summary>
    public class LearnFromSessionController : ObjectViewController<ObjectView, AIChatSession>
    {
        private readonly SimpleAction _learnAction;

        public LearnFromSessionController()
        {
            _learnAction = new SimpleAction(this, "LearnFromSession", PredefinedCategory.Edit)
            {
                Caption   = "Learn from Session",
                ToolTip   = "Creates a draft Case Study from this chat session for review and embedding into the AI knowledge base.",
                ImageName = "BO_Scheduler_Resource",
            };
            _learnAction.Execute += OnLearnExecute;
        }

        private void OnLearnExecute(object sender, SimpleActionExecuteEventArgs e)
        {
            var session = e.CurrentObject as AIChatSession;
            if (session == null) return;

            // Build the case study draft in a fresh ObjectSpace so we can navigate to it.
            var os = Application.CreateObjectSpace(typeof(AICaseStudy));
            var cs = os.CreateObject<AICaseStudy>();

            cs.Title       = session.Title?.Length > 0
                ? $"[Draft] {session.Title}"
                : $"[Draft] Case study from session {session.CreatedAt:dd MMM yyyy}";
            cs.Status      = CaseStudyStatus.Draft;
            cs.CreatedAt   = DateTime.UtcNow;
            cs.OccurredOn  = session.CreatedAt;

            // Re-load messages from the session's ObjectSpace (the session passed in may
            // belong to a different OS — reload by key to be safe).
            var sessionInOs = os.GetObjectByKey<AIChatSession>(session.Oid);
            var messages = sessionInOs?.Messages
                .OrderBy(m => m.SortOrder)
                .Where(m => m.Role != "system")
                .ToList() ?? [];

            if (messages.Count > 0)
            {
                // Build a structured transcript to pre-fill the problem description.
                var transcript = new System.Text.StringBuilder();
                transcript.AppendLine("--- Chat Transcript ---");
                foreach (var m in messages)
                {
                    var role = m.Role == "assistant" ? "AI" : "User";
                    transcript.AppendLine($"[{role}] {m.Content}");
                    transcript.AppendLine();
                }

                cs.ProblemDescription = transcript.ToString();

                // Pre-fill Tags from the agent name if known.
                if (session.Agent != null)
                    cs.Tags = session.Agent.Name;
            }

            cs.LessonsLearned =
                "TODO: Review the transcript above.\n" +
                "1. Summarise the problem in 'Problem Description'.\n" +
                "2. Fill in 'Root Cause', 'Resolution', and 'Outcome'.\n" +
                "3. Set Status to Approved to embed into the knowledge base.";

            os.CommitChanges();

            // Navigate to the new case study DetailView so the expert can review and edit it.
            var sv = Application.CreateDetailView(os, cs);
            e.ShowViewParameters.CreatedView = sv;
            e.ShowViewParameters.TargetWindow = TargetWindow.NewModalWindow;
            e.ShowViewParameters.Context = TemplateContext.PopupWindow;
        }
    }
}
