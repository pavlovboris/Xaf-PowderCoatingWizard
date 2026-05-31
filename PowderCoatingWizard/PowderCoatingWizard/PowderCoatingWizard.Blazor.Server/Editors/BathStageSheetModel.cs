using DevExpress.ExpressApp;
using DevExpress.ExpressApp.Blazor.Components.Models;
using PowderCoatingWizard.Module.BusinessObjects;
using PowderCoatingWizard.Module.Editors;
using System.Data;

namespace PowderCoatingWizard.Blazor.Server.Editors
{
    public class BathStageSheetModel : ComponentModelBase
    {
        public override Type ComponentType => typeof(BathStageSheetComponent);

        public DataTable SheetData
        {
            get => GetPropertyValue<DataTable>();
            set => SetPropertyValue(value);
        }

        public List<BathStageColumnMeta> Columns
        {
            get => GetPropertyValue<List<BathStageColumnMeta>>();
            set => SetPropertyValue(value);
        }

        public List<StageCriterion> Criteria
        {
            get => GetPropertyValue<List<StageCriterion>>();
            set => SetPropertyValue(value);
        }

        public List<StageCalculatedField> CalculatedFields
        {
            get => GetPropertyValue<List<StageCalculatedField>>();
            set => SetPropertyValue(value);
        }

        public List<StageExcelTemplate> ExcelTemplates
        {
            get => GetPropertyValue<List<StageExcelTemplate>>();
            set => SetPropertyValue(value);
        }

        /// <summary>Current persisted Blazor grid layout JSON for this stage (loaded from DB).</summary>
        public string BlazorLayoutJson
        {
            get => GetPropertyValue<string>();
            set => SetPropertyValue(value);
        }

        /// <summary>Called by the component when the layout changes; persists to the business object.</summary>
        public Action<string> OnLayoutSaved { get; set; }

        /// <summary>Called by the component when the user saves an edited row.</summary>
        public Action<System.Data.DataRowView> OnRowSave { get; set; }

        /// <summary>Called by the toolbar when the user clicks Load with a mode/date selection.</summary>
        public Action<bool, DateTime?, DateTime?> OnFilterChanged { get; set; }

        /// <summary>
        /// Called by the component when the user clicks "+ New Session".
        /// Returns the Guid of the newly created <see cref="MeasurementSession"/>.
        /// </summary>
        public Func<Guid> OnNewSession { get; set; }

        private IObjectSpace _os;
        private LineStage    _stage;

        // ── Filter / mode state ────────────────────────────────────────────
        public bool UseArchive
        {
            get => GetPropertyValue<bool>();
            set => SetPropertyValue(value);
        }

        public DateTime? DateFrom
        {
            get => GetPropertyValue<DateTime?>();
            set => SetPropertyValue(value);
        }

        public DateTime? DateTo
        {
            get => GetPropertyValue<DateTime?>();
            set => SetPropertyValue(value);
        }

        public void Load(IObjectSpace os, LineStage stage)
        {
            _os    = os;
            _stage = stage;
            // Apply defaults only on first load
            if (!_defaultsApplied)
            {
                UseArchive       = true;
                DateFrom         = DateTime.Today.AddDays(-30);
                DateTo           = null;
                _defaultsApplied = true;
            }
            Reload();
        }

        private bool _defaultsApplied;

        public void Reload()
        {
            if (_os == null || _stage == null) return;
            var (table, cols, criteria, calcFields, excelTmpls) = UseArchive
                ? BathStageSheetService.BuildHybrid(_os, _stage, DateFrom, DateTo)
                : BathStageSheetService.Build(_os, _stage, DateFrom, DateTo);
            SheetData        = table;
            Columns          = cols;
            Criteria         = criteria;
            CalculatedFields = calcFields;
            ExcelTemplates   = excelTmpls;
            BlazorLayoutJson = _stage.BlazorLayoutJson;
            RefreshArchiveTimestamp();
        }

        /// <summary>True if at least one archive row exists for this stage.</summary>
        public bool HasArchive => _stage != null && _os != null &&
            _os.GetObjects<BathStageSheetArchiveRow>()
               .Any(r => r.Stage?.Oid == _stage.Oid);

        /// <summary>Timestamp of the most recently archived row for this stage (bound to component).</summary>
        public DateTime? LatestArchivedOn
        {
            get => GetPropertyValue<DateTime?>();
            private set => SetPropertyValue(value);
        }

        private void RefreshArchiveTimestamp()
        {
            LatestArchivedOn = _stage == null || _os == null ? null :
                _os.GetObjects<BathStageSheetArchiveRow>()
                   .Where(r => r.Stage?.Oid == _stage.Oid)
                   .Select(r => (DateTime?)r.ArchivedOn)
                   .DefaultIfEmpty(null).Max();
        }

        public void Clear()
        {
            _stage           = null;
            SheetData        = null;
            Columns          = null;
            Criteria         = null;
            CalculatedFields = null;
            ExcelTemplates   = null;
        }

        /// <summary>
        /// Creates a new <see cref="MeasurementSession"/> for the current stage's line,
        /// appends an empty row at the bottom of <see cref="SheetData"/>,
        /// and returns the new session's Oid so the component can start editing that row.
        /// </summary>
        public Guid AddNewSession()
        {
            if (_os == null || _stage == null) return Guid.Empty;

            // Create session + seed empty measurements for every stage parameter
            var session = BathStageSheetService.CreateSessionForStage(_os, _stage);

            // Append a placeholder row to the existing DataTable so the grid shows it immediately
            // without a full reload (which would scroll the user away).
            if (SheetData != null)
            {
                var row = SheetData.NewRow();
                row[BathStageSheetService.ColOid]      = session.Oid;
                row[BathStageSheetService.ColDate]     = session.MeasuredOn;
                row[BathStageSheetService.ColOperator] = string.Empty;
                row[BathStageSheetService.ColNotes]    = string.Empty;
                SheetData.Rows.Add(row);
            }

            return session.Oid;
        }

        public void SaveRow(DataRowView editedRow)
        {
            if (_os == null || _stage == null) return;

            var sessionOid = (Guid)editedRow[BathStageSheetService.ColOid];
            var session    = _os.GetObjectByKey<MeasurementSession>(sessionOid);
            if (session == null) return;

            // Persist session header fields
            var newOperator = editedRow[BathStageSheetService.ColOperator]?.ToString() ?? string.Empty;
            if (session.OperatorName != newOperator)
                session.OperatorName = newOperator;

            if (editedRow[BathStageSheetService.ColDate] is DateTime newDate && session.MeasuredOn != newDate)
                session.MeasuredOn = newDate;

            // Persist the Notes field
            var newNotes = editedRow[BathStageSheetService.ColNotes]?.ToString() ?? string.Empty;
            if (session.Notes != newNotes)
                session.Notes = newNotes;

            foreach (var meta in Columns ?? [])
            {
                if (!editedRow.Row.Table.Columns.Contains(meta.ValueKey)) continue;
                var raw = editedRow[meta.ValueKey]?.ToString() ?? string.Empty;

                var param = meta.Parameter;

                var m = _os.GetObjects<ParameterMeasurement>()
                    .FirstOrDefault(x => x.MeasurementSession?.Oid == session.Oid
                                      && x.Stage?.Oid              == _stage.Oid
                                      && x.Parameter?.Oid          == param.Oid);

                if (m == null)
                {
                    m = _os.CreateObject<ParameterMeasurement>();
                    m.MeasurementSession = session;
                    m.Stage              = _stage;
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
