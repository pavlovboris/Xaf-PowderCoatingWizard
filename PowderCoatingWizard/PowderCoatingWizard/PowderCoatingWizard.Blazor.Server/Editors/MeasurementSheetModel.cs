using DevExpress.ExpressApp;
using DevExpress.ExpressApp.Blazor.Components.Models;
using PowderCoatingWizard.Module.BusinessObjects;
using PowderCoatingWizard.Module.Editors;
using System.Data;

namespace PowderCoatingWizard.Blazor.Server.Editors
{
    /// <summary>
    /// ComponentModel that feeds the MeasurementSheetComponent Razor component.
    /// All properties use GetPropertyValue/SetPropertyValue so Blazor re-renders when data changes.
    /// </summary>
    public class MeasurementSheetModel : ComponentModelBase
    {
        public override Type ComponentType => typeof(MeasurementSheetComponent);

        // ── bound properties (trigger Blazor re-render on change) ───────────
        public DataTable SheetData
        {
            get => GetPropertyValue<DataTable>();
            set => SetPropertyValue(value);
        }

        public List<ColumnMeta> Columns
        {
            get => GetPropertyValue<List<ColumnMeta>>();
            set => SetPropertyValue(value);
        }

        /// <summary>Current persisted Blazor grid layout JSON for this line (loaded from DB).</summary>
        public string BlazorLayoutJson
        {
            get => GetPropertyValue<string>();
            set => SetPropertyValue(value);
        }

        /// <summary>Called by the component when the layout changes; persists to the business object.</summary>
        public Action<string> OnLayoutSaved { get; set; }

        /// <summary>Called by the component when the user saves an edited row.</summary>
        public Action<System.Data.DataRowView> OnRowSave { get; set; }

        // ── internal state (not bound to component parameters) ──────────────
        private IObjectSpace   _os;
        private ProductionLine _line;

        public void Load(IObjectSpace os, ProductionLine line)
        {
            _os   = os;
            _line = line;
            Reload();
        }

        public void Reload()
        {
            if (_os == null || _line == null) return;
            var (table, cols) = MeasurementSheetService.Build(_os, _line);
            SheetData        = table;
            Columns          = cols;
            BlazorLayoutJson = _line.BlazorLayoutJson;
        }

        public void Clear()
        {
            _line     = null;
            SheetData = null;
            Columns   = null;
        }

        /// <summary>
        /// Persist edits made inside the sheet back to XPO.
        /// Called from the Razor component when the user commits a row.
        /// </summary>
        public void SaveRow(DataRowView editedRow)
        {
            if (_os == null || _line == null) return;

            var sessionOid = (Guid)editedRow[MeasurementSheetService.ColOid];
            var session    = _os.GetObjectByKey<MeasurementSession>(sessionOid);
            if (session == null) return;

            // Persist the Notes field
            var newNotes = editedRow[MeasurementSheetService.ColNotes]?.ToString() ?? string.Empty;
            if (session.Notes != newNotes)
                session.Notes = newNotes;

            foreach (var meta in Columns ?? [])
            {
                if (!editedRow.Row.Table.Columns.Contains(meta.ValueKey)) continue;
                var raw = editedRow[meta.ValueKey]?.ToString() ?? string.Empty;

                var stage = _line.Stages.FirstOrDefault(
                    s => s.Position == meta.StagePosition && s.Name == meta.StageName);
                var param = _os.GetObjects<BathParameter>()
                    .FirstOrDefault(p => p.Name == meta.ParameterName);

                if (stage == null || param == null) continue;

                var m = session.Measurements.FirstOrDefault(
                    x => x.Stage?.Oid == stage.Oid && x.Parameter?.Oid == param.Oid);

                if (m == null)
                {
                    m = _os.CreateObject<ParameterMeasurement>();
                    m.MeasurementSession = session;
                    m.Stage              = stage;
                    m.Parameter          = param;
                }

                if (meta.IsPredefined)
                {
                    var predefined = param.PredefinedValues
                        .FirstOrDefault(v => v.Name == raw);
                    m.SelectedValue = predefined;
                    m.NumericValue  = null;
                    m.TextValue     = null;
                }
                else if (double.TryParse(raw.Split(' ')[0],
                        System.Globalization.NumberStyles.Any,
                        System.Globalization.CultureInfo.InvariantCulture,
                        out var num))
                {
                    m.NumericValue  = num;
                    m.TextValue     = null;
                    m.SelectedValue = null;
                }
                else
                {
                    m.NumericValue  = null;
                    m.TextValue     = raw;
                    m.SelectedValue = null;
                }
            }

            _os.CommitChanges();
            Reload();
        }
    }
}
