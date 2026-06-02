using DevExpress.ExpressApp;
using DevExpress.ExpressApp.Editors;
using DevExpress.ExpressApp.Model;
using DevExpress.AIIntegration.WinForms;
using DevExpress.Utils.Behaviors;
using DevExpress.XtraEditors;
using DevExpress.XtraEditors.Repository;
using DevExpress.XtraGrid;
using DevExpress.XtraGrid.Columns;
using DevExpress.XtraGrid.Views.Grid;
using PowderCoatingWizard.Module.BusinessObjects;
using PowderCoatingWizard.Module.Editors;
using PowderCoatingWizard.Win.Dialogs;
using System.Data;
using System.Drawing;
using System.Windows.Forms;

namespace PowderCoatingWizard.Win.Editors
{
    public interface IModelBathStageSheetItemWin : IModelViewItem
    {
        /// <summary>Base-64 encoded GridView layout XML.</summary>
        string LayoutXml { get; set; }
    }

    /// <summary>
    /// XAF WinForms ViewItem – Bath Stage Sheet.
    ///
    /// Flat (non-banded) grid:
    ///   Date | Operator  |  Value + Status per StageParameter  |  one column per StageCriterion
    ///
    /// To add to LineStage_DetailView:
    ///   1. Open Model Editor (Model.xafml in Win project).
    ///   2. Views > PowderCoatingWizard.Module.BusinessObjects > LineStage_DetailView > Items.
    ///   3. Right-click Items > Add > BathStageSheetItemWin.
    ///   4. Drag into layout.
    /// </summary>
    [ViewItem(typeof(IModelBathStageSheetItemWin))]
    public class BathStageSheetWinViewItem : ViewItem, IComplexViewItem
    {
        private static readonly Color _alarmColor   = Color.FromArgb(255, 180, 180);
        private static readonly Color _warningColor = Color.FromArgb(255, 240, 180);

        private static readonly Color[] _palette =
        [
            Color.FromArgb(220, 235, 255),
            Color.FromArgb(220, 255, 230),
            Color.FromArgb(255, 245, 210),
            Color.FromArgb(245, 220, 255),
            Color.FromArgb(210, 255, 255),
            Color.FromArgb(255, 225, 225),
            Color.FromArgb(230, 255, 215),
            Color.FromArgb(255, 235, 210),
        ];

        private IObjectSpace             _os;
        private GridControl              _grid;
        private GridView                 _gridView;
        private BehaviorManager          _aiCriteriaBehaviorManager;
        private List<BathStageColumnMeta>   _columns       = [];
        private List<StageCriterion>         _criteria      = [];
        private List<StageCalculatedField>   _calcFields    = [];
        private List<StageExcelTemplate>     _excelTemplates = [];
        private bool                         _savingLayout;

        // ── Filter / mode state ────────────────────────────────────────────────────────────────────────────────────────────
        private bool      _useArchive = true;
        private DateTime? _dateFrom   = DateTime.Today.AddDays(-30);
        private DateTime? _dateTo     = null;

        // toolbar controls (kept for programmatic access)
        private CheckEdit    _chkArchive;
        private DateEdit     _deFrom;
        private DateEdit     _deTo;
        private SimpleButton _btnLoad;
        private SimpleButton _btnNewSession;
        private SimpleButton _btnExport;

        // FieldName → pair cell colour (for parameter columns)
        private readonly Dictionary<string, Color> _colColor = [];

        public BathStageSheetWinViewItem(IModelViewItem modelItem, Type objectType)
            : base(objectType, modelItem.Id)
        {
        }

        // ── IComplexViewItem ────────────────────────────────────────────────

        void IComplexViewItem.Setup(IObjectSpace objectSpace, XafApplication application)
        {
            _os = objectSpace;
            _os.Committed += OnCommitted;
        }

        private bool _addingSession;

        private void OnCommitted(object sender, EventArgs e)
        {
            if (_gridView == null || _savingLayout || _addingSession) return;
            LoadData();
        }

        private void OnNewSessionClick(object sender, EventArgs e)
        {
            if (_os == null || CurrentObject is not LineStage stage) return;

            _addingSession = true;
            try
            {
                var candidateSessions = BathStageSheetService.GetSessionsMissingStage(_os, stage);
                MeasurementSession session;

                if (candidateSessions.Count > 0)
                {
                    // Offer choice: new session or append to an existing one
                    using var dlg = new NewSessionPickerDialog(candidateSessions);
                    if (dlg.ShowDialog() != System.Windows.Forms.DialogResult.OK) return;

                    if (dlg.SelectedSession != null)
                    {
                        BathStageSheetService.AppendStageToSession(_os, stage, dlg.SelectedSession);
                        session = dlg.SelectedSession;
                    }
                    else
                    {
                        session = BathStageSheetService.CreateSessionForStage(_os, stage);
                    }
                }
                else
                {
                    session = BathStageSheetService.CreateSessionForStage(_os, stage);
                }

                // Reload data manually so we control the flow
                LoadData();

                // Find and focus the row for the session we just created/appended
                var sessionOid = session.Oid;
                for (int i = 0; i < _gridView.DataRowCount; i++)
                {
                    var rowOid = _gridView.GetRowCellValue(i, BathStageSheetService.ColOid);
                    if (rowOid is Guid g && g == sessionOid)
                    {
                        _gridView.FocusedRowHandle = i;
                        _gridView.Focus();
                        _gridView.ShowEditor();
                        break;
                    }
                }
            }
            finally
            {
                _addingSession = false;
            }
        }

        // ── ViewItem ────────────────────────────────────────────────────────

        protected override object CreateControlCore()
        {
            // ── Toolbar ────────────────────────────────────────────────────
            _chkArchive = new CheckEdit
            {
                Width  = 80,
                Height = 22,
                Margin = new Padding(0, 0, 12, 0),
            };
            _chkArchive.Properties.Caption = "Archive";
            _chkArchive.CheckedChanged += (s, e) =>
            {
                _useArchive = _chkArchive.Checked;
                UpdateToolbarState();
            };

            var lblFrom = new LabelControl
            {
                Text         = "From:",
                AutoSizeMode = LabelAutoSizeMode.None,
                Width        = 36,
                Appearance   = { TextOptions = { VAlignment = DevExpress.Utils.VertAlignment.Center } },
                Margin       = new Padding(0, 0, 4, 0),
            };
            _deFrom = new DateEdit { Width = 108, Margin = new Padding(0, 0, 10, 0) };
            _deFrom.Properties.NullDate              = DateTime.MinValue;
            _deFrom.Properties.AllowNullInput        = DevExpress.Utils.DefaultBoolean.True;
            _deFrom.Properties.DisplayFormat.FormatType   = DevExpress.Utils.FormatType.DateTime;
            _deFrom.Properties.DisplayFormat.FormatString = "dd.MM.yyyy";
            _deFrom.Properties.EditFormat.FormatType      = DevExpress.Utils.FormatType.DateTime;
            _deFrom.Properties.EditFormat.FormatString    = "dd.MM.yyyy";

            var lblTo = new LabelControl
            {
                Text         = "To:",
                AutoSizeMode = LabelAutoSizeMode.None,
                Width        = 24,
                Appearance   = { TextOptions = { VAlignment = DevExpress.Utils.VertAlignment.Center } },
                Margin       = new Padding(0, 0, 4, 0),
            };
            _deTo = new DateEdit { Width = 108, Margin = new Padding(0, 0, 12, 0) };
            _deTo.Properties.NullDate              = DateTime.MinValue;
            _deTo.Properties.AllowNullInput        = DevExpress.Utils.DefaultBoolean.True;
            _deTo.Properties.DisplayFormat.FormatType   = DevExpress.Utils.FormatType.DateTime;
            _deTo.Properties.DisplayFormat.FormatString = "dd.MM.yyyy";
            _deTo.Properties.EditFormat.FormatType      = DevExpress.Utils.FormatType.DateTime;
            _deTo.Properties.EditFormat.FormatString    = "dd.MM.yyyy";

            _btnLoad = new SimpleButton
            {
                Text   = "Load",
                Width  = 72,
                Height = 24,
            };
            _btnLoad.Click += (s, e) =>
            {
                _dateFrom = _deFrom.DateTime == DateTime.MinValue ? null : (DateTime?)_deFrom.DateTime.Date;
                _dateTo   = _deTo.DateTime   == DateTime.MinValue ? null : (DateTime?)_deTo.DateTime.Date.AddDays(1).AddTicks(-1);
                LoadData();
            };

            _btnNewSession = new SimpleButton
            {
                Text      = "+ New Session",
                Width     = 110,
                Height    = 24,
                Margin    = new Padding(16, 0, 0, 0),
                Appearance = { BackColor = Color.FromArgb(198, 239, 206), ForeColor = Color.FromArgb(0, 97, 0), Font = new Font(SystemFonts.DefaultFont, FontStyle.Bold) },
            };
            _btnNewSession.Appearance.Options.UseBackColor = true;
            _btnNewSession.Appearance.Options.UseForeColor = true;
            _btnNewSession.Appearance.Options.UseFont      = true;
            _btnNewSession.Click += OnNewSessionClick;

            _btnExport = new SimpleButton
            {
                Text   = "Export to Excel",
                Width  = 120,
                Height = 24,
                Margin = new Padding(16, 0, 0, 0),
            };
            _btnExport.Click += (s, e) => ExportToExcel();

            // Use a panel with manual positioning for crisp vertical centering
            var toolbar = new Panel
            {
                Dock        = DockStyle.Top,
                Height      = 36,
                BackColor   = System.Drawing.Color.FromArgb(245, 247, 250),
            };

            // Draw a subtle bottom border
            toolbar.Paint += (s, e) =>
            {
                var p = toolbar as Panel;
                using var pen = new System.Drawing.Pen(System.Drawing.Color.FromArgb(210, 215, 220));
                e.Graphics.DrawLine(pen, 0, p!.Height - 1, p.Width, p.Height - 1);
            };

            static void CenterV(Control c, int panelH)
            {
                c.Top = (panelH - c.Height) / 2;
            }

            toolbar.SizeChanged += (s, e) =>
            {
                int left = 8;
                int h    = toolbar.Height;

                _chkArchive.Height = 22; _chkArchive.Left = left; CenterV(_chkArchive, h); left += _chkArchive.Width + _chkArchive.Margin.Right;
                lblFrom.Height     = 20; lblFrom.Left     = left; CenterV(lblFrom,     h); left += lblFrom.Width     + lblFrom.Margin.Right;
                _deFrom.Height     = 22; _deFrom.Left     = left; CenterV(_deFrom,     h); left += _deFrom.Width     + _deFrom.Margin.Right;
                lblTo.Height       = 20; lblTo.Left       = left; CenterV(lblTo,       h); left += lblTo.Width       + lblTo.Margin.Right;
                _deTo.Height       = 22; _deTo.Left       = left; CenterV(_deTo,       h); left += _deTo.Width       + _deTo.Margin.Right;
                _btnLoad.Height       = 24; _btnLoad.Left       = left; CenterV(_btnLoad,       h); left += _btnLoad.Width + _btnLoad.Margin.Right + _btnNewSession.Margin.Left;
                _btnNewSession.Height = 24; _btnNewSession.Left = left; CenterV(_btnNewSession, h); left += _btnNewSession.Width + 8 + _btnExport.Margin.Left;
                _btnExport.Height     = 24; _btnExport.Left     = left; CenterV(_btnExport,     h);
            };

            toolbar.Controls.AddRange([_chkArchive, lblFrom, _deFrom, lblTo, _deTo, _btnLoad, _btnNewSession, _btnExport]);
            // Trigger initial layout by simulating a size event
            toolbar.Size = toolbar.Size;

            // ── Grid ───────────────────────────────────────────────────────
            _grid     = new GridControl { Dock = DockStyle.Fill };
            _gridView = new GridView(_grid)
            {
                OptionsBehavior      = { Editable = true },
                OptionsView          = { ShowGroupPanel = false, ColumnAutoWidth = false, ColumnHeaderAutoHeight = DevExpress.Utils.DefaultBoolean.True },
                OptionsCustomization = { AllowColumnMoving = true, AllowColumnResizing = true }
            };
            _grid.MainView = _gridView;
            _gridView.OptionsMenu.ShowConditionalFormattingItem = true;
            _gridView.OptionsFilter.AllowFilterEditor = true;

            _aiCriteriaBehaviorManager = new BehaviorManager();
            _aiCriteriaBehaviorManager.Attach<PromptToExpressionBehavior>(_gridView, behavior =>
            {
                behavior.Properties.RetryAttemptCount = 3;
                behavior.Properties.Temperature = 1.0f;
                behavior.Properties.PromptAugmentation =
                    "Generate only valid DevExpress filter criteria for the current grid columns. " +
                    "Do not include explanations or markdown.";
            });

            _gridView.CustomDrawCell          += OnCustomDrawCell;
            _gridView.CustomColumnDisplayText  += OnCustomColumnDisplayText;
            _gridView.RowUpdated               += OnRowUpdated;

            // ── Container panel ────────────────────────────────────────────
            var container = new Panel { Dock = DockStyle.Fill };
            container.Controls.Add(_grid);      // Fill — added first
            container.Controls.Add(toolbar);    // Top  — added second (paints on top)

            UpdateToolbarState();
            // Sync toolbar controls to default state
            _chkArchive.Checked  = _useArchive;
            _deFrom.EditValue    = _dateFrom;
            _deTo.EditValue      = _dateTo.HasValue ? (object)_dateTo.Value : null;
            LoadData();
            return container;
        }

        private void ExportToExcel()
        {
            if (_gridView == null) return;
            using var dlg = new SaveFileDialog
            {
                Title      = "Export to Excel",
                Filter     = "Excel Workbook (*.xlsx)|*.xlsx",
                DefaultExt = "xlsx",
                FileName   = $"StageSheet_{DateTime.Now:yyyyMMdd_HHmm}.xlsx",
            };
            if (dlg.ShowDialog() != DialogResult.OK) return;

            // Temporarily clear display format on numeric parameter columns so Excel
            // receives a plain number cell (not an unrecognised custom format string).
            var savedFormats = new Dictionary<GridColumn, (DevExpress.Utils.FormatType ft, string fs)>();
            foreach (GridColumn col in _gridView.Columns)
            {
                if (col.Tag is BathStageColumnMeta meta && col.FieldName == meta.ValueKey && meta.IsNumeric
                    && !string.IsNullOrEmpty(col.DisplayFormat.FormatString))
                {
                    savedFormats[col] = (col.DisplayFormat.FormatType, col.DisplayFormat.FormatString);
                    col.DisplayFormat.FormatType   = DevExpress.Utils.FormatType.None;
                    col.DisplayFormat.FormatString = string.Empty;
                }
            }

            try
            {
                var opts = new DevExpress.XtraPrinting.XlsxExportOptions
                {
                    TextExportMode = DevExpress.XtraPrinting.TextExportMode.Value,
                };
                _gridView.ExportToXlsx(dlg.FileName, opts);
            }
            finally
            {
                // Restore display formats
                foreach (var (col, (ft, fs)) in savedFormats)
                {
                    col.DisplayFormat.FormatType   = ft;
                    col.DisplayFormat.FormatString = fs;
                }
            }
        }

        /// <summary>Enables/disables date pickers based on selected mode.</summary>
        private void UpdateToolbarState()
        {
            bool enabled = true; // date filter works in both modes
            _deFrom.Enabled = enabled;
            _deTo.Enabled   = enabled;
        }

        protected override void SaveModelCore()
        {
            base.SaveModelCore();
            SaveLayout();
        }

        public override void Refresh()
        {
            base.Refresh();
            LoadData();
        }

        protected override void OnCurrentObjectChanged()
        {
            base.OnCurrentObjectChanged();
            if (_gridView != null)
            {
                SaveLayout();
                LoadData();
            }
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing && _os != null)
                _os.Committed -= OnCommitted;
            if (disposing)
                _aiCriteriaBehaviorManager?.Dispose();
            base.Dispose(disposing);
        }

        // ── Layout ──────────────────────────────────────────────────────────

        private void SaveLayout()
        {
            if (_gridView == null || _savingLayout || CurrentObject is not LineStage stage) return;
            using var ms = new System.IO.MemoryStream();
            var saveOpts = DevExpress.Utils.OptionsLayoutBase.FullLayout;
            _gridView.SaveLayoutToStream(ms, saveOpts);
            var xml = Convert.ToBase64String(ms.ToArray());
            if (stage.GridLayoutXml == xml) return;
            stage.GridLayoutXml = xml;
            _savingLayout = true;
            try   { _os.CommitChanges(); }
            finally { _savingLayout = false; }
        }

        private void RestoreLayout()
        {
            if (_gridView == null || CurrentObject is not LineStage stage) return;
            var xml = stage.GridLayoutXml;
            if (string.IsNullOrEmpty(xml)) return;
            try
            {
                var bytes = Convert.FromBase64String(xml);
                using var ms = new System.IO.MemoryStream(bytes);
                _gridView.RestoreLayoutFromStream(ms, DevExpress.Utils.OptionsLayoutBase.FullLayout);
            }
            catch { /* stale / corrupt layout */ }
        }

        // ── Data loading ────────────────────────────────────────────────────

        private void LoadData()
        {
            if (_os == null || CurrentObject is not LineStage stage) return;

            var (table, cols, criteria, calcFields, excelTmpls) = _useArchive
                ? BathStageSheetService.BuildHybrid(_os, stage, _dateFrom, _dateTo)
                : BathStageSheetService.Build(_os, stage, _dateFrom, _dateTo);
            _columns        = cols;
            _criteria       = criteria;
            _calcFields     = calcFields;
            _excelTemplates = excelTmpls;
            _colColor.Clear();

            _grid?.DataSource = null;
            _gridView?.Columns.Clear();

            // ── Fixed columns ─────────────────────────────────────────────
            AddColumn(BathStageSheetService.ColOid,      visible: false);

            var dateCol = AddColumn(BathStageSheetService.ColDate, "Date", width: 140, readOnly: false);
            dateCol.DisplayFormat.FormatType   = DevExpress.Utils.FormatType.DateTime;
            dateCol.DisplayFormat.FormatString = "dd.MM.yyyy HH:mm";
            var dateEdit = new DevExpress.XtraEditors.Repository.RepositoryItemDateEdit();
            dateEdit.DisplayFormat.FormatType      = DevExpress.Utils.FormatType.DateTime;
            dateEdit.DisplayFormat.FormatString    = "dd.MM.yyyy HH:mm";
            dateEdit.EditFormat.FormatType         = DevExpress.Utils.FormatType.DateTime;
            dateEdit.EditFormat.FormatString       = "dd.MM.yyyy HH:mm";
            dateEdit.Mask.MaskType                 = DevExpress.XtraEditors.Mask.MaskType.DateTime;
            dateEdit.Mask.EditMask                 = "dd.MM.yyyy HH:mm";
            dateEdit.Mask.UseMaskAsDisplayFormat   = true;
            dateCol.ColumnEdit = dateEdit;

            var opCol = AddColumn(BathStageSheetService.ColOperator, "Operator", width: 120, readOnly: false);
            if (stage.Line != null)
            {
                var opRepo = new RepositoryItemComboBox { TextEditStyle = DevExpress.XtraEditors.Controls.TextEditStyles.Standard };
                foreach (var op in stage.Line.Operators.Where(o => o.IsActive).OrderBy(o => o.Name))
                    opRepo.Items.Add(op.Name);
                opCol.ColumnEdit = opRepo;
            }

            var notesCol = AddColumn(BathStageSheetService.ColNotes, "Notes", width: 200, readOnly: false);
            notesCol.ColumnEdit = new DevExpress.XtraEditors.Repository.RepositoryItemMemoEdit { AcceptsReturn = false };

            // ── Parameter value + status pairs ────────────────────────────
            for (int i = 0; i < cols.Count; i++)
            {
                var meta      = cols[i];
                int paletteIdx = i % _palette.Length;
                float baseHue = _palette[paletteIdx].GetHue();

                // Alternate lightness so value and status are visually paired
                var valColor    = HslToColor(baseHue, 0.55f, 0.90f);
                var statusColor = HslToColor(baseHue, 0.55f, 0.84f);

                var valCol = AddColumn(meta.ValueKey, meta.ParameterName, width: 100);
                valCol.Tag = meta;
                valCol.OptionsColumn.AllowSort = DevExpress.Utils.DefaultBoolean.False;

                if (meta.IsNumeric)
                {
                    var unit = meta.Parameter.Unit?.Symbol;
                    if (!string.IsNullOrWhiteSpace(unit))
                    {
                        valCol.DisplayFormat.FormatType   = DevExpress.Utils.FormatType.Custom;
                        valCol.DisplayFormat.FormatString = $"{{0:G}} {unit}";
                    }
                    else
                    {
                        valCol.DisplayFormat.FormatType   = DevExpress.Utils.FormatType.Numeric;
                        valCol.DisplayFormat.FormatString = "G";
                    }
                }
                else if (meta.IsPredefined)
                {
                    var repo = new RepositoryItemComboBox { TextEditStyle = DevExpress.XtraEditors.Controls.TextEditStyles.DisableTextEditor };
                    var options = meta.Parameter.PredefinedValues
                        .OrderBy(v => v.SortOrder).ThenBy(v => v.Name).ToList();
                    foreach (var opt in options)
                        repo.Items.Add(opt.Name);
                    valCol.ColumnEdit = repo;
                }

                var stCol = AddColumn(meta.StatusKey, $"\u25cf {meta.ParameterName}", width: 80, readOnly: true);
                stCol.Tag = meta;

                _colColor[meta.ValueKey]  = valColor;
                _colColor[meta.StatusKey] = statusColor;
            }

            // ── Criterion columns ─────────────────────────────────────────
            var criterionColor = Color.FromArgb(235, 235, 245);
            foreach (var criterion in criteria)
            {
                var key = BathStageSheetService.CriterionColKey(criterion);
                var col = AddColumn(key, criterion.Name, width: 160, readOnly: true);
                col.Tag = criterion;
            }

            // ── Calculated field columns ──────────────────────────────────
            foreach (var field in calcFields)
            {
                var key = BathStageSheetService.CalcFieldColKey(field);
                var col = AddColumn(key, field.Name, width: field.Width > 0 ? field.Width : 140, readOnly: true);
                col.Tag = field;
            }

            // ── Excel template output columns ─────────────────────────────
            foreach (var tmpl in excelTmpls)
                foreach (var outMap in tmpl.OutputMaps.OrderBy(m => m.SortOrder).ThenBy(m => m.ColumnName))
                {
                    if (string.IsNullOrWhiteSpace(outMap.ColumnName)) continue;
                    var key = BathStageSheetService.ExcelOutputColKey(tmpl, outMap);
                    var col = AddColumn(key, outMap.ColumnName, width: outMap.Width > 0 ? outMap.Width : 140, readOnly: true);
                    col.Tag = outMap;
                }

            _grid?.DataSource = table;
            RestoreLayout();

            // RetagColumns FIRST so ReapplyViewOptions can read col.Tag for ReadOnly logic
            RetagColumns();

            // RestoreLayout (FullLayout) may serialise old values — force these back.
            ReapplyViewOptions();

            // Force the grid to re-measure header heights after WordWrap was reapplied.
            _gridView?.LayoutChanged();
        }

        private GridColumn AddColumn(
            string fieldName,
            string caption  = null,
            int    width    = 0,
            bool   readOnly = false,
            bool   visible  = true)
        {
            var col = new GridColumn
            {
                FieldName     = fieldName,
                Caption       = caption ?? fieldName,
                Visible       = visible,
                OptionsColumn = { ReadOnly = readOnly }
            };
            if (width > 0) col.Width = width;
            _gridView?.Columns.Add(col);
            return col;
        }

        private void RetagColumns()
        {
            if (_gridView == null) return;
            var metaLookup = new Dictionary<string, BathStageColumnMeta>(StringComparer.Ordinal);
            foreach (var meta in _columns)
            {
                metaLookup[meta.ValueKey]  = meta;
                metaLookup[meta.StatusKey] = meta;
            }
            var criterionLookup = _criteria.ToDictionary(
                c => BathStageSheetService.CriterionColKey(c), StringComparer.Ordinal);
            var calcLookup = _calcFields.ToDictionary(
                f => BathStageSheetService.CalcFieldColKey(f), StringComparer.Ordinal);
            var excelOutputLookup = _excelTemplates
                .SelectMany(t => t.OutputMaps.Select(m => (Key: BathStageSheetService.ExcelOutputColKey(t, m), Map: m)))
                .ToDictionary(x => x.Key, x => x.Map, StringComparer.Ordinal);

            foreach (GridColumn col in _gridView.Columns)
            {
                if (metaLookup.TryGetValue(col.FieldName, out var meta))
                    col.Tag = meta;
                else if (criterionLookup.TryGetValue(col.FieldName, out var criterion))
                    col.Tag = criterion;
                else if (calcLookup.TryGetValue(col.FieldName, out var field))
                    col.Tag = field;
                else if (excelOutputLookup.TryGetValue(col.FieldName, out var outMap))
                    col.Tag = outMap;

                ApplyHeaderWordWrap(col);
            }
        }

        private static void ApplyHeaderWordWrap(GridColumn col)
        {
            col.AppearanceHeader.TextOptions.WordWrap   = DevExpress.Utils.WordWrap.Wrap;
            col.AppearanceHeader.Options.UseTextOptions = true;
        }

        private void ReapplyViewOptions()
        {
            if (_gridView == null) return;
            _gridView.OptionsBehavior.Editable                  = true;
            _gridView.OptionsView.ColumnHeaderAutoHeight         = DevExpress.Utils.DefaultBoolean.True;
            _gridView.OptionsMenu.ShowConditionalFormattingItem  = true;

            foreach (GridColumn col in _gridView.Columns)
            {
                // Reapply ReadOnly — FullLayout restore overwrites it from saved layout
                col.OptionsColumn.ReadOnly = col.FieldName switch
                {
                    BathStageSheetService.ColOid      => false,
                    BathStageSheetService.ColDate     => false,
                    BathStageSheetService.ColOperator => false,
                    BathStageSheetService.ColNotes    => false,
                    _ when col.Tag is BathStageColumnMeta m
                        => col.FieldName == m.StatusKey,
                    _ => true
                };

                // Reapply Date format — FullLayout restore overwrites DisplayFormat and ColumnEdit
                if (col.FieldName == BathStageSheetService.ColDate)
                {
                    col.DisplayFormat.FormatType   = DevExpress.Utils.FormatType.DateTime;
                    col.DisplayFormat.FormatString = "dd.MM.yyyy HH:mm";

                    // Rebuild ColumnEdit if lost (FullLayout can clear it)
                    if (col.ColumnEdit is not DevExpress.XtraEditors.Repository.RepositoryItemDateEdit)
                    {
                        var de = new DevExpress.XtraEditors.Repository.RepositoryItemDateEdit();
                        de.DisplayFormat.FormatType    = DevExpress.Utils.FormatType.DateTime;
                        de.DisplayFormat.FormatString  = "dd.MM.yyyy HH:mm";
                        de.EditFormat.FormatType       = DevExpress.Utils.FormatType.DateTime;
                        de.EditFormat.FormatString     = "dd.MM.yyyy HH:mm";
                        de.Mask.MaskType               = DevExpress.XtraEditors.Mask.MaskType.DateTime;
                        de.Mask.EditMask               = "dd.MM.yyyy HH:mm";
                        de.Mask.UseMaskAsDisplayFormat = true;
                        col.ColumnEdit = de;
                    }
                    else
                    {
                        var de = (DevExpress.XtraEditors.Repository.RepositoryItemDateEdit)col.ColumnEdit;
                        de.DisplayFormat.FormatType   = DevExpress.Utils.FormatType.DateTime;
                        de.DisplayFormat.FormatString = "dd.MM.yyyy HH:mm";
                        de.EditFormat.FormatType      = DevExpress.Utils.FormatType.DateTime;
                        de.EditFormat.FormatString    = "dd.MM.yyyy HH:mm";
                    }
                }

                // Reapply numeric parameter DisplayFormat with unit — FullLayout may clear it
                if (col.Tag is BathStageColumnMeta metaR && col.FieldName == metaR.ValueKey && metaR.IsNumeric)
                {
                    var unit = metaR.Parameter.Unit?.Symbol;
                    if (!string.IsNullOrWhiteSpace(unit))
                    {
                        col.DisplayFormat.FormatType   = DevExpress.Utils.FormatType.Custom;
                        col.DisplayFormat.FormatString = $"{{0:G}} {unit}";
                    }
                    else
                    {
                        col.DisplayFormat.FormatType   = DevExpress.Utils.FormatType.Numeric;
                        col.DisplayFormat.FormatString = "G";
                    }
                }
            }
        }

        private void OnCustomColumnDisplayText(
            object sender,
            DevExpress.XtraGrid.Views.Base.CustomColumnDisplayTextEventArgs e)
        {
            // Criterion columns: show message text
            if (e.Column.Tag is StageCriterion criterion)
            {
                if (e.Value is CriterionCellValue cell)
                    e.DisplayText = cell.Message;
                return;
            }

            // Parameter value columns — display format (with unit) is applied via column DisplayFormat
        }

        private void OnCustomDrawCell(
            object sender,
            DevExpress.XtraGrid.Views.Base.RowCellCustomDrawEventArgs e)
        {
            if (e.RowHandle < 0 || _grid.DataSource is not DataTable table) return;
            var row = _gridView.GetDataRow(e.RowHandle);
            if (row == null) return;

            // ── Parameter columns: alarm/warning override ─────────────────
            if (e.Column.Tag is BathStageColumnMeta meta)
            {
                if (table.Columns.Contains(meta.StatusKey))
                {
                    var rawVal  = row[meta.StatusKey];
                    var hasMeas = rawVal != DBNull.Value && rawVal != null;
                    var status  = hasMeas ? (ParameterStatus)rawVal : ParameterStatus.OK;

                    // Skip our colour only if the user has defined explicit CF rules for this column
                    bool hasUserCf = _gridView.FormatRules
                        .Cast<DevExpress.XtraGrid.GridFormatRule>()
                        .Any(r => r.Enabled && (r.Column == e.Column || r.ApplyToRow));
                    if (!hasUserCf)
                    {
                        e.Appearance.BackColor = status switch
                        {
                            ParameterStatus.Alarm   => _alarmColor,
                            ParameterStatus.Warning => _warningColor,
                            _ when _colColor.TryGetValue(e.Column.FieldName, out var c) => c,
                            _ => e.Appearance.BackColor
                        };
                        e.Appearance.Options.UseBackColor = true;
                    }

                    if (e.Column.FieldName == meta.StatusKey)
                    {
                        e.DisplayText = !hasMeas ? string.Empty : status switch
                        {
                            ParameterStatus.OK      => "✔ OK",
                            ParameterStatus.Warning => "⚠ Warn",
                            ParameterStatus.Alarm   => "⛔ Alarm",
                            _                       => status.ToString()
                        };
                    }
                }
                return;
            }

            // ── Criterion columns: colour by status ───────────────────────
            if (e.Column.Tag is StageCriterion criterion)
            {
                var colKey = BathStageSheetService.CriterionColKey(criterion);
                if (!table.Columns.Contains(colKey)) return;

                var cell = row[colKey] as CriterionCellValue;
                if (cell == null) return;

                bool hasUserCf = _gridView.FormatRules
                    .Cast<DevExpress.XtraGrid.GridFormatRule>()
                    .Any(r => r.Enabled && (r.Column == e.Column || r.ApplyToRow));
                if (!hasUserCf)
                {
                    e.Appearance.BackColor = cell.Status switch
                    {
                        ParameterStatus.Alarm   => _alarmColor,
                        ParameterStatus.Warning => _warningColor,
                        _                       => Color.FromArgb(235, 235, 245)
                    };
                    e.Appearance.Options.UseBackColor = true;
                }
                e.DisplayText = cell.Message;
            }
        }

        // ── Row save ─────────────────────────────────────────────────────────

        private void OnRowUpdated(
            object sender,
            DevExpress.XtraGrid.Views.Base.RowObjectEventArgs e)
        {
            if (_os == null || CurrentObject is not LineStage stage) return;
            if (e.Row is not System.Data.DataRowView editedRow) return;

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

            foreach (var meta in _columns)
            {
                if (!editedRow.Row.Table.Columns.Contains(meta.ValueKey)) continue;
                var raw = editedRow[meta.ValueKey]?.ToString() ?? string.Empty;

                var param = meta.Parameter;

                var m = _os.GetObjects<ParameterMeasurement>()
                    .FirstOrDefault(x => x.MeasurementSession?.Oid == session.Oid
                                      && x.Stage?.Oid              == stage.Oid
                                      && x.Parameter?.Oid          == param.Oid);

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
            LoadData();
        }

        // ── Colour helper ────────────────────────────────────────────────────

        private static Color HslToColor(float h, float s, float l)
        {
            float c = (1f - Math.Abs(2f * l - 1f)) * s;
            float x = c * (1f - Math.Abs((h / 60f) % 2f - 1f));
            float m = l - c / 2f;
            (float r, float g, float b) = (int)(h / 60) switch
            {
                0 => (c, x, 0f),
                1 => (x, c, 0f),
                2 => (0f, c, x),
                3 => (0f, x, c),
                4 => (x, 0f, c),
                _ => (c, 0f, x)
            };
            return Color.FromArgb(
                (int)Math.Round((r + m) * 255),
                (int)Math.Round((g + m) * 255),
                (int)Math.Round((b + m) * 255));
        }
    }
}
