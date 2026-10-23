using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Input;

namespace MP4Shrinker.App;
public abstract class Observable : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;
    protected void Changed([CallerMemberName]string? name=null) => PropertyChanged?.Invoke(this,new(name));
    protected bool Set<T>(ref T field,T value,[CallerMemberName]string? name=null)
    { if(EqualityComparer<T>.Default.Equals(field,value)) return false; field=value; Changed(name); return true; }
}
public sealed class RelayCommand(Action action,Func<bool>? can=null) : ICommand
{
    public bool CanExecute(object? parameter)=>can?.Invoke()??true;
    public void Execute(object? parameter)=>action();
    public event EventHandler? CanExecuteChanged;
    public void Refresh()=>CanExecuteChanged?.Invoke(this,EventArgs.Empty);
}
public sealed class AsyncCommand(Func<Task> action,Action<Exception> onError,Func<bool>? can=null) : ICommand
{
    private bool running;
    public bool CanExecute(object? parameter)=>!running && (can?.Invoke()??true);
    public async void Execute(object? parameter)
    {
        if(!CanExecute(parameter)) return;
        running=true; Refresh();
        try { await action(); } catch(Exception ex) { onError(ex); }
        finally { running=false; Refresh(); }
    }
    public event EventHandler? CanExecuteChanged;
    public void Refresh()=>CanExecuteChanged?.Invoke(this,EventArgs.Empty);
}
