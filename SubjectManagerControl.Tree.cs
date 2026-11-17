using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;

namespace SubjectManager
{
    /// <summary>
    /// Pannello di anteprima a destra: albero dei subject come appare su CRM
    /// (gerarchia per subject padre, figli in ordine alfabetico), calcolato sui valori correnti
    /// non ancora salvati e ricostruito a ogni modifica.
    /// </summary>
    public partial class SubjectManagerControl
    {
        private TreeView _tree;
        private SplitContainer _split;
        private bool _syncingSelection;

        private void BuildTreePanel(Control host)
        {
            var header = new ToolStrip { GripStyle = ToolStripGripStyle.Hidden, Dock = DockStyle.Top };
            var lbl = new ToolStripLabel("Tree preview") { Font = new Font(header.Font, FontStyle.Bold) };
            var btnExpand = new ToolStripButton("Expand all");
            var btnCollapse = new ToolStripButton("Collapse all");
            btnExpand.Click += (s, e) => { _tree.ExpandAll(); _tree.SelectedNode?.EnsureVisible(); };
            btnCollapse.Click += (s, e) => _tree.CollapseAll();
            SetIcon(lbl, ToolbarIcons.Tree, Color.SeaGreen);
            header.Items.AddRange(new ToolStripItem[] { lbl, new ToolStripSeparator(), btnExpand, btnCollapse });

            _tree = new TreeView
            {
                Dock = DockStyle.Fill,
                HideSelection = false,
                ShowLines = true,
                ShowRootLines = true,
                FullRowSelect = false,
                BorderStyle = BorderStyle.None
            };
            _tree.AfterSelect += Tree_AfterSelect;

            var legend = new Label
            {
                Dock = DockStyle.Bottom,
                AutoSize = false,
                Height = 22,
                TextAlign = ContentAlignment.MiddleLeft,
                ForeColor = Color.DimGray,
                Text = "  Green = new   ·   Orange = changed   ·   Red strikethrough = to delete   ·   Grey = hidden"
            };

            host.Controls.Add(_tree);
            host.Controls.Add(legend);
            host.Controls.Add(header);
        }

        protected override void OnLoad(EventArgs e)
        {
            base.OnLoad(e);
            if (_split.Width > 0) _split.SplitterDistance = (int)(_split.Width * 0.62);
        }

        /// <summary>Ricostruisce l'albero mantenendo nodi espansi, selezione e scroll.</summary>
        private void RebuildTree()
        {
            if (_tree == null) return;

            var expanded = new HashSet<Guid>();
            CollectExpanded(_tree.Nodes, expanded);
            var selectedId = _tree.SelectedNode?.Tag as Guid?;
            var topId = _tree.TopNode?.Tag as Guid?;

            var byId = RowsById();
            var children = _allRows
                .GroupBy(r => r.ParentId != Guid.Empty && byId.ContainsKey(r.ParentId) ? r.ParentId : Guid.Empty)
                .ToDictionary(g => g.Key, g => g.OrderBy(r => r.Title, StringComparer.CurrentCultureIgnoreCase).ToList());

            _syncingSelection = true;
            _tree.BeginUpdate();
            try
            {
                _tree.Nodes.Clear();
                var visited = new HashSet<Guid>();
                AddChildren(_tree.Nodes, Guid.Empty, children, expanded, visited);

                if (topId.HasValue)
                {
                    var top = FindNode(topId.Value);
                    if (top != null) _tree.TopNode = top;
                }
                if (selectedId.HasValue)
                {
                    var sel = FindNode(selectedId.Value);
                    if (sel != null) _tree.SelectedNode = sel;
                }
            }
            finally
            {
                _tree.EndUpdate();
                _syncingSelection = false;
            }

            // Porta in vista il subject appena modificato (es. spostato sotto un altro padre)
            SyncTreeToGrid();
        }

        private void AddChildren(TreeNodeCollection nodes, Guid parentId, Dictionary<Guid, List<SubjectRow>> children,
            HashSet<Guid> expanded, HashSet<Guid> visited)
        {
            if (!children.TryGetValue(parentId, out var list)) return;
            foreach (var row in list)
            {
                if (!visited.Add(row.Id)) continue; // difesa contro cicli
                var node = new TreeNode(row.Title) { Tag = row.Id, Name = row.Id.ToString() };
                if (!row.Visible) node.ForeColor = Color.Gray;
                if (row.IsDeleted)
                {
                    node.ForeColor = Color.Firebrick;
                    node.BackColor = Color.MistyRose;
                    node.NodeFont = new Font(_tree.Font, FontStyle.Strikeout);
                }
                else if (row.IsNew)
                {
                    node.ForeColor = row.Visible ? Color.SeaGreen : Color.FromArgb(130, 190, 150);
                    node.BackColor = Color.Honeydew;
                }
                else if (row.IsDirty)
                {
                    node.ForeColor = row.Visible ? Color.DarkOrange : Color.FromArgb(220, 170, 110);
                    node.BackColor = Color.LightYellow;
                }
                node.ToolTipText = row.Path;
                nodes.Add(node);
                AddChildren(node.Nodes, row.Id, children, expanded, visited);
                if (expanded.Contains(row.Id)) node.Expand();
            }
        }

        private static void CollectExpanded(TreeNodeCollection nodes, HashSet<Guid> expanded)
        {
            foreach (TreeNode n in nodes)
            {
                if (n.IsExpanded && n.Tag is Guid id) expanded.Add(id);
                CollectExpanded(n.Nodes, expanded);
            }
        }

        private TreeNode FindNode(Guid id) => _tree.Nodes.Find(id.ToString(), true).FirstOrDefault();

        /// <summary>Selezione griglia → albero.</summary>
        private void SyncTreeToGrid()
        {
            if (_syncingSelection || _tree == null) return;
            var row = _grid.CurrentRow?.DataBoundItem as SubjectRow;
            if (row == null) return;

            var node = FindNode(row.Id);
            if (node == null || _tree.SelectedNode == node) return;

            _syncingSelection = true;
            try
            {
                _tree.SelectedNode = node;
                node.EnsureVisible();
            }
            finally
            {
                _syncingSelection = false;
            }
        }

        /// <summary>Selezione albero → griglia.</summary>
        private void Tree_AfterSelect(object sender, TreeViewEventArgs e)
        {
            if (_syncingSelection || !(e.Node?.Tag is Guid id)) return;

            for (var i = 0; i < _grid.Rows.Count; i++)
            {
                if ((_grid.Rows[i].DataBoundItem as SubjectRow)?.Id != id) continue;

                _syncingSelection = true;
                try
                {
                    _grid.ClearSelection();
                    _grid.CurrentCell = _grid.Rows[i].Cells[_colTitle.Index];
                    _grid.Rows[i].Selected = true;
                }
                finally
                {
                    _syncingSelection = false;
                }
                return;
            }
        }
    }
}
