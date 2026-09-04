using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace GeometryTransferTool.Models
{
    /// <summary>
    /// Represents a dynamic field item from a layer for attribute selection (§18).
    /// </summary>
    public class DynamicFieldItem : INotifyPropertyChanged
    {
        private bool _isSelected;

        public string FieldName { get; set; } = string.Empty;
        public string FieldType { get; set; } = string.Empty;
        public string Alias { get; set; } = string.Empty;

        public bool IsSelected
        {
            get => _isSelected;
            set
            {
                if (_isSelected != value)
                {
                    _isSelected = value;
                    OnPropertyChanged();
                }
            }
        }

        public string DisplayText => string.IsNullOrWhiteSpace(Alias) || Alias == FieldName
            ? $"{FieldName} ({FieldType})"
            : $"{FieldName} [{Alias}] ({FieldType})";

        public event PropertyChangedEventHandler? PropertyChanged;

        protected virtual void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}
