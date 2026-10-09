using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Compositor.Core.Format;

namespace Compositor.Desktop;

public sealed partial class MainWindow
{
    private readonly TextBlock _layerCount = new() { Foreground = Skin.SecondaryBrush, VerticalAlignment = VerticalAlignment.Center };
    private Control LayerHeader()
    {
        _layerCount.Text = (_document?.Layers.Count ?? 0).ToString();
        var row = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto"), Margin = new Thickness(18) };
        row.Children.Add(new TextBlock { [!TextBlock.TextProperty] = UiText.Bind("Layers"),
            FontWeight = Avalonia.Media.FontWeight.SemiBold, VerticalAlignment = VerticalAlignment.Center });
        Grid.SetColumn(_layerCount, 1);
        row.Children.Add(_layerCount);
        return new Border { BorderBrush = new Avalonia.Media.SolidColorBrush(Avalonia.Media.Colors.White, 0.08),
            BorderThickness = new Thickness(0, 0, 0, 1), Child = row };
    }

    private Border? _panelResize;
    private Control LayersDock()
    {
        // ContentView.swift permits panel widths from 202 through 352, with an 8-point drag target.
        var dock = new Grid { ColumnDefinitions = new ColumnDefinitions("8,Auto"), ZIndex = 5,
            Margin = new Thickness(-3.5, 0, -3.5, 0) };
        var edge = _panelResize = new Border { Width = 8, Background = Avalonia.Media.Brushes.Transparent,
            HorizontalAlignment = HorizontalAlignment.Center, Cursor = new Cursor(StandardCursorType.SizeWestEast), ZIndex = 10 };
        double? startWidth = null;
        double startX = 0;
        edge.PointerPressed += (_, args) =>
        {
            if (!args.GetCurrentPoint(edge).Properties.IsLeftButtonPressed) return;
            startWidth = _layersSide.Width;
            startX = args.GetPosition(this).X;
            args.Pointer.Capture(edge);
            args.Handled = true;
        };
        edge.PointerMoved += (_, args) =>
        {
            if (startWidth is not { } width) return;
            _layersSide.Width = Math.Clamp(Math.Round(width - (args.GetPosition(this).X - startX)), 202, 352);
            args.Handled = true;
        };
        edge.PointerReleased += (_, args) => { startWidth = null; args.Pointer.Capture(null); };
        edge.PointerCaptureLost += (_, _) => startWidth = null;
        Grid.SetColumn(_layersSide, 1);
        dock.Children.Add(edge);
        dock.Children.Add(_layersSide);
        dock.IsVisible = _layersSide.IsVisible;
        _layersSide.PropertyChanged += (_, change) =>
        {
            if (change.Property == IsVisibleProperty) dock.IsVisible = _layersSide.IsVisible;
        };
        DockPanel.SetDock(dock, Dock.Right);
        UiText.Set(edge, ToolTip.TipProperty, "Drag to resize the panel");
        return dock;
    }

    private Control OpacityRow()
    {
        // A translated caption measures itself; four Chinese characters need more room than "Opacity".
        var row = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,8,*,8,44,4,10"), Tag = "opacity-row" };
        row.Children.Add(new TextBlock { [!TextBlock.TextProperty] = UiText.Bind("Opacity"),
            VerticalAlignment = VerticalAlignment.Center });
        Grid.SetColumn(_opacity, 2);
        Grid.SetColumn(_opacityReadout, 4);
        row.Children.Add(_opacity);
        row.Children.Add(_opacityReadout);
        var unit = new TextBlock { Text = "%", Foreground = Skin.SecondaryBrush, VerticalAlignment = VerticalAlignment.Center };
        Grid.SetColumn(unit, 6); row.Children.Add(unit);
        return row;
    }

    private void WireOpacityField()
    {
        void Commit()
        {
            if (_showingAppearance) return;
            if (double.TryParse(_opacityReadout.Text, System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture, out var value) && double.IsFinite(value))
                _opacity.Value = Math.Clamp(value, 0, 100);
            _opacityReadout.Text = $"{_opacity.Value:0}";
        }
        _opacityReadout.LostFocus += (_, _) => Commit();
        _opacityReadout.KeyDown += (_, args) =>
        {
            if (args.Key == Key.Enter) { Commit(); _canvas.Focus(); args.Handled = true; }
            else if (args.Key == Key.Escape) { _opacityReadout.Text = $"{_opacity.Value:0}"; _canvas.Focus(); args.Handled = true; }
            else if (args.Key is Key.Up or Key.Down)
            {
                Commit();
                _opacity.Value = Math.Clamp(_opacity.Value + (args.Key == Key.Up ? 1 : -1)
                    * (args.KeyModifiers.HasFlag(KeyModifiers.Shift) ? 10 : 1), 0, 100);
                args.Handled = true;
            }
        };
    }

    private readonly Dictionary<int, Button> _footerButtons = [];
    private Control LayerFooter()
    {
        var row = new Grid { ColumnDefinitions = new ColumnDefinitions("30,30,30,30,30,*,30"),
            Margin = new Thickness(8, 4, 8, 4), Height = 40 };
        Button Add(int column, string help, Action action)
        {
            var button = new Button { Classes = { "plain" }, Content = new FooterIcon(column) { Width = 18, Height = 18 }, FontSize = 18,
                Height = 40, HorizontalContentAlignment = HorizontalAlignment.Center };
            UiText.Set(button, ToolTip.TipProperty, help);
            button.Click += (_, _) => action();
            Grid.SetColumn(button, column);
            row.Children.Add(button);
            _footerButtons[column] = button;
            return button;
        }
        Add(0, "New blank layer", NewBlankLayer);
        Add(1, "Group selected layers", GroupSelected);
        var maskKeys = KeyModifiers.None;
        var mask = Add(2, "Add layer mask (Alt-click for a black mask)", () => AddMask(!maskKeys.HasFlag(KeyModifiers.Alt)));
        mask.AddHandler(PointerPressedEvent, (_, args) => maskKeys = args.KeyModifiers, Avalonia.Interactivity.RoutingStrategies.Tunnel);
        mask.Click += (_, _) => maskKeys = KeyModifiers.None;
        Button? effects = null;
        effects = Add(3, "Layer Effects", () =>
        {
            var menu = new ContextMenu();
            foreach (var kind in Enum.GetValues<EffectKind>())
            {
                var item = new MenuItem { Header = UiText.Get(EffectDialog.TitleFor(kind)) };
                item.Click += (_, _) => _ = EditEffect(kind);
                menu.Items.Add(item);
            }
            menu.Open(effects!);
        });
        Button? adjustments = null;
        adjustments = Add(4, "New Adjustment Layer", () =>
        {
            var menu = new ContextMenu();
            foreach (var kind in Enum.GetValues<AdjustmentKind>())
            {
                var item = new MenuItem { Header = UiText.Get(kind.ToString()) };
                item.Click += (_, _) => _ = NewAdjustment(kind);
                menu.Items.Add(item);
            }
            menu.Open(adjustments!);
        });
        Add(6, "Delete Layer", DeleteLayerOrMask);
        return row;
    }

    private void UpdateFooterState(Compositor.Core.Model.CanvasDocument? document, Compositor.Core.Model.ImageLayer? layer)
    {
        foreach (var button in _footerButtons.Values) button.IsEnabled = document is not null;
        if (_footerButtons.TryGetValue(0, out var add)) add.IsEnabled = document is not null && document.Layers.Count < Compositor.Core.Document.LayerPlacement.MaxLayers;
        if (_footerButtons.TryGetValue(1, out var group)) group.IsEnabled = layer is not null;
        if (_footerButtons.TryGetValue(2, out var mask)) mask.IsEnabled = layer is { Mask: null };
        if (mask is not null) UiText.Set(mask, ToolTip.TipProperty, document?.Selection.Path is null
            ? "Add layer mask (Alt-click for a black mask)" : "Add layer mask revealing the selection (Alt-click to hide it)");
        if (_footerButtons.TryGetValue(3, out var effects)) effects.IsEnabled = layer is { IsGroup: false, Adjustment: null };
        if (_footerButtons.TryGetValue(6, out var delete))
        {
            delete.IsEnabled = layer is not null;
            UiText.Set(delete, ToolTip.TipProperty, _options.PaintOnMask ? "Delete Mask" : "Delete Layer");
        }
    }
}
