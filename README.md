# MP4 Shrinker

A native Windows desktop app that compresses MP4 folders with FFmpeg while
retaining their folder structure and leaving the original videos untouched.

Built with C#, .NET 10, WPF and an independent media-processing core.

## How it works

Choose an original folder, then click **Scan** or **Start compression**.

```text
Source: D:\Videos\Follower Of Christ\Sermons\video.mp4
Output: D:\Videos\smaller\Follower Of Christ\Sermons\video.mp4
```

The default profile uses CPU H.265, CRF 20, the slow preset, AAC 160 kbps per
audio track and MP4 fast-start. Encoding is lossy, with high visible quality as
the goal. Size savings depend on the original.

## Features

- Recursive scanning, including Unicode filenames and mixed-case MP4 extensions.
- Temporary-file encoding, output validation and atomic finalisation.
- Outputs larger than their originals are discarded by default.
- Duration-weighted progress, queue details, pause between files and cancellation.
- Verified skip/resume records, explicit overwrite mode and crash cleanup.
- Multiple audio tracks, compatible subtitle tracks, chapters and rotation.
- Optional NVIDIA, Intel and AMD encoding with a real encoder test and CPU fallback.
- File-date preservation, local logs, saved settings and a guarded shutdown countdown.
- System light/dark theme and a self-contained Windows x64 publish configuration.

HDR, unusual pixel formats and unsupported extra streams are explicitly refused
to avoid silently discarding information. See [README.txt](README.txt) for the
full usage guide and supported formats.

## Build and test

Install the .NET 10 SDK, then run:

```powershell
dotnet build MP4Shrinker.sln -c Release
dotnet run --project tests/MP4Shrinker.Tests -c Release
dotnet publish src/MP4Shrinker.App -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -o artifacts/MP4-Shrinker-v1.0-win-x64
```

Place compatible `ffmpeg.exe` and `ffprobe.exe` beside the published EXE.
The application does not download or update tools automatically. Release
binaries and third-party source archives are excluded from Git history.

For real-media integration tests, put FFmpeg on `PATH` and run:

```powershell
dotnet run --project tests/MP4Shrinker.Tests -c Release -- --integration
```

The executable test harness does not use `dotnet test`. Symbolic-link tests on
Windows require Developer Mode or permission to create symbolic links.

Read [BUILD-FROM-SOURCE.md](BUILD-FROM-SOURCE.md) for packaging instructions.

## Validation

The solution builds without warnings, and self-contained Windows x64 publishing
succeeded. **43 automated unit/integration checks passed on Linux**, including
real encodes, source integrity, cancellation, multiple tracks, rotation,
chapters, long paths, 4K and 10-bit SDR.

Native Windows UI execution, the bundled Windows FFmpeg executables, GPU
devices and actual shutdown were not tested in that environment. See
[TEST-RESULTS.txt](TEST-RESULTS.txt) for the evidence and Windows smoke-test list.

## Project layout

- `src/MP4Shrinker.App`: WPF interface and view models.
- `src/MP4Shrinker.Core`: scanning, encoding, validation and persistent state.
- `tests/MP4Shrinker.Tests`: unit checks and synthetic-video integration tests.
- `third-party`: original licences, provenance and pinned source references.

See [THIRD-PARTY-NOTICES.txt](THIRD-PARTY-NOTICES.txt) for dependency information.
