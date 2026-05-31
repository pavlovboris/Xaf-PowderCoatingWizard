using DevExpress.ExpressApp;
using DevExpress.ExpressApp.Security;
using DevExpress.XtraEditors;
using DevExpress.XtraGrid;
using DevExpress.XtraGrid.Views.Grid;
using PowderCoatingWizard.Module.BusinessObjects;
using PowderCoatingWizard.Module.BusinessObjects.AI;

namespace PowderCoatingWizard.Win.Forms
{
    /// <summary>
    /// Modal dialog that lets the user start a new session or restore an existing one.
    /// Shows sessions owned by the current user and all public sessions.
    /// </summary>
    internal class AIChatSessionPickerForm : XtraForm
    {
        private GridControl _grid = null!;
        private GridView _view = null!;
        private SimpleButton _newButton = null!;
        private SimpleButton _restoreButton = null!;
        private SimpleButton _cancelButton = null!;

        private readonly IObjectSpace _os;
        private readonly List<AIChatSession> _sessions;

        /// <summary>Null = start a brand-new session; non-null = restore the selected session.</summary>
        public AIChatSession? SelectedSession { get; private set; }

        /// <summary>True when the user clicked "New Session".</summary>
        public bool IsNew { get; private set; }

        public AIChatSessionPickerForm(IObjectSpace os)
        {
            _os = os;

            // Load sessions: owned by current user OR public
            var currentUserName = SecuritySystem.CurrentUserName;
            _sessions = _os.GetObjects<AIChatSession>()
                .Where(s => s.IsPublic || (s.Owner != null && s.Owner.UserName == currentUserName))
                .OrderByDescending(s => s.UpdatedAt)
                .ToList();

            BuildUI();
        }

        private void BuildUI()
        {
            Text = "Chat Sessions";
            Size = new System.Drawing.Size(700, 480);
            MinimumSize = new System.Drawing.Size(500, 350);
            StartPosition = System.Windows.Forms.FormStartPosition.CenterParent;
            FormBorderStyle = System.Windows.Forms.FormBorderStyle.Sizable;
            MaximizeBox = true;
            MinimizeBox = false;

            var headerLabel = new LabelControl
            {
                Text = "Select a session to restore or start a new one:",
                Dock = System.Windows.Forms.DockStyle.Top,
                Padding = new System.Windows.Forms.Padding(4)
            };

            _grid = new GridControl { Dock = System.Windows.Forms.DockStyle.Fill };
            _view = new GridView(_grid)
            {
                OptionsBehavior = { Editable = false },
                OptionsView =
                {
                    ShowGroupPanel = false,
                    ColumnAutoWidth = true
                }
            };
            _grid.MainView = _view;

            // Build columns manually so we control exactly what appears and how DateTime is formatted.
            // Do NOT call PopulateColumns() - it assigns RepositoryItemDateEdit (date-only) automatically.
            var colTitle = new DevExpress.XtraGrid.Columns.GridColumn
            {
                FieldName = "Title",
                Caption = "Session Title",
                VisibleIndex = 0,
                OptionsColumn = { ReadOnly = true }
            };

            var dateEdit = new DevExpress.XtraEditors.Repository.RepositoryItemTextEdit();
            _grid.RepositoryItems.Add(dateEdit);

            var colUpdated = new DevExpress.XtraGrid.Columns.GridColumn
            {
                FieldName = "UpdatedAt",
                Caption = "Last Updated",
                VisibleIndex = 1,
                MinWidth = 180,
                MaxWidth = 180,
                OptionsColumn = { ReadOnly = true }
            };
            colUpdated.DisplayFormat.FormatType = DevExpress.Utils.FormatType.DateTime;
            colUpdated.DisplayFormat.FormatString = "dd MMM yyyy  HH:mm";

            var colPublic = new DevExpress.XtraGrid.Columns.GridColumn
            {
                FieldName = "IsPublic",
                Caption = "Public",
                VisibleIndex = 2,
                MinWidth = 60,
                MaxWidth = 60,
                OptionsColumn = { ReadOnly = true }
            };

            _view.Columns.AddRange(new[] { colTitle, colUpdated, colPublic });
            _grid.DataSource = _sessions;

            _view.DoubleClick += (_, _) => RestoreSelected();

            // Button panel at the bottom
            var buttonPanel = new System.Windows.Forms.Panel
            {
                Dock = System.Windows.Forms.DockStyle.Bottom,
                Height = 40
            };

            _newButton = new SimpleButton
            {
                Text = "New Session",
                Width = 110,
                Left = 8,
                Top = 6,
                Anchor = System.Windows.Forms.AnchorStyles.Left
            };
            _newButton.Click += (_, _) =>
            {
                IsNew = true;
                SelectedSession = null;
                DialogResult = System.Windows.Forms.DialogResult.OK;
                Close();
            };

            _restoreButton = new SimpleButton
            {
                Text = "Restore",
                Width = 90,
                Left = 126,
                Top = 6,
                Anchor = System.Windows.Forms.AnchorStyles.Left
            };
            _restoreButton.Click += (_, _) => RestoreSelected();

            _cancelButton = new SimpleButton
            {
                Text = "Cancel",
                Width = 80,
                Left = 224,
                Top = 6,
                Anchor = System.Windows.Forms.AnchorStyles.Left
            };
            _cancelButton.Click += (_, _) =>
            {
                DialogResult = System.Windows.Forms.DialogResult.Cancel;
                Close();
            };

            buttonPanel.Controls.AddRange(new System.Windows.Forms.Control[] { _newButton, _restoreButton, _cancelButton });

            // Docking order matters: Bottom and Top anchors must be added before Fill.
            Controls.Add(buttonPanel);
            Controls.Add(headerLabel);
            Controls.Add(_grid);
        }

        private void RestoreSelected()
        {
            var idx = _view.GetFocusedDataSourceRowIndex();
            if (idx >= 0 && idx < _sessions.Count)
            {
                SelectedSession = _sessions[idx];
                IsNew = false;
                DialogResult = System.Windows.Forms.DialogResult.OK;
                Close();
            }
        }
    }
}
