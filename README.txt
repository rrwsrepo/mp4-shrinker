MP4 SHRINKER 1.0.0
Windows 10 22H2 / Windows 11, x64

QUICK START
1. Extract the complete portable ZIP to a normal folder.
2. Double-click MP4-Shrinker.exe. No .NET installation is needed.
3. Browse to the ORIGINAL video folder (or drop that folder onto the window).
4. Leave High quality, CPU H.265, Normal/slow and AAC 160 selected to start.
5. Click Scan to inspect the queue, or Start compression to scan and process it.

Example:
  Source: D:\Videos\Follower Of Christ\Sermons\video.mp4
  Output: D:\Videos\smaller\Follower Of Christ\Sermons\video.mp4

All subfolders are mirrored. Original files are never intentionally changed,
moved, renamed, overwritten or deleted. The program makes a new copy only.
You need write permission in the source folder's PARENT to create 'smaller'.
Choose a named folder, not a drive root or a folder inside 'smaller'.

QUALITY AND SPEED
Default: HEVC/H.265, CRF 20, slow preset, AAC 160 kbps per audio track.
Maximum quality uses CRF 18. Balanced uses 22. Smaller files uses 24.
Lower CRF usually means higher quality and larger files. Results depend on the
source. This is lossy encoding, intended to look very close to the original.
There is no guaranteed size saving or guarantee of imperceptible differences.
Frame timing and resolution are retained; the app does not resize videos.
The CPU encoder limits its worker pool to four threads plus frame workers to
avoid overwhelming a typical desktop during a large batch.

CPU speed: medium is faster; slow is the default; slower can improve efficiency.
Advanced settings allow CRF 16 to 28 and other supported CPU presets.
Hardware modes use separate quality controls; those numbers are not equivalent
to CPU CRF. NVIDIA/Intel/AMD modes run a tiny real encoder test before the batch.
If a device/driver is unsuitable, CPU fallback is enabled by default.

OUTPUT SAFETY
The app first writes a unique temporary MP4 beside the intended output.
It checks FFmpeg's exit code and inspects the result with ffprobe: non-empty
file, video codec, duration, audio track/channel count, subtitles, chapters,
dimensions, bit depth and rotation. Only then is the temporary file renamed.
Source size and modification date must still match the scanned version.
On Windows, a read handle prevents other processes writing/deleting the source
while that individual file is being processed.

By default, a result that is not smaller is discarded. The queue says
'Not smaller'. There is then no new output for that file. Any old output stays
unchanged. Advanced settings can keep larger outputs when deliberately chosen.

Existing output policy:
- Skip verified outputs (default): requires a matching history record for the
  source, settings and output fingerprint, plus fresh output validation.
- Replace existing outputs: encodes and validates a new temporary copy first.
  An existing destination is replaced only after success. Failure/cancellation
  leaves the previous output intact.
- Stop batch if outputs exist: resolves conflicts before any file is encoded.

Unknown existing MP4s are not treated as completed work. Changed source files,
changed compression settings or missing history produce an explicit conflict.
Select Replace existing outputs if you intend to reprocess those destinations.

STREAMS, COLOUR AND ORIENTATION
All audio tracks are mapped. AAC 160/192 kbps keeps the channel count; choose
Copy compatible to preserve AAC, MP3, AC-3, E-AC-3 or ALAC audio without encoding.
Copy mode refuses other audio codecs instead of quietly dropping a track.
MP4 mov_text subtitles and chapters are retained. Other subtitle codecs,
extra video/cover art and non-chapter data/timecode streams are refused with a
clear queue message. Metadata is copied where the MP4 muxer supports it.
Phone video retains its original stored dimensions and display rotation.
Ordinary 8-bit and 10-bit 4:2:0 SDR are supported. HDR/HDR10/Dolby Vision,
12-bit, unusual chroma/pixel formats and odd dimensions are protected by
refusing the file, rather than silently converting it. Use the original for
these files. The app does not strip HDR and present the result as equivalent.

PROGRESS, PAUSE AND CANCEL
The upper bar is the current file. Overall progress is weighted by duration.
New-file size during an encode is provisional; totals include only kept or
verified outputs. A file's streaming optimization/validation can take time
after the encoding progress reaches its end.
Pause after file pauses between files. Resume continues the same queue.
Cancel kills the active FFmpeg process tree, awaits exit and removes its
temporary file. Completed outputs stay in place. Closing the window performs
the same safe cancellation before the application exits.
Select a queue row to read/copy its detailed result. Failed only filters the
queue. Scan warnings (such as an unreadable subfolder) are recorded in Logs.
Junctions/symlinks and nested generated/reserved 'smaller' folders are excluded.

RECOVERY AND SETTINGS
Preferences, logs and history are in %LOCALAPPDATA%\MP4 Shrinker.
Scan is repeated on each Start so current file sizes and media are inspected.
Each generated output root contains a small ownership marker and lock file.
Only the app's explicitly registered unfinished temporary file is eligible
for automatic cleanup on the next Start. Similar-looking arbitrary files are
never deleted. A crashed, promoted output without a saved history record is
treated as unverified on the next run.
Only one app instance runs per Windows session. Output-folder locking prevents
two copies from processing the same generated destination at once.
Keep the history folder if you want reliable skip/resume detection.

SHUTDOWN
Off by default, and must be re-enabled for each app launch. Other preferences
are remembered. After a successful batch, a 60-second countdown offers Cancel
shutdown. No shutdown follows cancellation, a failed file, a fatal error,
an empty queue, or an incomplete scan. Windows is never asked to force-close
other applications; unsaved work in another app may prevent shutdown.

PRIVACY AND REQUIREMENTS
There are no uploads, accounts, telemetry or automatic downloads in this app.
Processing happens locally. FFmpeg tools are found beside the app, then on PATH.
Keep ffmpeg.exe and ffprobe.exe with the main EXE. About shows their versions.
HEVC files require a compatible player/device. The app does not install a
system HEVC decoder. Original files remain available for compatibility needs.
The portable application is unsigned; Windows may identify it as an unrecognised
publisher. There is no installer and the app does not require administrator rights.

VALIDATION AND KNOWN LIMITS
See TEST-RESULTS.txt for the exact build and checks completed.
The Windows x64 WPF app was compiled and self-contained publishing succeeded.
43 automated unit/integration checks passed on Linux using FFmpeg 6.1.1.
The native Windows UI, bundled Windows FFmpeg build, physical GPUs, actual
Windows shutdown, audio/video playback synchronisation by a human, and a real
disk-full condition could not be exercised in that environment. They are not
represented as tested. The published EXE has not been code-signed.
Network paths depend on server/Windows permissions and locking behaviour;
local NTFS folders are the intended initial use case.
Validation uses stream/container metadata; it is not a perceptual-quality
measurement or a full second decode of the output. Compare a few representative
results before choosing a quality setting for a very large library.

DEVELOPERS
The separate source ZIP contains the Visual Studio solution, app, core engine,
tests, build script, build guide and notices. Read BUILD-FROM-SOURCE.md.
FFmpeg is a separate command-line program with its own licence. Its original
GPLv3 licence, exact version, checksums and source references are included.
