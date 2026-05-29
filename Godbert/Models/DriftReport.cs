using System.Collections.Generic;
using System.ComponentModel;

namespace Godbert.Models {
    public class DriftReport : INotifyPropertyChanged {
        public string SheetName { get; set; }
        public int RawColumns { get; set; }
        public int DefinedMax { get; set; }
        public bool HasDefinition { get; set; }

        /// <summary>
        /// Column indices whose current definition throws InvalidCastException when
        /// applied to real data (structural type mismatch — needs manual fixing).
        /// Null until the (lazy) cast test has run for this sheet.
        /// </summary>
        public List<int> CastFailingColumns { get; set; }

        /// <summary>True once the cast test has been run for this sheet.</summary>
        public bool CastTested { get; set; }

        public int Delta => RawColumns - DefinedMax;
        public bool HasDrift => HasDefinition && Delta != 0;
        public bool MissingDefinition => !HasDefinition;
        public bool HasCastFailure => CastFailingColumns != null && CastFailingColumns.Count > 0;
        /// <summary>Column-count drift badge only when there isn't a (higher-priority) cast failure.</summary>
        public bool ShowDriftBadge => HasDrift && !HasCastFailure;

        /// <summary>Drift / cast badge text. Cast failures take visual priority.</summary>
        public string Badge {
            get {
                if (HasCastFailure) return "✗" + CastFailingColumns.Count;
                if (!HasDefinition) return "?";
                if (Delta == 0) return string.Empty;
                return Delta > 0 ? "+" + Delta.ToString() : Delta.ToString();
            }
        }

        public event PropertyChangedEventHandler PropertyChanged;

        /// <summary>Refresh all computed bindings (badge, drift/cast flags) for this row.</summary>
        public void NotifyChanged() {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(null));
        }
    }
}
