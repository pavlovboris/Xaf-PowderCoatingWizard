using DevExpress.ExpressApp;
using DevExpress.ExpressApp.Actions;
using DevExpress.ExpressApp.Win;
using DevExpress.Persistent.Base;
using Microsoft.Extensions.DependencyInjection;
using PowderCoatingWizard.Module.BusinessObjects.AI;
using PowderCoatingWizard.Module.Services.AI;

namespace PowderCoatingWizard.Win.Controllers
{
    /// <summary>
    /// "Sync / Ingest" action available on both ListView and DetailView of AIDocument.
    /// On DetailView ingests only the current document; on ListView ingests all un-synced ones.
    /// Also resets IsSynced when the attached file changes so the user knows re-ingestion is needed.
    /// </summary>
    public class DocumentIngestionController : ObjectViewController<ObjectView, AIDocument>
    {
        private readonly SimpleAction _ingestAction;

        public DocumentIngestionController()
        {
            _ingestAction = new SimpleAction(this, "IngestDocuments", PredefinedCategory.Tools)
            {
                Caption = "Sync (Generate Embeddings)",
                ImageName = "Actions_Refresh",
                ToolTip = "Chunks this document (or all un-synced documents) and generates embedding vectors for semantic search."
            };
            _ingestAction.Execute += OnIngest;
        }

        protected override void OnActivated()
        {
            base.OnActivated();
            ObjectSpace.ObjectChanged += OnObjectChanged;
        }

        protected override void OnDeactivated()
        {
            ObjectSpace.ObjectChanged -= OnObjectChanged;
            base.OnDeactivated();
        }

        // Reset IsSynced when the file property is changed so the badge turns red
        private void OnObjectChanged(object? sender, ObjectChangedEventArgs e)
        {
            if (e.Object is AIDocument doc && e.PropertyName == nameof(AIDocument.File))
                doc.IsSynced = false;
        }

        private async void OnIngest(object sender, SimpleActionExecuteEventArgs e)
        {
            var sp = Application.ServiceProvider;
            var settings = sp?.GetService<AISettingsService>()?.LoadSettings();
            var embGen = AISettingsService.BuildEmbeddingGenerator(settings);
            var embSvc = new EmbeddingService(embGen);

            if (!embSvc.IsAvailable)
            {
                DevExpress.XtraEditors.XtraMessageBox.Show(
                    "Embedding model is not configured.\n\n" +
                    "Set an Embedding Model ID in AI Provider Settings\n(e.g. text-embedding-3-small).",
                    "AI Assistant",
                    System.Windows.Forms.MessageBoxButtons.OK,
                    System.Windows.Forms.MessageBoxIcon.Warning);
                return;
            }

            var osFactory = sp!.GetRequiredService<IObjectSpaceFactory>();
            var svc = new DocumentIngestionService(osFactory, embSvc);

            int count;
            using (new DevExpress.Utils.WaitDialogForm("Generating embeddings, please wait…"))
            {
                // DetailView — ingest only this document
                if (View is DetailView dv && dv.CurrentObject is AIDocument current)
                    count = await svc.ReIngestAsync((Guid)current.Oid);
                else
                    count = await svc.IngestPendingAsync();
            }

            View.Refresh();
            DevExpress.XtraEditors.XtraMessageBox.Show(
                $"Sync complete — {count} chunk(s) written.",
                "AI Assistant",
                System.Windows.Forms.MessageBoxButtons.OK,
                System.Windows.Forms.MessageBoxIcon.Information);
        }
    }
}
