using Avalonia.Platform.Storage;
using Compositor.Core.IO;

namespace Compositor.Desktop;

public sealed partial class MainWindow
{
    private Func<Task<string[]>>? _fontImportPicker;
    private bool _importingFonts;

    private async void ImportFonts()
    {
        if (_importingFonts || _document is null) return;
        var document = _document; var session = _text; var selected = Selected;
        _importingFonts = true; _optionsBar.ImportFontButton.IsEnabled = false;
        try
        {
            var paths = _fontImportPicker is { } picker ? await picker() :
                (await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
                {
                    Title = UiText.Get("Import Font…"), AllowMultiple = true,
                    FileTypeFilter = [new FilePickerFileType("TTF / OTF") { Patterns = ["*.ttf", "*.otf"] }],
                })).Select(file => file.TryGetLocalPath()).Where(path => path is not null).Cast<string>().ToArray();
            if (paths.Length == 0) return;
            var fonts = await Task.Run(() => FontLibrary.Current.Import(paths));
            _optionsBar.LoadTextFonts(reload: true);
            if (fonts.Count > 0 && !_previewClosed && ReferenceEquals(_document, document) && ReferenceEquals(_text, session) && Selected == selected)
            {
                BeginTextStyle();
                ChangeTextStyle(style => { style.FontName = fonts[^1].Name; style.FontRuns = null; });
                EndTextStyle();
            }
            Say(UiText.Format("Imported {0} fonts", fonts.Count));
        }
        catch (Exception error) { Say(UiText.Format("Could not import font: {0}", UiText.Get(error.Message))); }
        finally { _importingFonts = false; _optionsBar.ImportFontButton.IsEnabled = true; _canvas.Focus(); }
    }
}
