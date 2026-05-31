using DevExpress.XtraEditors;
using DevExpress.XtraGrid;
using DevExpress.XtraGrid.Views.Grid;
using PowderCoatingWizard.Module.BusinessObjects;
using System.Data;
using System.Drawing;
using System.Windows.Forms;

namespace PowderCoatingWizard.Win.Dialogs
{
    /// <summary>
    /// Dialog that lets the operator choose between creating a brand-new
    /// <see cref="MeasurementSession"/> or appending this stage's measurements
    /// to an existing session that does not yet have records for this stage.
    /// </summary>
    internal sealed class NewSessionPickerDialog : XtraForm
    {
        private readonly List<MeasurementSession> _candidates;
        private GridView                          _gridView;
        private SimpleButton                      _btnNew;
        private SimpleButton                      _btnAppend;
        private SimpleButton                      _btnCancel;

        /// <summary>
        /// After <see cref="DialogResult.OK"/>, this is non-null when the user chose to
        /// append to an existing session; null means "create a new session".
        /// </summary>
        public MeasurementSession? SelectedSession { get; private set; }

        public NewSessionPickerDialog(List<MeasurementSession> candidateSessions)
        {
            _candidates = candidateSessions;
            BuildUI();
        }

        private void BuildUI()
        {
            Text            = "New Session";
            Size            = new Size(580, 400);
            MinimumSize     = new Size(440, 320);
            StartPosition   = FormStartPosition.CenterParent;
            FormBorderStyle = FormBorderStyle.Sizable;

            var info = new LabelControl
            {
                Dock    = DockStyle.Top,
                Height  = 46,
                Padding = new Padding(8, 10, 8, 4),
                Text    = "There are sessions that do not yet include measurements for this stage.\r\n" +
                          "Select one to append this stage's measurements, or create a new session.",
                AutoSizeMode = LabelAutoSizeMode.None,
            };
            info.Appearance.TextOptions.WordWrap = DevExpress.Utils.WordWrap.Wrap;

            // ── Grid of candidate sessions ────────────────────────────────
            var table = BuildTable();

            var grid     = new GridControl { Dock = DockStyle.Fill };
            _gridView = new GridView(grid)
            {
                OptionsBehavior      = { Editable = false },
                OptionsView          = { ShowGroupPanel = false },
                OptionsSelection     = { MultiSelect = false },
            };
            grid.MainView = _gridView;

            var colDate = new DevExpress.XtraGrid.Columns.GridColumn
            {
                FieldName    = "Date",
                Caption      = "Date",
                VisibleIndex = 0,
                Width        = 150,
                OptionsColumn = { ReadOnly = true },
            };
            colDate.DisplayFormat.FormatType   = DevExpress.Utils.FormatType.DateTime;
            colDate.DisplayFormat.FormatString = "dd MMM yyyy  HH:mm";

            var colOperator = new DevExpress.XtraGrid.Columns.GridColumn
            {
                FieldName     = "Operator",
                Caption       = "Operator",
                VisibleIndex  = 1,
                Width         = 130,
                OptionsColumn = { ReadOnly = true },
            };

            var colNotes = new DevExpress.XtraGrid.Columns.GridColumn
            {
                FieldName     = "Notes",
                Caption       = "Notes",
                VisibleIndex  = 2,
                OptionsColumn = { ReadOnly = true },
            };

            _gridView.Columns.AddRange([colDate, colOperator, colNotes]);
            grid.DataSource = table;

            _gridView.DoubleClick += (s, e) =>
            {
                if (_gridView.FocusedRowHandle >= 0) AppendToSelected();
            };

            // ── Buttons ──────────────────────────────────────────────────
            _btnNew = new SimpleButton
            {
                Text   = "+ Create New Session",
                Width  = 160,
                Height = 28,
                Appearance = { BackColor = Color.FromArgb(198, 239, 206), ForeColor = Color.FromArgb(0, 97, 0), Font = new Font(SystemFonts.DefaultFont, FontStyle.Bold) },
            };
            _btnNew.Appearance.Options.UseBackColor = true;
            _btnNew.Appearance.Options.UseForeColor = true;
            _btnNew.Appearance.Options.UseFont      = true;
            _btnNew.Click += (s, e) =>
            {
                SelectedSession = null;
                DialogResult    = DialogResult.OK;
                Close();
            };

            _btnAppend = new SimpleButton
            {
                Text   = "Append to Selected",
                Width  = 150,
                Height = 28,
            };
            _btnAppend.Click += (s, e) => AppendToSelected();

            _btnCancel = new SimpleButton
            {
                Text         = "Cancel",
                Width        = 80,
                Height       = 28,
                DialogResult = DialogResult.Cancel,
            };

            var btnPanel = new Panel { Dock = DockStyle.Bottom, Height = 44 };
            btnPanel.Paint += (s, e) =>
            {
                using var pen = new System.Drawing.Pen(Color.FromArgb(210, 215, 220));
                e.Graphics.DrawLine(pen, 0, 0, (s as Panel)!.Width, 0);
            };

            btnPanel.SizeChanged += (s, e) =>
            {
                int right = btnPanel.Width - 8;
                int top   = (btnPanel.Height - _btnCancel.Height) / 2;

                _btnCancel.Top  = top; _btnCancel.Left  = right - _btnCancel.Width;
                right -= _btnCancel.Width + 8;

                _btnAppend.Top  = top; _btnAppend.Left  = right - _btnAppend.Width;
                right -= _btnAppend.Width + 8;

                _btnNew.Top  = top; _btnNew.Left  = right - _btnNew.Width;
            };
            btnPanel.Controls.AddRange([_btnNew, _btnAppend, _btnCancel]);

            Controls.Add(grid);
            Controls.Add(info);
            Controls.Add(btnPanel);

            AcceptButton = _btnNew;
            CancelButton = _btnCancel;
        }

        private DataTable BuildTable()
        {
            var t = new DataTable();
            t.Columns.Add("Date",     typeof(DateTime));
            t.Columns.Add("Operator", typeof(string));
            t.Columns.Add("Notes",    typeof(string));

            foreach (var s in _candidates)
            {
                var row = t.NewRow();
                row["Date"]     = s.MeasuredOn;
                row["Operator"] = s.OperatorName ?? string.Empty;
                row["Notes"]    = s.Notes        ?? string.Empty;
                t.Rows.Add(row);
            }
            return t;
        }

        private void AppendToSelected()
        {
            if (_gridView.FocusedRowHandle < 0) return;
            int index = _gridView.GetDataSourceRowIndex(_gridView.FocusedRowHandle);
            if (index < 0 || index >= _candidates.Count) return;
            SelectedSession = _candidates[index];
            DialogResult    = DialogResult.OK;
            Close();
        }
    }
}
