namespace MP4Shrinker.Core;

public sealed class BatchProcessor(ToolPaths tools, StateStore state, LoggingService log)
{
    private readonly ProcessRunner runner = new();
    public async Task<BatchSummary> RunAsync(ScanResult scan, CompressionProfile profile,
        IProgress<BatchUpdate>? progress, Func<CancellationToken,Task>? pauseBetweenFiles, CancellationToken ct)
    {
        profile.Validate();
        var plan = PathPlan.Create(scan.Paths.SourceRoot);
        if (plan != scan.Paths) throw new IOException("Folder paths changed; please rescan.");
        plan.AssertDestination(plan.OutputRoot); Directory.CreateDirectory(plan.OutputRoot);
        string lockPath=Path.Combine(plan.OutputRoot,".mp4shrinker.lock"); plan.AssertDestination(lockPath);
        using var folderLock=new FileStream(lockPath,FileMode.OpenOrCreate,FileAccess.ReadWrite,FileShare.None);
        string marker = Path.Combine(plan.OutputRoot,PathPlan.Marker); plan.AssertDestination(marker);
        var oldMarker=state.Read<ActiveRecord>(marker);
        if(File.Exists(marker) && (oldMarker == null || !string.Equals(oldMarker.SourceRoot,plan.SourceRoot,StringComparison.OrdinalIgnoreCase)))
            throw new IOException("This output folder belongs to another source or has an invalid marker.");
        state.Write(marker,new ActiveRecord(plan.SourceRoot,plan.OutputRoot,""));
        int cleaned=state.CleanStale(plan); if(cleaned>0) log.Write($"Removed {cleaned} registered stale temporary output.");
        if (profile.Existing == ExistingMode.RequireClear && scan.Jobs.Any(j => File.Exists(j.DestinationPath)))
            throw new IOException("Output conflicts found. Choose 'Skip verified outputs' or 'Replace existing outputs' before starting.");
        log.Write($"Source: {plan.SourceRoot}\nOutput: {plan.OutputRoot}\nProfile: {profile.Signature}");
        var probe=new FfprobeService(tools.Ffprobe,runner);
        var detector=new EncoderDetector(tools.Ffmpeg,runner);
        CompressionProfile active=profile;
        try { await detector.VerifyAsync(active,ct); }
        catch(Exception ex) when (ex is not OperationCanceledException && profile.Encoder != EncoderKind.Cpu && profile.CpuFallback)
        {
            log.Write("Hardware unavailable; CPU fallback: "+ex.Message);
            active=profile with { Encoder=EncoderKind.Cpu }; await detector.VerifyAsync(active,ct);
        }
        double total=scan.Jobs.Sum(j=>j.Weight), finished=0; bool cancelled=false;
        int index=0;
        foreach(var job in scan.Jobs)
        {
            if(ct.IsCancellationRequested) { cancelled=true; break; }
            try { if(pauseBetweenFiles != null) await pauseBetweenFiles(ct); }
            catch(OperationCanceledException) { cancelled=true; break; }
            index++;
            void Report() => progress?.Report(new(job.Snapshot(),total>0 ? Math.Clamp((finished+job.Weight*job.Fraction)/total,0,1) : 0,index,scan.Jobs.Count));
            if(job.Media == null || job.Status == JobStatus.Failed)
            { job.Fraction=1; Report(); log.Write($"Skipped unsafe/unreadable source: {job.SourcePath}: {job.Message}"); finished+=job.Weight; continue; }
            string? temp=null; bool promoted=false;
            try
            {
                ct.ThrowIfCancellationRequested();
                PathPlan.AssertNoReparse(job.SourcePath); plan.AssertDestination(job.DestinationPath);
                FileInfo source=new(job.SourcePath);
                if(!source.Exists || source.Length!=job.SourceBytes || source.LastWriteTimeUtc!=job.SourceModifiedUtc)
                    throw new IOException("Source changed since scanning. Rescan before processing it.");
                // On Windows, a read-only handle without write/delete sharing protects the source during work.
                using var sourceGuard=new FileStream(job.SourcePath,FileMode.Open,FileAccess.Read,FileShare.Read);
                job.Fraction=0; job.OutputBytes=null;
                if(File.Exists(job.DestinationPath) && profile.Existing != ExistingMode.Overwrite)
                {
                    var record=state.History(job);
                    if(record == null || !record.Matches(job,profile)) throw new IOException("Existing output is unverified, or source/settings changed. Choose 'Replace existing outputs' to reprocess it.");
                    job.Status=JobStatus.Validating; job.Message="Checking existing output"; Report();
                    OutputValidator.Validate(job.Media,await probe.ProbeAsync(job.DestinationPath,ct));
                    job.OutputBytes=new FileInfo(job.DestinationPath).Length; job.Status=JobStatus.Skipped; job.Fraction=1; job.Message="Verified existing output"; Report();
                    log.Write($"Verified existing output: {job.DestinationPath}"); finished+=job.Weight; continue;
                }
                Directory.CreateDirectory(Path.GetDirectoryName(job.DestinationPath)!); plan.AssertDestination(job.DestinationPath);
                temp=job.DestinationPath+".__mp4shrinker_"+Guid.NewGuid().ToString("N")+"__.mp4";
                plan.AssertDestination(temp); state.RegisterTemp(plan,temp);
                job.Status=JobStatus.Encoding; job.Message=$"Encoding with {CompressionArguments.EncoderName(active.Encoder)}"; Report();
                async Task Encode(CompressionProfile selected)
                {
                    var parser=new ProgressParser(job.Media.Duration);
                    var args=CompressionArguments.Build(job.SourcePath,temp,job.Media,selected);
                    log.Write($"Encoding {job.SourcePath} ({job.SourceBytes} bytes) -> {job.DestinationPath}; encoder={CompressionArguments.EncoderName(selected.Encoder)}");
                    var result=await runner.RunAsync(tools.Ffmpeg,args,ct,line=>
                    {
                        var p=parser.Feed(line); if(p==null) return;
                        job.Fraction=p.Fraction*.98; job.OutputBytes=p.Bytes; job.Speed=p.Speed; Report();
                    },log.Write);
                    log.Write($"FFmpeg exit code {result.ExitCode}");
                    if(result.ExitCode != 0) throw new IOException($"FFmpeg exited with {result.ExitCode}: {result.Error.Trim()}");
                }
                var actual=active;
                try { await Encode(actual); }
                catch(Exception ex) when (ex is not OperationCanceledException && actual.Encoder!=EncoderKind.Cpu && profile.CpuFallback)
                {
                    log.Write("Hardware encode failed. Retrying on CPU: "+ex.Message);
                    if(File.Exists(temp)) File.Delete(temp);
                    actual=profile with {Encoder=EncoderKind.Cpu}; job.Fraction=0; job.Message="Hardware failed; retrying on CPU"; Report();
                    await Encode(actual);
                }
                ct.ThrowIfCancellationRequested();
                job.Status=JobStatus.Validating; job.Message="Validating duration, streams and orientation"; Report();
                if(!File.Exists(temp) || new FileInfo(temp).Length<=0) throw new IOException("The encoder did not produce a non-empty output.");
                OutputValidator.Validate(job.Media,await probe.ProbeAsync(temp,ct));
                source.Refresh(); if(source.Length!=job.SourceBytes || source.LastWriteTimeUtc!=job.SourceModifiedUtc) throw new IOException("Source changed during encoding; output was discarded.");
                long bytes=new FileInfo(temp).Length; job.OutputBytes=bytes;
                if(!OutputValidator.ShouldKeep(job.SourceBytes,bytes,profile.KeepLarger))
                { job.Status=JobStatus.NotSmaller; job.Message="Not smaller. No new output kept; any previous output is unchanged."; job.OutputBytes=null; }
                else
                {
                    ct.ThrowIfCancellationRequested(); plan.AssertDestination(job.DestinationPath);
                    // Same-directory move is atomic. Replace only the directory entry, never write into an existing file/hardlink.
                    File.Move(temp,job.DestinationPath,profile.Existing==ExistingMode.Overwrite); promoted=true;
                    string warning="";
                    if(profile.PreserveDates)
                    {
                        try { File.SetCreationTimeUtc(job.DestinationPath,source.CreationTimeUtc); File.SetLastWriteTimeUtc(job.DestinationPath,source.LastWriteTimeUtc); }
                        catch(Exception ex) when(ex is IOException or UnauthorizedAccessException) { warning=" File dates could not be preserved."; log.Write(ex.ToString()); }
                    }
                    try { state.SaveHistory(job,profile,CompressionArguments.EncoderName(actual.Encoder)); }
                    catch(Exception ex) { warning+=" Resume record could not be saved; output remains valid."; log.Write(ex.ToString()); }
                    job.Status=JobStatus.Completed;
                    job.Message=$"Saved {Sizes.Format(job.SourceBytes-bytes)} ({100d*(job.SourceBytes-bytes)/job.SourceBytes:0.0}%)."+warning;
                    log.Write($"Completed: {job.DestinationPath}; {job.SourceBytes} -> {bytes} bytes. {job.Message}");
                }
                job.Fraction=1; Report();
            }
            catch(OperationCanceledException)
            { job.Status=JobStatus.Cancelled; job.Message="Cancelled; original untouched"; job.OutputBytes=null; Report(); cancelled=true; log.Write($"Cancelled: {job.SourcePath}"); }
            catch(Exception ex)
            { job.Status=JobStatus.Failed; job.Message=ex.Message; job.OutputBytes=null; job.Fraction=1; Report(); log.Write(ex.ToString()); }
            finally
            {
                if(temp!=null)
                {
                    try { plan.AssertDestination(temp); if(!promoted && File.Exists(temp)) File.Delete(temp); state.ClearTemp(plan); }
                    catch(Exception ex) { log.Write("Temporary cleanup deferred to next run: "+ex.Message); }
                }
            }
            finished+=job.Weight;
            if(cancelled) break;
        }
        var kept=scan.Jobs.Where(j=>j.Status is JobStatus.Completed or JobStatus.Skipped).ToList();
        var summary=new BatchSummary(scan.Jobs.Count(j=>j.Status==JobStatus.Completed),scan.Jobs.Count(j=>j.Status==JobStatus.Skipped),scan.Jobs.Count(j=>j.Status==JobStatus.NotSmaller),scan.Jobs.Count(j=>j.Status==JobStatus.Failed),cancelled,kept.Sum(j=>j.SourceBytes),kept.Sum(j=>j.OutputBytes??0));
        log.Write("Finished: "+summary); return summary;
    }
}
