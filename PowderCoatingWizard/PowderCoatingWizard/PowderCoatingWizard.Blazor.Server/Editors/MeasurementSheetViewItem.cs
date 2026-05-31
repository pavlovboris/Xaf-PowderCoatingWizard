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
    public interface IModelMeasurementSheetItem : IModelViewItem { }

    /// <summary>
    /// XAF Blazor ViewItem for the horizontal measurement sheet.
    /// 
    /// To add it to the ProductionLine_DetailView:
    ///   1. Open the Model Editor (double-click Model.xafml in Blazor.Server project).
    ///   2. Navigate to Views > PowderCoatingWizard.Module.BusinessObjects > ProductionLine_DetailView > Items.
    ///   3. Right-click Items > Add > MeasurementSheetItem.
    ///   4. Navigate to Layout, right-click > Customize Layout, drag the new item into the form.
    /// </summary>
    [ViewItem(typeof(IModelMeasurementSheetItem))]
    public class MeasurementSheetViewItem
        : ViewItem, IComponentContentHolder, IComplexViewItem
    {
        private MeasurementSheetModel _model;
        private IObjectSpace          _os;

        public MeasurementSheetViewItem(IModelViewItem modelItem, Type objectType)
            : base(objectType, modelItem.Id) { }

        // ── IComplexViewItem ────────────────────────────────────────────────
        void IComplexViewItem.Setup(IObjectSpace objectSpace, XafApplication application)
        {
            _os = objectSpace;
            // Auto-refresh the component model whenever any commit happens
            _os.Committed += OnObjectSpaceCommitted;
        }

        private void OnObjectSpaceCommitted(object sender, EventArgs e)
        {
            if (_model == null || _savingLayout) return;
            if (CurrentObject is ProductionLine line)
                _model.Load(_os, line);
        }

        // ── IComponentContentHolder ─────────────────────────────────────────
        RenderFragment IComponentContentHolder.ComponentContent
            => ComponentModelObserver.Create(_model, _model.GetComponentContent());

        // ── ViewItem ────────────────────────────────────────────────────────
        protected override object CreateControlCore()
        {
            _model = new MeasurementSheetModel();
            _model.OnLayoutSaved = SaveLayoutToObject;
            _model.OnRowSave     = drv => _model.SaveRow(drv);

            if (CurrentObject is ProductionLine line)
                _model.Load(_os, line);

            return _model;
        }

        protected override void OnCurrentObjectChanged()
        {
            base.OnCurrentObjectChanged();
            if (_model == null || _os == null) return;
            if (CurrentObject is ProductionLine line)
                _model.Load(_os, line);
            else
                _model.Clear();
        }

        public override void Refresh()
        {
            base.Refresh();
            if (CurrentObject is ProductionLine line)
                _model?.Load(_os, line);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing && _os != null)
                _os.Committed -= OnObjectSpaceCommitted;
            base.Dispose(disposing);
        }

        private bool _savingLayout;
        private void SaveLayoutToObject(string json)
        {
            if (_savingLayout || CurrentObject is not ProductionLine line) return;
            if (line.BlazorLayoutJson == json) return;
            line.BlazorLayoutJson = json;
            _savingLayout = true;
            try   { _os.CommitChanges(); }
            finally { _savingLayout = false; }
        }
    }
}
