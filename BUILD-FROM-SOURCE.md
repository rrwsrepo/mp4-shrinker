# Building MP4 Shrinker

Install the .NET 10 SDK and use Windows 10 22H2 or Windows 11 x64. The project
has no third-party NuGet dependencies. It uses WPF and built-in .NET APIs.
Visual Studio with .NET 10 support can open `MP4Shrinker.sln`.

From this source folder:

```powershell
dotnet build MP4Shrinker.sln -c Release
dotnet run --project tests/MP4Shrinker.Tests -c Release
dotnet publish src/MP4Shrinker.App -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -o artifacts/MP4-Shrinker-v1.0-win-x64
```

The publish folder contains the app and its .NET runtime in one executable.
It is not trimmed. Native WPF runtime components extract automatically when
needed. Copy `ffmpeg.exe` and `ffprobe.exe` from the supplied portable release
beside the published EXE, together with README and third-party notices.
No downloader runs inside the application.

Or use the included `build.ps1 -FfmpegDirectory 'C:\path\to\portable-release'`.
It builds, runs unit checks, publishes, copies the selected tools and creates
a portable ZIP. It does not download FFmpeg or silently update it.

For the real-media integration suite, make FFmpeg available on PATH:

```powershell
$env:PATH = 'C:\path\to\portable-release;' + $env:PATH
dotnet run --project tests/MP4Shrinker.Tests -c Release -- --integration
```

This is a dependency-free executable test harness, not a `dotnet test` project.
It creates synthetic clips and temporary test directories and removes its
successful test data. The junction/symlink checks require Windows Developer
Mode or a session with permission to create symbolic links. The rest of the
application and its normal use need no elevated permissions.

Architecture:

- `MP4Shrinker.Core`: path safety, media inspection, encoder profiles, progress,
  persistent settings/history, validation, recovery and sequential batching.
- `MP4Shrinker.App`: WPF Fluent system theme, MVVM bindings, asynchronous commands,
  folder picker/drop, pause/cancel, progress and guarded shutdown.
- `MP4Shrinker.Tests`: isolated checks and synthetic-media integration tests.

Build-time Linux cross-compilation works using `EnableWindowsTargeting=true`.
Run the Core tests on Linux with a Linux FFmpeg build. Cross-compilation does
not validate native Windows UI rendering or hardware acceleration.

Before a wider release, run the checklist in TEST-RESULTS.txt on a real Windows
10/11 desktop, test the exact bundled FFmpeg binaries and relevant GPU drivers,
and sign the final executable if a signing certificate is available.

No source-control service, hosting or external deployment is required.
