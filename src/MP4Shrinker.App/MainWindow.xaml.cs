using System.ComponentModel;
using System.IO;
using System.Windows;

namespace MP4Shrinker.App;
public partial class MainWindow : Window
{
    private readonly MainViewModel model;
    private bool closeReady, closing;
    public MainWindow() { InitializeComponent(); model=new(); DataContext=model; }
    private async void OnLoaded(object sender,RoutedEventArgs e) => await model.InitializeAsync();
    private void OnDragOver(object sender,DragEventArgs e) { e.Effects=!model.Busy && e.Data.GetDataPresent(DataFormats.FileDrop)?DragDropEffects.Copy:DragDropEffects.None; e.Handled=true; }
    private void OnDrop(object sender,DragEventArgs e)
    {
        if(model.Busy) return;
        if(e.Data.GetData(DataFormats.FileDrop) is string[] {Length:1} paths && Directory.Exists(paths[0])) model.SourceFolder=paths[0];
    }
    private async void OnClosing(object? sender,CancelEventArgs e)
    {
        if(closeReady) return;
        e.Cancel=true; if(closing) return; closing=true;
        await model.CancelAndWaitAsync(); model.Dispose(); closeReady=true; Close();
    }
}
