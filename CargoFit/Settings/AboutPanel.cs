using System;
using System.Reflection;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;

namespace CargoFit;

internal sealed class AboutPanel : UserControl
{
    public AboutPanel()
    {
        Content = BuildRoot();
    }

    private Control BuildRoot()
    {
        var ver = Assembly.GetExecutingAssembly().GetName().Version;
        string versionText = ver is null ? "–" : $"v{ver.Major}.{ver.Minor}.{ver.Build}";

        // App name
        var appName = new TextBlock
        {
            Text               = "CargoFit",
            FontSize           = 28,
            FontWeight         = FontWeight.Bold,
            Foreground         = ThemeColors.AccentText,
            HorizontalAlignment = HorizontalAlignment.Center,
            Margin             = new Avalonia.Thickness(0, 0, 0, 6)
        };

        // Version
        var version = new TextBlock
        {
            Text               = versionText,
            FontSize           = 14,
            Foreground         = ThemeColors.InkMuted,
            HorizontalAlignment = HorizontalAlignment.Center,
            Margin             = new Avalonia.Thickness(0, 0, 0, 24)
        };

        // Separator
        var separator = new Border
        {
            Height          = 1,
            Background      = ThemeColors.BorderLight,
            Margin          = new Avalonia.Thickness(0, 0, 0, 24),
            HorizontalAlignment = HorizontalAlignment.Stretch
        };

        // "เขียนโดย PingSunn"
        var author = new TextBlock
        {
            Text               = "เขียนโดย  PingSunn",
            FontSize           = 14,
            Foreground         = ThemeColors.Ink,
            HorizontalAlignment = HorizontalAlignment.Center,
            Margin             = new Avalonia.Thickness(0, 0, 0, 10)
        };

        // GitHub hyperlink button
        var linkText = new TextBlock
        {
            Text                = "github.com/pingsunn",
            FontSize            = 14,
            Foreground          = ThemeColors.AccentText,
            TextDecorations     = TextDecorations.Underline,
            HorizontalAlignment = HorizontalAlignment.Center,
            Cursor              = new Cursor(StandardCursorType.Hand)
        };

        var linkButton = new Button
        {
            Content         = linkText,
            Background      = Brushes.Transparent,
            BorderThickness = new Avalonia.Thickness(0),
            Padding         = new Avalonia.Thickness(0),
            Cursor          = new Cursor(StandardCursorType.Hand),
            HorizontalAlignment = HorizontalAlignment.Center
        };
        linkButton.Click += (_, _) =>
        {
            TopLevel.GetTopLevel(this)?.Launcher
                .LaunchUriAsync(new Uri("https://github.com/pingsunn"));
        };

        var card = new StackPanel
        {
            Orientation         = Orientation.Vertical,
            HorizontalAlignment = HorizontalAlignment.Center,
            Width               = 320,
            Margin              = new Avalonia.Thickness(0, 64, 0, 0)
        };
        card.Children.Add(appName);
        card.Children.Add(version);
        card.Children.Add(separator);
        card.Children.Add(author);
        card.Children.Add(linkButton);

        return new ScrollViewer { Content = card };
    }
}
