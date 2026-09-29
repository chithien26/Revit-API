using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using ComboBox = System.Windows.Controls.ComboBox;

namespace CTTools.Features.AutoDim;

/// <summary>
/// Asks which linear dimension type to use, preselecting the last choice (or the project default).
/// </summary>
internal static class DimensionTypePicker
{
    private sealed record Item(DimensionType Type, string Label);

    /// <returns>The chosen type, or null when the project has no linear dimension types (Revit then uses its default).</returns>
    /// <exception cref="OperationCanceledException">The user cancelled.</exception>
    public static DimensionType? Pick(UIApplication uiApp, string title)
    {
        var doc = uiApp.ActiveUIDocument.Document;
        var items = new FilteredElementCollector(doc)
            .OfClass(typeof(DimensionType))
            .Cast<DimensionType>()
            .Where(t => t.StyleType == DimensionStyleType.Linear && !string.IsNullOrWhiteSpace(t.Name) && t.Name != t.FamilyName)
            .OrderBy(t => t.Name)
            .Select(t => new Item(t, $"{t.Name}   (text {TextSizeMm(t):0.##} mm)"))
            .ToList();
        if (items.Count == 0)
            return null;

        var rememberedName = AutoDimSettings.DimensionTypeName;
        var defaultId = doc.GetDefaultElementTypeId(ElementTypeGroup.LinearDimensionType);
        var selected = items.FirstOrDefault(i => i.Type.Name == rememberedName)
            ?? items.FirstOrDefault(i => i.Type.Id == defaultId)
            ?? items[0];

        var activeView = uiApp.ActiveUIDocument.ActiveView;
        var combo = new ComboBox
        {
            ItemsSource = items,
            DisplayMemberPath = nameof(Item.Label),
            SelectedItem = selected,
            MinWidth = 360,
            Margin = new Thickness(0, 6, 0, 6),
        };
        var hint = new TextBlock { Foreground = Brushes.Gray, TextWrapping = TextWrapping.Wrap, MaxWidth = 360 };
        void UpdateHint()
        {
            if (combo.SelectedItem is not Item item)
                return;
            var mm = TextSizeMm(item.Type);
            hint.Text = activeView is ViewPlan
                ? $"Text size is measured on paper. At this view's scale 1:{activeView.Scale} the text is {mm * activeView.Scale:0} mm tall in the model."
                : "Text size is measured on paper, so it looks the same size on every viewport of the sheet.";
        }
        combo.SelectionChanged += (_, _) => UpdateHint();
        UpdateHint();

        var ok = new Button { Content = "OK", IsDefault = true, Width = 80, Margin = new Thickness(0, 0, 8, 0) };
        var cancel = new Button { Content = "Cancel", IsCancel = true, Width = 80 };
        var buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(0, 12, 0, 0),
            Children = { ok, cancel },
        };

        var window = new Window
        {
            Title = title,
            SizeToContent = SizeToContent.WidthAndHeight,
            ResizeMode = ResizeMode.NoResize,
            WindowStartupLocation = WindowStartupLocation.CenterScreen,
            ShowInTaskbar = false,
            Content = new StackPanel
            {
                Margin = new Thickness(14),
                Children = { new TextBlock { Text = "Dimension type:" }, combo, hint, buttons },
            },
        };
        ok.Click += (_, _) => window.DialogResult = true;
        new WindowInteropHelper(window).Owner = uiApp.MainWindowHandle;

        if (window.ShowDialog() != true || combo.SelectedItem is not Item chosen)
            throw new OperationCanceledException();

        AutoDimSettings.DimensionTypeName = chosen.Type.Name;
        return chosen.Type;
    }

    private static double TextSizeMm(DimensionType type) =>
        (type.get_Parameter(BuiltInParameter.TEXT_SIZE)?.AsDouble() ?? 0) * 304.8;
}
