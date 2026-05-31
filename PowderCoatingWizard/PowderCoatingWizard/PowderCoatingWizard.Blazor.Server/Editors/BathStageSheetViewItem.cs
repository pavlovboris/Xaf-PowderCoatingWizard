using DevExpress.ExpressApp;
using DevExpress.ExpressApp.Blazor;
using DevExpress.ExpressApp.Blazor.Components;
using DevExpress.ExpressApp.Blazor.Components.Models;
using DevExpress.ExpressApp.Editors;
using DevExpress.ExpressApp.Model;
using Microsoft.AspNetCore.Components;
using PowderCoatingWizard.Module.BusinessObjects;

namespace PowderCoatingWizard.Blazor.Server.Editors
{
    public interface IModelBathStageSheetItem : IModelViewItem { }

    /// <summary>
    /// XAF Blazor ViewItem for the Bath Stage Sheet.
    /// Shows all measurement sessions for the current LineStage with
    /// parameter columns + dynamically evaluated StageCriterion columns.
    ///
    /// To add it to LineStage_DetailView:
    ///   1. Open the Model Editor (Model.xafml in Blazor.Server).
    ///   2. Navigate to Views > PowderCoatingWizard.Module.BusinessObjects > LineStage_DetailView > Items.
    ///   3. Right-click Items > Add > BathStageSheetItem.
    ///   4. Drag into layout.
    /// </summary>
    [ViewItem(typeof(IModelBathStageSheetItem))]
    public class BathStageSheetViewItem
        : ViewItem, IComponentContentHolder, IComplexViewItem
    {
        private BathStageSheetModel _model;
        private IObjectSpace        _os;

        public BathStageSheetViewItem(IModelViewItem modelItem, Type objectType)
            : base(objectType, modelItem.Id) { }

        // ── IComplexViewItem ────────────────────────────────────────────────
        void IComplexViewItem.Setup(IObjectSpace objectSpace, XafApplication application)
        {
            _os = objectSpace;
            _os.Committed += OnCommitted;
        }

        private void OnCommitted(object sender, EventArgs e)
        {
            if (_model == null || _savingLayout) return;
            if (CurrentObject is LineStage stage)
                _model.Load(_os, stage);
        }

        // ── IComponentContentHolder ─────────────────────────────────────────
        RenderFragment IComponentContentHolder.ComponentContent
            => ComponentModelObserver.Create(_model, _model.GetComponentContent());

        // ── ViewItem ────────────────────────────────────────────────────────
        protected override object CreateControlCore()
        {
            _model = new BathStageSheetModel();
            _model.OnLayoutSaved    = SaveLayoutToObject;
            _model.OnRowSave        = drv => _model.SaveRow(drv);
            _model.OnNewSession     = () => _model.AddNewSession();
            _model.OnFilterChanged  = (useArchive, from, to) =>
            {
                _model.UseArchive = useArchive;
                _model.DateFrom   = from;
                _model.DateTo     = to;
                _model.Reload();
            };
            if (CurrentObject is LineStage stage)
                _model.Load(_os, stage);
            return _model;
        }

        protected override void OnCurrentObjectChanged()
        {
            base.OnCurrentObjectChanged();
            if (_model == null || _os == null) return;
            if (CurrentObject is LineStage stage)
                _model.Load(_os, stage);
            else
                _model.Clear();
        }

        public override void Refresh()
        {
            base.Refresh();
            if (CurrentObject is LineStage stage)
                _model?.Load(_os, stage);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing && _os != null)
                _os.Committed -= OnCommitted;
            base.Dispose(disposing);
        }

        private bool _savingLayout;
        private void SaveLayoutToObject(string json)
        {
            if (_savingLayout || CurrentObject is not LineStage stage) return;
            if (stage.BlazorLayoutJson == json) return;
            stage.BlazorLayoutJson = json;
            _savingLayout = true;
            try   { _os.CommitChanges(); }
            finally { _savingLayout = false; }
        }
    }
}
