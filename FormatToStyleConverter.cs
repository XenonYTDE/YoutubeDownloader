using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Data;

namespace YoutubeDownloader
{
    public class FormatToStyleConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, string language)
        {
            if (value is string format && parameter is string buttonFormat)
            {
                return format.ToLower() == buttonFormat.ToLower()
                    ? Application.Current.Resources["AccentButtonStyle"] 
                    : Application.Current.Resources["DefaultButtonStyle"];
            }
            return Application.Current.Resources["DefaultButtonStyle"];
        }

        public object ConvertBack(object value, Type targetType, object parameter, string language)
        {
            throw new NotImplementedException();
        }
    }
} 