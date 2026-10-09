using Avalonia.Threading;
using Compositor.Core.Model;

namespace Compositor.Desktop;

public sealed partial class MainWindow
{
    private PreviewWorker? _previewWorker;
    private PreviewWorker.Frame? _previewFrame;
    private Task<PreviewWorker.Frame?>? _previewJob;
    private long _previewRevision;
    private bool _previewQueued;
    private bool _previewClosed;
    private Action? _previewDisplayed;

    private void StartPreviewJob()
    {
        if (_previewJob is not null || !_previewQueued || _preview is null || _previewApply is not { } apply || _previewClosed) return;
        _previewQueued = false;
        try { _previewWorker ??= new PreviewWorker(_preview.Document); }
        catch (Exception error)
        {
            Say(UiText.Format("Could not preview that edit: {0}", ErrorText(error)));
            return;
        }
        var worker = _previewWorker; var revision = _previewRevision; var displayed = _previewDisplayed;
        var job = worker.Run(apply); _previewJob = job;
        _ = job.ContinueWith(completed => Dispatcher.UIThread.Post(() =>
        {
            _previewJob = null;
            var failure = completed.Exception;
            var frame = completed.Status == TaskStatus.RanToCompletion ? completed.Result : null;
            if (!_previewClosed && revision == _previewRevision && ReferenceEquals(worker, _previewWorker)
                && _cameraRaw?.PreviewEnabled != false && frame is not null)
            {
                var previous = _previewFrame; var initial = previous is null ? _preview : null;
                _previewFrame = frame; _preview = frame.Preview;
                _canvas.PreviewDocument = frame.Preview.Document;
                previous?.Dispose(); initial?.Dispose();
                displayed?.Invoke();
                if (_cameraRaw is { } panel) panel.ShowScope(_cameraRawScope);
                _canvas.InvalidateVisual();
            }
            else frame?.Dispose();
            if (completed.IsFaulted && !_previewClosed && revision == _previewRevision)
                Say(UiText.Format("Could not preview that edit: {0}", ErrorText(failure!.GetBaseException())));
            if (_previewQueued && !_previewClosed) StartPreviewJob();
        }, DispatcherPriority.Background), TaskScheduler.Default);
    }

    private void DisposePreviewJobSession()
    {
        _previewRevision++; _previewQueued = false; _previewDisplayed = null;
        if (_previewFrame is { } frame) frame.Dispose(); else _preview?.Dispose();
        _previewFrame = null; _preview = null;
        _previewWorker?.Dispose(); _previewWorker = null;
    }

    // Diagnostics explicitly wait for workers, then pump their UI completion. Normal input never waits.
    private void FlushPreviewForCheck()
    {
        ShowPreviewOnce(null, EventArgs.Empty);
        var timer = System.Diagnostics.Stopwatch.StartNew();
        while (_previewJob is not null)
        {
            if (timer.Elapsed > TimeSpan.FromSeconds(30)) throw new TimeoutException("Preview worker did not finish.");
            Dispatcher.UIThread.RunJobs(); Thread.Sleep(1);
        }
        Dispatcher.UIThread.RunJobs();
    }
}
