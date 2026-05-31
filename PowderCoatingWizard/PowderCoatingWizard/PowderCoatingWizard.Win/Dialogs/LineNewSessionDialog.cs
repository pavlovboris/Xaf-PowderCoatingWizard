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
    /// Simple stage-picker dialog for the ProductionLine "New Session" action.
    ///
    /// The operator selects a stage; a brand-new <see cref="MeasurementSession"/> is
    /// always created — there is no append-to-existing logic at the line level.
    ///
    /// After <see cref="DialogResult.OK"/>, <see cref="SelectedStage"/> is set.
    /// </summary>
    internal sealed class LineNewSessionDialog : XtraForm
    {
        private readonly List<LineStage> _stages;
        private GridView                 _gridView;
        private SimpleButton             _btnOk;
        private SimpleButton             _btnCancel;

        public LineStage? SelectedStage { get; private set; }

        public LineNewSessionDialog(ProductionLine line)
        {
            _stages = line.Stages.OrderBy(s => s.Position).ToList();
            Text            = "New Session — " + (line.Name ?? "Production Line");
            Size            = new Size(520, 360);
            MinimumSize     = new Size(380, 280);
            StartPosition   = FormStartPosition.CenterParent;
            FormBorderStyle = FormBorderStyle.Sizable;
            BuildUI();
        }

        private void BuildUI()
        {
            var lbl = new LabelControl
            {
                Dock         = DockStyle.Top,
                Height       = 32,
                Padding      = new Padding(8, 10, 8, 0),
                Text         = "Select a stage to create a new measurement session for:",
                AutoSizeMode = LabelAutoSizeMode.None,
            };

            var grid = new GridControl { Dock = DockStyle.Fill };
            _gridView = new GridView(grid)
            {
                OptionsBehavior  = { Editable = false },
                OptionsView      = { ShowGroupPanel = false },
                OptionsSelection = { MultiSelect = false },
            };
            grid.MainView   = _gridView;
            grid.DataSource = BuildTable();
            _gridView.PopulateColumns();
            HideCol("_Oid");
            SetCaption("Position", "Pos");
            SetCaption("Name",     "Stage Name");
            SetCaption("Type",     "Type");

            // Double-click confirms immediately
            _gridView.DoubleClick += (s, e) =>
            {
                if (_gridView.FocusedRowHandle >= 0) Confirm();
            };
            _gridView.FocusedRowChanged += (s, e) =>
                _btnOk.Enabled = _gridView.FocusedRowHandle >= 0;

            // ── Buttons ──────────────────────────────────────────────────
            _btnOk = new SimpleButton
            {
                Text    = "+ Create New Session",
                Width   = 170,
                Height  = 28,
                Enabled = false,
                Appearance = { BackColor = Color.FromArgb(198, 239, 206), ForeColor = Color.FromArgb(0, 97, 0), Font = new Font(SystemFonts.DefaultFont, FontStyle.Bold) },
            };
            _btnOk.Appearance.Options.UseBackColor = true;
            _btnOk.Appearance.Options.UseForeColor = true;
            _btnOk.Appearance.Options.UseFont      = true;
            _btnOk.Click += (s, e) => Confirm();

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
                _btnCancel.Top = top; _btnCancel.Left = right - _btnCancel.Width; right -= _btnCancel.Width + 8;
                _btnOk.Top     = top; _btnOk.Left     = right - _btnOk.Width;
            };
            btnPanel.Controls.AddRange([_btnOk, _btnCancel]);

            Controls.Add(grid);
            Controls.Add(lbl);
            Controls.Add(btnPanel);

            AcceptButton = _btnOk;
            CancelButton = _btnCancel;
        }

        private void Confirm()
        {
            if (_gridView.FocusedRowHandle < 0) return;
            var oid = (Guid)_gridView.GetRowCellValue(_gridView.FocusedRowHandle, "_Oid");
            SelectedStage = _stages.FirstOrDefault(s => s.Oid == oid);
            if (SelectedStage == null) return;
            DialogResult = DialogResult.OK;
            Close();
        }

        private DataTable BuildTable()
        {
            var t = new DataTable();
            t.Columns.Add("_Oid",     typeof(Guid));
            t.Columns.Add("Position", typeof(int));
            t.Columns.Add("Name",     typeof(string));
            t.Columns.Add("Type",     typeof(string));
            foreach (var s in _stages)
            {
                var r = t.NewRow();
                r["_Oid"]     = s.Oid;
                r["Position"] = s.Position;
                r["Name"]     = s.Name ?? string.Empty;
                r["Type"]     = s.ChemistryType.ToString();
                t.Rows.Add(r);
            }
            return t;
        }

        private void HideCol(string name)
        {
            if (_gridView.Columns[name] != null) _gridView.Columns[name].Visible = false;
        }

        private void SetCaption(string name, string caption)
        {
            if (_gridView.Columns[name] != null) _gridView.Columns[name].Caption = caption;
        }
    }
}
