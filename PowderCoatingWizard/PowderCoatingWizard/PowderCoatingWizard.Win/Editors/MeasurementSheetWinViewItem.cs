using DevExpress.ExpressApp;
using DevExpress.ExpressApp.Editors;
using DevExpress.ExpressApp.Model;
using DevExpress.AIIntegration.WinForms;
using DevExpress.Utils.Behaviors;
using DevExpress.XtraEditors;
using DevExpress.XtraEditors.Repository;
using DevExpress.XtraGrid;
using DevExpress.XtraGrid.Columns;
using DevExpress.XtraGrid.Views.BandedGrid;
using PowderCoatingWizard.Module.BusinessObjects;
using PowderCoatingWizard.Module.Editors;
using System.Data;
using System.Drawing;
using System.Windows.Forms;

namespace PowderCoatingWizard.Win.Editors
{
    public interface IModelMeasurementSheetItemWin : IModelViewItem
    {
        /// <summary>
        /// Base-64 encoded BandedGridView layout XML.
        /// Saved automatically when the View closes; restored when data is loaded.
        /// </summary>
        string LayoutXml { get; set; }
    }

    /// <summary>
    /// XAF WinForms ViewItem – horizontal measurement sheet.
    ///
    /// Layout:
    ///   Band 0 "Session"   → Date | Operator          (fixed, read-only)
    ///   Band N "Stage N – Name" → one BandedGridColumn per parameter
    ///
    /// Colours:
    ///   Each stage band gets a unique pale band-header tint.
    ///   Each parameter column gets a matching ultra-light cell tint.
    ///   Alarm / Warning status overrides the cell background.
    /// </summary>
    [ViewItem(typeof(IModelMeasurementSheetItemWin))]
    public class MeasurementSheetWinViewItem : ViewItem, IComplexViewItem
    {
        // ── palette: pale stage tints (band header / 20 % alpha cell fill) ─
        private static readonly Color[] _bandPalette =
        [
            Color.FromArgb(220, 235, 255),   // soft blue
            Color.FromArgb(220, 255, 230),   // soft green
            Color.FromArgb(255, 245, 210),   // soft amber
            Color.FromArgb(245, 220, 255),   // soft purple
            Color.FromArgb(210, 255, 255),   // soft cyan
            Color.FromArgb(255, 225, 225),   // soft rose
            Color.FromArgb(230, 255, 215),   // soft lime
            Color.FromArgb(255, 235, 210),   // soft peach
        ];

        // Alarm / Warning override colours (stronger than stage tint)
        private static readonly Color _alarmColor   = Color.FromArgb(255, 180, 180);
        private static readonly Color _warningColor = Color.FromArgb(255, 240, 180);

        private IObjectSpace                  _os;
        private XafApplication                _app;
        private GridControl                   _grid;
        private AdvBandedGridView             _gridView;
        private BehaviorManager               _aiCriteriaBehaviorManager;
        private List<ColumnMeta>              _columns  = [];
        private bool                          _savingLayout;

        // Filter state
        private DateTime? _dateFrom = DateTime.Today.AddDays(-30);
        private DateTime? _dateTo   = null;

        // Toolbar controls
        private DateEdit     _deFrom;
        private DateEdit     _deTo;
        private SimpleButton _btnLoad;
        private SimpleButton _btnExport;

        // Maps ValueKey/StatusKey → pair-specific cell colour so OnCustomDrawCell can look it up
        private Dictionary<string, Color> _columnCellColor = [];

        public MeasurementSheetWinViewItem(IModelViewItem modelItem, Type objectType)
            : base(objectType, modelItem.Id)
        {
        }

        // ── IComplexViewItem ────────────────────────────────────────────────

        void IComplexViewItem.Setup(IObjectSpace objectSpace, XafApplication application)
        {
            _os  = objectSpace;
            _app = application;
            _os.Committed += OnObjectSpaceCommitted;
        }

        private void OnObjectSpaceCommitted(object sender, EventArgs e)
        {
            if (_gridView == null || _savingLayout) return;
            LoadData();
        }

        // ── ViewItem ────────────────────────────────────────────────────────

        protected override object CreateControlCore()
        {
            // ── Toolbar ────────────────────────────────────────────────────
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
            _deFrom.DateTime = _dateFrom ?? DateTime.MinValue;

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

            _btnLoad = new SimpleButton { Text = "Load", Width = 72, Height = 24 };
            _btnLoad.Click += (s, e) =>
            {
                _dateFrom = _deFrom.DateTime == DateTime.MinValue ? null : (DateTime?)_deFrom.DateTime.Date;
                _dateTo   = _deTo.DateTime   == DateTime.MinValue ? null : (DateTime?)_deTo.DateTime.Date.AddDays(1).AddTicks(-1);
                LoadData();
            };

            _btnExport = new SimpleButton { Text = "Export to Excel", Width = 120, Height = 24, Margin = new Padding(16, 0, 0, 0) };
            _btnExport.Click += (s, e) => ExportToExcel();

            var toolbar = new Panel
            {
                Dock      = DockStyle.Top,
                Height    = 36,
                BackColor = Color.FromArgb(245, 247, 250),
            };
            toolbar.Paint += (s, e) =>
            {
                var p = toolbar;
                using var pen = new System.Drawing.Pen(Color.FromArgb(210, 215, 220));
                e.Graphics.DrawLine(pen, 0, p.Height - 1, p.Width, p.Height - 1);
            };

            static void CenterV(Control c, int h) => c.Top = (h - c.Height) / 2;
            toolbar.SizeChanged += (s, e) =>
            {
                int left = 8;
                int h    = toolbar.Height;
                lblFrom.Height = 20; lblFrom.Left = left; CenterV(lblFrom, h); left += lblFrom.Width + lblFrom.Margin.Right;
                _deFrom.Height = 22; _deFrom.Left = left; CenterV(_deFrom, h); left += _deFrom.Width + _deFrom.Margin.Right;
                lblTo.Height   = 20; lblTo.Left   = left; CenterV(lblTo,   h); left += lblTo.Width   + lblTo.Margin.Right;
                _deTo.Height   = 22; _deTo.Left   = left; CenterV(_deTo,   h); left += _deTo.Width   + _deTo.Margin.Right;
                _btnLoad.Height = 24; _btnLoad.Left = left; CenterV(_btnLoad, h); left += _btnLoad.Width + 8;
                _btnExport.Height = 24; _btnExport.Left = left + _btnExport.Margin.Left; CenterV(_btnExport, h);
            };
            toolbar.Controls.AddRange([lblFrom, _deFrom, lblTo, _deTo, _btnLoad, _btnExport]);
            toolbar.Size = toolbar.Size; // trigger initial layout

            // ── Grid ───────────────────────────────────────────────────────
            _grid     = new GridControl { Dock = DockStyle.Fill };
            _gridView = new AdvBandedGridView(_grid)
            {
                OptionsBehavior      = { Editable = true },
                OptionsView          = { ShowGroupPanel = false, ColumnAutoWidth = false, ColumnHeaderAutoHeight = DevExpress.Utils.DefaultBoolean.True },
                OptionsCustomization =
                {
                    AllowColumnMoving       = true,
                    AllowColumnResizing     = true,
                    AllowBandMoving         = true,
                    AllowBandResizing       = true,
                    AllowChangeColumnParent = true,
                    AllowChangeBandParent   = true
                }
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

            _gridView.CustomDrawCell         += OnCustomDrawCell;
            _gridView.CustomDrawBandHeader   += OnCustomDrawBandHeader;
            _gridView.CustomColumnDisplayText += OnCustomColumnDisplayText;
            _gridView.RowUpdated             += OnRowUpdated;

            // ── Container ──────────────────────────────────────────────────
            var container = new Panel { Dock = DockStyle.Fill };
            container.Controls.Add(_grid);
            container.Controls.Add(toolbar);

            LoadData();
            return container;
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

        protected override void Dispose(bool disposing)
        {
            if (disposing && _os != null)
                _os.Committed -= OnObjectSpaceCommitted;
            if (disposing)
                _aiCriteriaBehaviorManager?.Dispose();
            base.Dispose(disposing);
        }

        private void ExportToExcel()
        {
            if (_gridView == null) return;
            using var dlg = new SaveFileDialog
            {
                Title            = "Export to Excel",
                Filter           = "Excel Workbook (*.xlsx)|*.xlsx",
                DefaultExt       = "xlsx",
                FileName         = $"Measurements_{DateTime.Now:yyyyMMdd_HHmm}.xlsx",
            };
            if (dlg.ShowDialog() != DialogResult.OK) return;

            // Temporarily clear display format on numeric parameter columns so Excel
            // receives a plain number cell (not an unrecognised custom format string).
            var savedFormats = new Dictionary<DevExpress.XtraGrid.Views.BandedGrid.BandedGridColumn, (DevExpress.Utils.FormatType ft, string fs)>();
            foreach (DevExpress.XtraGrid.Views.BandedGrid.BandedGridColumn col in _gridView.Columns)
            {
                if (col.Tag is ColumnMeta meta && col.FieldName == meta.ValueKey && meta.IsNumeric
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
                foreach (var (col, (ft, fs)) in savedFormats)
                {
                    col.DisplayFormat.FormatType   = ft;
                    col.DisplayFormat.FormatString = fs;
                }
            }
        }

        // ── Layout persistence ──────────────────────────────────────────────

        private void SaveLayout()
        {
            if (_gridView == null || _savingLayout || CurrentObject is not ProductionLine line) return;
            using var ms = new System.IO.MemoryStream();
            _gridView.SaveLayoutToStream(ms, DevExpress.Utils.OptionsLayoutBase.FullLayout);
            var xml = Convert.ToBase64String(ms.ToArray());
            if (line.GridLayoutXml == xml) return;
            line.GridLayoutXml = xml;
            _savingLayout = true;
            try   { _os.CommitChanges(); }
            finally { _savingLayout = false; }
        }

        private void RestoreLayout()
        {
            if (_gridView == null || CurrentObject is not ProductionLine line) return;
            var xml = line.GridLayoutXml;
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
            if (_os == null || CurrentObject is not ProductionLine line) return;

            var (table, cols) = MeasurementSheetService.Build(_os, line, _dateFrom, _dateTo);
            _columns = cols;
            _columnCellColor.Clear();

            // ── Step 1: detach data source and clear everything ───────────
            _grid?.DataSource = null;
            _gridView?.Bands.Clear();        // clears bands + all columns

            // ── Step 2: build bands structure BEFORE attaching data ───────

            // Session band (fixed left)
            var sessionBand = new GridBand
            {
                Caption = "Session",
                Fixed   = FixedStyle.Left,
                Width   = 260
            };
            _gridView?.Bands.Add(sessionBand);

            // Stage bands
            var stageGroups = cols
                .GroupBy(c => (c.StagePosition, c.StageName))
                .OrderBy(g => g.Key.StagePosition)
                .ToList();

            var stageBands = new List<(GridBand Band, Color BandColor, int PaletteIdx)>();

            for (int si = 0; si < stageGroups.Count; si++)
            {
                var group      = stageGroups[si];
                int paletteIdx = si % _bandPalette.Length;
                var bandColor  = _bandPalette[paletteIdx];

                var stageBand = new GridBand
                {
                    Caption = $"{group.Key.StagePosition}. {group.Key.StageName}",
                    AppearanceHeader =
                    {
                        BackColor    = bandColor,
                        BackColor2   = ControlPaint.Light(bandColor, 0.4f),
                        GradientMode = System.Drawing.Drawing2D.LinearGradientMode.Vertical,
                        Font         = new Font(SystemFonts.DefaultFont, FontStyle.Bold)
                    }
                };
                stageBand.AppearanceHeader.Options.UseBackColor = true;
                stageBand.AppearanceHeader.Options.UseFont      = true;
                _gridView?.Bands.Add(stageBand);
                stageBands.Add((stageBand, bandColor, paletteIdx));
            }

            // ── Step 3: add columns manually into the correct bands ───────

            // OID – hidden
            var oidCol = new BandedGridColumn
            {
                FieldName = MeasurementSheetService.ColOid,
                OwnerBand = sessionBand,
                Visible   = false
            };
            _gridView?.Columns.Add(oidCol);

            // Date
            var dateCol = new BandedGridColumn
            {
                FieldName     = MeasurementSheetService.ColDate,
                Caption       = "Date",
                OwnerBand     = sessionBand,
                Visible       = true,
                Width         = 140,
                OptionsColumn = { ReadOnly = false }
            };
            dateCol.DisplayFormat.FormatType   = DevExpress.Utils.FormatType.DateTime;
            dateCol.DisplayFormat.FormatString = "dd.MM.yyyy HH:mm";
            var dateRepo = new RepositoryItemDateEdit();
            dateRepo.DisplayFormat.FormatType   = DevExpress.Utils.FormatType.DateTime;
            dateRepo.DisplayFormat.FormatString = "dd.MM.yyyy HH:mm";
            dateRepo.EditFormat.FormatType      = DevExpress.Utils.FormatType.DateTime;
            dateRepo.EditFormat.FormatString    = "dd.MM.yyyy HH:mm";
            dateRepo.Mask.MaskType              = DevExpress.XtraEditors.Mask.MaskType.DateTime;
            dateRepo.Mask.EditMask              = "dd.MM.yyyy HH:mm";
            dateCol.ColumnEdit = dateRepo;
            dateCol.AppearanceHeader.TextOptions.WordWrap = DevExpress.Utils.WordWrap.Wrap;
            dateCol.AppearanceHeader.Options.UseTextOptions = true;

            // Operator — combo from line operators, but still allows free text
            var opCol = new BandedGridColumn
            {
                FieldName     = MeasurementSheetService.ColOperator,
                Caption       = "Operator",
                OwnerBand     = sessionBand,
                Visible       = true,
                Width         = 120,
                OptionsColumn = { ReadOnly = false }
            };
            if (CurrentObject is ProductionLine lineForOp)
            {
                var opRepo = new RepositoryItemComboBox { TextEditStyle = DevExpress.XtraEditors.Controls.TextEditStyles.Standard };
                foreach (var op in lineForOp.Operators.Where(o => o.IsActive).OrderBy(o => o.Name))
                    opRepo.Items.Add(op.Name);
                opCol.ColumnEdit = opRepo;
            }
            opCol.AppearanceHeader.TextOptions.WordWrap = DevExpress.Utils.WordWrap.Wrap;
            opCol.AppearanceHeader.Options.UseTextOptions = true;

            // Notes
            var notesCol = new BandedGridColumn
            {
                FieldName     = MeasurementSheetService.ColNotes,
                Caption       = "Notes",
                OwnerBand     = sessionBand,
                Visible       = true,
                Width         = 200,
                OptionsColumn = { ReadOnly = false }
            };
            notesCol.ColumnEdit = new RepositoryItemMemoEdit { AcceptsReturn = false };
            notesCol.AppearanceHeader.TextOptions.WordWrap = DevExpress.Utils.WordWrap.Wrap;
            notesCol.AppearanceHeader.Options.UseTextOptions = true;

            // Dynamic value + status columns per stage
            // Each pair gets a unique colour derived by rotating the band's base hue.
            // HSL is used so every pair is pastel (high lightness) but clearly distinguishable.
            for (int si = 0; si < stageGroups.Count; si++)
            {
                var (stageBand, bandColor, _) = stageBands[si];
                var metaList  = stageGroups[si].ToList();
                int pairCount = metaList.Count;

                // Base hue of the band colour
                float baseHue = bandColor.GetHue();

                for (int pi = 0; pi < pairCount; pi++)
                {
                    var meta = metaList[pi];

                    // Rotate hue by 30° steps per pair, keep saturation ~60%, lightness ~88%
                    float hue        = (baseHue + pi * 30f) % 360f;
                    float saturation = 0.60f;
                    float lightness  = 0.88f;
                    var   cellColor  = HslToColor(hue, saturation, lightness);

                    // Value column
                    var valCol = new BandedGridColumn
                    {
                        FieldName = meta.ValueKey,
                        Caption   = meta.ParameterName,
                        OwnerBand = stageBand,
                        Visible   = true,
                        MinWidth  = 70,
                        Width     = 100,
                        Tag       = meta,
                        OptionsColumn = { AllowSort = DevExpress.Utils.DefaultBoolean.False }
                    };
                    valCol.AppearanceHeader.TextOptions.WordWrap = DevExpress.Utils.WordWrap.Wrap;
                    valCol.AppearanceHeader.Options.UseTextOptions = true;

                    switch (meta.Parameter.ValueType)
                    {
                        case ParameterValueType.Numeric:
                        {
                            var repo = new RepositoryItemSpinEdit();
                            repo.IsFloatValue = true;
                            repo.Increment    = 0.01m;
                            repo.MinValue     = decimal.MinValue;
                            repo.MaxValue     = decimal.MaxValue;
                            valCol.ColumnEdit = repo;

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
                            break;
                        }
                        case ParameterValueType.Predefined:
                        {
                            var repo = new RepositoryItemComboBox
                            {
                                TextEditStyle = DevExpress.XtraEditors.Controls.TextEditStyles.Standard
                            };
                            foreach (var opt in meta.Parameter.PredefinedValues
                                .OrderBy(v => v.SortOrder).ThenBy(v => v.Name))
                                repo.Items.Add(opt.Name);
                            valCol.ColumnEdit = repo;
                            break;
                        }
                        // ParameterValueType.FreeText — default TextEdit, no ColumnEdit needed
                    }

                    _gridView?.Columns.Add(valCol);

                    // Status / evaluation column – same pair colour as value column
                    var statusCol = new BandedGridColumn
                    {
                        FieldName     = meta.StatusKey,
                        Caption       = $"● {meta.ParameterName}",
                        ToolTip       = $"Evaluation – {meta.ParameterName}",
                        OwnerBand     = stageBand,
                        Visible       = true,
                        MinWidth      = 22,
                        Width         = 80,
                        OptionsColumn = { ReadOnly = true },
                        Tag           = meta
                    };
                    statusCol.AppearanceHeader.TextOptions.WordWrap = DevExpress.Utils.WordWrap.Wrap;
                    statusCol.AppearanceHeader.Options.UseTextOptions = true;

                    _columnCellColor[meta.ValueKey]  = cellColor;
                    _columnCellColor[meta.StatusKey] = cellColor;
                }
            }

            // ── Step 4: attach data – columns are already defined ─────────
            _grid?.DataSource = table;

            // ── Step 5: restore saved layout (widths, order, filters…) ────
            RestoreLayout();

            // RestoreLayout (FullLayout) overwrites OptionsBehavior/OptionsMenu — restore critical settings.
            _gridView?.OptionsBehavior.Editable                 = true;
            _gridView?.OptionsMenu.ShowConditionalFormattingItem = true;

            // ── Step 6: re-tag columns – FullLayout restore creates new column
            //           objects that lose their Tag and AppearanceCell settings.
            RetagColumns();
        }

        private void RetagColumns()
        {
            if (_gridView == null) return;

            // Build FieldName → ColumnMeta lookup
            var valueKeys  = new HashSet<string>(_columns.Select(m => m.ValueKey),  StringComparer.Ordinal);
            var statusKeys = new HashSet<string>(_columns.Select(m => m.StatusKey), StringComparer.Ordinal);
            var lookup     = new Dictionary<string, ColumnMeta>(StringComparer.Ordinal);
            foreach (var meta in _columns)
            {
                lookup[meta.ValueKey]  = meta;
                lookup[meta.StatusKey] = meta;
            }

            // Fixed session columns — only OID is read-only; Date, Operator and Notes are editable
            var readOnlyFields = new HashSet<string>(
                [MeasurementSheetService.ColOid],
                StringComparer.Ordinal);

            foreach (BandedGridColumn col in _gridView.Columns)
            {
                // Restore Tag
                if (lookup.TryGetValue(col.FieldName, out var meta))
                    col.Tag = meta;

                // FullLayout restore serializes OptionsColumn.ReadOnly — force correct values.
                if (readOnlyFields.Contains(col.FieldName) || statusKeys.Contains(col.FieldName))
                    col.OptionsColumn.ReadOnly = true;
                else if (valueKeys.Contains(col.FieldName) || col.FieldName == MeasurementSheetService.ColNotes)
                    col.OptionsColumn.ReadOnly = false;

                // FullLayout restore wipes AppearanceHeader — reapply word-wrap on every column.
                col.AppearanceHeader.TextOptions.WordWrap   = DevExpress.Utils.WordWrap.Wrap;
                col.AppearanceHeader.Options.UseTextOptions = true;

                // Reapply numeric DisplayFormat with unit — FullLayout may clear it.
                if (col.Tag is ColumnMeta metaR && col.FieldName == metaR.ValueKey && metaR.IsNumeric)
                {
                    var unit = metaR.Parameter?.Unit?.Symbol;
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

        // ── Custom drawing ──────────────────────────────────────────────────

        private void OnCustomDrawBandHeader(
            object sender,
            DevExpress.XtraGrid.Views.BandedGrid.BandHeaderCustomDrawEventArgs e)
        {
            // Band already has AppearanceHeader set; just let DevExpress draw it.
            // Override here only if you need extra painting.
        }

        private void OnCustomColumnDisplayText(
            object sender,
            DevExpress.XtraGrid.Views.Base.CustomColumnDisplayTextEventArgs e)
        {
            // Parameter value columns — unit is shown via DisplayFormat on the column
        }

        private void OnCustomDrawCell(
            object sender,
            DevExpress.XtraGrid.Views.Base.RowCellCustomDrawEventArgs e)
        {
            if (e.Column.Tag is not ColumnMeta meta) return;
            if (_grid.DataSource is not DataTable table) return;
            if (e.RowHandle < 0) return;

            var row = _gridView.GetDataRow(e.RowHandle);
            if (row == null || !table.Columns.Contains(meta.StatusKey)) return;

            // DBNull means no measurement for this parameter/session — treat as unmeasured
            var rawVal  = row[meta.StatusKey];
            var hasMeas = rawVal != DBNull.Value && rawVal != null;
            var status  = hasMeas ? (ParameterStatus)rawVal : ParameterStatus.OK;

            // Skip our colour only if the user has defined explicit CF rules for this column
            bool hasUserCf = _gridView.FormatRules
                .Cast<DevExpress.XtraGrid.GridFormatRule>()
                .Any(r => r.Enabled && (r.Column == e.Column || r.ApplyToRow));

            if (!hasUserCf)
            {
                // Background – alarm/warning override, otherwise the pair-specific cell tint
                e.Appearance.BackColor = status switch
                {
                    ParameterStatus.Alarm   => _alarmColor,
                    ParameterStatus.Warning => _warningColor,
                    _ when _columnCellColor.TryGetValue(e.Column.FieldName, out var pairColor)
                        => pairColor,
                    _   => e.Appearance.BackColor
                };
                e.Appearance.Options.UseBackColor = true;
            }

            // For the status column: show a short text label instead of the raw enum int
            if (e.Column.FieldName == meta.StatusKey)
            {
                e.DisplayText = !hasMeas ? string.Empty : status switch
                {
                    ParameterStatus.OK      => "✔ OK",
                    ParameterStatus.Warning => "⚠ Warn",
                    ParameterStatus.Alarm   => "✖ Alarm",
                    _                       => status.ToString()
                };
            }
        }

        // ── Row edit / save ─────────────────────────────────────────────────

        private void OnRowUpdated(
            object sender,
            DevExpress.XtraGrid.Views.Base.RowObjectEventArgs e)
        {
            if (_os == null || CurrentObject is not ProductionLine line) return;
            if (e.Row is not DataRowView editedRow) return;

            var sessionOid = (Guid)editedRow[MeasurementSheetService.ColOid];
            var session    = _os.GetObjectByKey<MeasurementSession>(sessionOid);
            if (session == null) return;

            // Persist the Date, Notes and Operator fields
            var newNotes    = editedRow[MeasurementSheetService.ColNotes]?.ToString()    ?? string.Empty;
            var newOperator = editedRow[MeasurementSheetService.ColOperator]?.ToString() ?? string.Empty;
            if (editedRow[MeasurementSheetService.ColDate] is DateTime newDate && session.MeasuredOn != newDate)
                session.MeasuredOn = newDate;
            if (session.Notes        != newNotes)    session.Notes        = newNotes;
            if (session.OperatorName != newOperator) session.OperatorName = newOperator;

            foreach (var meta in _columns)
            {
                if (!editedRow.Row.Table.Columns.Contains(meta.ValueKey)) continue;
                var raw = editedRow[meta.ValueKey]?.ToString() ?? string.Empty;

                var stage = line.Stages.FirstOrDefault(
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

            _os.CommitChanges();   // triggers OnObjectSpaceCommitted → SaveLayout + LoadData
        }

        // ── Helpers ─────────────────────────────────────────────────────────

        /// Blend <paramref name="color"/> toward white by <paramref name="whiteness"/> (0–1).
        private static Color MixWithWhite(Color color, float whiteness)
        {
            whiteness = Math.Clamp(whiteness, 0f, 1f);
            return Color.FromArgb(
                (int)(color.R + (255 - color.R) * (1 - whiteness)),
                (int)(color.G + (255 - color.G) * (1 - whiteness)),
                (int)(color.B + (255 - color.B) * (1 - whiteness)));
        }

        /// Convert HSL (h ∈ [0,360), s ∈ [0,1], l ∈ [0,1]) to a <see cref="Color"/>.
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
