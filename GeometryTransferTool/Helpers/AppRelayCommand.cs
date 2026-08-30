using System;
using System.Threading.Tasks;
using System.Windows.Input;

namespace GeometryTransferTool.Helpers
{
    /// <summary>
    /// Lightweight ICommand implementation supporting synchronous, asynchronous, parameterized, and parameterless delegates.
    /// </summary>
    public class AppRelayCommand : ICommand
    {
        private readonly Action<object?>? _execute;
        private readonly Func<object?, Task>? _asyncExecute;
        private readonly Predicate<object?>? _canExecute;
        private bool _isExecuting;

        public AppRelayCommand(Action execute, Func<bool>? canExecute = null)
            : this(_ => execute(), canExecute == null ? null : _ => canExecute())
        {
        }

        public AppRelayCommand(Action<object?> execute, Predicate<object?>? canExecute = null)
        {
            _execute = execute ?? throw new ArgumentNullException(nameof(execute));
            _canExecute = canExecute;
        }

        public AppRelayCommand(Func<Task> asyncExecute, Func<bool>? canExecute = null)
        {
            _asyncExecute = _ => asyncExecute();
            _canExecute = canExecute == null ? null : _ => canExecute();
        }

        public AppRelayCommand(Func<object?, Task> asyncExecute, Predicate<object?>? canExecute = null)
        {
            _asyncExecute = asyncExecute ?? throw new ArgumentNullException(nameof(asyncExecute));
            _canExecute = canExecute;
        }

        public bool CanExecute(object? parameter)
        {
            if (_isExecuting) return false;
            return _canExecute == null || _canExecute(parameter);
        }

        public async void Execute(object? parameter)
        {
            if (!CanExecute(parameter)) return;

            try
            {
                _isExecuting = true;
                RaiseCanExecuteChanged();

                if (_asyncExecute != null)
                {
                    await _asyncExecute(parameter);
                }
                else if (_execute != null)
                {
                    _execute(parameter);
                }
            }
            catch (Exception ex)
            {
                Logger.Error("Unhandled exception in command execution", ex);
                MessageHelper.ShowError($"An unexpected error occurred: {ex.Message}");
            }
            finally
            {
                _isExecuting = false;
                RaiseCanExecuteChanged();
            }
        }

        public event EventHandler? CanExecuteChanged
        {
            add => CommandManager.RequerySuggested += value;
            remove => CommandManager.RequerySuggested -= value;
        }

        public void RaiseCanExecuteChanged()
        {
            CommandManager.InvalidateRequerySuggested();
        }
    }
}
