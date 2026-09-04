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
    public class LayerItem : INotifyPropertyChanged, IEquatable<LayerItem>
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

        public bool Equals(LayerItem? other)
        {
            if (other is null) return false;
            if (ReferenceEquals(this, other)) return true;

            // Match by underlying FeatureLayer instance first
            if (Layer != null && other.Layer != null)
            {
                if (ReferenceEquals(Layer, other.Layer)) return true;
                if (!string.IsNullOrEmpty(LayerUri) && !string.IsNullOrEmpty(other.LayerUri))
                {
                    return LayerUri.Equals(other.LayerUri, StringComparison.OrdinalIgnoreCase);
                }
            }

            return Name.Equals(other.Name, StringComparison.OrdinalIgnoreCase);
        }

        public override bool Equals(object? obj)
        {
            return Equals(obj as LayerItem);
        }

        public override int GetHashCode()
        {
            if (Layer != null)
            {
                if (!string.IsNullOrEmpty(LayerUri))
                {
                    return StringComparer.OrdinalIgnoreCase.GetHashCode(LayerUri);
                }
                return Layer.GetHashCode();
            }
            return StringComparer.OrdinalIgnoreCase.GetHashCode(Name);
        }

        public event PropertyChangedEventHandler? PropertyChanged;
        protected void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        {
            var handler = PropertyChanged;
            if (handler == null) return;

            var dispatcher = System.Windows.Application.Current?.Dispatcher;
            if (dispatcher != null && !dispatcher.CheckAccess())
            {
                dispatcher.BeginInvoke(new Action(() => handler.Invoke(this, new PropertyChangedEventArgs(propertyName))));
            }
            else
            {
                handler.Invoke(this, new PropertyChangedEventArgs(propertyName));
            }
        }
    }
}
