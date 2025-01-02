using Microsoft.UI.Xaml.Data;

namespace YoutubeDownloader
{
    public class TypeToIconConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, string language)
        {
            return (value as string) switch
            {
                "MP4" => "\uE714",  // Video icon
                "MP3" => "\uE8D6",  // Audio icon
                _ => "\uE8B9"       // Default icon
            };
        }

        public object ConvertBack(object value, Type targetType, object parameter, string language)
        {
            throw new NotImplementedException();
        }
    }
} 