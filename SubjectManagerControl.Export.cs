using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Windows.Forms;

namespace SubjectManager
{
    /// <summary>Export of the subjects to an Excel (.xlsx) file, in tree order (as in the CRM).</summary>
    public partial class SubjectManagerControl
    {
        private ToolStripButton _btnExport;

        private void ExportToExcel()
        {
            _grid.EndEdit();
            if (_allRows.Count == 0) return;

            // With an active filter, ask whether to export only the rows shown
            var rows = _allRows.ToList();
            if (_view.Count < _allRows.Count)
            {
                var answer = MessageBox.Show(this,
                    $"A filter is active ({_view.Count} of {_allRows.Count} subjects shown).\n\n" +
                    "Yes = export only the rows shown\nNo = export all subjects",
                    "Export to Excel", MessageBoxButtons.YesNoCancel, MessageBoxIcon.Question);
                if (answer == DialogResult.Cancel) return;
                if (answer == DialogResult.Yes) rows = _view.ToList();
            }

            if (_allRows.Any(r => r.IsDirty) &&
                MessageBox.Show(this,
                    "There are unsaved changes. The export will contain the current values shown in the grid, " +
                    "including unsaved changes (see the Status column).\n\nContinue?",
                    "Export to Excel", MessageBoxButtons.YesNo, MessageBoxIcon.Information) != DialogResult.Yes)
            {
                return;
            }

            var org = ConnectionDetail?.ConnectionName;
            using (var dlg = new SaveFileDialog
            {
                Title = "Export subjects to Excel",
                Filter = "Excel workbook (*.xlsx)|*.xlsx",
                DefaultExt = "xlsx",
                AddExtension = true,
                OverwritePrompt = true,
                FileName = SafeFileName($"Subjects{(string.IsNullOrEmpty(org) ? "" : " - " + org)} - {DateTime.Now:yyyy-MM-dd}.xlsx")
            })
            {
                if (dlg.ShowDialog(this) != DialogResult.OK) return;

                try
                {
                    var count = ExportToFile(dlg.FileName, rows);
                    if (MessageBox.Show(this, $"Exported {count} subjects to:\n{dlg.FileName}\n\nOpen the file now?",
                            "Export to Excel", MessageBoxButtons.YesNo, MessageBoxIcon.Information) == DialogResult.Yes)
                    {
                        Process.Start(new ProcessStartInfo(dlg.FileName) { UseShellExecute = true });
                    }
                }
                catch (IOException ex)
                {
                    MessageBox.Show(this, $"Cannot write the file (is it open in Excel?):\n{ex.Message}",
                        "Export to Excel", MessageBoxButtons.OK, MessageBoxIcon.Error);
                }
                catch (Exception ex)
                {
                    ShowErrorDialog(ex, "Error while exporting to Excel");
                }
            }
        }

        /// <summary>Writes the given rows (sorted in tree order) to an .xlsx file. Returns the number of rows written.</summary>
        internal int ExportToFile(string path, IEnumerable<SubjectRow> rows)
        {
            var include = new HashSet<Guid>(rows.Select(r => r.Id));
            var byId = RowsById();
            var ordered = TreeOrder().Where(t => include.Contains(t.Row.Id)).ToList();

            var headers = new[] { "Name", "Parent subject", "Full path", "Level", "Visible", "Feature mask", "Status", "Subject ID", "Parent subject ID" };
            var widths = new double[] { 35, 35, 60, 8, 10, 14, 14, 40, 40 };
            var data = ordered.Select(t =>
            {
                var r = t.Row;
                var parent = r.ParentId != Guid.Empty && byId.TryGetValue(r.ParentId, out var p) ? p : null;
                return new object[]
                {
                    r.Title,
                    parent?.Title,
                    r.Path,
                    t.Level,
                    r.Visible,
                    r.FeatureMask,
                    string.IsNullOrEmpty(r.Status) ? null : r.Status,
                    r.Id.ToString(),
                    parent != null ? r.ParentId.ToString() : null
                };
            });

            XlsxWriter.Write(path, "Subjects", headers, widths, data);
            return ordered.Count;
        }

        /// <summary>All subjects depth-first, children sorted by name (same order as the tree), with their level (1 = root).</summary>
        private List<(SubjectRow Row, int Level)> TreeOrder()
        {
            var byId = RowsById();
            var children = _allRows
                .GroupBy(r => r.ParentId != Guid.Empty && byId.ContainsKey(r.ParentId) ? r.ParentId : Guid.Empty)
                .ToDictionary(g => g.Key, g => g.OrderBy(r => r.Title, StringComparer.CurrentCultureIgnoreCase).ToList());

            var result = new List<(SubjectRow, int)>();
            var visited = new HashSet<Guid>();
            void Walk(Guid parentId, int level)
            {
                if (!children.TryGetValue(parentId, out var list)) return;
                foreach (var row in list)
                {
                    if (!visited.Add(row.Id)) continue;
                    result.Add((row, level));
                    Walk(row.Id, level + 1);
                }
            }
            Walk(Guid.Empty, 1);
            return result;
        }

        private static string SafeFileName(string name) =>
            string.Concat(name.Select(ch => Path.GetInvalidFileNameChars().Contains(ch) ? '_' : ch));
    }
}
