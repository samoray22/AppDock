using System.Windows;
using System.Windows.Controls;
namespace AppBox.App.Controls;
public class LayoutSelectionCard : RadioButton
{
    public static readonly DependencyProperty TitleProperty = DependencyProperty.Register(nameof(Title), typeof(string), typeof(LayoutSelectionCard), new PropertyMetadata(""));
    public static readonly DependencyProperty DescriptionProperty = DependencyProperty.Register(nameof(Description), typeof(string), typeof(LayoutSelectionCard), new PropertyMetadata(""));
    public static readonly DependencyProperty MultipleProperty = DependencyProperty.Register(nameof(Multiple), typeof(bool), typeof(LayoutSelectionCard), new PropertyMetadata(false));
    public string Title { get => (string)GetValue(TitleProperty); set => SetValue(TitleProperty, value); }
    public string Description { get => (string)GetValue(DescriptionProperty); set => SetValue(DescriptionProperty, value); }
    public bool Multiple { get => (bool)GetValue(MultipleProperty); set => SetValue(MultipleProperty, value); }
}
public class AppDockToggleSwitch : CheckBox { }
