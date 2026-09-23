using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;

namespace DeskPokemon;

/// <summary>Shared Avalonia dialogs so OS-native message boxes cannot change the UI.</summary>
internal static class AppDialog
{
    public static Task<bool> ConfirmAsync(Window owner, string title, string message) => Show(owner, title, message, true);
    public static Task<bool> ShowAsync(Window owner, string message, string title) => Show(owner, title, message, false);

    private static Task<bool> Show(Window owner, string title, string message, bool confirm)
        => Create(title, message, confirm).ShowDialog<bool>(owner);

    internal static Window StartupError(string message)
    {
        var window = Create("불러오기 실패", message, false);
        window.WindowStartupLocation = WindowStartupLocation.CenterScreen;
        return window;
    }

    private static Window Create(string title, string message, bool confirm)
    {
        var dialog = new Window
        {
            Title = title, SystemDecorations = SystemDecorations.None,
            TransparencyLevelHint = [WindowTransparencyLevel.Transparent], Background = Brushes.Transparent,
            CanResize = false, SizeToContent = SizeToContent.WidthAndHeight,
            WindowStartupLocation = WindowStartupLocation.CenterOwner, Topmost = true,
        };
        RenderOptions.SetTextRenderingMode(dialog, TextRenderingMode.Antialias);
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, HorizontalAlignment = HorizontalAlignment.Right };
        if (confirm)
        {
            var cancel = new Button { Content = "취소" };
            cancel.Click += (_, _) => dialog.Close(false);
            buttons.Children.Add(cancel);
        }
        var ok = new Button { Content = "확인" };
        ok.Click += (_, _) => dialog.Close(true);
        buttons.Children.Add(ok);
        dialog.Content = new Border
        {
            Background = Brush.Parse("#F0202020"), CornerRadius = new CornerRadius(10), Padding = new Thickness(16),
            Child = new StackPanel
            {
                Width = 320, Spacing = 12,
                Children =
                {
                    new TextBlock { Text = title, FontSize = 14, FontWeight = FontWeight.Bold, Foreground = Brushes.White },
                    new TextBlock { Text = message, TextWrapping = TextWrapping.Wrap, Foreground = Brushes.White },
                    buttons,
                },
            },
        };
        return dialog;
    }
}
