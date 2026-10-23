namespace MP4Shrinker.Core;

public sealed record PathPlan(string SourceRoot, string OutputRoot)
{
    public const string Marker = ".mp4shrinker-output.json";
    public static bool IsWithin(string path, string root) =>
        string.Equals(Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar), Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar), StringComparison.OrdinalIgnoreCase)
        || Path.GetFullPath(path).StartsWith(Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);

    public static PathPlan Create(string source)
    {
        if (string.IsNullOrWhiteSpace(source)) throw new ArgumentException("Choose a source folder first.");
        string root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(source));
        if (!Directory.Exists(root)) throw new DirectoryNotFoundException("The source folder does not exist.");
        DirectoryInfo dir = new(root);
        if (dir.Parent is null) throw new ArgumentException("Choose a named folder, not a drive or share root.");
        AssertNoReparse(root);
        for (DirectoryInfo? p = dir; p != null; p = p.Parent)
            if (p.Name.Equals("smaller", StringComparison.OrdinalIgnoreCase) || File.Exists(Path.Combine(p.FullName, Marker)))
                throw new ArgumentException("Choose the original source, outside any generated 'smaller' folder.");
        string output = Path.Combine(dir.Parent.FullName, "smaller", dir.Name);
        if (IsWithin(output, root) || IsWithin(root, output)) throw new IOException("Source and output folders must be separate.");
        AssertNoReparse(output);
        return new(root, output);
    }

    public string Destination(string sourceFile)
    {
        if (!IsWithin(sourceFile, SourceRoot)) throw new IOException("The source file escaped the selected folder.");
        string relative = Path.GetRelativePath(SourceRoot, sourceFile);
        string result = Path.GetFullPath(Path.Combine(OutputRoot, relative));
        if (!IsWithin(result, OutputRoot) || IsWithin(result, SourceRoot)) throw new IOException("Unsafe output path.");
        return result;
    }

    public void AssertDestination(string path)
    {
        if (!IsWithin(path, OutputRoot) || IsWithin(path, SourceRoot)) throw new IOException("Unsafe destination.");
        AssertNoReparse(SourceRoot); AssertNoReparse(path);
    }

    // Check existing ancestors as well as the leaf. Junctions must never redirect a write into the source tree.
    public static void AssertNoReparse(string path)
    {
        for (string? p = Path.GetFullPath(path); p != null; p = Path.GetDirectoryName(p))
        {
            try
            {
                if ((File.GetAttributes(p) & FileAttributes.ReparsePoint) != 0)
                    throw new IOException($"Symbolic links and junctions are not supported: {p}");
            }
            catch (FileNotFoundException) { }
            catch (DirectoryNotFoundException) { }
        }
    }
}

public sealed class FileScanner(FfprobeService probe)
{
    public async Task<ScanResult> ScanAsync(string source, IProgress<string>? progress, CancellationToken ct)
    {
        PathPlan plan = PathPlan.Create(source);
        List<VideoJob> jobs = []; List<string> warnings = [];
        Stack<string> pending = new(); pending.Push(plan.SourceRoot);
        while (pending.TryPop(out var dir))
        {
            ct.ThrowIfCancellationRequested();
            try
            {
                PathPlan.AssertNoReparse(dir);
                if (dir != plan.SourceRoot && (Path.GetFileName(dir).Equals("smaller", StringComparison.OrdinalIgnoreCase) || File.Exists(Path.Combine(dir, PathPlan.Marker))))
                { warnings.Add($"Excluded generated/reserved folder: {dir}"); continue; }
                foreach (string entry in Directory.EnumerateFileSystemEntries(dir))
                {
                    ct.ThrowIfCancellationRequested();
                    try
                    {
                        var attrs = File.GetAttributes(entry);
                        if ((attrs & FileAttributes.ReparsePoint) != 0) { warnings.Add($"Skipped junction/link: {entry}"); continue; }
                        if ((attrs & FileAttributes.Directory) != 0) { pending.Push(entry); continue; }
                        if (!Path.GetExtension(entry).Equals(".mp4", StringComparison.OrdinalIgnoreCase)) continue;
                        FileInfo file = new(entry);
                        var job = new VideoJob { SourcePath = entry, RelativePath = Path.GetRelativePath(plan.SourceRoot, entry), DestinationPath = plan.Destination(entry), SourceBytes = file.Length, SourceModifiedUtc = file.LastWriteTimeUtc, Status = JobStatus.Probing };
                        jobs.Add(job); progress?.Report($"Inspecting {jobs.Count}: {job.RelativePath}");
                        try
                        {
                            job.Media = await probe.ProbeAsync(entry, ct);
                            CompressionArguments.CheckMedia(job.Media);
                            job.Status = JobStatus.Ready; job.Message = "Ready";
                        }
                        catch (OperationCanceledException) { throw; }
                        catch (Exception ex) { job.Status = JobStatus.Failed; job.Message = ex.Message; }
                    }
                    catch (OperationCanceledException) { throw; }
                    catch (Exception ex) { warnings.Add($"Cannot inspect {entry}: {ex.Message}"); }
                }
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex) { warnings.Add($"Cannot scan {dir}: {ex.Message}"); }
        }
        jobs.Sort((a,b) => StringComparer.OrdinalIgnoreCase.Compare(a.RelativePath,b.RelativePath));
        return new(plan, jobs, warnings);
    }
}
