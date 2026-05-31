using DevExpress.ExpressApp;
using DevExpress.ExpressApp.Actions;
using DevExpress.Persistent.Base;
using PowderCoatingWizard.Module.BusinessObjects;

namespace PowderCoatingWizard.Win.Controllers
{
    /// <summary>
    /// WinForms controller that adds a "+ New Session" button on the
    /// <see cref="ProductionLine"/> detail view.
    ///
    /// Clicking it opens <see cref="LineNewSessionDialog"/> where the operator:
    ///   1. Picks a stage from the line.
    ///   2. Either creates a brand-new session or appends to an existing session
    ///      that does not yet have measurements for that stage.
    /// </summary>
    public class ProductionLineNewSessionController : ObjectViewController<DetailView, ProductionLine>
    {
        private readonly SimpleAction _newSessionAction;

        public ProductionLineNewSessionController()
        {
            _newSessionAction = new SimpleAction(
                this, "LineNewSession", PredefinedCategory.Edit)
            {
                Caption   = "+ New Session",
                ToolTip   = "Create a new measurement session for this production line.",
                ImageName = "Add",
            };
            _newSessionAction.Execute += OnNewSessionExecute;
        }

        private void OnNewSessionExecute(object sender, SimpleActionExecuteEventArgs e)
        {
            if (View.CurrentObject is not ProductionLine line) return;

            var session          = ObjectSpace.CreateObject<MeasurementSession>();
            session.Line         = ObjectSpace.GetObject(line);
            session.MeasuredOn   = DateTime.Now;
            session.OperatorName = string.Empty;
            ObjectSpace.CommitChanges();

            View.ObjectSpace.Refresh();
        }
    }
}
