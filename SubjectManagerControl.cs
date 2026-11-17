using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Drawing;
using System.Linq;
using System.Text;
using System.Windows.Forms;
using McTools.Xrm.Connection;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Messages;
using Microsoft.Xrm.Sdk.Query;
using XrmToolBox.Extensibility;

namespace SubjectManager
{
    public partial class SubjectManagerControl : PluginControlBase
    {
        private const int BatchSize = 100;
        private const int MaxSavePasses = 5;

        private readonly List<SubjectRow> _allRows = new List<SubjectRow>();
        private List<ParentOption> _parentOptions = new List<ParentOption>();
        private BindingList<SubjectRow> _view = new BindingList<SubjectRow>();

        private ToolStrip _toolStrip;
        private ToolStripButton _btnLoad, _btnSave, _btnDiscard, _btnNew, _btnBulkParent, _btnBulkVisibility, _btnReplace;
        private ToolStripTextBox _txtFilter;
        private ToolStripButton _chkOnlyChanged;
        private DataGridView _grid;
        private DataGridViewComboBoxColumn _colParent;
        private DataGridViewTextBoxColumn _colTitle, _colPath, _colStatus;
        private DataGridViewCheckBoxColumn _colVisible;
        private StatusStrip _statusStrip;
        private ToolStripStatusLabel _lblStatus;

        private bool _suspendGridEvents;

        public SubjectManagerControl()
        {
            BuildUi();
            UpdateUiState();
        }

        #region UI

        private void BuildUi()
        {
            SuspendLayout();

            _toolStrip = new ToolStrip { GripStyle = ToolStripGripStyle.Hidden };

            var btnClose = new ToolStripButton("Close");
            btnClose.Click += (s, e) => CloseTool();

            _btnLoad = new ToolStripButton("Load subjects") { ToolTipText = "Load all subjects from the connected environment" };
            _btnLoad.Click += (s, e) => ExecuteMethod(LoadSubjects);

            _btnSave = new ToolStripButton("Save changes") { ToolTipText = "Save all pending changes to the CRM" };
            _btnSave.Click += (s, e) => ExecuteMethod(SaveChanges);

            _btnDiscard = new ToolStripButton("Discard changes") { ToolTipText = "Revert all pending changes" };
            _btnDiscard.Click += (s, e) => DiscardChanges();

            _btnNew = new ToolStripButton("New subject…") { ToolTipText = "Create a new subject (by default a child of the selected row)" };
            _btnNew.Click += (s, e) => NewSubject();

            _btnDelete = new ToolStripButton("Delete") { ToolTipText = "Mark the selected rows and their sub-subjects for deletion (Del key)" };
            _btnDelete.Click += (s, e) => DeleteOrRestoreSelected();

            _btnBulkParent = new ToolStripButton("Set parent…") { ToolTipText = "Set the same parent subject on all selected rows" };
            _btnBulkParent.Click += (s, e) => BulkSetParent();

            _btnBulkVisibility = new ToolStripButton("Set visibility…") { ToolTipText = "Make all selected rows visible or hidden" };
            _btnBulkVisibility.Click += (s, e) => BulkSetVisibility();

            _btnReplace = new ToolStripButton("Find and replace…") { ToolTipText = "Find and replace text in the name of the selected rows" };
            _btnReplace.Click += (s, e) => BulkReplaceTitle();

            _btnExport = new ToolStripButton("Export to Excel…") { ToolTipText = "Export the subjects to an Excel file, in tree order" };
            _btnExport.Click += (s, e) => ExportToExcel();

            _txtFilter = new ToolStripTextBox { Width = 200, ToolTipText = "Filter by name or path" };
            _txtFilter.TextChanged += (s, e) => ApplyFilter();

            _chkOnlyChanged = new ToolStripButton("Changed only") { CheckOnClick = true };
            _chkOnlyChanged.CheckedChanged += (s, e) => ApplyFilter();

            var lblFilter = new ToolStripLabel("Filter:");

            SetIcon(btnClose, ToolbarIcons.Close, Color.Firebrick);
            SetIcon(_btnLoad, ToolbarIcons.Refresh, Color.SteelBlue);
            SetIcon(_btnSave, ToolbarIcons.Save, Color.SeaGreen);
            SetIcon(_btnDiscard, ToolbarIcons.Undo, Color.DarkOrange);
            SetIcon(_btnNew, ToolbarIcons.Add, Color.SeaGreen);
            SetIcon(_btnDelete, ToolbarIcons.Delete, Color.Firebrick);
            SetIcon(_btnBulkParent, ToolbarIcons.MoveToFolder, Color.Goldenrod);
            SetIcon(_btnBulkVisibility, ToolbarIcons.View, Color.MediumPurple);
            SetIcon(_btnReplace, ToolbarIcons.Rename, Color.SteelBlue);
            SetIcon(_btnExport, ToolbarIcons.Export, Color.ForestGreen);
            SetIcon(lblFilter, ToolbarIcons.Filter, Color.DimGray);
            SetIcon(_chkOnlyChanged, ToolbarIcons.Flag, Color.DarkOrange);

            _toolStrip.Items.AddRange(new ToolStripItem[]
            {
                btnClose, new ToolStripSeparator(),
                _btnLoad, _btnSave, _btnDiscard, new ToolStripSeparator(),
                _btnNew, _btnDelete, new ToolStripSeparator(),
                _btnBulkParent, _btnBulkVisibility, _btnReplace, new ToolStripSeparator(),
                _btnExport, new ToolStripSeparator(),
                lblFilter, _txtFilter, _chkOnlyChanged
            });

            _grid = new DataGridView
            {
                Dock = DockStyle.Fill,
                AutoGenerateColumns = false,
                AllowUserToAddRows = false,
                AllowUserToDeleteRows = false,
                AllowUserToResizeRows = false,
                SelectionMode = DataGridViewSelectionMode.FullRowSelect,
                MultiSelect = true,
                RowHeadersWidth = 24,
                EditMode = DataGridViewEditMode.EditOnKeystrokeOrF2,
                BackgroundColor = SystemColors.Window
            };

            _colTitle = new DataGridViewTextBoxColumn
            {
                Name = "colTitle", HeaderText = "Name", DataPropertyName = nameof(SubjectRow.Title),
                Width = 250, MaxInputLength = 500
            };
            _colParent = new DataGridViewComboBoxColumn
            {
                Name = "colParent", HeaderText = "Parent subject", DataPropertyName = nameof(SubjectRow.ParentId),
                ValueMember = nameof(ParentOption.Id), DisplayMember = nameof(ParentOption.Name),
                ValueType = typeof(Guid), Width = 350, FlatStyle = FlatStyle.Flat,
                DisplayStyle = DataGridViewComboBoxDisplayStyle.Nothing
            };
            _colVisible = new DataGridViewCheckBoxColumn
            {
                Name = "colVisible", HeaderText = "Visible", DataPropertyName = nameof(SubjectRow.Visible),
                ValueType = typeof(bool), Width = 70, ThreeState = false
            };
            _colPath = new DataGridViewTextBoxColumn
            {
                Name = "colPath", HeaderText = "Full path", DataPropertyName = nameof(SubjectRow.Path),
                ReadOnly = true, AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill, MinimumWidth = 200
            };
            _colPath.DefaultCellStyle.ForeColor = Color.DimGray;
            _colStatus = new DataGridViewTextBoxColumn
            {
                Name = "colStatus", HeaderText = "Status", DataPropertyName = nameof(SubjectRow.Status),
                ReadOnly = true, Width = 90
            };

            _grid.Columns.AddRange(_colTitle, _colParent, _colVisible, _colPath, _colStatus);

            _grid.CellBeginEdit += Grid_CellBeginEdit;
            _grid.CellValueChanged += Grid_CellValueChanged;
            _grid.CurrentCellDirtyStateChanged += Grid_CurrentCellDirtyStateChanged;
            _grid.CellFormatting += Grid_CellFormatting;
            _grid.DataError += Grid_DataError;
            _grid.KeyDown += Grid_KeyDown;
            _grid.SelectionChanged += (s, e) => { UpdateUiState(); SyncTreeToGrid(); };

            _statusStrip = new StatusStrip();
            _lblStatus = new ToolStripStatusLabel("Connect to an environment and click \"Load subjects\".");
            _statusStrip.Items.Add(_lblStatus);

            _split = new SplitContainer { Dock = DockStyle.Fill, FixedPanel = FixedPanel.None };
            _split.Panel1.Controls.Add(_grid);
            BuildTreePanel(_split.Panel2);

            Controls.Add(_split);
            Controls.Add(_toolStrip);
            Controls.Add(_statusStrip);

            Size = new Size(1100, 600);
            ResumeLayout(true);
        }

        private static void SetIcon(ToolStripItem item, char glyph, Color color)
        {
            var image = ToolbarIcons.Create(glyph, color);
            if (image == null) return;
            item.Image = image;
            item.DisplayStyle = ToolStripItemDisplayStyle.ImageAndText;
            item.ImageScaling = ToolStripItemImageScaling.None;
        }

        private void UpdateUiState()
        {
            var loaded = _allRows.Count > 0;
            var dirtyCount = _allRows.Count(r => r.IsDirty);
            var selected = _grid?.SelectedRows.Count ?? 0;

            _btnSave.Enabled = dirtyCount > 0;
            _btnDiscard.Enabled = dirtyCount > 0;
            _btnBulkParent.Enabled = loaded && selected > 0;
            _btnBulkVisibility.Enabled = loaded && selected > 0;
            _btnReplace.Enabled = loaded && selected > 0;
            _btnNew.Enabled = loaded;
            _btnExport.Enabled = loaded;
            _btnDelete.Enabled = loaded && selected > 0;
            var restore = loaded && SelectionIsAllDeleted();
            if (_btnDelete.Text != (restore ? "Restore" : "Delete"))
            {
                _btnDelete.Text = restore ? "Restore" : "Delete";
                _btnDelete.ToolTipText = restore
                    ? "Cancel the deletion of the selected rows"
                    : "Mark the selected rows and their sub-subjects for deletion (Del key)";
                SetIcon(_btnDelete, restore ? ToolbarIcons.Undo : ToolbarIcons.Delete, restore ? Color.DarkOrange : Color.Firebrick);
            }
            _btnSave.Text = dirtyCount > 0 ? $"Save changes ({dirtyCount})" : "Save changes";

            if (loaded)
            {
                _lblStatus.Text = $"Subjects: {_allRows.Count}   Shown: {_view.Count}   Selected: {selected}   Changed: {dirtyCount}";
            }
        }

        #endregion

        #region Connessione / chiusura

        public override void UpdateConnection(IOrganizationService newService, ConnectionDetail detail, string actionName, object parameter)
        {
            base.UpdateConnection(newService, detail, actionName, parameter);

            // Cambiando ambiente i dati caricati non sono più validi
            if (_allRows.Count > 0)
            {
                ClearData();
                _lblStatus.Text = $"Connected to {detail?.ConnectionName}. Click \"Load subjects\".";
            }
        }

        public override void ClosingPlugin(PluginCloseInfo info)
        {
            if (_allRows.Any(r => r.IsDirty) &&
                MessageBox.Show(this, "There are unsaved changes. Close anyway?", "Subject Manager",
                    MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes)
            {
                info.Cancel = true;
                return;
            }
            base.ClosingPlugin(info);
        }

        private void ClearData()
        {
            _allRows.Clear();
            _parentOptions.Clear();
            _view = new BindingList<SubjectRow>();
            _grid.DataSource = null;
            _tree.Nodes.Clear();
            UpdateUiState();
        }

        #endregion

        #region Caricamento

        private void LoadSubjects()
        {
            if (_allRows.Any(r => r.IsDirty) &&
                MessageBox.Show(this, "Unsaved changes will be lost. Reload?", "Subject Manager",
                    MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes)
            {
                return;
            }

            WorkAsync(new WorkAsyncInfo
            {
                Message = "Loading subjects...",
                Work = (worker, args) =>
                {
                    var query = new QueryExpression("subject")
                    {
                        ColumnSet = new ColumnSet("title", "parentsubject", "featuremask"),
                        PageInfo = new PagingInfo { Count = 5000, PageNumber = 1 }
                    };
                    query.AddOrder("title", OrderType.Ascending);

                    var result = new List<Entity>();
                    while (true)
                    {
                        var page = Service.RetrieveMultiple(query);
                        result.AddRange(page.Entities);
                        if (!page.MoreRecords) break;
                        query.PageInfo.PageNumber++;
                        query.PageInfo.PagingCookie = page.PagingCookie;
                    }
                    args.Result = result;
                },
                PostWorkCallBack = args =>
                {
                    if (args.Error != null)
                    {
                        ShowErrorDialog(args.Error, "Error while loading subjects");
                        return;
                    }

                    PopulateFromEntities((List<Entity>)args.Result);
                }
            });
        }

        internal void PopulateFromEntities(List<Entity> entities)
        {
            _allRows.Clear();
            foreach (var e in entities)
            {
                _allRows.Add(new SubjectRow(
                    e.Id,
                    e.GetAttributeValue<string>("title"),
                    e.GetAttributeValue<EntityReference>("parentsubject")?.Id ?? Guid.Empty,
                    e.Contains("featuremask") ? e.GetAttributeValue<int?>("featuremask") : null));
            }

            RecomputePaths();
            _allRows.Sort((a, b) => string.Compare(a.Path, b.Path, StringComparison.CurrentCultureIgnoreCase));
            RebuildParentOptions();
            ApplyFilter();
            RebuildTree();
        }

        #endregion

        #region Gerarchia

        private Dictionary<Guid, SubjectRow> RowsById() => _allRows.ToDictionary(r => r.Id);

        private void RecomputePaths()
        {
            var byId = RowsById();
            foreach (var row in _allRows)
            {
                var parts = new List<string>();
                var visited = new HashSet<Guid>();
                var current = row;
                while (current != null && visited.Add(current.Id))
                {
                    parts.Insert(0, current.Title);
                    current = current.ParentId != Guid.Empty && byId.TryGetValue(current.ParentId, out var p) ? p : null;
                }
                row.Path = string.Join(" > ", parts);
            }
        }

        private void RebuildParentOptions()
        {
            // Serve sempre una NUOVA istanza di lista: WinForms tiene in cache il binding per oggetto,
            // e riassegnare la stessa lista svuotata/riempita lascia la combo con le voci vecchie
            // (al secondo "Carica subject" i padri apparivano vuoti).
            var options = new List<ParentOption> { new ParentOption { Id = Guid.Empty, Name = "-" } };
            options.AddRange(_allRows
                .OrderBy(r => r.Path, StringComparer.CurrentCultureIgnoreCase)
                .Select(r => new ParentOption { Id = r.Id, Name = r.Path }));

            _grid.DataSource = null; // scollega le righe vecchie prima di cambiare le voci della combo
            _parentOptions = options;
            _colParent.DataSource = _parentOptions;
        }

        /// <summary>Aggiorna i nomi delle voci combo (percorsi) senza ricreare la lista.</summary>
        private void RefreshParentOptionNames()
        {
            var byId = RowsById();
            foreach (var opt in _parentOptions)
            {
                if (opt.Id != Guid.Empty && byId.TryGetValue(opt.Id, out var r)) opt.Name = r.Path;
            }
        }

        /// <summary>True se impostare <paramref name="newParentId"/> come padre di <paramref name="row"/> crea un ciclo.</summary>
        private bool WouldCreateCycle(SubjectRow row, Guid newParentId, Dictionary<Guid, SubjectRow> byId)
        {
            var visited = new HashSet<Guid>();
            var currentId = newParentId;
            while (currentId != Guid.Empty && visited.Add(currentId))
            {
                if (currentId == row.Id) return true;
                currentId = byId.TryGetValue(currentId, out var p) ? p.ParentId : Guid.Empty;
            }
            return false;
        }

        private void AfterHierarchyOrTitleChange()
        {
            RecomputePaths();
            RefreshParentOptionNames();
            _grid.Invalidate();
            UpdateUiState();
            RebuildTree();
        }

        #endregion

        #region Griglia

        private void ApplyFilter()
        {
            var text = _txtFilter.Text.Trim();
            IEnumerable<SubjectRow> rows = _allRows;
            if (text.Length > 0)
            {
                rows = rows.Where(r =>
                    r.Title.IndexOf(text, StringComparison.CurrentCultureIgnoreCase) >= 0 ||
                    (r.Path ?? string.Empty).IndexOf(text, StringComparison.CurrentCultureIgnoreCase) >= 0);
            }
            if (_chkOnlyChanged.Checked) rows = rows.Where(r => r.IsDirty);

            _suspendGridEvents = true;
            try
            {
                _view = new BindingList<SubjectRow>(rows.ToList());
                _grid.DataSource = _view;
            }
            finally
            {
                _suspendGridEvents = false;
            }
            UpdateUiState();
        }

        private SubjectRow RowAt(int index) =>
            index >= 0 && index < _grid.Rows.Count ? _grid.Rows[index].DataBoundItem as SubjectRow : null;

        private IEnumerable<SubjectRow> SelectedSubjectRows() =>
            _grid.SelectedRows.Cast<DataGridViewRow>().Select(r => r.DataBoundItem as SubjectRow).Where(r => r != null);

        private void Grid_CellBeginEdit(object sender, DataGridViewCellCancelEventArgs e)
        {
            var row = RowAt(e.RowIndex);
            if (row != null && row.IsDeleted)
            {
                e.Cancel = true; // le righe da eliminare non si modificano: prima vanno ripristinate
                return;
            }
        }

        private void Grid_CurrentCellDirtyStateChanged(object sender, EventArgs e)
        {
            // Combo e checkbox applicano il valore subito, senza dover uscire dalla cella
            if (_grid.IsCurrentCellDirty &&
                (_grid.CurrentCell is DataGridViewComboBoxCell || _grid.CurrentCell is DataGridViewCheckBoxCell))
                _grid.CommitEdit(DataGridViewDataErrorContexts.Commit);
        }

        private void Grid_CellValueChanged(object sender, DataGridViewCellEventArgs e)
        {
            if (_suspendGridEvents) return;
            var row = RowAt(e.RowIndex);
            if (row == null) return;

            if (e.ColumnIndex == _colParent.Index)
            {
                if (WouldCreateCycle(row, row.ParentId, RowsById()))
                {
                    var invalid = row.ParentId;
                    BeginInvoke(new Action(() =>
                    {
                        row.ParentId = row.PreviousParentId;
                        AfterHierarchyOrTitleChange();
                        MessageBox.Show(this,
                            $"Cannot set \"{_parentOptions.FirstOrDefault(o => o.Id == invalid)?.Name}\" as parent of \"{row.Title}\": " +
                            "it would create a circular reference (the parent is the subject itself or one of its descendants).",
                            "Subject Manager", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    }));
                    return;
                }

                var newParent = _allRows.FirstOrDefault(r => r.Id == row.ParentId);
                if (newParent != null && newParent.IsDeleted)
                {
                    BeginInvoke(new Action(() =>
                    {
                        row.ParentId = row.PreviousParentId;
                        AfterHierarchyOrTitleChange();
                        MessageBox.Show(this,
                            $"\"{newParent.Title}\" is marked for deletion and cannot be used as a parent.",
                            "Subject Manager", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    }));
                    return;
                }
            }

            if (e.ColumnIndex == _colTitle.Index && string.IsNullOrWhiteSpace(row.Title))
            {
                BeginInvoke(new Action(() =>
                {
                    row.Title = row.OriginalTitle;
                    AfterHierarchyOrTitleChange();
                    MessageBox.Show(this, "The subject name cannot be empty.", "Subject Manager",
                        MessageBoxButtons.OK, MessageBoxIcon.Warning);
                }));
                return;
            }

            AfterHierarchyOrTitleChange();
        }

        private void Grid_CellFormatting(object sender, DataGridViewCellFormattingEventArgs e)
        {
            var row = RowAt(e.RowIndex);
            if (row == null || !row.IsDirty) return;

            if (row.IsDeleted)
            {
                e.CellStyle.BackColor = Color.MistyRose;
                e.CellStyle.ForeColor = Color.Firebrick;
                e.CellStyle.SelectionForeColor = Color.MistyRose;
                e.CellStyle.Font = new Font(_grid.Font, e.ColumnIndex == _colStatus.Index ? FontStyle.Bold : FontStyle.Strikeout);
                return;
            }

            if (row.IsNew)
            {
                e.CellStyle.BackColor = Color.Honeydew;
                if (e.ColumnIndex == _colStatus.Index)
                {
                    e.CellStyle.ForeColor = Color.SeaGreen;
                    e.CellStyle.Font = new Font(_grid.Font, FontStyle.Bold);
                }
                return;
            }

            e.CellStyle.BackColor = Color.LightYellow;

            var changed =
                (e.ColumnIndex == _colTitle.Index && row.IsTitleChanged) ||
                (e.ColumnIndex == _colParent.Index && row.IsParentChanged) ||
                (e.ColumnIndex == _colVisible.Index && row.IsFeatureMaskChanged);
            if (changed)
            {
                e.CellStyle.BackColor = Color.Khaki;
                e.CellStyle.Font = new Font(_grid.Font, FontStyle.Bold);
            }
        }

        private void Grid_DataError(object sender, DataGridViewDataErrorEventArgs e)
        {
            e.ThrowException = false;
        }

        #endregion

        #region Azioni massive

        private void BulkSetParent()
        {
            var selected = SelectedSubjectRows().Where(r => !r.IsDeleted).ToList();
            if (selected.Count == 0) return;
            _grid.EndEdit();

            using (var dlg = new BulkParentDialog(SelectableParentOptions(), selected.Count))
            {
                if (dlg.ShowDialog(this) != DialogResult.OK) return;

                var byId = RowsById();
                var skipped = new List<string>();
                foreach (var row in selected)
                {
                    if (WouldCreateCycle(row, dlg.SelectedParentId, byId))
                    {
                        skipped.Add(row.Title);
                        continue;
                    }
                    row.ParentId = dlg.SelectedParentId;
                }
                AfterHierarchyOrTitleChange();

                if (skipped.Count > 0)
                {
                    MessageBox.Show(this,
                        $"{skipped.Count} subject(s) were not changed because it would create a circular reference:\n\n" +
                        string.Join("\n", skipped.Take(30)) + (skipped.Count > 30 ? "\n..." : string.Empty),
                        "Subject Manager", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                }
            }
        }

        private void BulkSetVisibility()
        {
            var selected = SelectedSubjectRows().Where(r => !r.IsDeleted).ToList();
            if (selected.Count == 0) return;
            _grid.EndEdit();

            using (var dlg = new BulkVisibilityDialog(selected.First().Visible, selected.Count))
            {
                if (dlg.ShowDialog(this) != DialogResult.OK) return;
                foreach (var row in selected) row.Visible = dlg.MakeVisible;
                AfterHierarchyOrTitleChange();
            }
        }

        private void BulkReplaceTitle()
        {
            var selected = SelectedSubjectRows().Where(r => !r.IsDeleted).ToList();
            if (selected.Count == 0) return;
            _grid.EndEdit();

            using (var dlg = new ReplaceDialog(selected.Count))
            {
                if (dlg.ShowDialog(this) != DialogResult.OK || dlg.Find.Length == 0) return;

                var comparison = dlg.MatchCase ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase;
                var changed = 0;
                foreach (var row in selected)
                {
                    var newTitle = ReplaceString(row.Title, dlg.Find, dlg.Replace, comparison);
                    if (string.IsNullOrWhiteSpace(newTitle) || newTitle == row.Title) continue;
                    row.Title = newTitle;
                    changed++;
                }
                AfterHierarchyOrTitleChange();
                _lblStatus.Text = $"Replaced text in {changed} subject(s). " + _lblStatus.Text;
            }
        }

        private static string ReplaceString(string source, string find, string replace, StringComparison comparison)
        {
            var sb = new StringBuilder();
            var index = 0;
            while (true)
            {
                var found = source.IndexOf(find, index, comparison);
                if (found < 0) break;
                sb.Append(source, index, found - index).Append(replace);
                index = found + find.Length;
            }
            sb.Append(source, index, source.Length - index);
            return sb.ToString();
        }

        private void DiscardChanges()
        {
            var dirty = _allRows.Where(r => r.IsDirty).ToList();
            if (dirty.Count == 0) return;
            if (MessageBox.Show(this, $"Discard the changes on {dirty.Count} subject(s)?", "Subject Manager",
                    MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return;

            _grid.CancelEdit();
            foreach (var row in dirty.Where(r => !r.IsNew)) row.RejectChanges();

            // I subject nuovi non ancora salvati vengono eliminati
            var removed = _allRows.RemoveAll(r => r.IsNew);
            if (removed > 0)
            {
                RecomputePaths();
                RebuildParentOptions();
                ApplyFilter();
            }
            AfterHierarchyOrTitleChange();
            if (_chkOnlyChanged.Checked) ApplyFilter();
        }

        private void NewSubject()
        {
            _grid.EndEdit();
            var current = _grid.CurrentRow?.DataBoundItem as SubjectRow;

            using (var dlg = new NewSubjectDialog(SelectableParentOptions(), current?.Id ?? Guid.Empty))
            {
                if (dlg.ShowDialog(this) != DialogResult.OK) return;
                AddNewSubject(dlg.SubjectTitle, dlg.ParentId, dlg.IsVisible);
            }
        }

        internal SubjectRow AddNewSubject(string title, Guid parentId, bool visible)
        {
            var row = SubjectRow.CreateNew(title, parentId, visible);

            // Inserisce la riga in fondo al ramo del padre (o in fondo all'elenco se radice)
            var index = _allRows.Count;
            if (row.ParentId != Guid.Empty)
            {
                var byId = RowsById();
                var parentIndex = _allRows.FindIndex(r => r.Id == row.ParentId);
                if (parentIndex >= 0)
                {
                    index = parentIndex + 1;
                    while (index < _allRows.Count && IsDescendantOf(_allRows[index], row.ParentId, byId)) index++;
                }
            }
            _allRows.Insert(index, row);

            RecomputePaths();
            RebuildParentOptions();
            ApplyFilter();
            SelectRow(row.Id);
            AfterHierarchyOrTitleChange();
            return row;
        }

        private static bool IsDescendantOf(SubjectRow row, Guid ancestorId, Dictionary<Guid, SubjectRow> byId)
        {
            var visited = new HashSet<Guid>();
            var parentId = row.ParentId;
            while (parentId != Guid.Empty && visited.Add(parentId))
            {
                if (parentId == ancestorId) return true;
                parentId = byId.TryGetValue(parentId, out var p) ? p.ParentId : Guid.Empty;
            }
            return false;
        }

        private void SelectRow(Guid id)
        {
            for (var i = 0; i < _grid.Rows.Count; i++)
            {
                if ((_grid.Rows[i].DataBoundItem as SubjectRow)?.Id != id) continue;
                _grid.ClearSelection();
                _grid.CurrentCell = _grid.Rows[i].Cells[_colTitle.Index];
                _grid.Rows[i].Selected = true;
                return;
            }
        }

        #endregion

        #region Salvataggio

        private void SaveChanges()
        {
            _grid.EndEdit();
            var dirty = _allRows.Where(r => r.IsDirty).ToList();
            if (dirty.Count == 0) return;

            var delCount = dirty.Count(r => r.IsDeleted);
            var newCount = dirty.Count(r => r.IsNew && !r.IsDeleted);
            var updCount = dirty.Count - newCount - delCount;
            var parts = new List<string>();
            if (newCount > 0) parts.Add($"• create {newCount} new subject(s)");
            if (updCount > 0) parts.Add($"• update {updCount} subject(s)");
            if (delCount > 0) parts.Add($"• PERMANENTLY DELETE {delCount} subject(s) (cannot be undone)");
            if (MessageBox.Show(this, $"The following operations will be run on {ConnectionDetail?.ConnectionName}:\n\n{string.Join("\n", parts)}\n\nContinue?",
                    "Subject Manager", MessageBoxButtons.YesNo,
                    delCount > 0 ? MessageBoxIcon.Warning : MessageBoxIcon.Question) != DialogResult.Yes) return;

            // Snapshot dei valori da inviare (la griglia resta bloccata durante il lavoro). Ordine:
            // 1) creazioni, dai livelli più alti ai più bassi (un padre nuovo va creato prima dei figli);
            // 2) aggiornamenti (possono puntare a padri appena creati o spostare subject fuori da rami da eliminare);
            // 3) eliminazioni, dai livelli più bassi ai più alti (prima i figli, poi il padre).
            var byId = RowsById();
            var pending = dirty
                .Select(r => new PendingUpdate(r,
                    r.IsDeleted ? SaveKind.Delete : r.IsNew ? SaveKind.Create : SaveKind.Update,
                    Depth(r, byId)))
                .OrderBy(p => p.Kind)
                .ThenBy(p => p.Kind == SaveKind.Delete ? -p.Depth : p.Kind == SaveKind.Create ? p.Depth : 0)
                .ToList();

            WorkAsync(new WorkAsyncInfo
            {
                Message = "Saving...",
                Work = (worker, args) =>
                {
                    var total = pending.Count;
                    var toProcess = pending;

                    // Più passaggi: uno spostamento di gerarchia può fallire se dipende da un altro
                    // spostamento non ancora applicato (es. scambio padre/figlio). Si riprova finché si progredisce.
                    for (var pass = 1; pass <= MaxSavePasses && toProcess.Count > 0; pass++)
                    {
                        var failed = new List<PendingUpdate>();
                        for (var i = 0; i < toProcess.Count; i += BatchSize)
                        {
                            var batch = toProcess.Skip(i).Take(BatchSize).ToList();
                            var request = new ExecuteMultipleRequest
                            {
                                Settings = new ExecuteMultipleSettings { ContinueOnError = true, ReturnResponses = false },
                                Requests = new OrganizationRequestCollection()
                            };
                            foreach (var p in batch) request.Requests.Add(p.Request);

                            var response = (ExecuteMultipleResponse)Service.Execute(request);
                            var faults = response.Responses.Where(r => r.Fault != null)
                                .ToDictionary(r => r.RequestIndex, r => r.Fault.Message);

                            for (var j = 0; j < batch.Count; j++)
                            {
                                if (faults.TryGetValue(j, out var msg))
                                {
                                    batch[j].Error = msg;
                                    failed.Add(batch[j]);
                                }
                                else
                                {
                                    batch[j].Error = null;
                                    batch[j].Succeeded = true;
                                }
                            }

                            var done = pending.Count(p => p.Succeeded);
                            worker.ReportProgress(done * 100 / total, $"Pass {pass}: saved {done} of {total}...");
                        }

                        if (failed.Count == toProcess.Count) break; // nessun progresso
                        toProcess = failed;
                    }
                    args.Result = pending;
                },
                ProgressChanged = e => SetWorkingMessage(e.UserState as string),
                PostWorkCallBack = args =>
                {
                    if (args.Error != null)
                    {
                        ShowErrorDialog(args.Error, "Error while saving");
                        return;
                    }

                    var results = (List<PendingUpdate>)args.Result;
                    foreach (var p in results.Where(p => p.Succeeded && p.Kind != SaveKind.Delete)) p.Row.AcceptChanges();

                    // I subject eliminati su CRM spariscono dalla griglia
                    var deletedIds = new HashSet<Guid>(results.Where(p => p.Succeeded && p.Kind == SaveKind.Delete).Select(p => p.Row.Id));
                    if (deletedIds.Count > 0)
                    {
                        _allRows.RemoveAll(r => deletedIds.Contains(r.Id));
                        RecomputePaths();
                        RebuildParentOptions();
                        ApplyFilter();
                    }

                    var errors = results.Where(p => !p.Succeeded).ToList();
                    AfterHierarchyOrTitleChange();
                    if (_chkOnlyChanged.Checked) ApplyFilter();

                    if (errors.Count == 0)
                    {
                        var details = new List<string>();
                        var created = results.Count(p => p.Kind == SaveKind.Create);
                        var updated = results.Count(p => p.Kind == SaveKind.Update);
                        if (created > 0) details.Add($"{created} created");
                        if (updated > 0) details.Add($"{updated} updated");
                        if (deletedIds.Count > 0) details.Add($"{deletedIds.Count} deleted");
                        MessageBox.Show(this, $"Completed: {string.Join(", ", details)}.", "Subject Manager",
                            MessageBoxButtons.OK, MessageBoxIcon.Information);
                    }
                    else
                    {
                        var sb = new StringBuilder();
                        sb.AppendLine($"Saved {results.Count - errors.Count} subject(s), {errors.Count} failed (still highlighted):");
                        sb.AppendLine();
                        foreach (var err in errors.Take(30)) sb.AppendLine($"• {err.Row.Title}: {err.Error}");
                        if (errors.Count > 30) sb.AppendLine("...");
                        MessageBox.Show(this, sb.ToString(), "Subject Manager", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    }
                }
            });
        }

        private static Entity BuildCreateEntity(SubjectRow row)
        {
            // Id assegnato dal client: serve perché altri subject nuovi possono già puntare a questo come padre
            var entity = new Entity("subject", row.Id) { ["title"] = row.Title, ["featuremask"] = row.FeatureMask ?? 1 };
            if (row.ParentId != Guid.Empty) entity["parentsubject"] = new EntityReference("subject", row.ParentId);
            return entity;
        }

        private static int Depth(SubjectRow row, Dictionary<Guid, SubjectRow> byId)
        {
            var depth = 0;
            var visited = new HashSet<Guid>();
            var parentId = row.ParentId;
            while (parentId != Guid.Empty && visited.Add(parentId) && byId.TryGetValue(parentId, out var p))
            {
                depth++;
                parentId = p.ParentId;
            }
            return depth;
        }

        private static Entity BuildUpdateEntity(SubjectRow row)
        {
            var entity = new Entity("subject", row.Id);
            if (row.IsTitleChanged) entity["title"] = row.Title;
            if (row.IsParentChanged)
                entity["parentsubject"] = row.ParentId == Guid.Empty ? null : new EntityReference("subject", row.ParentId);
            if (row.IsFeatureMaskChanged) entity["featuremask"] = row.FeatureMask;
            return entity;
        }

        /// <summary>Tipo di operazione; l'ordine dei valori è l'ordine di esecuzione.</summary>
        private enum SaveKind { Create = 0, Update = 1, Delete = 2 }

        private class PendingUpdate
        {
            public PendingUpdate(SubjectRow row, SaveKind kind, int depth)
            {
                Row = row;
                Kind = kind;
                Depth = depth;
                switch (kind)
                {
                    case SaveKind.Create: Request = new CreateRequest { Target = BuildCreateEntity(row) }; break;
                    case SaveKind.Update: Request = new UpdateRequest { Target = BuildUpdateEntity(row) }; break;
                    default: Request = new DeleteRequest { Target = new EntityReference("subject", row.Id) }; break;
                }
            }
            public SubjectRow Row { get; }
            public SaveKind Kind { get; }
            public int Depth { get; }
            public OrganizationRequest Request { get; }
            public bool Succeeded { get; set; }
            public string Error { get; set; }
        }

        #endregion
    }
}
