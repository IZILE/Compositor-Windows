using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;

namespace Compositor.Desktop;

public sealed partial class MainWindow
{
    private readonly Border _maskBadge = new() { IsVisible = false, Height = 26, Margin = new Thickness(12),
        HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Top, CornerRadius = new CornerRadius(13),
        Background = new SolidColorBrush(Colors.Black, 0.75), BorderBrush = new SolidColorBrush(Colors.White, 0.14), BorderThickness = new Thickness(1), ZIndex = 20 };
    private TextBlock? _maskBadgeName;
    private Button? _maskBadgeClose;

    private void SynchronizeMaskView()
    {
        if (_open.MaskTarget is not { } id || _document?.Layers.FirstOrDefault(layer => layer.ID == id) is not { Mask: not null })
            _open.ViewsMaskAlone = false;
        var layer = _open.ViewsMaskAlone ? _document?.Layers.FirstOrDefault(layer => layer.ID == _open.MaskTarget) : null;
        _canvas.MaskAloneLayerID = layer?.ID;
        _maskBadge.IsVisible = layer is not null;
        if (layer is not null)
        {
            if (_maskBadgeName is null)
            {
                _maskBadgeName = new TextBlock { MaxWidth = 220, TextTrimming = TextTrimming.CharacterEllipsis, FontSize = 12,
                    Foreground = Skin.SecondaryBrush, VerticalAlignment = VerticalAlignment.Center };
                var title = new TextBlock { [!TextBlock.TextProperty] = UiText.Bind("Layer Mask"), FontSize = 12,
                    FontWeight = FontWeight.SemiBold, VerticalAlignment = VerticalAlignment.Center };
                _maskBadgeClose = new Button { Classes = { "plain" }, Content = "×", Width = 18, Height = 24,
                    Padding = new Thickness(0), CornerRadius = new CornerRadius(9) };
                UiText.Set(_maskBadgeClose, ToolTip.TipProperty, "Show the image again (or Alt-click the mask thumbnail)");
                _maskBadgeClose.Click += (_, _) => { _open.ViewsMaskAlone = false; SynchronizeMaskView(); };
                _maskBadge.Child = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 7, Margin = new Thickness(11, 0, 8, 0),
                    Children = { new FooterIcon(2) { Width = 11, Height = 14, VerticalAlignment = VerticalAlignment.Center }, title, _maskBadgeName, _maskBadgeClose } };
            }
            _maskBadgeName.Text = layer.Name;
        }
        _canvas.InvalidateVisual();
    }
}
