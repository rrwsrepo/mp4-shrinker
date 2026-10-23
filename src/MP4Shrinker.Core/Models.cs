using System.Globalization;
using System.Text.Json;

namespace MP4Shrinker.Core;

public enum JobStatus { Pending, Probing, Ready, Encoding, Validating, Completed, Skipped, NotSmaller, Failed, Cancelled }
public enum ExistingMode { SkipVerified, Overwrite, RequireClear }
public enum AudioMode { Aac160, Aac192, CopyCompatible }
public enum EncoderKind { Cpu, Nvidia, Intel, Amd }

public sealed record CompressionProfile
{
    public int Crf { get; init; } = 20;
    public string Preset { get; init; } = "slow";
    public EncoderKind Encoder { get; init; }
    public AudioMode Audio { get; init; }
    public bool FastStart { get; init; } = true;
    public bool KeepLarger { get; init; }
    public bool PreserveDates { get; init; } = true;
    public bool CpuFallback { get; init; } = true;
    public ExistingMode Existing { get; init; }
    [System.Text.Json.Serialization.JsonIgnore]
    public string Signature => JsonSerializer.Serialize(this with { Existing = ExistingMode.SkipVerified, PreserveDates = true });
    public void Validate()
    {
        if (Crf is < 16 or > 28) throw new ArgumentException("Quality must be between 16 and 28.");
        if (!new[] { "ultrafast", "superfast", "veryfast", "faster", "fast", "medium", "slow", "slower", "veryslow" }.Contains(Preset))
            throw new ArgumentException("Select a supported CPU preset.");
        if (!Enum.IsDefined(Encoder) || !Enum.IsDefined(Audio) || !Enum.IsDefined(Existing)) throw new ArgumentException("Invalid profile option.");
    }
}

public sealed record AppSettings
{
    public string SourceFolder { get; init; } = "";
    public CompressionProfile Profile { get; init; } = new();
    public bool Shutdown { get; init; }
}

public sealed record StreamInfo(int Index, string Type, string Codec, int Width, int Height, string PixelFormat,
    int BitDepth, double Rotation, string ColorTransfer, string ColorPrimaries, string ColorSpace, string ColorRange,
    string FrameRate, int Channels, bool AttachedPicture, bool HdrSideData);
public sealed record MediaInfo(double Duration, List<StreamInfo> Streams, int Chapters)
{
    public StreamInfo Video => Streams.First(s => s.Type == "video" && !s.AttachedPicture);
    public List<StreamInfo> Audio => Streams.Where(s => s.Type == "audio").ToList();
    public List<StreamInfo> Subtitles => Streams.Where(s => s.Type == "subtitle").ToList();
    public bool IsHdr => Video.ColorTransfer is "smpte2084" or "arib-std-b67" || Video.HdrSideData;
    public string Describe => $"{Video.Width}×{Video.Height} · {Video.Codec} · {Video.BitDepth}-bit · {Audio.Count} audio · {Subtitles.Count} subtitles";
}

public sealed class VideoJob
{
    public Guid Id { get; } = Guid.NewGuid();
    public required string SourcePath { get; init; }
    public required string RelativePath { get; init; }
    public required string DestinationPath { get; init; }
    public long SourceBytes { get; set; }
    public DateTime SourceModifiedUtc { get; set; }
    public MediaInfo? Media { get; set; }
    public JobStatus Status { get; set; }
    public string Message { get; set; } = "Waiting";
    public long? OutputBytes { get; set; }
    public double Fraction { get; set; }
    public string Speed { get; set; } = "";
    public double Weight => Math.Max(1, Media?.Duration ?? 1);
    public JobUpdate Snapshot() => new(Id, RelativePath, SourceBytes, OutputBytes, Status, Message, Fraction, Speed, Media?.Describe ?? "");
}
public sealed record JobUpdate(Guid Id, string RelativePath, long SourceBytes, long? OutputBytes, JobStatus Status,
    string Message, double Fraction, string Speed, string Media);
public sealed record BatchUpdate(JobUpdate Job, double Overall, int Index, int Count);
public sealed record ScanResult(PathPlan Paths, List<VideoJob> Jobs, List<string> Warnings);
public sealed record BatchSummary(int Completed, int Skipped, int NotSmaller, int Failed, bool Cancelled,
    long OriginalBytes, long OutputBytes)
{
    public long SavedBytes => OriginalBytes - OutputBytes;
    public bool CanShutdown => !Cancelled && Failed == 0 && Completed + Skipped + NotSmaller > 0;
    public override string ToString() => $"{Completed} compressed · {Skipped} verified/skipped · {NotSmaller} not smaller · {Failed} failed" +
        (Cancelled ? " · Cancelled" : "") + $"\nKept/verified outputs: {Sizes.Format(OriginalBytes)} → {Sizes.Format(OutputBytes)}. Saved {Sizes.Format(SavedBytes)}.";
}
public static class Sizes
{
    public static string Format(long bytes)
    {
        double n = bytes; string[] u = ["B", "KB", "MB", "GB", "TB"];
        int i = 0; while (Math.Abs(n) >= 1024 && i < u.Length - 1) { n /= 1024; i++; }
        return $"{n.ToString(i == 0 ? "0" : "0.##", CultureInfo.CurrentCulture)} {u[i]}";
    }
}
