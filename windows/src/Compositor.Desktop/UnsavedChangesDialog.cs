using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;

namespace Compositor.Desktop;

internal enum UnsavedChoice { Cancel, Save, Discard }

internal sealed class UnsavedChangesDialog : DialogWindow
{
    private UnsavedChoice _choice = UnsavedChoice.Cancel;

    internal UnsavedChangesDialog(string name)
    {
        UiText.Set(this, Window.TitleProperty, "Unsaved changes");
        Width = 460;
        SizeToContent = SizeToContent.Height;
        CanResize = false;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        var save = new Button { [!ContentControl.ContentProperty] = UiText.Bind("Save"), IsDefault = true };
        var discard = new Button { [!ContentControl.ContentProperty] = UiText.Bind("Don't Save") };
        var cancel = new Button { [!ContentControl.ContentProperty] = UiText.Bind("Cancel"), IsCancel = true };
        save.Click += (_, _) => Answer(UnsavedChoice.Save);
        discard.Click += (_, _) => Answer(UnsavedChoice.Discard);
        cancel.Click += (_, _) => Close();
        Content = new StackPanel
        {
            Margin = new Thickness(20), Spacing = 18,
            Children =
            {
                new TextBlock
                {
                    Text = UiText.Format("Save changes to {0} before closing?", name),
                    TextWrapping = Avalonia.Media.TextWrapping.Wrap,
                },
                new StackPanel
                {
                    Orientation = Orientation.Horizontal, Spacing = 8,
                    HorizontalAlignment = HorizontalAlignment.Right,
                    Children = { discard, cancel, save },
                },
            },
        };
        Opened += (_, _) => save.Focus();
    }

    private void Answer(UnsavedChoice choice) { _choice = choice; Close(); }

    internal static async Task<UnsavedChoice> Ask(Window owner, string name)
    {
        var dialog = new UnsavedChangesDialog(name);
        await dialog.ShowDialog(owner);
        return dialog._choice;
    }
}
