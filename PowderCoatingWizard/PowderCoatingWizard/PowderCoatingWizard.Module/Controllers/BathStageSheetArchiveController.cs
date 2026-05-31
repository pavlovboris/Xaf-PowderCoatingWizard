using DevExpress.ExpressApp;
using DevExpress.ExpressApp.Actions;
using DevExpress.Persistent.Base;
using PowderCoatingWizard.Module.BusinessObjects;
using PowderCoatingWizard.Module.Editors;

namespace PowderCoatingWizard.Module.Controllers
{
    public class BathStageSheetArchiveController : ObjectViewController<DetailView, LineStage>
    {
        private readonly SimpleAction          _archiveNewAction;
        private readonly PopupWindowShowAction _fullRebuildAction;

        public BathStageSheetArchiveController()
        {
            _archiveNewAction = new SimpleAction(
                this, "ArchiveNewSessions", PredefinedCategory.Edit)
            {
                Caption   = "Archive New",
                ToolTip   = "Archive only measurement sessions that have not been archived yet for this stage.",
                ImageName = "Save",
            };
            _archiveNewAction.Execute += OnArchiveNewExecute;

            _fullRebuildAction = new PopupWindowShowAction(
                this, "FullRebuildArchive", PredefinedCategory.Edit)
            {
                Caption   = "Rebuild Archive",
                ToolTip   = "Recalculate archived rows for this stage. Optionally limit to a date range.",
                ImageName = "Refresh",
            };
            _fullRebuildAction.CustomizePopupWindowParams += OnRebuildCustomizePopup;
            _fullRebuildAction.Execute                    += OnFullRebuildExecute;
        }

        private void OnArchiveNewExecute(object sender, SimpleActionExecuteEventArgs e)
        {
            if (View.CurrentObject is not LineStage stage) return;
            int written = BathStageSheetArchiveService.Archive(
                ObjectSpace, stage, BathStageSheetArchiveService.ArchiveMode.UpdateNew);
            Application.ShowViewStrategy.ShowMessage(
                $"Archive complete — {written} row(s) written.",
                InformationType.Success);
        }

        private void OnRebuildCustomizePopup(object sender, CustomizePopupWindowParamsEventArgs e)
        {
            var os     = Application.CreateObjectSpace(typeof(ArchiveRebuildParams));
            var @params = os.CreateObject<ArchiveRebuildParams>();
            e.View = Application.CreateDetailView(os, @params);
            e.DialogController.AcceptAction.Caption = "Rebuild";
        }

        private void OnFullRebuildExecute(object sender, PopupWindowShowActionExecuteEventArgs e)
        {
            if (View.CurrentObject is not LineStage stage) return;
            var @params = e.PopupWindowView.CurrentObject as ArchiveRebuildParams;

            int written = BathStageSheetArchiveService.Archive(
                ObjectSpace, stage,
                BathStageSheetArchiveService.ArchiveMode.FullRebuild,
                @params?.DateFrom,
                @params?.DateTo);

            Application.ShowViewStrategy.ShowMessage(
                $"Rebuild complete — {written} row(s) written.",
                InformationType.Success);
        }
    }
}
