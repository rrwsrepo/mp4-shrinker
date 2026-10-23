using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace MP4Shrinker.Core;

public sealed class StateStore
{
    public string Root { get; }
    public StateStore(string? root = null)
    {
        Root = root ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "MP4 Shrinker");
        Directory.CreateDirectory(Root); Directory.CreateDirectory(Path.Combine(Root,"history")); Directory.CreateDirectory(Path.Combine(Root,"logs"));
    }
    public static string Key(string text) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text.ToUpperInvariant())));
    public T? Read<T>(string file)
    {
        try { return JsonSerializer.Deserialize<T>(File.ReadAllText(file)); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException or NotSupportedException) { return default; }
    }
    public void Write<T>(string path, T value)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        string temp = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            using (FileStream file = new(temp,FileMode.CreateNew,FileAccess.Write,FileShare.None))
            { JsonSerializer.Serialize(file,value,new JsonSerializerOptions { WriteIndented = true }); file.Flush(true); }
            File.Move(temp,path,true);
        }
        finally { if (File.Exists(temp)) File.Delete(temp); }
    }
    public AppSettings LoadSettings()
    {
        var s = Read<AppSettings>(Path.Combine(Root,"settings.json")) ?? new();
        try { s.Profile.Validate(); return s; } catch { return new(); }
    }
    public void SaveSettings(AppSettings settings) => Write(Path.Combine(Root,"settings.json"),settings);
    public string HistoryPath(VideoJob job) => Path.Combine(Root,"history",Key(job.DestinationPath)+".json");
    public CompletionRecord? History(VideoJob job) => Read<CompletionRecord>(HistoryPath(job));
    public void SaveHistory(VideoJob job, CompressionProfile requested, string actualEncoder)
    {
        var dest = new FileInfo(job.DestinationPath);
        Write(HistoryPath(job), new CompletionRecord(job.SourcePath,job.SourceBytes,job.SourceModifiedUtc,requested.Signature,job.DestinationPath,dest.Length,dest.LastWriteTimeUtc,actualEncoder));
    }
    public string ActivePath(PathPlan plan) => Path.Combine(Root,"history","active-"+Key(plan.OutputRoot)+".json");
    public void RegisterTemp(PathPlan plan, string temp) => Write(ActivePath(plan),new ActiveRecord(plan.SourceRoot,plan.OutputRoot,temp));
    public void ClearTemp(PathPlan plan) { string p=ActivePath(plan); if (File.Exists(p)) File.Delete(p); }
    public int CleanStale(PathPlan plan)
    {
        var record = Read<ActiveRecord>(ActivePath(plan)); if (record == null) return 0;
        if (!string.Equals(record.SourceRoot,plan.SourceRoot,StringComparison.OrdinalIgnoreCase) || !string.Equals(record.OutputRoot,plan.OutputRoot,StringComparison.OrdinalIgnoreCase)) throw new IOException("Recovery record does not match this folder.");
        if (!Regex.IsMatch(Path.GetFileName(record.TemporaryPath), @"\.__mp4shrinker_[0-9a-f]{32}__\.mp4$")) throw new IOException("Recovery file name was not recognised.");
        plan.AssertDestination(record.TemporaryPath);
        int count=0; if(File.Exists(record.TemporaryPath)) { File.Delete(record.TemporaryPath); count=1; }
        ClearTemp(plan); return count;
    }
}
public sealed record ActiveRecord(string SourceRoot,string OutputRoot,string TemporaryPath);
public sealed record CompletionRecord(string SourcePath,long SourceBytes,DateTime SourceModifiedUtc,string Profile,string DestinationPath,long OutputBytes,DateTime OutputModifiedUtc,string Encoder)
{
    public bool Matches(VideoJob job, CompressionProfile profile)
    {
        var output = new FileInfo(job.DestinationPath);
        return output.Exists && output.Length > 0 && output.Length == OutputBytes && output.LastWriteTimeUtc == OutputModifiedUtc
            && SourceBytes == job.SourceBytes && SourceModifiedUtc == job.SourceModifiedUtc && Profile == profile.Signature
            && string.Equals(SourcePath,job.SourcePath,StringComparison.OrdinalIgnoreCase) && string.Equals(DestinationPath,job.DestinationPath,StringComparison.OrdinalIgnoreCase);
    }
}
public sealed class LoggingService : IDisposable
{
    private readonly StreamWriter writer; private readonly object gate = new();
    public string FilePath { get; }
    public LoggingService(StateStore store)
    {
        FilePath=Path.Combine(store.Root,"logs",$"{DateTime.Now:yyyyMMdd-HHmmss}-{Guid.NewGuid():N}.log");
        writer=new(FilePath,false,Encoding.UTF8) { AutoFlush=true };
        Write("MP4 Shrinker 1.0.0 started");
    }
    public void Write(string line)
    {
        // A full log drive must not stop pipe draining and deadlock the encoder.
        lock(gate) try { writer.WriteLine($"{DateTimeOffset.Now:O} {line}"); }
        catch(IOException) { } catch(UnauthorizedAccessException) { }
    }
    public void Dispose() { lock(gate) writer.Dispose(); }
}
