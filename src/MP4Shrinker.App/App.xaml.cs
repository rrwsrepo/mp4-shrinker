using System.Windows;

namespace MP4Shrinker.App;
public partial class App : Application
{
    private Mutex? instance;
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        instance=new Mutex(true,"Local\\MP4Shrinker-6C71C6E8-1D66-4DAF-A4ED-9050D780A942",out bool created);
        if(!created) { MessageBox.Show("MP4 Shrinker is already open. Use the existing window.","MP4 Shrinker"); Shutdown(); return; }
        try { new MainWindow().Show(); }
        catch(Exception ex) { MessageBox.Show("MP4 Shrinker could not start:\n"+ex.Message,"MP4 Shrinker"); Shutdown(1); }
    }
    protected override void OnExit(ExitEventArgs e) { instance?.Dispose(); base.OnExit(e); }
}
