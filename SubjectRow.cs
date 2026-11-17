using System;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace SubjectManager
{
    /// <summary>
    /// Riga della griglia: un subject con valori originali (da CRM) e valori correnti (modificati dall'utente).
    /// </summary>
    public class SubjectRow : INotifyPropertyChanged
    {
        private string _title;
        private Guid _parentId;
        private int? _featureMask;
        private string _path;

        public SubjectRow(Guid id, string title, Guid parentId, int? featureMask)
        {
            Id = id;
            OriginalTitle = _title = title ?? string.Empty;
            OriginalParentId = _parentId = parentId;
            OriginalFeatureMask = _featureMask = featureMask;
        }

        /// <summary>
        /// Crea un subject nuovo, non ancora salvato su CRM. L'Id viene assegnato subito
        /// (e usato nella Create) così il nuovo subject può fare da padre ad altri subject.
        /// </summary>
        public static SubjectRow CreateNew(string title, Guid parentId, bool visible)
        {
            var row = new SubjectRow(Guid.NewGuid(), title, parentId, 1) { IsNew = true };
            if (!visible) row._featureMask = 0;
            return row;
        }

        public event PropertyChangedEventHandler PropertyChanged;

        public Guid Id { get; }

        /// <summary>True finché il subject non è stato creato su CRM.</summary>
        public bool IsNew { get; private set; }

        public string OriginalTitle { get; private set; }
        public Guid OriginalParentId { get; private set; }
        public int? OriginalFeatureMask { get; private set; }

        public string Title
        {
            get => _title;
            set => Set(ref _title, value ?? string.Empty);
        }

        /// <summary>Guid.Empty = nessun padre (subject radice).</summary>
        public Guid ParentId
        {
            get => _parentId;
            set
            {
                if (_parentId == value) return;
                PreviousParentId = _parentId;
                Set(ref _parentId, value);
            }
        }

        /// <summary>Parent before the last change: used to revert a rejected parent change.</summary>
        public Guid PreviousParentId { get; private set; }

        public int? FeatureMask
        {
            get => _featureMask;
            set => Set(ref _featureMask, value);
        }

        /// <summary>
        /// Visibilità come toggle: visibile se Feature Mask diverso da 0.
        /// Riattivando un subject si ripristina il valore originale (se era diverso da 0), altrimenti 1.
        /// </summary>
        public bool Visible
        {
            get => _featureMask.GetValueOrDefault() != 0;
            set
            {
                if (value == Visible) return;
                FeatureMask = value
                    ? (OriginalFeatureMask.GetValueOrDefault() != 0 ? OriginalFeatureMask : 1)
                    : 0;
                OnPropertyChanged(nameof(Visible));
            }
        }

        /// <summary>Percorso completo calcolato (Radice &gt; ... &gt; Subject), sui valori correnti.</summary>
        public string Path
        {
            get => _path;
            set => Set(ref _path, value);
        }

        public bool IsTitleChanged => !string.Equals(_title, OriginalTitle, StringComparison.Ordinal);
        public bool IsParentChanged => _parentId != OriginalParentId;
        public bool IsFeatureMaskChanged => _featureMask != OriginalFeatureMask;
        public bool IsDirty => IsNew || IsDeleted || IsTitleChanged || IsParentChanged || IsFeatureMaskChanged;

        /// <summary>Contrassegnato per l'eliminazione: verrà eliminato su CRM al salvataggio.</summary>
        public bool IsDeleted
        {
            get => _isDeleted;
            set
            {
                if (_isDeleted == value) return;
                _isDeleted = value;
                OnPropertyChanged(nameof(IsDeleted));
                OnPropertyChanged(nameof(Status));
            }
        }
        private bool _isDeleted;

        public string Status => IsDeleted ? "To delete" : IsNew ? "New" : IsDirty ? "Changed" : string.Empty;

        public void AcceptChanges()
        {
            IsNew = false;
            OriginalTitle = _title;
            OriginalParentId = _parentId;
            OriginalFeatureMask = _featureMask;
            OnPropertyChanged(nameof(Status));
        }

        public void RejectChanges()
        {
            IsDeleted = false;
            Title = OriginalTitle;
            ParentId = OriginalParentId;
            FeatureMask = OriginalFeatureMask;
        }

        private void Set<T>(ref T field, T value, [CallerMemberName] string name = null)
        {
            if (Equals(field, value)) return;
            field = value;
            OnPropertyChanged(name);
            if (name != nameof(Path)) OnPropertyChanged(nameof(Status));
        }

        private void OnPropertyChanged(string name) =>
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }

    /// <summary>Voce della combo "Subject padre".</summary>
    public class ParentOption
    {
        public Guid Id { get; set; }
        public string Name { get; set; }
        public override string ToString() => Name;
    }
}
