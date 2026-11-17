using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;

namespace SubjectManager
{
    /// <summary>Base per i piccoli dialoghi modali con pulsanti OK/Annulla.</summary>
    internal abstract class SimpleDialog : Form
    {
        protected readonly TableLayoutPanel Body;

        protected SimpleDialog(string title)
        {
            Text = title;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            StartPosition = FormStartPosition.CenterParent;
            MinimizeBox = false;
            MaximizeBox = false;
            ShowInTaskbar = false;
            AutoSize = true;
            AutoSizeMode = AutoSizeMode.GrowAndShrink;
            Padding = new Padding(10);

            Body = new TableLayoutPanel { AutoSize = true, ColumnCount = 1, Dock = DockStyle.Fill };
            Controls.Add(Body);
        }

        protected void AddButtons()
        {
            var ok = new Button { Text = "OK", DialogResult = DialogResult.OK, Width = 90 };
            var cancel = new Button { Text = "Cancel", DialogResult = DialogResult.Cancel, Width = 90 };
            var panel = new FlowLayoutPanel
            {
                FlowDirection = FlowDirection.RightToLeft, AutoSize = true, Dock = DockStyle.Fill,
                Margin = new Padding(0, 10, 0, 0)
            };
            panel.Controls.Add(cancel);
            panel.Controls.Add(ok);
            Body.Controls.Add(panel);
            AcceptButton = ok;
            CancelButton = cancel;
        }

        protected void AddLabel(string text) =>
            Body.Controls.Add(new Label { Text = text, AutoSize = true, MaximumSize = new Size(480, 0), Margin = new Padding(0, 4, 0, 4) });
    }

    internal class BulkParentDialog : SimpleDialog
    {
        private readonly ComboBox _combo;

        public BulkParentDialog(IEnumerable<ParentOption> options, int count) : base("Set parent subject")
        {
            AddLabel($"New parent subject for the {count} selected subject(s):");
            _combo = new ComboBox
            {
                Width = 480,
                DropDownStyle = ComboBoxStyle.DropDown,
                AutoCompleteMode = AutoCompleteMode.SuggestAppend,
                AutoCompleteSource = AutoCompleteSource.ListItems,
                DisplayMember = nameof(ParentOption.Name)
            };
            _combo.Items.AddRange(options.Cast<object>().ToArray());
            if (_combo.Items.Count > 0) _combo.SelectedIndex = 0;
            Body.Controls.Add(_combo);
            AddButtons();

            FormClosing += (s, e) =>
            {
                if (DialogResult == DialogResult.OK && !(_combo.SelectedItem is ParentOption))
                {
                    MessageBox.Show(this, "Select a subject from the list.", Text, MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    e.Cancel = true;
                }
            };
        }

        public Guid SelectedParentId => (_combo.SelectedItem as ParentOption)?.Id ?? Guid.Empty;
    }

    internal class BulkVisibilityDialog : SimpleDialog
    {
        private readonly RadioButton _yes, _no;

        public BulkVisibilityDialog(bool initial, int count) : base("Set visibility")
        {
            AddLabel($"Make the {count} selected subject(s) visible?");
            _yes = new RadioButton { Text = "Yes, visible", AutoSize = true, Checked = initial };
            _no = new RadioButton { Text = "No, hidden", AutoSize = true, Checked = !initial };
            var panel = new FlowLayoutPanel { AutoSize = true, FlowDirection = FlowDirection.LeftToRight };
            panel.Controls.Add(_yes);
            panel.Controls.Add(_no);
            Body.Controls.Add(panel);
            AddButtons();
        }

        public bool MakeVisible => _yes.Checked;
    }

    internal class NewSubjectDialog : SimpleDialog
    {
        private readonly TextBox _title;
        private readonly ComboBox _parent;
        private readonly CheckBox _visible;

        public NewSubjectDialog(IEnumerable<ParentOption> options, Guid defaultParentId) : base("New subject")
        {
            AddLabel("Name:");
            _title = new TextBox { Width = 480, MaxLength = 500 };
            Body.Controls.Add(_title);

            AddLabel("Parent subject:");
            _parent = new ComboBox
            {
                Width = 480,
                DropDownStyle = ComboBoxStyle.DropDown,
                AutoCompleteMode = AutoCompleteMode.SuggestAppend,
                AutoCompleteSource = AutoCompleteSource.ListItems,
                DisplayMember = nameof(ParentOption.Name)
            };
            var items = options.ToList();
            _parent.Items.AddRange(items.Cast<object>().ToArray());
            _parent.SelectedIndex = Math.Max(0, items.FindIndex(o => o.Id == defaultParentId));
            Body.Controls.Add(_parent);

            _visible = new CheckBox { Text = "Visible", Checked = true, AutoSize = true, Margin = new Padding(0, 8, 0, 0) };
            Body.Controls.Add(_visible);
            AddButtons();

            Shown += (s, e) => _title.Focus();
            FormClosing += (s, e) =>
            {
                if (DialogResult != DialogResult.OK) return;
                if (string.IsNullOrWhiteSpace(_title.Text))
                {
                    MessageBox.Show(this, "Enter the subject name.", Text, MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    e.Cancel = true;
                }
                else if (!(_parent.SelectedItem is ParentOption))
                {
                    MessageBox.Show(this, "Select the parent subject from the list (or \"-\" for no parent).", Text, MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    e.Cancel = true;
                }
            };
        }

        public string SubjectTitle => _title.Text.Trim();
        public Guid ParentId => (_parent.SelectedItem as ParentOption)?.Id ?? Guid.Empty;
        public bool IsVisible => _visible.Checked;
    }

    internal class ReplaceDialog : SimpleDialog
    {
        private readonly TextBox _find, _replace;
        private readonly CheckBox _matchCase;

        public ReplaceDialog(int count) : base("Find and replace in name")
        {
            AddLabel($"Applies to the {count} selected subject(s).");
            AddLabel("Find:");
            _find = new TextBox { Width = 400 };
            Body.Controls.Add(_find);
            AddLabel("Replace with:");
            _replace = new TextBox { Width = 400 };
            Body.Controls.Add(_replace);
            _matchCase = new CheckBox { Text = "Match case", AutoSize = true };
            Body.Controls.Add(_matchCase);
            AddButtons();
        }

        public string Find => _find.Text;
        public string Replace => _replace.Text;
        public bool MatchCase => _matchCase.Checked;
    }
}
