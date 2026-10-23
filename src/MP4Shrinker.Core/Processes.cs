using System.Diagnostics;
using System.Globalization;
using System.Text;
using System.Text.Json;

namespace MP4Shrinker.Core;

public sealed record ProcessResult(int ExitCode, string Output, string Error);
public sealed class ProcessRunner
{
    public async Task<ProcessResult> RunAsync(string executable, IEnumerable<string> args, CancellationToken ct,
        Action<string>? stdoutLine = null, Action<string>? stderrLine = null)
    {
        ct.ThrowIfCancellationRequested();
        ProcessStartInfo info = new(executable) { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true };
        foreach (string arg in args) info.ArgumentList.Add(arg);
        using Process p = new() { StartInfo = info };
        if (!p.Start()) throw new IOException($"Could not start {executable}");
        StringBuilder output = new(), error = new();
        async Task Drain(StreamReader reader, StringBuilder buffer, Action<string>? handler)
        {
            while (await reader.ReadLineAsync() is { } line)
            {
                // Bounded diagnostic capture; the log receives the complete stderr stream.
                if (buffer.Length > 2_000_000) buffer.Remove(0, 1_000_000);
                buffer.AppendLine(line); handler?.Invoke(line);
            }
        }
        Task readOut = Drain(p.StandardOutput, output, stdoutLine), readErr = Drain(p.StandardError, error, stderrLine);
        using var registration = ct.Register(() => { try { if (!p.HasExited) p.Kill(entireProcessTree: true); } catch (InvalidOperationException) { } catch (System.ComponentModel.Win32Exception) { } });
        try
        {
            await p.WaitForExitAsync(ct);
            await Task.WhenAll(readOut, readErr);
            ct.ThrowIfCancellationRequested();
            return new(p.ExitCode, output.ToString(), error.ToString());
        }
        finally
        {
            try { if (!p.HasExited) p.Kill(entireProcessTree: true); } catch (InvalidOperationException) { } catch (System.ComponentModel.Win32Exception) { }
            await p.WaitForExitAsync(CancellationToken.None);
            await Task.WhenAll(readOut, readErr);
        }
    }
}

public sealed record ToolPaths(string Ffmpeg, string Ffprobe)
{
    public static ToolPaths Locate(string directory)
    {
        string Find(string name)
        {
            string filename = name + (OperatingSystem.IsWindows() ? ".exe" : "");
            var dirs = new[] { directory }.Concat((Environment.GetEnvironmentVariable("PATH") ?? "").Split(Path.PathSeparator));
            return dirs.Where(d => !string.IsNullOrWhiteSpace(d)).Select(d => Path.Combine(d.Trim('"'), filename)).FirstOrDefault(File.Exists)
                ?? throw new FileNotFoundException($"{filename} is missing. Extract the complete portable ZIP, including both FFmpeg tools, into one folder.");
        }
        return new(Find("ffmpeg"), Find("ffprobe"));
    }
}

public sealed class FfprobeService(string executable, ProcessRunner runner)
{
    public async Task<MediaInfo> ProbeAsync(string file, CancellationToken ct)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct); timeout.CancelAfter(TimeSpan.FromMinutes(2));
        ProcessResult result;
        try { result = await runner.RunAsync(executable, ["-v", "error", "-protocol_whitelist", "file,pipe", "-show_format", "-show_streams", "-show_chapters", "-of", "json", file], timeout.Token); }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested) { throw new IOException("Media inspection timed out."); }
        if (result.ExitCode != 0) throw new IOException("Cannot read video: " + result.Error.Trim());
        return Parse(result.Output);
    }
    public static MediaInfo Parse(string json)
    {
        using var doc = JsonDocument.Parse(json); var root = doc.RootElement;
        static string S(JsonElement e, string key) => e.TryGetProperty(key, out var v) ? v.ToString() : "";
        static int I(JsonElement e, string key) => int.TryParse(S(e,key), out var i) ? i : 0;
        static double D(string s) => double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out var d) && double.IsFinite(d) ? d : 0;
        List<StreamInfo> streams = []; double longest = 0;
        foreach (var s in root.GetProperty("streams").EnumerateArray())
        {
            double rotation = 0; bool hdr = false;
            if (s.TryGetProperty("tags", out var tags)) rotation = D(S(tags,"rotate"));
            if (s.TryGetProperty("side_data_list", out var side)) foreach(var data in side.EnumerateArray())
            {
                if (data.TryGetProperty("rotation", out var r)) rotation = D(r.ToString());
                string type = S(data,"side_data_type");
                hdr |= type.Contains("DOVI", StringComparison.OrdinalIgnoreCase) || type.Contains("Mastering display", StringComparison.OrdinalIgnoreCase) || type.Contains("HDR", StringComparison.OrdinalIgnoreCase);
            }
            string pixel = S(s,"pix_fmt"); int depth = I(s,"bits_per_raw_sample");
            if (depth == 0) depth = pixel.Contains("12") ? 12 : pixel.Contains("10") || pixel.Contains("p010") ? 10 : 8;
            longest = Math.Max(longest, D(S(s,"duration")));
            bool attached = s.TryGetProperty("disposition",out var disp) && I(disp,"attached_pic") == 1;
            streams.Add(new(I(s,"index"), S(s,"codec_type"), S(s,"codec_name"), I(s,"width"), I(s,"height"), pixel, depth, rotation,
                S(s,"color_transfer"), S(s,"color_primaries"), S(s,"color_space"), S(s,"color_range"), S(s,"avg_frame_rate"), I(s,"channels"), attached, hdr));
        }
        if (!streams.Any(s => s.Type == "video" && !s.AttachedPicture)) throw new IOException("No playable video stream was found.");
        double duration = root.TryGetProperty("format",out var fmt) ? D(S(fmt,"duration")) : 0;
        if (duration <= 0) duration = longest;
        return new(duration, streams, root.TryGetProperty("chapters",out var chapters) ? chapters.GetArrayLength() : 0);
    }
}

public sealed record EncodeProgress(double Fraction, long Bytes, string Speed);
public sealed class ProgressParser(double duration)
{
    private double seconds; private long bytes; private string speed = "";
    public EncodeProgress? Feed(string line)
    {
        int split = line.IndexOf('='); if (split < 0) return null;
        string key = line[..split], value = line[(split+1)..];
        if ((key == "out_time_us" || key == "out_time_ms") && long.TryParse(value, out var micro)) seconds = micro / 1_000_000d;
        else if (key == "out_time" && TimeSpan.TryParse(value, CultureInfo.InvariantCulture, out var t)) seconds = t.TotalSeconds;
        else if (key == "total_size" && long.TryParse(value,out var size)) bytes = Math.Max(0,size);
        else if (key == "speed") speed = value;
        if (key != "progress") return null;
        double fraction = duration > 0 ? Math.Clamp(seconds / duration, 0, 1) : 0;
        return new(value == "end" ? 1 : fraction, bytes, speed);
    }
}
