using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.VisualTree;
using System.Globalization;

namespace Compositor.Desktop;

internal static class UiLayoutAudit
{
    internal static int CheckText(Control root, string context)
    {
        var count = 0;
        bool Visible(Control control) => control.IsVisible && control.Bounds.Width > 0 && control.Bounds.Height > 0
            && !control.GetVisualAncestors().OfType<Control>().Any(parent => !parent.IsVisible);
        foreach (var text in root.GetVisualDescendants().OfType<TextBlock>().Where(Visible))
        {
            if (string.IsNullOrEmpty(text.Text) || text.TextTrimming != TextTrimming.None) continue;
            var caption = text is Avalonia.Controls.Primitives.AccessText ? System.Text.RegularExpressions.Regex.Replace(text.Text, "_(.)", "$1") : text.Text;
            var natural = new FormattedText(caption, CultureInfo.CurrentCulture, text.FlowDirection,
                new Typeface(text.FontFamily, text.FontStyle, text.FontWeight, text.FontStretch), text.FontSize, text.Foreground);
            if (text.TextWrapping == TextWrapping.NoWrap && natural.Width > text.Bounds.Width + 1.5)
            {
                var button = text.GetVisualAncestors().OfType<Button>().FirstOrDefault();
                throw new InvalidOperationException($"{context}: clipped caption '{text.Text}': needs {natural.Width:0.##}, has {text.Bounds.Width:0.##}; font {text.FontFamily}/{text.FontSize}/{text.FontWeight}; button {button?.Width}/{button?.Bounds}/{button?.Padding}.");
            }
            if (text.TextLayout.Height > text.Bounds.Height + 1.5)
                throw new InvalidOperationException($"{context}: clipped caption height '{text.Text}'.");
            count++;
        }
        foreach (var field in root.GetVisualDescendants().OfType<TextBox>().Where(Visible))
        {
            if (!field.Classes.Contains("numeric")) continue;
            if (field.TextAlignment != TextAlignment.Center || field.VerticalContentAlignment != Avalonia.Layout.VerticalAlignment.Center)
                throw new InvalidOperationException($"{context}: number '{field.Text}' is not centered: {field.TextAlignment}/{field.VerticalContentAlignment}.");
            var text = field.GetVisualDescendants().OfType<Avalonia.Controls.Presenters.TextPresenter>().FirstOrDefault();
            if (text is not null && text.TextLayout.WidthIncludingTrailingWhitespace > text.Bounds.Width + 1.5)
                throw new InvalidOperationException($"{context}: clipped number '{field.Text}'.");
            count++;
        }
        return count;
    }
}
