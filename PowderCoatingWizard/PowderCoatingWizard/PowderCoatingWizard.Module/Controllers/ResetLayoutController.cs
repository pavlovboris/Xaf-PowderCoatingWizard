using DevExpress.ExpressApp;
using DevExpress.ExpressApp.Actions;
using DevExpress.Persistent.Base;
using DevExpress.Persistent.BaseImpl;
using PowderCoatingWizard.Module.BusinessObjects;

namespace PowderCoatingWizard.Module.Controllers
{
    public class ResetLayoutController : ObjectViewController<DetailView, BaseObject>
    {
        private readonly SimpleAction _resetAction;

        public ResetLayoutController()
        {
            _resetAction = new SimpleAction(this, "ResetGridLayout", PredefinedCategory.Edit)
            {
                Caption             = "Reset Layout",
                ToolTip             = "Clear the saved grid layout and restore default column order and widths.",
                ImageName           = "Action_Refresh",
                ConfirmationMessage = "Reset the saved grid layout to defaults?",
            };
            _resetAction.Execute += OnResetExecute;
        }

        protected override void OnActivated()
        {
            base.OnActivated();
            _resetAction.Active["SupportedType"] =
                View.CurrentObject is ProductionLine || View.CurrentObject is LineStage;
        }

        private void OnResetExecute(object sender, SimpleActionExecuteEventArgs e)
        {
            switch (View.CurrentObject)
            {
                case ProductionLine line:
                    line.GridLayoutXml    = null;
                    line.BlazorLayoutJson = null;
                    break;
                case LineStage stage:
                    stage.GridLayoutXml    = null;
                    stage.BlazorLayoutJson = null;
                    break;
                default:
                    return;
            }
            ObjectSpace.CommitChanges();
        }
    }
}