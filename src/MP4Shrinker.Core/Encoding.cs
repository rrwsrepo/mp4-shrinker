using System.Globalization;

namespace MP4Shrinker.Core;

public static class CompressionArguments
{
    public static string EncoderName(EncoderKind kind) => kind switch { EncoderKind.Nvidia => "hevc_nvenc", EncoderKind.Intel => "hevc_qsv", EncoderKind.Amd => "hevc_amf", _ => "libx265" };
    public static void CheckMedia(MediaInfo media)
    {
        if (media.Duration <= 0 || !double.IsFinite(media.Duration)) throw new IOException("Video duration is unknown. This file needs manual inspection.");
        if (media.IsHdr) throw new IOException("HDR/Dolby Vision is protected: this version skips it to avoid losing HDR metadata or changing colours.");
        if (media.Streams.Count(s => s.Type == "video") != 1) throw new IOException("Additional video/cover-art streams require manual handling; nothing will be discarded.");
        if (media.Subtitles.Any(s => s.Codec != "mov_text")) throw new IOException("Unsupported subtitle format. This file is skipped so its subtitles are not lost.");
        if (media.Streams.Any(s => s.Type is not ("video" or "audio" or "subtitle") && !(s.Type == "data" && s.Codec == "bin_data" && media.Chapters > 0)))
            throw new IOException("Extra data/timecode streams require manual handling; this file will not be re-encoded.");
        var v = media.Video;
        if (v.Width <= 0 || v.Height <= 0 || (v.Width % 2 != 0) || (v.Height % 2 != 0)) throw new IOException("Odd or invalid video dimensions are protected; no automatic resize will be applied.");
        if (v.BitDepth > 10 || !new[] { "yuv420p", "yuvj420p", "yuv420p10le", "nv12", "p010le" }.Contains(v.PixelFormat))
            throw new IOException($"Unsupported pixel format {v.PixelFormat}. This file is protected from a lossy colour/bit-depth conversion.");
    }

    public static List<string> VideoOptions(CompressionProfile profile, bool tenBit)
    {
        List<string> a = ["-c:v", EncoderName(profile.Encoder)];
        string q = profile.Crf.ToString(CultureInfo.InvariantCulture);
        switch(profile.Encoder)
        {
            case EncoderKind.Cpu:
                a.AddRange(["-preset", profile.Preset, "-crf", q, "-pix_fmt", tenBit ? "yuv420p10le" : "yuv420p", "-x265-params", "pools=4:frame-threads=2"]); break;
            case EncoderKind.Nvidia:
                a.AddRange(["-preset", "p6", "-tune", "hq", "-rc", "vbr", "-cq", (profile.Crf + 2).ToString(CultureInfo.InvariantCulture), "-b:v", "0", "-pix_fmt", tenBit ? "p010le" : "yuv420p"]); break;
            case EncoderKind.Intel:
                a.AddRange(["-preset", "slow", "-global_quality", (profile.Crf + 2).ToString(CultureInfo.InvariantCulture), "-pix_fmt", tenBit ? "p010le" : "nv12"]); break;
            case EncoderKind.Amd:
                a.AddRange(["-quality", "quality", "-rc", "cqp", "-qp_i", q, "-qp_p", (profile.Crf + 2).ToString(CultureInfo.InvariantCulture), "-pix_fmt", tenBit ? "p010le" : "nv12"]); break;
        }
        return a;
    }

    public static List<string> Build(string input, string temp, MediaInfo media, CompressionProfile profile)
    {
        profile.Validate(); CheckMedia(media);
        // Keep the original pixel orientation and display matrix. No scaling or frame-rate change.
        List<string> a = ["-hide_banner", "-nostdin", "-n", "-xerror", "-loglevel", "warning", "-stats_period", "0.5", "-protocol_whitelist", "file,pipe", "-noautorotate", "-i", input,
            "-map", $"0:{media.Video.Index}", "-map", "0:a?", "-map", "0:s?", "-map_metadata", "0", "-map_chapters", "0"];
        a.AddRange(VideoOptions(profile, media.Video.BitDepth > 8));
        a.AddRange(["-tag:v", "hvc1", "-fps_mode", "passthrough"]);
        var v = media.Video;
        foreach (var (key,value) in new[] { ("-color_trc",v.ColorTransfer), ("-color_primaries",v.ColorPrimaries), ("-colorspace",v.ColorSpace), ("-color_range",v.ColorRange) })
            if (!string.IsNullOrEmpty(value) && value is not ("unknown" or "reserved")) a.AddRange([key,value]);
        for(int i=0;i<media.Audio.Count;i++)
        {
            var audio = media.Audio[i];
            if (profile.Audio == AudioMode.CopyCompatible)
            {
                if (!new[] { "aac", "mp3", "ac3", "eac3", "alac" }.Contains(audio.Codec))
                    throw new IOException($"Audio track {i+1} ({audio.Codec}) cannot safely be copied to this MP4 profile. Choose AAC.");
                a.AddRange([$"-c:a:{i}", "copy"]);
            }
            else a.AddRange([$"-c:a:{i}", "aac", $"-b:a:{i}", profile.Audio == AudioMode.Aac192 ? "192k" : "160k"]);
        }
        if (media.Subtitles.Count > 0) a.AddRange(["-c:s", "copy"]);
        if (profile.FastStart) a.AddRange(["-movflags", "+faststart"]);
        a.AddRange(["-max_muxing_queue_size", "4096", "-progress", "pipe:1", "-nostats", "-f", "mp4", temp]);
        return a;
    }
}

public static class OutputValidator
{
    public static void Validate(MediaInfo source, MediaInfo output)
    {
        double tolerance = Math.Max(0.75, Math.Min(3, source.Duration * .005));
        if (output.Duration <= 0 || Math.Abs(source.Duration-output.Duration) > tolerance)
            throw new IOException($"Duration mismatch: source {source.Duration:0.00}s, output {output.Duration:0.00}s.");
        if (output.Audio.Count != source.Audio.Count) throw new IOException("Audio track count changed.");
        for (int i=0;i<source.Audio.Count;i++) if (source.Audio[i].Channels != output.Audio[i].Channels) throw new IOException($"Audio track {i+1} channel count changed.");
        if (output.Subtitles.Count != source.Subtitles.Count) throw new IOException("Subtitle count changed.");
        if (output.Chapters != source.Chapters) throw new IOException("Chapter count changed.");
        if (output.Video.Codec != "hevc") throw new IOException("Output is not HEVC video.");
        if (output.Video.Width != source.Video.Width || output.Video.Height != source.Video.Height) throw new IOException("Video dimensions changed unexpectedly.");
        double angle = ((source.Video.Rotation-output.Video.Rotation)%360+360)%360;
        if (Math.Min(angle,360-angle) > .5) throw new IOException("Video orientation metadata changed.");
        if (output.Video.BitDepth < source.Video.BitDepth) throw new IOException("Video bit depth was reduced.");
    }
    public static bool ShouldKeep(long source, long output, bool keepLarger) => output > 0 && (keepLarger || output < source);
}

public sealed class EncoderDetector(string ffmpeg, ProcessRunner runner)
{
    public async Task<string> VerifyAsync(CompressionProfile profile, CancellationToken ct)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct); timeout.CancelAfter(TimeSpan.FromSeconds(40));
        List<string> args = ["-hide_banner", "-nostdin", "-v", "error", "-f", "lavfi", "-i", "color=c=black:s=128x128:r=25", "-frames:v", "2"];
        args.AddRange(CompressionArguments.VideoOptions(profile, false)); args.AddRange(["-f", "null", "-"]);
        ProcessResult result;
        try { result = await runner.RunAsync(ffmpeg,args,timeout.Token); }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested) { throw new IOException("Encoder test timed out."); }
        if (result.ExitCode != 0) throw new IOException($"{CompressionArguments.EncoderName(profile.Encoder)} is not usable: {result.Error.Trim()}");
        return CompressionArguments.EncoderName(profile.Encoder);
    }
}
