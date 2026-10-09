using System.Windows;
using System.Windows.Controls;

namespace CTTools.Features.Site;

internal static class SiteDialog
{
    public static bool Text(IntPtr owner, string title, string label, string initial, out string value)
    {
        var ok = Show(owner, title, label, showText: true, initial, items: null, out value, out _);
        return ok;
    }

    public static bool Choice(IntPtr owner, string title, string label, IReadOnlyList<string> items, out int index)
    {
        var ok = Show(owner, title, label, showText: false, "", items, out _, out index);
        return ok && index >= 0;
    }

    public static bool TextAndChoice(IntPtr owner, string title, string label, string initial, IReadOnlyList<string> items, out string value, out int index) =>
        Show(owner, title, label, showText: true, initial, items, out value, out index) && index >= 0;

    private static bool Show(IntPtr owner, string title, string label, bool showText, string initial, IReadOnlyList<string>? items, out string value, out int index)
    {
        value = initial;
        index = -1;
        var box = new TextBox { Text = initial, Margin = new Thickness(0, 6, 0, 8), Padding = new Thickness(4) };
        var combo = new ComboBox { Margin = new Thickness(0, 6, 0, 8), ItemsSource = items };
        if (items is { Count: > 0 })
            combo.SelectedIndex = 0;
        var window = new Window
        {
            Title = title,
            Width = 440,
            SizeToContent = SizeToContent.Height,
            WindowStartupLocation = WindowStartupLocation.CenterScreen,
            ResizeMode = ResizeMode.NoResize,
            Background = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(236, 239, 241)),
            ShowInTaskbar = false,
        };
        new System.Windows.Interop.WindowInteropHelper(window).Owner = owner;
        var accepted = false;
        var ok = new Button { Content = "OK", Width = 90, Margin = new Thickness(0, 0, 8, 0), IsDefault = true };
        var cancel = new Button { Content = "Cancel", Width = 90, IsCancel = true };
        ok.Click += (_, _) =>
        {
            accepted = true;
            window.DialogResult = true;
        };
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        buttons.Children.Add(ok);
        buttons.Children.Add(cancel);
        var stack = new StackPanel { Margin = new Thickness(14) };
        stack.Children.Add(new TextBlock { Text = label, TextWrapping = TextWrapping.Wrap });
        if (items is not null)
            stack.Children.Add(combo);
        if (showText)
            stack.Children.Add(box);
        stack.Children.Add(buttons);
        window.Content = stack;
        var result = window.ShowDialog() == true && accepted;
        value = box.Text;
        index = combo.SelectedIndex;
        return result;
    }
}
