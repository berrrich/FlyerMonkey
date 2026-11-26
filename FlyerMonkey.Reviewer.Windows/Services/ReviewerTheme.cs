using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace FlyerMonkey.Reviewer.Windows.Services;

public enum ReviewerThemeChoice { Dark, Medium, Light }

public static class ReviewerTheme
{
    private const ReviewerThemeChoice DefaultTheme = ReviewerThemeChoice.Dark;

    public static string SettingsPath => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "FlyerMonkey", "Reviewer", "theme.txt");

    public static ReviewerThemeChoice Load(string path)
    {
        try
        {
            var value = File.ReadAllText(path).Trim();
            if (Enum.TryParse<ReviewerThemeChoice>(value, out var theme)
                && value == theme.ToString()) return theme;
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
        return DefaultTheme;
    }

    public static void Save(string path, ReviewerThemeChoice theme)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        File.WriteAllText(path, theme.ToString());
    }

    public static void Apply(Window window, ReviewerThemeChoice theme)
    {
        window.Resources[typeof(ReviewerThemeChoice)] = theme;
        string[] keys = ["Canvas", "Panel", "Surface", "Text", "Muted", "Border", "Accent", "Selection",
            "Saved", "Committed", "CommittedBorder", "Warning", "WarningText", "Error", "ErrorBorder", "Running"];
        string[] colors = theme switch
        {
            ReviewerThemeChoice.Dark => ["#171B22", "#202630", "#29313D", "#F0F4FA", "#C3CEDB", "#5C6A7E",
                "#9CC9FF", "#284A72", "#254767", "#214D3C", "#5BA883", "#543B1E", "#FFD992", "#592C36", "#F1919E", "#4F4328"],
            ReviewerThemeChoice.Light => ["#F2F4F8", "#FFFFFF", "#E9EEF5", "#172234", "#435269", "#8895A8",
                "#174EA6", "#DCEBFF", "#CDE5FF", "#CBEED9", "#47825F", "#FFF0CA", "#783900", "#FDE5E8", "#AC273A", "#FFF1BE"],
            _ => ["#2E3541", "#394353", "#465266", "#F8FAFE", "#DFE7F2", "#8E9DB3",
                "#BBD9FF", "#3B5C84", "#355B7F", "#315C4A", "#79BD96", "#675033", "#FFE2A9", "#70404C", "#FFB0B9", "#655437"]
        };

#pragma warning disable WPF0001
        window.ThemeMode = theme == ReviewerThemeChoice.Light ? ThemeMode.Light : ThemeMode.Dark;
#pragma warning restore WPF0001
        for (var index = 0; index < keys.Length; index++)
        {
            var brush = (SolidColorBrush)new BrushConverter().ConvertFromString(colors[index])!;
            brush.Freeze();
            window.Resources["Reviewer" + keys[index]] = brush;
        }
        window.SetResourceReference(Control.BackgroundProperty, "ReviewerCanvas");
        window.SetResourceReference(Control.ForegroundProperty, "ReviewerText");
        window.Resources[SystemColors.HighlightBrushKey] = window.Resources["ReviewerSelection"];
        window.Resources[SystemColors.HighlightTextBrushKey] = window.Resources["ReviewerText"];
        window.Resources[SystemColors.WindowBrushKey] = window.Resources["ReviewerPanel"];
        window.Resources[SystemColors.WindowTextBrushKey] = window.Resources["ReviewerText"];
        window.Resources[SystemColors.ControlBrushKey] = window.Resources["ReviewerSurface"];
        window.Resources[SystemColors.ControlTextBrushKey] = window.Resources["ReviewerText"];
        window.Resources[SystemColors.InactiveSelectionHighlightBrushKey] = window.Resources["ReviewerSelection"];
        window.Resources[SystemColors.InactiveSelectionHighlightTextBrushKey] = window.Resources["ReviewerText"];
        foreach (var type in new[] { typeof(Button), typeof(TextBox), typeof(ComboBox), typeof(ListBox),
            typeof(CheckBox), typeof(RadioButton), typeof(Label), typeof(Expander) })
        {
            var style = new Style(type);
            style.Setters.Add(new Setter(Control.ForegroundProperty, window.Resources["ReviewerText"]));
            if (type == typeof(Button) || type == typeof(TextBox) || type == typeof(ComboBox) || type == typeof(ListBox))
            {
                style.Setters.Add(new Setter(Control.BackgroundProperty, window.Resources["ReviewerSurface"]));
                style.Setters.Add(new Setter(Control.BorderBrushProperty, window.Resources["ReviewerBorder"]));
            }
            window.Resources[type] = style;
        }
    }

    public static ReviewerThemeChoice ChoiceFor(Window window) =>
        window.TryFindResource(typeof(ReviewerThemeChoice)) is ReviewerThemeChoice theme ? theme : DefaultTheme;

    public static MessageBoxResult ShowMessage(Window owner, ReviewerThemeChoice theme, string message, string title = "Flyer Monkey Reviewer",
        MessageBoxButton buttons = MessageBoxButton.OK, MessageBoxImage icon = MessageBoxImage.None)
    {
        var dialog = new Window
        {
            Title = title, SizeToContent = SizeToContent.WidthAndHeight, MaxWidth = 640, MaxHeight = 500,
            WindowStartupLocation = owner.IsVisible ? WindowStartupLocation.CenterOwner : WindowStartupLocation.CenterScreen,
            ResizeMode = ResizeMode.NoResize, ShowInTaskbar = false
        };
        if (owner.IsVisible) dialog.Owner = owner;
        Apply(dialog, theme);
        var content = new DockPanel { Margin = new Thickness(22), MinWidth = 290 };
        var actions = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(0, 16, 0, 0) };
        DockPanel.SetDock(actions, Dock.Bottom);
        content.Children.Add(actions);
        var text = new TextBlock { Text = message, TextWrapping = TextWrapping.Wrap, MaxWidth = 550 };
        content.Children.Add(new ScrollViewer { Content = text, VerticalScrollBarVisibility = ScrollBarVisibility.Auto });
        var result = buttons == MessageBoxButton.YesNo ? MessageBoxResult.No
            : buttons == MessageBoxButton.OK ? MessageBoxResult.OK : MessageBoxResult.Cancel;
        var choices = buttons switch
        {
            MessageBoxButton.YesNo => new[] { MessageBoxResult.Yes, MessageBoxResult.No },
            MessageBoxButton.YesNoCancel => [MessageBoxResult.Yes, MessageBoxResult.No, MessageBoxResult.Cancel],
            MessageBoxButton.OKCancel => [MessageBoxResult.OK, MessageBoxResult.Cancel],
            _ => [MessageBoxResult.OK]
        };
        for (var index = 0; index < choices.Length; index++)
        {
            var choice = choices[index];
            var button = new Button { Content = choice.ToString(), MinWidth = 70, Padding = new Thickness(12, 5, 12, 5),
                Margin = new Thickness(6, 0, 0, 0), IsDefault = index == 0, IsCancel = index == choices.Length - 1 };
            button.Click += (_, _) => { result = choice; dialog.Close(); };
            actions.Children.Add(button);
        }
        if (icon == MessageBoxImage.Warning || icon == MessageBoxImage.Error)
        {
            var heading = new TextBlock { Text = icon == MessageBoxImage.Error ? "Error" : "Warning",
                FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 0, 0, 8) };
            heading.SetResourceReference(TextBlock.ForegroundProperty, "ReviewerWarningText");
            DockPanel.SetDock(heading, Dock.Top);
            content.Children.Insert(0, heading);
        }
        dialog.Content = content;
        dialog.ShowDialog();
        return result;
    }
}
