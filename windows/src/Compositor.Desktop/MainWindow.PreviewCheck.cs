using Avalonia.Threading;
using Compositor.Core.Document;

namespace Compositor.Desktop;

public sealed partial class MainWindow
{
    private void PreviewWorkerSelfCheck(List<string> report)
    {
        if (_document is not { } document || document.Layers.FirstOrDefault(layer => layer.Asset is not null) is not { } layer) return;
        void Check(bool value, string name)
        {
            if (!value) throw new InvalidOperationException("PREVIEW WORKER FAILED: " + name);
            report.Add("PASS: " + name);
        }
        using var entered = new ManualResetEventSlim(); using var release = new ManualResetEventSlim();
        var originalOpacity = layer.Opacity; var originalPixels = layer.Asset;
        try
        {
            StartPreview(document, layer.ID);
            RequestPreview(current => { entered.Set(); if (!release.Wait(5000)) throw new TimeoutException(); return LayerEdits.SetOpacity(current, layer.ID, 0.12); });
            ShowPreviewOnce(null, EventArgs.Empty);
            Check(entered.Wait(5000), "filter preview starts on its worker");
            var handled = false; Dispatcher.UIThread.Post(() => handled = true); Dispatcher.UIThread.RunJobs();
            Check(handled && _previewJob is { IsCompleted: false }, "the UI dispatcher keeps handling input work while a preview computes");
            RequestPreview(current => LayerEdits.SetOpacity(current, layer.ID, 0.37));
            release.Set(); FlushPreviewForCheck();
            Check(Math.Abs(_canvas.PreviewDocument!.Layers.Single(item => item.ID == layer.ID).Opacity - 0.37) < 0.0001,
                "queued previews display the latest amount and discard obsolete results");
            Check(layer.Opacity == originalOpacity && ReferenceEquals(layer.Asset, originalPixels), "background previews leave original pixels and history untouched");
            StopPreview(); entered.Reset(); release.Reset();
            StartPreview(document, layer.ID);
            RequestPreview(current => { entered.Set(); if (!release.Wait(5000)) throw new TimeoutException(); return LayerEdits.SetOpacity(current, layer.ID, 0.22); });
            ShowPreviewOnce(null, EventArgs.Empty); Check(entered.Wait(5000), "a subsequent preview session starts normally");
            StopPreview(); release.Set(); FlushPreviewForCheck();
            Check(_canvas.PreviewDocument is null && _preview is null, "closing a preview rejects its late worker result without flashing it back");
        }
        finally { release.Set(); StopPreview(); FlushPreviewForCheck(); }
    }
}
