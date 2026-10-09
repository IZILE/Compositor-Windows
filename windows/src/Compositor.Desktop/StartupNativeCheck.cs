using System.Runtime.InteropServices;
using System.Text.Json;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Threading;

namespace Compositor.Desktop;

internal static class StartupNativeCheck
{
    internal static void Run(MainWindow window,string output,IClassicDesktopStyleApplicationLifetime desktop)
    {
        window.Opened += async (_,_) =>
        {
            var succeeded=false;
            try
            {
                await window.FirstFrame.Completed.Task.WaitAsync(TimeSpan.FromSeconds(6));
                await Dispatcher.UIThread.InvokeAsync(()=>
                {
                    var native=window.TryGetPlatformHandle(); var cloaked=0;
                    var query=native is not null ? DwmGetWindowAttribute(native.Handle,14,out cloaked,sizeof(int)) : -1;
                    succeeded=window.FirstFrame.WasCloaked && window.FirstFrame.FrameRendered && !window.FirstFrame.TimedOut
                        && !window.FirstFrame.IsCloaked && query==0 && (cloaked&1)==0;
                    Directory.CreateDirectory(output);
                    File.WriteAllText(Path.Combine(output,"startup-native.json"),JsonSerializer.Serialize(new {
                        succeeded, window.FirstFrame.WasCloaked,window.FirstFrame.FrameRendered,window.FirstFrame.TimedOut,
                        native_cloaked=cloaked,query_result=query,window.Width,window.Height,window.StartupPreparationCount,
                        limitation="Native lifecycle and render completion, not a physical display recording."
                    },new JsonSerializerOptions {WriteIndented=true}));
                });
            }
            catch(Exception error) { Directory.CreateDirectory(output);File.WriteAllText(Path.Combine(output,"error.txt"),error.ToString()); }
            finally { await Dispatcher.UIThread.InvokeAsync(()=>desktop.Shutdown(succeeded?0:1)); }
        };
    }
    [DllImport("dwmapi.dll")] private static extern int DwmGetWindowAttribute(IntPtr window,int attribute,out int value,int size);
}
