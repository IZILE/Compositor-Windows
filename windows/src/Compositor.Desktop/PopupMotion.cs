using Avalonia;
using Avalonia.Animation;
using Avalonia.Animation.Easings;
using Avalonia.Controls.Primitives;
using Avalonia.Threading;
using System.Runtime.CompilerServices;

namespace Compositor.Desktop;

/// <summary>One opening fade for app popups; closing cancels pending work and restores the next opening.</summary>
internal sealed class PopupMotion : AvaloniaObject
{
    public static readonly AttachedProperty<bool> EnabledProperty = AvaloniaProperty.RegisterAttached<PopupMotion, Popup, bool>("Enabled");
    private static readonly ConditionalWeakTable<Popup, State> States = new();
    static PopupMotion() => EnabledProperty.Changed.AddClassHandler<Popup>((popup, change) =>
    {
        if (change.NewValue is true) States.GetValue(popup, item => new State(item));
        else if (States.TryGetValue(popup, out var state)) { state.Detach(); States.Remove(popup); }
    });
    public static bool GetEnabled(Popup popup) => popup.GetValue(EnabledProperty);
    public static void SetEnabled(Popup popup, bool enabled) => popup.SetValue(EnabledProperty, enabled);

    private sealed class State
    {
        private readonly Popup _popup;
        private Avalonia.Controls.Control? _child;
        private Transitions? _previous;
        private double _opacity;
        private int _generation;
        internal State(Popup popup) { _popup = popup; popup.Opened += Opened; popup.Closed += Closed; }
        private void Opened(object? sender, EventArgs args)
        {
            Restore();
            if (_popup.Child is not { } child) return;
            var generation = ++_generation;
            _child = child; _previous = child.Transitions; _opacity = child.Opacity;
            child.Transitions = null; child.Opacity = 0;
            var transitions = new Transitions();
            if (_previous is not null)
                foreach (var transition in _previous.Where(item => item is not TransitionBase known || known.Property != Avalonia.Visual.OpacityProperty)) transitions.Add(transition);
            transitions.Add(new DoubleTransition { Property = Avalonia.Visual.OpacityProperty,
                Duration = TimeSpan.FromMilliseconds(80), Easing = new SplineEasing(0, 0, 0.58, 1) });
            child.Transitions = transitions;
            Dispatcher.UIThread.Post(() =>
            {
                if (_generation == generation && _popup.IsOpen && ReferenceEquals(_child, child)) Motion.Set(child, Avalonia.Visual.OpacityProperty, _opacity);
            }, DispatcherPriority.Loaded);
        }
        private void Closed(object? sender, EventArgs args) { ++_generation; Restore(); }
        private void Restore()
        {
            if (_child is not { } child) return;
            child.Transitions = null; child.Opacity = _opacity; child.Transitions = _previous;
            _child = null; _previous = null;
        }
        internal void Detach() { _popup.Opened -= Opened; _popup.Closed -= Closed; ++_generation; Restore(); }
    }
}
