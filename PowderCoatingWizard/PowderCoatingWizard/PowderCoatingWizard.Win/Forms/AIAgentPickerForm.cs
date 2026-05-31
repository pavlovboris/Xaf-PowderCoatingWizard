using DevExpress.XtraEditors;
using DevExpress.XtraLayout;
using PowderCoatingWizard.Module.BusinessObjects.AI;

namespace PowderCoatingWizard.Win.Forms
{
    /// <summary>
    /// Simple modal dialog that lets the user pick an active <see cref="AIAgent"/>
    /// (or choose "Default / No agent") before opening the AI assistant chat window.
    /// </summary>
    internal class AIAgentPickerForm : XtraForm
    {
        private ListBoxControl _listBox = null!;
        private SimpleButton _okButton = null!;
        private SimpleButton _cancelButton = null!;

        private readonly IReadOnlyList<AIAgent?> _agents;

        /// <summary>The agent selected by the user; null means "default, no specific agent".</summary>
        public AIAgent? SelectedAgent { get; private set; }

        public AIAgentPickerForm(IEnumerable<AIAgent> activeAgents)
        {
            _agents = new[] { (AIAgent?)null }
                .Concat(activeAgents)
                .ToList();

            BuildUI();
        }

        private void BuildUI()
        {
            Text = "Choose AI Agent";
            Size = new System.Drawing.Size(420, 400);
            StartPosition = System.Windows.Forms.FormStartPosition.CenterParent;
            FormBorderStyle = System.Windows.Forms.FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;

            var layout = new LayoutControl { Dock = System.Windows.Forms.DockStyle.Fill };
            Controls.Add(layout);

            _listBox = new ListBoxControl
            {
                SelectionMode = System.Windows.Forms.SelectionMode.One
            };
            foreach (var agent in _agents)
                _listBox.Items.Add(agent == null ? "⬜  Default (all instructions & documents)" : $"🤖  {agent.Name}");
            _listBox.SelectedIndex = 0;
            _listBox.DoubleClick += (_, _) => Accept();

            _okButton = new SimpleButton { Text = "Open", DialogResult = System.Windows.Forms.DialogResult.OK };
            _okButton.Click += (_, _) => Accept();

            _cancelButton = new SimpleButton { Text = "Cancel", DialogResult = System.Windows.Forms.DialogResult.Cancel };
            _cancelButton.Click += (_, _) => { DialogResult = System.Windows.Forms.DialogResult.Cancel; Close(); };

            // Add a label for description
            var descLabel = new LabelControl
            {
                Text = "Select an agent profile or use the default assistant:",
                AutoSizeMode = LabelAutoSizeMode.Vertical
            };

            var lc = layout.Root;
            lc.GroupBordersVisible = false;

            var labelItem = lc.AddItem("", descLabel);
            labelItem.TextVisible = false;

            var listItem = lc.AddItem("", _listBox);
            listItem.TextVisible = false;

            var buttonGroup = lc.AddGroup();
            buttonGroup.GroupBordersVisible = false;

            var okItem = buttonGroup.AddItem("", _okButton);
            okItem.TextVisible = false;
            okItem.SizeConstraintsType = SizeConstraintsType.Custom;
            okItem.MaxSize = okItem.MinSize = new System.Drawing.Size(100, 30);

            var cancelItem = buttonGroup.AddItem("", _cancelButton);
            cancelItem.TextVisible = false;
            cancelItem.SizeConstraintsType = SizeConstraintsType.Custom;
            cancelItem.MaxSize = cancelItem.MinSize = new System.Drawing.Size(100, 30);

            layout.BestFit();
        }

        private void Accept()
        {
            int idx = _listBox.SelectedIndex;
            if (idx >= 0 && idx < _agents.Count)
                SelectedAgent = _agents[idx];
            DialogResult = System.Windows.Forms.DialogResult.OK;
            Close();
        }
    }
}
