using DevExpress.ExpressApp;
using DevExpress.ExpressApp.Actions;
using DevExpress.Persistent.Base;
using DevExpress.XtraEditors;
using PowderCoatingWizard.Module.BusinessObjects.AI;

namespace PowderCoatingWizard.Win.Controllers
{
    /// <summary>
    /// Adds soft-archive actions for saved AI chat sessions.
    /// Archived sessions are hidden from the assistant restore picker but remain available in XAF.
    /// </summary>
    public class ArchiveChatSessionController : ObjectViewController<ObjectView, AIChatSession>
    {
        private readonly SimpleAction _archiveAction;

        public ArchiveChatSessionController()
        {
            _archiveAction = new SimpleAction(this, "ToggleArchiveChatSession", PredefinedCategory.Edit)
            {
                Caption = "Archive Session",
                ToolTip = "Archive or restore the selected AI chat session.",
                ImageName = "Action_Archive"
            };
            _archiveAction.Execute += OnArchiveExecute;
        }

        protected override void OnActivated()
        {
            base.OnActivated();
            UpdateActionCaption();
            View.CurrentObjectChanged += OnCurrentObjectChanged;
        }

        protected override void OnDeactivated()
        {
            View.CurrentObjectChanged -= OnCurrentObjectChanged;
            base.OnDeactivated();
        }

        private void OnCurrentObjectChanged(object sender, EventArgs e) => UpdateActionCaption();

        private void OnArchiveExecute(object sender, SimpleActionExecuteEventArgs e)
        {
            var session = e.CurrentObject as AIChatSession;
            if (session == null) return;

            bool archive = !session.IsArchived;
            if (archive)
            {
                var result = XtraMessageBox.Show(
                    $"Archive chat session '{session.Title}'?\n\nIt will be hidden from the assistant restore list but kept in the database.",
                    "Archive Chat Session",
                    System.Windows.Forms.MessageBoxButtons.YesNo,
                    System.Windows.Forms.MessageBoxIcon.Question);

                if (result != System.Windows.Forms.DialogResult.Yes)
                    return;
            }

            session.IsArchived = archive;
            session.ArchivedAt = archive ? DateTime.UtcNow : null;
            ObjectSpace.CommitChanges();
            View.ObjectSpace.Refresh();
            UpdateActionCaption();
        }

        private void UpdateActionCaption()
        {
            if (View?.CurrentObject is AIChatSession session && session.IsArchived)
            {
                _archiveAction.Caption = "Restore Session";
                _archiveAction.ToolTip = "Restore this AI chat session to the assistant restore list.";
                _archiveAction.ImageName = "Action_Reset";
            }
            else
            {
                _archiveAction.Caption = "Archive Session";
                _archiveAction.ToolTip = "Hide this AI chat session from the assistant restore list.";
                _archiveAction.ImageName = "Action_Archive";
            }
        }
    }
}
