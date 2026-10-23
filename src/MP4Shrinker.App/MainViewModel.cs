using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Data;
using Microsoft.Win32;
using MP4Shrinker.Core;

namespace MP4Shrinker.App;

public sealed class MainViewModel : Observable, IDisposable
{
    private readonly StateStore state=new();
    private readonly LoggingService log;
    private ToolPaths? tools;
    private CancellationTokenSource? cancellation, shutdownCancellation;
    private Task? activeOperation;
    private volatile bool pauseRequested;
    private string versions="Tools are being checked.";
    private ScanResult? scan;
    private readonly Dictionary<Guid,JobRow> rows=[];
    public ObservableCollection<JobRow> Jobs { get; }=[];
    public ICollectionView JobsView { get; }
    public string[] QualityChoices { get; }=["Maximum quality · 18","High quality · 20","Balanced · 22","Smaller files · 24","Custom"];
    public string[] EncoderChoices { get; }=["Best compression · CPU H.265","Fast · NVIDIA H.265","Fast · Intel H.265","Fast · AMD H.265"];
    public string[] SpeedChoices { get; }=["Fast · medium","Normal · slow","Maximum · slower","Custom"];
    public string[] AudioChoices { get; }=["AAC 160 kbps","AAC 192 kbps","Copy compatible"];
    public string[] ExistingChoices { get; }=["Skip verified outputs","Replace existing outputs","Stop batch if outputs exist"];
    public string[] Presets { get; }=["ultrafast","superfast","veryfast","faster","fast","medium","slow","slower","veryslow"];

    private string sourceFolder="", outputFolder="Choose a source folder", status="Ready", diskNote="", currentFile="Ready when you are", queueInfo="No files scanned", fileSizes="", speed="", crf="20", preset="slow";
    private bool busy=true, scanning, encoding, preserveDates=true, fastStart=true, keepLarger, cpuFallback=true, shutdown, failedOnly;
    private int qualityIndex=1, speedIndex=1, encoderIndex, audioIndex, existingIndex;
    private double currentPercent, overallPercent;
    private JobRow? selectedJob;
    public string SourceFolder { get=>sourceFolder; set { if(Set(ref sourceFolder,value)) { scan=null; UpdateOutput(); } } }
    public string OutputFolder { get=>outputFolder; private set=>Set(ref outputFolder,value); }
    public string Status { get=>status; private set=>Set(ref status,value); }
    public string DiskNote { get=>diskNote; private set=>Set(ref diskNote,value); }
    public string CurrentFile { get=>currentFile; private set=>Set(ref currentFile,value); }
    public string QueueInfo { get=>queueInfo; private set=>Set(ref queueInfo,value); }
    public string FileSizes { get=>fileSizes; private set=>Set(ref fileSizes,value); }
    public string Speed { get=>speed; private set=>Set(ref speed,value); }
    public string BatchTotals
    {
        get
        {
            long original=Jobs.Where(j=>j.Kept).Sum(j=>j.SourceBytes), output=Jobs.Where(j=>j.Kept).Sum(j=>j.OutputBytes);
            return $"Kept outputs: {Sizes.Format(original)} → {Sizes.Format(output)} · Total saved: {Sizes.Format(original-output)}"+(original>0?$" ({100d*(original-output)/original:0.0}%)":"");
        }
    }
    public double CurrentPercent { get=>currentPercent; private set=>Set(ref currentPercent,value); }
    public double OverallPercent { get=>overallPercent; private set=>Set(ref overallPercent,value); }
    public bool Busy { get=>busy; private set { if(Set(ref busy,value)) { Changed(nameof(Idle)); RefreshCommands(); } } }
    public bool Idle=>!Busy;
    public bool Scanning { get=>scanning; private set=>Set(ref scanning,value); }
    public bool PreserveDates { get=>preserveDates; set=>Set(ref preserveDates,value); }
    public bool FastStart { get=>fastStart; set=>Set(ref fastStart,value); }
    public bool KeepLarger { get=>keepLarger; set=>Set(ref keepLarger,value); }
    public bool CpuFallback { get=>cpuFallback; set=>Set(ref cpuFallback,value); }
    public bool Shutdown { get=>shutdown; set=>Set(ref shutdown,value); }
    public int EncoderIndex { get=>encoderIndex; set=>Set(ref encoderIndex,value); }
    public int AudioIndex { get=>audioIndex; set=>Set(ref audioIndex,value); }
    public int ExistingIndex { get=>existingIndex; set=>Set(ref existingIndex,value); }
    public string Crf { get=>crf; set { if(Set(ref crf,value)) { int i=Array.IndexOf(new[]{"18","20","22","24"},value); qualityIndex=i<0?4:i; Changed(nameof(QualityIndex)); } } }
    public string Preset { get=>preset; set { if(Set(ref preset,value)) { int i=Array.IndexOf(new[]{"medium","slow","slower"},value); speedIndex=i<0?3:i; Changed(nameof(SpeedIndex)); } } }
    public int QualityIndex { get=>qualityIndex; set { if(Set(ref qualityIndex,value) && value>=0 && value<4) Crf=new[]{"18","20","22","24"}[value]; } }
    public int SpeedIndex { get=>speedIndex; set { if(Set(ref speedIndex,value) && value>=0 && value<3) Preset=new[]{"medium","slow","slower"}[value]; } }
    public bool FailedOnly { get=>failedOnly; set { if(Set(ref failedOnly,value)) JobsView.Refresh(); } }
    public JobRow? SelectedJob { get=>selectedJob; set=>Set(ref selectedJob,value); }
    public string PauseText=>pauseRequested?"Resume":"Pause after file";

    public RelayCommand BrowseCommand { get; }
    public AsyncCommand StartCommand { get; }
    public AsyncCommand ScanCommand { get; }
    public RelayCommand CancelCommand { get; }
    public RelayCommand PauseCommand { get; }
    public RelayCommand LogsCommand { get; }
    public RelayCommand OutputCommand { get; }
    public RelayCommand AboutCommand { get; }
    public RelayCommand CancelShutdownCommand { get; }
    public MainViewModel()
    {
        log=new(state);
        JobsView=CollectionViewSource.GetDefaultView(Jobs); JobsView.Filter=o=>!FailedOnly || o is JobRow { Failed:true };
        BrowseCommand=new(Browse,()=>Idle);
        ScanCommand=new(()=>RunOperation(false),ShowError,()=>Idle && tools!=null);
        StartCommand=new(()=>RunOperation(true),ShowError,()=>Idle && tools!=null);
        CancelCommand=new(()=>{ cancellation?.Cancel(); shutdownCancellation?.Cancel(); Status="Cancelling safely…"; },()=>Busy && cancellation!=null);
        PauseCommand=new(()=>{ pauseRequested=!pauseRequested; Changed(nameof(PauseText)); Status=pauseRequested?"Pause requested. The current file will finish first.":"Resuming…"; },()=>encoding);
        LogsCommand=new(()=>OpenFolder(Path.Combine(state.Root,"logs")));
        OutputCommand=new(()=>OpenFolder(OutputFolder),()=>Directory.Exists(OutputFolder));
        AboutCommand=new(()=>MessageBox.Show("MP4 Shrinker 1.0.0\nNative Windows x64 · .NET 10\n\n"+versions+"\n\nNo uploads or tracking. All video work stays on your PC.\nHEVC playback needs a compatible player.\n\nLicences and source details are in the portable folder.","About MP4 Shrinker"));
        CancelShutdownCommand=new(()=>{ shutdownCancellation?.Cancel(); Status="Shutdown cancelled."; },()=>shutdownCancellation!=null);
        var settings=state.LoadSettings();
        SourceFolder=settings.SourceFolder; Crf=settings.Profile.Crf.ToString(); Preset=settings.Profile.Preset;
        EncoderIndex=(int)settings.Profile.Encoder; AudioIndex=(int)settings.Profile.Audio; ExistingIndex=(int)settings.Profile.Existing;
        FastStart=settings.Profile.FastStart; PreserveDates=settings.Profile.PreserveDates; KeepLarger=settings.Profile.KeepLarger; CpuFallback=settings.Profile.CpuFallback;
        // Shutdown must be deliberately armed for each launch, never inherited from an unattended previous run.
        Shutdown=false;
    }
    public Task InitializeAsync() { activeOperation=InitializeCore(); return activeOperation; }
    private async Task InitializeCore()
    {
        try
        {
            tools=ToolPaths.Locate(AppContext.BaseDirectory);
            using var timeout=new CancellationTokenSource(TimeSpan.FromSeconds(30)); var runner=new ProcessRunner();
            var ffmpeg=await runner.RunAsync(tools.Ffmpeg,["-version"],timeout.Token);
            var ffprobe=await runner.RunAsync(tools.Ffprobe,["-version"],timeout.Token);
            if(ffmpeg.ExitCode!=0 || ffprobe.ExitCode!=0) throw new IOException("The bundled tools could not start. Extract the ZIP again.");
            versions=ffmpeg.Output.Split('\n')[0]+"\n"+ffprobe.Output.Split('\n')[0];
            log.Write(ffmpeg.Output); log.Write(ffprobe.Output);
            Status="Choose a folder, then scan or start compression.";
            if(Directory.Exists(SourceFolder))
            {
                try { var plan=PathPlan.Create(SourceFolder); if(File.Exists(state.ActivePath(plan))) Status="An interrupted encode was found. Its registered temporary file will be safely cleaned at the next start."; }
                catch(Exception ex) { log.Write(ex.Message); }
            }
        }
        catch(Exception ex) { tools=null; ShowError(ex); }
        finally { Busy=false; }
    }
    private CompressionProfile GetProfile()
    {
        if(!int.TryParse(Crf,out int q)) throw new ArgumentException("Enter a whole-number CRF between 16 and 28.");
        var profile=new CompressionProfile { Crf=q,Preset=Preset,Encoder=(EncoderKind)EncoderIndex,Audio=(AudioMode)AudioIndex,Existing=(ExistingMode)ExistingIndex,FastStart=FastStart,PreserveDates=PreserveDates,KeepLarger=KeepLarger,CpuFallback=CpuFallback };
        profile.Validate(); return profile;
    }
    private Task RunOperation(bool start) { activeOperation=Operate(start); return activeOperation; }
    private async Task Operate(bool start)
    {
        shutdownCancellation?.Cancel();
        var profile=GetProfile();
        cancellation=new(); Busy=true; pauseRequested=false; Changed(nameof(PauseText));
        CurrentPercent=0; OverallPercent=0; FileSizes=""; Speed=""; FailedOnly=false;
        try
        {
            state.SaveSettings(new(){SourceFolder=SourceFolder,Profile=profile,Shutdown=Shutdown});
            var token=cancellation.Token;
            Scanning=true; CurrentFile="Scanning source files…";
            var scanProgress=new Progress<string>(s=>Status=s);
            scan=await Task.Run(()=>new FileScanner(new FfprobeService(tools!.Ffprobe,new())).ScanAsync(SourceFolder,scanProgress,token),token);
            Scanning=false; Jobs.Clear(); rows.Clear();
            foreach(var job in scan.Jobs) { JobRow row=new(job.Snapshot()); Jobs.Add(row); rows.Add(job.Id,row); }
            Changed(nameof(BatchTotals));
            QueueInfo=$"{scan.Jobs.Count} files · {Sizes.Format(scan.Jobs.Sum(j=>j.SourceBytes))} original";
            CurrentFile="Ready to compress";
            int failed=scan.Jobs.Count(j=>j.Status==JobStatus.Failed);
            Status=$"Scan complete. {scan.Jobs.Count-failed} ready, {failed} need attention.";
            if(scan.Warnings.Count>0) { Status+=$" {scan.Warnings.Count} scan warnings; see log."; foreach(var warning in scan.Warnings) log.Write(warning); }
            DiskNote=GetDiskNote(scan);
            if(!start || scan.Jobs.Count==0) return;
            encoding=true; RefreshCommands();
            var report=new Progress<BatchUpdate>(update=>
            {
                if(rows.TryGetValue(update.Job.Id,out var row)) row.Apply(update.Job);
                Changed(nameof(BatchTotals));
                CurrentFile=update.Job.RelativePath; CurrentPercent=update.Job.Fraction*100; OverallPercent=update.Overall*100;
                QueueInfo=$"File {update.Index} of {update.Count} · {Jobs.Count(j=>j.Status=="Completed")} compressed · {Jobs.Count(j=>j.Status=="Skipped")} skipped · {Jobs.Count(j=>j.Failed)} failed";
                FileSizes=$"Original: {Sizes.Format(update.Job.SourceBytes)}"+(update.Job.OutputBytes is {} n ? $"   New: {Sizes.Format(n)}   Difference: {Sizes.Format(update.Job.SourceBytes-n)}" : "");
                Speed=update.Job.Speed; Status=ShortStatus(update.Job.Message);
                if(FailedOnly) JobsView.Refresh();
            });
            async Task Pause(CancellationToken ct) { while(pauseRequested) await Task.Delay(200,ct); }
            var result=await Task.Run(()=>new BatchProcessor(tools!,state,log).RunAsync(scan,profile,report,Pause,token),token);
            Status=result.ToString(); CurrentFile=result.Cancelled?"Batch cancelled":"Batch finished";
            if(!result.Cancelled) { OverallPercent=100; CurrentPercent=100; }
            encoding=false; RefreshCommands();
            if(Shutdown && result.CanShutdown && scan.Warnings.Count==0) await CountdownShutdown();
            else if(Shutdown) Status+="\nShutdown was not triggered because the batch was cancelled, incomplete, or had errors.";
        }
        catch(OperationCanceledException) { Status="Cancelled. Completed outputs are kept; originals are untouched."; }
        catch(Exception ex) { ShowError(ex); }
        finally { encoding=false; Scanning=false; cancellation.Dispose(); cancellation=null; Busy=false; }
    }
    private string GetDiskNote(ScanResult result)
    {
        try
        {
            var drive=new DriveInfo(Path.GetPathRoot(result.Paths.OutputRoot)!);
            long free=drive.AvailableFreeSpace;
            long estimate=result.Jobs.Sum(j=>j.SourceBytes)+result.Jobs.Select(j=>j.SourceBytes).DefaultIfEmpty(0).Max();
            return $"Destination free space: {Sizes.Format(free)}."+(free<estimate?" Space may be low for the whole batch plus temporary output. Individual write failures leave originals safe.":"");
        }
        catch { return "Free space could not be determined. Ensure the destination has room for outputs and the current temporary file."; }
    }
    private async Task CountdownShutdown()
    {
        shutdownCancellation=new(); RefreshCommands();
        try
        {
            for(int seconds=60;seconds>0;seconds--)
            { Status=$"Batch completed successfully. Shutting down in {seconds} seconds. Use Cancel shutdown to keep working."; await Task.Delay(1000,shutdownCancellation.Token); }
            shutdownCancellation.Token.ThrowIfCancellationRequested();
            var start=new ProcessStartInfo(Path.Combine(Environment.SystemDirectory,"shutdown.exe")) {UseShellExecute=false,CreateNoWindow=true};
            start.ArgumentList.Add("/s"); start.ArgumentList.Add("/t"); start.ArgumentList.Add("0");
            using var process=Process.Start(start) ?? throw new IOException("Windows could not start shutdown.");
            await process.WaitForExitAsync(); if(process.ExitCode!=0) throw new IOException("Windows refused the shutdown request.");
        }
        catch(OperationCanceledException) { Status="Batch completed. Shutdown cancelled."; }
        finally { shutdownCancellation.Dispose(); shutdownCancellation=null; RefreshCommands(); }
    }
    private void Browse()
    {
        var dialog=new OpenFolderDialog {Title="Select the original video folder",Multiselect=false};
        if(Directory.Exists(SourceFolder)) dialog.InitialDirectory=SourceFolder;
        if(dialog.ShowDialog()==true) SourceFolder=dialog.FolderName;
    }
    private void UpdateOutput()
    {
        try { OutputFolder=PathPlan.Create(SourceFolder).OutputRoot; }
        catch(Exception ex) { OutputFolder=ex.Message; }
        OutputCommand?.Refresh();
    }
    private void OpenFolder(string path)
    {
        try { if(Directory.Exists(path)) Process.Start(new ProcessStartInfo(path){UseShellExecute=true}); }
        catch(Exception ex) { ShowError(ex); }
    }
    private void RefreshCommands()
    {
        BrowseCommand?.Refresh(); ScanCommand?.Refresh(); StartCommand?.Refresh(); CancelCommand?.Refresh(); PauseCommand?.Refresh(); OutputCommand?.Refresh(); CancelShutdownCommand?.Refresh();
    }
    private static string ShortStatus(string message)=>message.Length>450?message[..450]+"… Select the file or open Logs for full details.":message;
    public void ShowError(Exception ex) { Status=ShortStatus(ex.Message); log.Write(ex.ToString()); }
    public async Task CancelAndWaitAsync()
    {
        cancellation?.Cancel(); shutdownCancellation?.Cancel();
        if(activeOperation!=null) try { await activeOperation; } catch(Exception ex) { log.Write(ex.ToString()); }
    }
    public void Dispose()
    {
        try { state.SaveSettings(new(){SourceFolder=SourceFolder,Profile=GetProfile(),Shutdown=Shutdown}); } catch(Exception ex) { log.Write(ex.Message); }
        log.Dispose();
    }
}
