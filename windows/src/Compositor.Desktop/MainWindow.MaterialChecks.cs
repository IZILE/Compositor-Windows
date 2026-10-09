using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Compositor.Core.Document;
using Compositor.Core.Format;
using Compositor.Core.IO;
using SkiaSharp;

namespace Compositor.Desktop;

public sealed partial class MainWindow
{
    internal string MaterialSelfCheck(string sample,string output)
    {
        var report=new List<string>();
        OpenInput(sample); // Startup file arguments are loaded before the first visible frame.
        var openedBounds=default(Size); Opened+=(_,_)=>openedBounds=new Size(Width,Height);
        Show(); UpdateLayout(); Dispatcher.UIThread.RunJobs();
        if (StartupPreparationCount!=1 || openedBounds!=new Size(Width,Height) || _document is null)
            throw new InvalidOperationException("Startup bounds or input changed after the first show.");
        report.Add("PASS: startup project and final window bounds are prepared before Opened");
        Hide(); Show(); Dispatcher.UIThread.RunJobs();
        if (StartupPreparationCount!=1 || openedBounds!=new Size(Width,Height)) throw new InvalidOperationException("Showing again re-ran startup placement.");
        report.Add("PASS: hide/show does not repeat startup placement or resize");
        MaterialControlsSelfCheck(output,report); return string.Join(Environment.NewLine,report);
    }
    private void MaterialControlsSelfCheck(string output,List<string> report)
    {
        MaterialPreviewChecks.Run(report);
        void Check(bool good,string name) { if(!good) throw new InvalidOperationException("MATERIAL UI FAILED: "+name); report.Add("PASS: "+name); }
        void Layout(Window? window=null) { (window??this).UpdateLayout(); Dispatcher.UIThread.RunJobs(); }
        void Click(Control control)
        {
            control.BringIntoView(); var root=TopLevel.GetTopLevel(control)!; root.UpdateLayout();
            var at=control.TranslatePoint(new Point(control.Bounds.Width/2,control.Bounds.Height/2),root)!.Value;
            root.MouseMove(at,RawInputModifiers.None); root.MouseDown(at,MouseButton.Left,RawInputModifiers.LeftMouseButton);
            root.MouseUp(at,MouseButton.Left,RawInputModifiers.None); Layout();
        }
        void Wait(Task task)
        {
            var timer=System.Diagnostics.Stopwatch.StartNew();
            while(!task.IsCompleted && timer.ElapsedMilliseconds<30000) { Dispatcher.UIThread.RunJobs(); Thread.Sleep(1); }
            if(!task.IsCompleted) throw new TimeoutException("The material handler did not complete."); task.GetAwaiter().GetResult(); Layout();
        }
        void Capture(Window window,string name)
        {
            Layout(window); AvaloniaHeadlessPlatform.ForceRenderTimerTick();
            using var frame=window.CaptureRenderedFrame()!; frame.Save(Path.Combine(output,name+".png"),new PngBitmapEncoderOptions());
        }
        var oldData=Environment.GetEnvironmentVariable("COMPOSITOR_DATA_DIR");
        var language=UiText.Language; var brush=_options.Brush; var shape=_options.Shape; var custom=_options.CustomShape; var gradient=_options.GradientPreset;
        Environment.SetEnvironmentVariable("COMPOSITOR_DATA_DIR",Path.Combine(output,"material-data"));
        try
        {
            Width=1180; Height=780; _canvas.Fit();
            foreach(var locale in new[]{"en","zh-CN"})
            {
                ChangeLanguage(locale); SetTool(Tool.Shape); Layout();
                Click(_optionsBar.ShapeChoice.ButtonAt(3));
                var dialog=OwnedWindows.OfType<MaterialPickerDialog<ShapePreset>>().Single(); Layout(dialog);
                Check(dialog.Choices.Count>=18,$"{locale}: common shapes are available from the visible preview button");
                Check(UiLayoutAudit.CheckText(dialog,"shape materials")>0,$"{locale}: material dialog captions fit");
                using(var clock=new UiAnimationClock(dialog.Indicator.Position,dialog.Indicator))
                {
                    clock.Pulse(0); clock.Pulse(200); var tick=200; var frame=0;
                    foreach(var index in new[]{3,5,6,0,4})
                    {
                        var before=dialog.Indicator.Position.Y; Click(dialog.Rows.Children[index]);
                        clock.Pulse(tick); Check(Math.Abs(dialog.Indicator.Position.Y-before)<.001,$"{locale}: selection retarget starts at its displayed position");
                        Check(_options.Shape==dialog.Choices[index].Kind && ReferenceEquals(dialog.Preview.Item,dialog.Choices[index]),$"{locale}: click updates the actual shape and large preview immediately");
                        for(var elapsed=0;elapsed<=192;elapsed+=16)
                        { clock.Pulse(tick+elapsed); if(locale=="zh-CN") Capture(dialog,$"material-motion-{frame++:000}"); }
                        tick+=208;
                    }
                }
                Capture(dialog,"shape-materials-"+locale); Click(dialog.CancelButton);
                Check(_options.Shape==shape && _options.CustomShape==custom,$"{locale}: Cancel restores the original shape tool settings");
            }
            var svg=Path.Combine(output,"import-shape.svg");
            File.WriteAllText(svg,"<svg xmlns=\"http://www.w3.org/2000/svg\"><path fill-rule=\"evenodd\" d=\"M0 0H20V20H0Z M5 5H15V15H5Z\"/></svg>");
            Click(_optionsBar.ShapeChoice.ButtonAt(3)); var shapes=OwnedWindows.OfType<MaterialPickerDialog<ShapePreset>>().Single();
            Wait(shapes.ImportFiles([svg])); Layout(shapes);
            Check(_options.Shape==ShapeKind.Custom && _options.CustomShape?.PathData is not null,"imported SVG updates the live shape settings before Use");
            Capture(shapes,"shape-imported-zh-CN"); Click(shapes.UseButton);
            var count=_document!.Layers.Count;
            var from=_canvas.TranslatePoint(_canvas.InView(new SKPoint(100,100)),this)!.Value;
            var to=_canvas.TranslatePoint(_canvas.InView(new SKPoint(260,260)),this)!.Value;
            this.MouseDown(from,MouseButton.Left,RawInputModifiers.LeftMouseButton); this.MouseMove(to,RawInputModifiers.LeftMouseButton); Layout();
            Check(_canvas.ShapeDraftPixels?.GetPixel(80,80).Alpha==0,"imported SVG holes remain transparent in the actual drag preview");
            this.MouseUp(to,MouseButton.Left,RawInputModifiers.None); Layout();
            Check(_document.Layers.Count==count+1 && _document.Layers.First(l=>l.ID==Selected).LiveShape?.PathData is not null,"drawing the SVG commits editable vector metadata");
            Undo(); Check(_document.Layers.Count==count,"SVG drawing remains one undo step");
            SetTool(Tool.Gradient); Layout(); var previous=_options.GradientPreset;
            foreach(var choice in _optionsBar.GetVisualDescendants().OfType<StableChoice>().Where(c=>c.IsVisible))
                Check(choice.Template is not null && choice.ItemTemplate is not null
                    && choice.GetVisualDescendants().OfType<TextBlock>().Any(t=>t.Text==UiText.Get(choice.SelectedItem?.ToString()??"") && t.Bounds.Width>0),
                    "derived tool choices inherit their theme and render localized selected text instead of an empty gap");
            Click(_optionsBar.GradientsButton); var gradients=OwnedWindows.OfType<MaterialPickerDialog<GradientPreset>>().Single();
            var ggr=Path.Combine(output,"import-gradient.ggr"); File.WriteAllText(ggr,"GIMP Gradient\nName: Imported\n1\n0 .3 1 1 0 0 1 0 0 1 1 0 0\n");
            Wait(gradients.ImportFiles([ggr])); Layout(gradients);
            Check(_options.GradientPreset?.Name=="Imported" && _optionsBar.GradientPreview.Item is GradientPreset {Name:"Imported"},"GGR selection updates the toolbar's actual gradient preview");
            Capture(gradients,"gradient-materials-zh-CN"); Click(gradients.CancelButton);
            Check(_options.GradientPreset==previous,"Cancel restores the previous gradient preset");
            Capture(this,"gradient-toolbar-zh-CN");
            var selected=_document.Layers.First(l=>l.ID==Selected); var asset=selected.Asset;
            var fill=ChoosePatternFill(); var patterns=OwnedWindows.OfType<MaterialPickerDialog<PatternPreset>>().Single();
            var tilePath=Path.Combine(output,"import-pattern.png");
            using(var tile=MaterialRendering.Render(MaterialPresets.Patterns[4],32,32))
            { using var data=tile.Encode(SKEncodedImageFormat.Png,100); File.WriteAllBytes(tilePath,data.ToArray()); }
            Wait(patterns.ImportFiles([tilePath])); Layout(patterns);
            Check(patterns.Preview.Item is PatternPreset {Name:"import-pattern"},"image pattern import selects and renders the actual tile immediately");
            Click(patterns.Rows.Children[3]); FlushPreviewForCheck();
            Check(ReferenceEquals(selected.Asset,asset) && _canvas.PreviewDocument is not null,"pattern selection previews on a separate document without changing the project");
            Capture(this,"pattern-canvas-preview-zh-CN"); Capture(patterns,"pattern-materials-zh-CN"); Click(patterns.CancelButton); Wait(fill);
            Check(ReferenceEquals(selected.Asset,asset) && _canvas.PreviewDocument is null,"Cancel discards the pattern preview without changing pixels");
            fill=ChoosePatternFill(); patterns=OwnedWindows.OfType<MaterialPickerDialog<PatternPreset>>().Single(); Click(patterns.Rows.Children[2]); Click(patterns.UseButton); Wait(fill);
            Check(!ReferenceEquals(selected.Asset,asset),"Use commits the selected pattern to the actual layer"); Undo();
            Check(ReferenceEquals(_document.Layers.First(l=>l.ID==Selected).Asset,asset),"one undo restores the original pixels after pattern fill");
            var colors=MaterialPickers.Colors(); colors.Show(this);
            var gpl=Path.Combine(output,"import-colors.gpl"); File.WriteAllText(gpl,"GIMP Palette\nName: Imported\nColumns: 2\n255 20 40 Warm red\n20 40 255 Cool blue\n");
            Wait(colors.ImportFiles([gpl])); Layout(colors);
            Check(colors.Preview.Item is ColorPreset {Name:"Cool blue"},"GPL import selects and renders the imported swatch immediately");
            Capture(colors,"color-materials-zh-CN"); Click(colors.UseButton);
            var picker = new ColorPickerDialog("Color",(.2,.4,.6)); picker.Show(this); Layout(picker);
            var colorBefore = picker.Colour; Click(picker.SwatchesButton);
            colors=picker.OwnedWindows.OfType<MaterialPickerDialog<ColorPreset>>().Single();
            Click(colors.Rows.Children[8]);
            Check(picker.Colour!=colorBefore,"choosing a swatch updates the actual color picker immediately");
            Click(colors.CancelButton); Check(picker.Colour==colorBefore,"canceling the swatch panel restores the color picker value"); picker.Close();
            SetTool(Tool.Brush); Layout(); var renders=_optionsBar.BrushPreview.RenderCount;
            for(var i=0;i<30;i++) { _options.Brush=_options.Brush with{Diameter=40+i}; OptionsChanged(); }
            Check(_optionsBar.BrushPreview.RenderCount==renders,"diameter changes reuse the brush-style thumbnail instead of rendering it again");
            var previewAt=_optionsBar.BrushPreview.TranslatePoint(new Point(_optionsBar.BrushPreview.Bounds.Width/2,_optionsBar.BrushPreview.Bounds.Height/2),_optionsBar.BrushesButton)!.Value;
            Check(Math.Abs(previewAt.X-_optionsBar.BrushesButton.Bounds.Width/2)<.5 && Math.Abs(previewAt.Y-_optionsBar.BrushesButton.Bounds.Height/2)<.5,"the arrow-free brush preview stays centered inside its unchanged button width");
            Click(_optionsBar.BrushesButton); var brushes=OwnedWindows.OfType<BrushLibraryDialog>().Single();
            var originalBrush=_options.Brush; Click(brushes.Rows.Children[4]);
            Check(_options.Brush.Tip is not null && brushes.Preview.Item is BrushStyle {Name:"Flat Brush"},"brush style selection updates both the tool and large actual stroke preview");
            Capture(brushes,"brush-materials-zh-CN"); brushes.Close(); Layout();
            Check(_options.Brush==originalBrush,"closing the brush chooser restores the original tip and settings");
            Capture(this,"main-materials-zh-CN");
        }
        finally
        {
            StopPreview(); _options.Brush=brush; _options.Shape=shape; _options.CustomShape=custom; _options.GradientPreset=gradient;
            Environment.SetEnvironmentVariable("COMPOSITOR_DATA_DIR",oldData); ChangeLanguage(language); SetTool(Tool.Brush); OptionsChanged();
        }
    }
}
