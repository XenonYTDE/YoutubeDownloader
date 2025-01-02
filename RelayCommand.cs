using System.Windows.Input;

namespace YoutubeDownloader
{
    public class RelayCommand : ICommand
    {
        private readonly Action _execute;
        
        public RelayCommand(Action execute)
        {
            _execute = execute;
        }

        public event EventHandler? CanExecuteChanged;

        public bool CanExecute(object? parameter) => true;

        public void Execute(object? parameter)
        {
            _execute();
        }
    }
} 