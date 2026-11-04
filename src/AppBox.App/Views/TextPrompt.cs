using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Media.Imaging;
using AppBox.Core;

namespace AppBox.App.Views;

public static class AppDockDialog
{
    public static void Show(Window? owner, string message, string title = "AppDock", MessageBoxImage icon = MessageBoxImage.Information)
    {
        var window = CreateShell(owner, title, 420, out var body, out var footer);
        body.Child = BuildMessageCard(message, icon);

        var okButton = CreatePrimaryButton(LocalizationService.Get("Common.OK"), isDanger: false);
        okButton.IsDefault = true;
        okButton.IsCancel = true;
        okButton.Click += (_, _) => window.DialogResult = true;
        footer.Children.Add(okButton);

        window.ShowDialog();
    }

    public static bool Confirm(Window? owner, string message, string title = "AppDock", string? confirmText = null, bool isDanger = true)
    {
        var window = CreateShell(owner, title, 440, out var body, out var footer);
        body.Child = BuildMessageCard(message, MessageBoxImage.Question, isDanger);

        var cancelButton = new Button
        {
            Content = LocalizationService.Get("Common.Cancel"),
            IsCancel = true,
            MinWidth = 88,
            Padding = new Thickness(16, 8, 16, 8),
            Margin = new Thickness(0, 0, 8, 0)
        };
        cancelButton.Click += (_, _) => window.DialogResult = false;

        var confirmButton = CreatePrimaryButton(confirmText ?? LocalizationService.Get("Common.Confirm"), isDanger);
        confirmButton.IsDefault = true;
        confirmButton.Click += (_, _) => window.DialogResult = true;

        footer.Children.Add(cancelButton);
        footer.Children.Add(confirmButton);

        return window.ShowDialog() == true;
    }

    internal static Window CreateShell(Window? owner, string title, double width, out Border contentCard, out StackPanel footerPanel)
    {
        var validOwner = owner is { IsLoaded: true, IsVisible: true } ? owner : null;
        var window = new Window
        {
            Title = title,
            Owner = validOwner,
            Width = width,
            SizeToContent = SizeToContent.Height,
            ResizeMode = ResizeMode.NoResize,
            ShowInTaskbar = false,
            WindowStyle = WindowStyle.None,
            AllowsTransparency = true,
            Background = Brushes.Transparent,
            WindowStartupLocation = validOwner is not null ? WindowStartupLocation.CenterOwner : WindowStartupLocation.CenterScreen
        };
        window.SetResourceReference(FrameworkElement.StyleProperty, "AppWindow");
        window.Background = Brushes.Transparent;

        var outerGrid = new Grid { Margin = new Thickness(14) };
        var shadowBorder = new Border
        {
            CornerRadius = new CornerRadius(18),
            Effect = new DropShadowEffect { Color = Colors.Black, BlurRadius = 22, ShadowDepth = 4, Opacity = 0.28 }
        };
        shadowBorder.SetResourceReference(Border.BackgroundProperty, "Surface");
        outerGrid.Children.Add(shadowBorder);

        var shellBorder = new Border
        {
            CornerRadius = new CornerRadius(18),
            BorderThickness = new Thickness(1),
            Padding = new Thickness(20, 16, 20, 18)
        };
        shellBorder.SetResourceReference(Border.BackgroundProperty, "Surface");
        shellBorder.SetResourceReference(Border.BorderBrushProperty, "Line");
        shellBorder.MouseLeftButtonDown += (_, e) =>
        {
            if (e.ButtonState == MouseButtonState.Pressed)
                try { window.DragMove(); } catch { }
        };

        var stack = new StackPanel();

        // Header bar with AppDock icon, title, and close button
        var header = new DockPanel { Margin = new Thickness(0, 0, 0, 14), LastChildFill = true };
        var brandImage = new Image
        {
            Source = new BitmapImage(new Uri("pack://application:,,,/AppDock;component/Resources/Branding/AppDock64.png")),
            Width = 22,
            Height = 22,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 9, 0)
        };
        RenderOptions.SetBitmapScalingMode(brandImage, BitmapScalingMode.HighQuality);
        DockPanel.SetDock(brandImage, Dock.Left);
        header.Children.Add(brandImage);

        var closeBtn = new Button
        {
            Content = "✕",
            Width = 28,
            Height = 28,
            Padding = new Thickness(0),
            Margin = new Thickness(8, 0, 0, 0),
            FontSize = 12,
            VerticalAlignment = VerticalAlignment.Center,
            ToolTip = "Close"
        };
        closeBtn.SetResourceReference(FrameworkElement.StyleProperty, "MacHeaderPillButton");
        closeBtn.Click += (_, _) => window.DialogResult = false;
        DockPanel.SetDock(closeBtn, Dock.Right);
        header.Children.Add(closeBtn);

        var titleBlock = new TextBlock
        {
            Text = title,
            FontSize = 15.5,
            FontWeight = FontWeights.SemiBold,
            VerticalAlignment = VerticalAlignment.Center,
            TextTrimming = TextTrimming.CharacterEllipsis
        };
        header.Children.Add(titleBlock);
        stack.Children.Add(header);

        // Card container for body content
        contentCard = new Border
        {
            CornerRadius = new CornerRadius(12),
            BorderThickness = new Thickness(1),
            Padding = new Thickness(16, 14, 16, 14)
        };
        contentCard.SetResourceReference(Border.BackgroundProperty, "Card");
        contentCard.SetResourceReference(Border.BorderBrushProperty, "Line");
        stack.Children.Add(contentCard);

        // Footer button row
        footerPanel = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(0, 16, 0, 0)
        };
        stack.Children.Add(footerPanel);

        shellBorder.Child = stack;
        outerGrid.Children.Add(shellBorder);
        window.Content = outerGrid;
        return window;
    }

    private static UIElement BuildMessageCard(string message, MessageBoxImage icon, bool isDanger = false)
    {
        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        var (glyph, badgeColor, fgColor) = (icon, isDanger) switch
        {
            (_, true) or (MessageBoxImage.Error, _) => ("\uE783", "#FDE7E9", "#D13438"),
            (MessageBoxImage.Warning, _) => ("\uE7BA", "#FFF4CE", "#9D5D00"),
            (MessageBoxImage.Question, _) => ("\uE897", "#E3F0FF", "#0078D4"),
            _ => ("\uE946", "#E3F0FF", "#0078D4")
        };

        var badge = new Border
        {
            Width = 38,
            Height = 38,
            CornerRadius = new CornerRadius(19),
            Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString(badgeColor)),
            VerticalAlignment = VerticalAlignment.Top,
            Margin = new Thickness(0, 0, 14, 0),
            Child = new TextBlock
            {
                Text = glyph,
                FontFamily = new FontFamily("Segoe Fluent Icons, Segoe MDL2 Assets"),
                FontSize = 18,
                Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString(fgColor)),
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            }
        };
        grid.Children.Add(badge);

        var textBlock = new TextBlock
        {
            Text = message,
            FontSize = 13.5,
            LineHeight = 20,
            TextWrapping = TextWrapping.Wrap,
            VerticalAlignment = VerticalAlignment.Center
        };
        textBlock.SetResourceReference(TextBlock.ForegroundProperty, "Ink");
        Grid.SetColumn(textBlock, 1);
        grid.Children.Add(textBlock);

        return grid;
    }

    internal static Button CreatePrimaryButton(string text, bool isDanger)
    {
        var normalBrush = isDanger
            ? new SolidColorBrush((Color)ColorConverter.ConvertFromString("#D13438"))
            : new SolidColorBrush((Color)ColorConverter.ConvertFromString("#0078D4"));
        var hoverBrush = isDanger
            ? new SolidColorBrush((Color)ColorConverter.ConvertFromString("#B8272B"))
            : new SolidColorBrush((Color)ColorConverter.ConvertFromString("#006CBE"));

        var factory = new FrameworkElementFactory(typeof(Border), "Frame");
        factory.SetValue(Border.BackgroundProperty, new TemplateBindingExtension(Control.BackgroundProperty));
        factory.SetValue(Border.CornerRadiusProperty, new CornerRadius(9));
        factory.SetValue(Border.PaddingProperty, new TemplateBindingExtension(Control.PaddingProperty));

        var presenter = new FrameworkElementFactory(typeof(ContentPresenter));
        presenter.SetValue(FrameworkElement.HorizontalAlignmentProperty, HorizontalAlignment.Center);
        presenter.SetValue(FrameworkElement.VerticalAlignmentProperty, VerticalAlignment.Center);
        factory.AppendChild(presenter);

        var template = new ControlTemplate(typeof(Button)) { VisualTree = factory };
        var hoverTrigger = new Trigger { Property = UIElement.IsMouseOverProperty, Value = true };
        hoverTrigger.Setters.Add(new Setter(Border.BackgroundProperty, hoverBrush, "Frame"));
        template.Triggers.Add(hoverTrigger);

        return new Button
        {
            Content = text,
            MinWidth = 92,
            Padding = new Thickness(18, 8, 18, 8),
            Margin = new Thickness(0),
            FontWeight = FontWeights.SemiBold,
            Foreground = Brushes.White,
            Background = normalBrush,
            BorderThickness = new Thickness(0),
            Cursor = Cursors.Hand,
            Template = template
        };
    }
}

public static class TextPrompt
{
    public static string? Ask(Window owner, string title, string initial)
    {
        var window = AppDockDialog.CreateShell(owner, title, 420, out var body, out var footer);

        var inputStack = new StackPanel();
        var label = new TextBlock
        {
            Text = LocalizationService.Get("Dialog.EnterNameLabel"),
            FontSize = 12,
            Margin = new Thickness(2, 0, 0, 6)
        };
        label.SetResourceReference(TextBlock.ForegroundProperty, "Muted");
        var input = new TextBox
        {
            Text = initial,
            MaxLength = 60,
            Margin = new Thickness(0),
            FontSize = 13.5
        };
        inputStack.Children.Add(label);
        inputStack.Children.Add(input);
        body.Child = inputStack;

        var cancelBtn = new Button
        {
            Content = LocalizationService.Get("Common.Cancel"),
            IsCancel = true,
            MinWidth = 84,
            Padding = new Thickness(14, 8, 14, 8),
            Margin = new Thickness(0, 0, 8, 0)
        };
        cancelBtn.Click += (_, _) => window.DialogResult = false;

        var saveBtn = AppDockDialog.CreatePrimaryButton(LocalizationService.Get("Common.Save"), isDanger: false);
        saveBtn.IsDefault = true;
        saveBtn.Click += (_, _) =>
        {
            if (string.IsNullOrWhiteSpace(input.Text)) return;
            window.DialogResult = true;
        };

        footer.Children.Add(cancelBtn);
        footer.Children.Add(saveBtn);

        window.Loaded += (_, _) => { input.Focus(); input.SelectAll(); };
        return window.ShowDialog() == true ? input.Text : null;
    }
}
