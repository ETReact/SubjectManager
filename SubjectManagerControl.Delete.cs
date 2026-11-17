using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows.Forms;

namespace SubjectManager
{
    /// <summary>
    /// Eliminazione: i subject vengono contrassegnati "Da eliminare" e cancellati su CRM solo al salvataggio.
    /// Un subject viene sempre eliminato insieme a tutto il suo ramo (sotto-subject), che il tool elimina
    /// esplicitamente dal basso verso l'alto. I subject nuovi non ancora salvati vengono rimossi subito.
    /// </summary>
    public partial class SubjectManagerControl
    {
        private ToolStripButton _btnDelete;

        /// <summary>True se tutte le righe selezionate sono già contrassegnate: il pulsante diventa "Ripristina".</summary>
        private bool SelectionIsAllDeleted()
        {
            var selected = SelectedSubjectRows().ToList();
            return selected.Count > 0 && selected.All(r => r.IsDeleted);
        }

        private void DeleteOrRestoreSelected()
        {
            _grid.EndEdit();
            var selected = SelectedSubjectRows().ToList();
            if (selected.Count == 0) return;

            if (selected.All(r => r.IsDeleted)) RestoreRows(selected);
            else MarkForDeletion(selected.Where(r => !r.IsDeleted).ToList());
        }

        private void MarkForDeletion(List<SubjectRow> selected)
        {
            var byId = RowsById();

            // Selezionati + tutti i discendenti (calcolati sulla gerarchia corrente)
            var branch = new HashSet<Guid>(selected.Select(r => r.Id));
            foreach (var row in _allRows)
            {
                if (selected.Any(s => IsDescendantOf(row, s.Id, byId))) branch.Add(row.Id);
            }
            var rows = _allRows.Where(r => branch.Contains(r.Id) && !r.IsDeleted).ToList();
            var descendants = rows.Count - selected.Count;
            var newRows = rows.Count(r => r.IsNew);

            var msg = selected.Count == 1
                ? $"Delete the subject \"{selected[0].Title}\""
                : $"Delete the {selected.Count} selected subjects";
            if (descendants > 0)
            {
                var owner = selected.Count == 1 ? "it contains" : "they contain";
                msg += descendants == 1 ? $" and the sub-subject {owner}" : $" and the {descendants} sub-subjects {owner}";
            }
            msg += (rows.Count - newRows <= 1
                ? "?\n\nIt will be deleted from the CRM when you save; until then you can restore it."
                : "?\n\nThey will be deleted from the CRM when you save; until then you can restore them.");
            if (newRows > 0) msg += (newRows == 1 ? "\n1 new unsaved subject will be removed immediately." : $"\n{newRows} new unsaved subjects will be removed immediately.");

            if (MessageBox.Show(this, msg, "Subject Manager", MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes)
                return;

            foreach (var row in rows.Where(r => !r.IsNew)) row.IsDeleted = true;

            if (newRows > 0)
            {
                _allRows.RemoveAll(r => r.IsNew && branch.Contains(r.Id));
                RecomputePaths();
                RebuildParentOptions();
                ApplyFilter();
            }
            AfterHierarchyOrTitleChange();
        }

        private void RestoreRows(List<SubjectRow> selected)
        {
            // Ripristinando un subject si ripristinano anche gli antenati contrassegnati, altrimenti
            // l'eliminazione del padre lo cancellerebbe comunque.
            var byId = RowsById();
            var toRestore = new HashSet<SubjectRow>(selected);
            foreach (var row in selected)
            {
                var visited = new HashSet<Guid>();
                var parentId = row.ParentId;
                while (parentId != Guid.Empty && visited.Add(parentId) && byId.TryGetValue(parentId, out var p))
                {
                    if (p.IsDeleted) toRestore.Add(p);
                    parentId = p.ParentId;
                }
            }
            foreach (var row in toRestore) row.IsDeleted = false;
            AfterHierarchyOrTitleChange();
        }

        /// <summary>Le righe contrassegnate per l'eliminazione non sono modificabili.</summary>
        private bool IsRowLocked(int rowIndex) => RowAt(rowIndex)?.IsDeleted == true;

        /// <summary>Voci padre valide per i dialoghi (senza i subject da eliminare).</summary>
        private List<ParentOption> SelectableParentOptions()
        {
            var deleted = new HashSet<Guid>(_allRows.Where(r => r.IsDeleted).Select(r => r.Id));
            return _parentOptions.Where(o => !deleted.Contains(o.Id)).ToList();
        }

        private void Grid_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Delete && !_grid.IsCurrentCellInEditMode && _allRows.Count > 0)
            {
                DeleteOrRestoreSelected();
                e.Handled = true;
            }
        }
    }
}
