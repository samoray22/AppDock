using System.Globalization;
using System.Windows.Data;
using AppBox.Core;
using AppBox.Infrastructure;
namespace AppBox.App.Views;
public sealed class ContainerIconConverter : IValueConverter
{
    public object Convert(object value, System.Type type, object parameter, CultureInfo culture) => new ContainerIcons("").Get(value as ContainerIcon);
    public object ConvertBack(object value, System.Type type, object parameter, CultureInfo culture) => throw new System.NotSupportedException();
}
