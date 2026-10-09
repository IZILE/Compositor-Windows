using System.Reflection;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Compositor.Core.Document;
using Compositor.Core.Format;
using Compositor.Core.Model;

namespace Compositor.Desktop;

internal static partial class Program
{
    private static int DialogCheck(string output)
    {
        BuildHeadless().SetupWithoutStarting();
        Directory.CreateDirectory(output);
        var checkedDialogs = 0;
        Window Make<T>(params object?[] args) => (Window)(Activator.CreateInstance(typeof(T),
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic, null, args, null)
            ?? throw new InvalidOperationException("No dialog constructor."));
        foreach (var language in new[] { "en", "zh-CN" })
        {
            using var rangeDocument = new CanvasDocument(Guid.NewGuid(), 64, 64);
            using var range = ColorRangeSession.Begin(rangeDocument)!;
            UiText.Language = language;
            var cases = new List<(string Name, Func<Window> Build)>
            {
                ("new-project", () => new NewDocumentDialog()),
                ("image-size", () => Make<ImageSizeDialog>(1920, 1080, 72.0, LayerSampling.HighQuality)),
                ("canvas-size", () => Make<CanvasSizeDialog>(1920, 1080, 4)),
                ("grid", () => new GridSettingsDialog(new LayoutGrid())),
                ("guide", () => Make<GuideDialog>(1920, 1080)),
                ("trim", () => Make<TrimDialog>(new TrimOptions())),
                ("quality", () => Make<QualityDialog>()),
                ("text", () => Make<TextDialog>("Edit Text", new LayerTextStyle { Content = "中文 / English" })),
                ("unsaved", () => new UnsavedChangesDialog("中文 / English")),
                ("color-range", () => new ColorRangePanel(range)),
                ("color-picker", () => new ColorPickerDialog("Color", (0.2, 0.4, 0.8))),
            };
            foreach (var kind in Enum.GetValues<FilterKind>())
                cases.Add(("filter-" + kind, () => Make<FilterDialog>(kind, new FilterSettings())));
            foreach (var kind in Enum.GetValues<EffectKind>())
                cases.Add(("effect-" + kind, () => Make<EffectDialog>(kind, null)));
            foreach (var kind in Enum.GetValues<AdjustmentKind>())
                cases.Add(("adjustment-" + kind, () => new AdjustmentDialog(new LayerAdjustment { Kind = kind })));
            foreach (var style in Enum.GetValues<DitherStyle>())
                cases.Add(("dither-" + style, () => Make<DitherDialog>(style, new DitherSettings())));

            foreach (var (name, build) in cases)
            {
                var window = build();
                window.Show();
                window.UpdateLayout();
                Dispatcher.UIThread.RunJobs();
                var textChecks = UiLayoutAudit.CheckText(window, language + " " + name);
                if (window is ColorPickerDialog picker)
                {
                    var channel = window.GetVisualDescendants().OfType<NumericUpDown>().First();
                    var increase = channel.GetVisualDescendants().OfType<RepeatButton>()
                        .Single(button => button.Name == "PART_IncreaseButton");
                    var before = channel.Value;
                    var at = increase.TranslatePoint(new Point(increase.Bounds.Width / 2, increase.Bounds.Height / 2), window)!.Value;
                    window.MouseDown(at, MouseButton.Left, RawInputModifiers.LeftMouseButton);
                    window.MouseUp(at, MouseButton.Left, RawInputModifiers.None); window.UpdateLayout();
                    if (channel.Value != before + 1) throw new InvalidOperationException("The color stepper does not increment its actual channel.");
                    channel.GetVisualDescendants().OfType<TextBox>().Single().Focus();
                    window.KeyPress(Key.Down, RawInputModifiers.None, PhysicalKey.ArrowDown, null); window.UpdateLayout();
                    if (channel.Value != before || picker.Ok.Bounds.Height > 25 || picker.Cancel.Bounds.Height > 25)
                        throw new InvalidOperationException($"Color fields must keep Windows arrow-key input and compact action buttons: {before} to {channel.Value}, actions {picker.Ok.Bounds.Height}/{picker.Cancel.Bounds.Height}.");
                }
                using var frame = window.CaptureRenderedFrame() ?? throw new InvalidOperationException("No dialog frame.");
                frame.Save(Path.Combine(output, name + "-" + language + ".png"), new PngBitmapEncoderOptions());
                bool Visible(Control control) => control.Bounds.Width > 0 && control.IsVisible
                    && !control.GetVisualAncestors().OfType<Control>().Any(parent => !parent.IsVisible);
                foreach (var control in window.GetVisualDescendants().OfType<Control>().Where(control =>
                             control is Slider or TextBox or ComboBox or CheckBox or Button && Visible(control)))
                {
                    if (!Visible(control)) continue;
                    var at = control.TranslatePoint(new Point(), window)!.Value;
                    if (at.X < -1 || at.X + control.Bounds.Width > window.Bounds.Width + 1)
                        throw new InvalidOperationException($"{language} {name}: {control.GetType().Name} spills horizontally at {at}, {control.Bounds} in {window.Bounds}.");
                    if (control is Slider && control.Bounds.Width < 60)
                        throw new InvalidOperationException($"{language} {name}: slider is too narrow to use.");
                }
                foreach (var row in window.GetVisualDescendants().OfType<Grid>().Where(row => Equals(row.Tag, "labeled-row") && Visible(row)))
                {
                    var label = row.Children[0];
                    var field = row.Children[1];
                    var labelMiddle = label.Bounds.Y + label.Bounds.Height / 2;
                    var fieldMiddle = field.Bounds.Y + field.Bounds.Height / 2;
                    if (Math.Abs(labelMiddle - fieldMiddle) > 1)
                        throw new InvalidOperationException($"{language} {name}: label and field centers differ by {Math.Abs(labelMiddle - fieldMiddle)}.");
                }
                Console.WriteLine($"PASS: {language} {name}: controls fit and label centers align; {textChecks} captions and numeric fields are not clipped");
                window.Close();
                checkedDialogs++;
            }
        }
        Console.WriteLine($"PASS: {checkedDialogs} live dialog cases across both languages");
        return 0;
    }
}
