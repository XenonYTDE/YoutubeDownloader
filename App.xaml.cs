using Microsoft.UI.Xaml;

namespace YoutubeDownloader
{
    public partial class App : Application
    {
        private Window? m_window;

        public App()
        {
            InitializeComponent();
        }

        protected override void OnLaunched(Microsoft.UI.Xaml.LaunchActivatedEventArgs args)
        {
            m_window = new MainWindow();
            m_window.Activate();
        }
    }
}
