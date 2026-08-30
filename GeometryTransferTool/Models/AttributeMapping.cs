using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace GeometryTransferTool.Models
{
    /// <summary>
    /// Represents an individual field mapping row between Source and Target layers.
    /// </summary>
    public class AttributeMappingItem : INotifyPropertyChanged
    {
        private string _sourceField = string.Empty;
        private string _targetField = string.Empty;
        private bool _isEnabled = true;

        public string SourceField
        {
            get => _sourceField;
            set
            {
                if (_sourceField != value)
                {
                    _sourceField = value;
                    OnPropertyChanged();
                }
            }
        }

        public string TargetField
        {
            get => _targetField;
            set
            {
                if (_targetField != value)
                {
                    _targetField = value;
                    OnPropertyChanged();
                }
            }
        }

        public bool IsEnabled
        {
            get => _isEnabled;
            set
            {
                if (_isEnabled != value)
                {
                    _isEnabled = value;
                    OnPropertyChanged();
                }
            }
        }

        public event PropertyChangedEventHandler? PropertyChanged;
        protected void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}
