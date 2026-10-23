# Pinned third-party sources

The downloadable v1.0 source/portable packages include these source snapshots.
The Git repository keeps pinned upstream references instead of binary archives:

| Component | Revision | Source archive |
| --- | --- | --- |
| FFmpeg | `4cf298e66a` | [Download](https://codeload.github.com/FFmpeg/FFmpeg/tar.gz/4cf298e66a) |
| BtbN build recipes | `937868360a21d711ec94a33f5591d345ad9b7754` | [Download](https://codeload.github.com/BtbN/FFmpeg-Builds/tar.gz/937868360a21d711ec94a33f5591d345ad9b7754) |
| x265 | `020d7054dbecfd3be8e62efe33ff8a305d41856e` | [Download](https://codeload.github.com/Multicorewareinc/x265/tar.gz/020d7054dbecfd3be8e62efe33ff8a305d41856e) |
| x264 | `0480cb05fa188d37ae87e8f4fd8f1aea3711f7ee` | [Download](https://code.videolan.org/videolan/x264/-/archive/0480cb05fa188d37ae87e8f4fd8f1aea3711f7ee/x264-0480cb05fa188d37ae87e8f4fd8f1aea3711f7ee.tar.gz) |

The recipe snapshot identifies additional dependencies, revisions, patches and
build steps. See [DEPENDENCY-SOURCES.txt](../DEPENDENCY-SOURCES.txt),
[FFMPEG-BUILD-CONFIG.txt](../FFMPEG-BUILD-CONFIG.txt) and
[FFMPEG-PROVENANCE.json](../FFMPEG-PROVENANCE.json).

These archives are not needed to compile the C# application. FFmpeg is run as a
separate executable, supplied when assembling the portable release. The source
snapshots do not constitute a complete mirror of every FFmpeg dependency.
