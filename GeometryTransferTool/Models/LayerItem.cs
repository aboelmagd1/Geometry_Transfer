using System;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using ArcGIS.Desktop.Mapping;

namespace GeometryTransferTool.Models
{
    /// <summary>
    /// Wrapper for FeatureLayer display in UI ComboBoxes with selection count tracking.
    /// Implements INotifyPropertyChanged for smooth UI updates without collection recreation.
    /// </summary>
    public class LayerItem : INotifyPropertyChanged
    {
        private int _selectionCount;

        public FeatureLayer Layer { get; }
        public string Name { get; }
        public string LayerUri { get; }

        public int SelectionCount
        {
            get => _selectionCount;
            set
            {
                if (_selectionCount != value)
                {
                    _selectionCount = value;
                    OnPropertyChanged();
                    OnPropertyChanged(nameof(DisplayText));
                }
            }
        }

        public string DisplayText
        {
            get
            {
                if (string.IsNullOrEmpty(Name)) return "(None)";
                return SelectionCount > 0
                    ? $"{Name} ({SelectionCount} selected)"
                    : $"{Name} (0 selected)";
            }
        }

        public LayerItem(FeatureLayer layer, int selectionCount = 0)
        {
            Layer = layer;
            Name = layer?.Name ?? "(None)";
            LayerUri = layer?.URI ?? string.Empty;
            _selectionCount = selectionCount;
        }

        public override string ToString() => DisplayText;

        public event PropertyChangedEventHandler? PropertyChanged;
        protected void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}
