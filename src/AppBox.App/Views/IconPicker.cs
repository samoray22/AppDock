using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using AppBox.Core;
using AppBox.Infrastructure;
using AppBox.App.Services;
using Microsoft.Win32;

namespace AppBox.App.Views;

public sealed class IconPicker : Window
{
    private readonly ContainerIcons icons;
    private readonly Image preview = new() { Width = 64, Height = 64, Stretch = Stretch.Uniform };
    private readonly TextBlock selectedTitle = new() { FontSize = 15, FontWeight = FontWeights.SemiBold };
    private readonly TextBlock selectedSubtitle = new() { FontSize = 12, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 2, 0, 8) };
    private readonly TextBox filterBox = new() { Width = 175, Height = 26, Padding = new Thickness(8, 3, 8, 3), FontSize = 12, VerticalContentAlignment = VerticalAlignment.Center };
    private readonly Dictionary<string, Border> cards = new(StringComparer.Ordinal);
    public ContainerIcon Choice { get; private set; }

    public IconPicker(ContainerIcons icons, ContainerIcon current)
    {
        this.icons = icons;
        Choice = new() { Kind = current.Kind, BuiltIn = current.BuiltIn, ResourcePath = current.ResourcePath };
        Title = "Change Container Icon — AppDock";
        Width = 580;
        Height = 600;
        MinWidth = 480;
        MinHeight = 480;
        ResizeMode = ResizeMode.NoResize;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        AllowDrop = true;
        Style = (Style)FindResource("AppWindow");
        SourceInitialized += (_, _) => WindowPlacement.Round(this, ThemeService.Dark);

        selectedSubtitle.SetResourceReference(TextBlock.ForegroundProperty, "Muted");

        var root = new Grid { Margin = new Thickness(20) };
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        Content = root;

        // Top Live Preview & Custom Upload Card
        var topCard = new Border
        {
            CornerRadius = new CornerRadius(14),
            BorderThickness = new Thickness(1),
            Padding = new Thickness(14),
            Margin = new Thickness(0, 0, 0, 14)
        };
        topCard.SetResourceReference(Border.BackgroundProperty, "Card");
        topCard.SetResourceReference(Border.BorderBrushProperty, "Line");

        var topGrid = new Grid();
        topGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        topGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        var previewFrame = new Border
        {
            Width = 82,
            Height = 82,
            CornerRadius = new CornerRadius(16),
            BorderThickness = new Thickness(1),
            Margin = new Thickness(0, 0, 16, 0),
            Child = preview
        };
        previewFrame.SetResourceReference(Border.BackgroundProperty, "Surface");
        previewFrame.SetResourceReference(Border.BorderBrushProperty, "Line");
        RenderOptions.SetBitmapScalingMode(preview, BitmapScalingMode.HighQuality);
        topGrid.Children.Add(previewFrame);

        var infoStack = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        infoStack.Children.Add(selectedTitle);
        infoStack.Children.Add(selectedSubtitle);

        var uploadRow = new WrapPanel();
        var uploadBtn = CreateButton("Upload Custom Icon…", ImportFromDialog);
        var resetBtn = CreateButton("Reset to Default", () => { Choice = new(); Refresh(); });
        uploadRow.Children.Add(uploadBtn);
        uploadRow.Children.Add(resetBtn);
        infoStack.Children.Add(uploadRow);

        Grid.SetColumn(infoStack, 1);
        topGrid.Children.Add(infoStack);
        topCard.Child = topGrid;
        root.Children.Add(topCard);

        // Section Heading + Quick Filter Box
        var headerRow = new DockPanel { Margin = new Thickness(2, 0, 2, 8), LastChildFill = false };
        var galleryHeading = new TextBlock
        {
            Text = $"Container Icons by Category ({ContainerIcons.Categories.Length})",
            FontSize = 12.5,
            FontWeight = FontWeights.SemiBold,
            VerticalAlignment = VerticalAlignment.Center
        };
        galleryHeading.SetResourceReference(TextBlock.ForegroundProperty, "Muted");
        DockPanel.SetDock(galleryHeading, Dock.Left);
        headerRow.Children.Add(galleryHeading);

        filterBox.ToolTip = "Filter icon categories by name";
        filterBox.TextChanged += (_, _) => ApplyFilter();
        var filterLabel = new TextBlock
        {
            Text = "🔍 Filter:",
            FontSize = 11.5,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 6, 0)
        };
        filterLabel.SetResourceReference(TextBlock.ForegroundProperty, "Muted");
        DockPanel.SetDock(filterBox, Dock.Right);
        DockPanel.SetDock(filterLabel, Dock.Right);
        headerRow.Children.Add(filterBox);
        headerRow.Children.Add(filterLabel);

        Grid.SetRow(headerRow, 1);
        root.Children.Add(headerRow);

        // Visual Icon Grid
        var scroller = new ScrollViewer
        {
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled
        };
        var wrap = new WrapPanel { HorizontalAlignment = HorizontalAlignment.Center };
        foreach (var category in ContainerIcons.Categories)
        {
            var cat = category;
            var img = new Image
            {
                Source = ContainerIcons.BuiltIn(cat),
                Width = 46,
                Height = 46,
                Margin = new Thickness(0, 0, 0, 5)
            };
            RenderOptions.SetBitmapScalingMode(img, BitmapScalingMode.HighQuality);

            var label = new TextBlock
            {
                Text = cat,
                FontSize = 11,
                TextAlignment = TextAlignment.Center,
                TextTrimming = TextTrimming.CharacterEllipsis,
                MaxWidth = 98,
                HorizontalAlignment = HorizontalAlignment.Center
            };

            var stack = new StackPanel { HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
            stack.Children.Add(img);
            stack.Children.Add(label);

            var card = new Border
            {
                Width = 110,
                Height = 86,
                Margin = new Thickness(4),
                Padding = new Thickness(6),
                CornerRadius = new CornerRadius(12),
                BorderThickness = new Thickness(1.5),
                Cursor = Cursors.Hand,
                ToolTip = cat,
                Child = stack
            };
            card.MouseLeftButtonDown += (_, e) =>
            {
                Choice = new() { Kind = ContainerIconKind.BuiltIn, BuiltIn = cat };
                Refresh();
                if (e.ClickCount >= 2) DialogResult = true;
            };
            cards[cat] = card;
            wrap.Children.Add(card);
        }
        scroller.Content = wrap;
        Grid.SetRow(scroller, 2);
        root.Children.Add(scroller);

        // Bottom Actions
        var footer = new DockPanel { Margin = new Thickness(0, 14, 0, 0), LastChildFill = false };
        var hint = new TextBlock
        {
            Text = "Drop or upload .png, .ico, .jpg, .bmp, .exe & .lnk",
            FontSize = 11.5,
            VerticalAlignment = VerticalAlignment.Center
        };
        hint.SetResourceReference(TextBlock.ForegroundProperty, "Muted");
        DockPanel.SetDock(hint, Dock.Left);
        footer.Children.Add(hint);

        var applyBtn = CreateButton("Apply Icon", () => DialogResult = true);
        applyBtn.IsDefault = true;
        DockPanel.SetDock(applyBtn, Dock.Right);
        footer.Children.Add(applyBtn);

        var cancelBtn = CreateButton("Cancel", () => DialogResult = false);
        cancelBtn.IsCancel = true;
        DockPanel.SetDock(cancelBtn, Dock.Right);
        footer.Children.Add(cancelBtn);

        Grid.SetRow(footer, 3);
        root.Children.Add(footer);

        PreviewDragOver += (_, e) =>
        {
            e.Handled = true;
            e.Effects = e.Data.GetDataPresent(DataFormats.FileDrop) ? DragDropEffects.Copy : DragDropEffects.None;
        };
        PreviewDrop += (_, e) =>
        {
            e.Handled = true;
            if (e.Data.GetData(DataFormats.FileDrop) is string[] { Length: > 0 } files)
                ImportFile(files[0]);
        };

        Refresh();
    }

    private void ApplyFilter()
    {
        var query = filterBox.Text.Trim();
        foreach (var (cat, card) in cards)
        {
            var match = string.IsNullOrEmpty(query) || cat.Contains(query, StringComparison.OrdinalIgnoreCase);
            card.Visibility = match ? Visibility.Visible : Visibility.Collapsed;
        }
    }

    private void ImportFromDialog()
    {
        var dialog = new OpenFileDialog
        {
            Title = "Choose or Upload Custom Container Icon",
            Filter = "Icon & Image files (*.png;*.ico;*.jpg;*.jpeg;*.bmp;*.exe;*.lnk)|*.png;*.ico;*.jpg;*.jpeg;*.bmp;*.exe;*.lnk",
            DereferenceLinks = false
        };
        if (dialog.ShowDialog(this) == true)
            ImportFile(dialog.FileName);
    }

    private void ImportFile(string file)
    {
        try
        {
            Choice = icons.Import(file);
            Refresh();
        }
        catch (Exception ex)
        {
            AppDockDialog.Show(this, "This icon could not be loaded. " + ex.Message, "AppDock", MessageBoxImage.Warning);
        }
    }

    private void Refresh()
    {
        preview.Source = icons.Get(Choice);
        if (Choice.Kind == ContainerIconKind.Custom)
        {
            selectedTitle.Text = "Custom Uploaded Icon";
            selectedSubtitle.Text = Choice.ResourcePath is { } p ? Path.GetFileName(p) : "Custom icon resource";
        }
        else
        {
            var name = Choice.Kind == ContainerIconKind.BuiltIn ? Choice.BuiltIn : "General Applications";
            selectedTitle.Text = name;
            selectedSubtitle.Text = $"AppDock Container Icon — {name}";
        }

        var activeCat = Choice.Kind == ContainerIconKind.Custom
            ? null
            : (Choice.Kind == ContainerIconKind.BuiltIn ? Choice.BuiltIn : "General Applications");

        foreach (var (cat, card) in cards)
        {
            var isSelected = string.Equals(cat, activeCat, StringComparison.OrdinalIgnoreCase);
            card.SetResourceReference(Border.BackgroundProperty, isSelected ? "SelectionFill" : "Card");
            card.SetResourceReference(Border.BorderBrushProperty, isSelected ? "Accent" : "Line");
        }
    }

    private static Button CreateButton(string label, Action action)
    {
        var button = new Button
        {
            Content = label,
            Margin = new Thickness(0, 0, 6, 0),
            Padding = new Thickness(12, 6, 12, 6),
            FontSize = 12.5
        };
        button.Click += (_, _) => action();
        return button;
    }
}
