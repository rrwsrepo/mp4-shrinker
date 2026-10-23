using System.Security.Cryptography;
using System.Text.Json;
using MP4Shrinker.Core;

int passed=0,failed=0;
string testRoot=Path.Combine(Path.GetTempPath(),"mp4-shrinker-tests-"+Guid.NewGuid().ToString("N")); Directory.CreateDirectory(testRoot);
void Assert(bool condition,string message="Assertion failed") { if(!condition) throw new Exception(message); }
void Throws(Action action) { try {action();} catch {return;} throw new Exception("Expected an exception"); }
async Task Check(string name,Func<Task> test) {try {await test(); passed++; Console.WriteLine("PASS "+name);} catch(Exception ex) {failed++; Console.WriteLine("FAIL "+name+": "+ex);} }
Task Unit(Action action) {action();return Task.CompletedTask;}
string Folder(string name) {var p=Path.Combine(testRoot,name);Directory.CreateDirectory(p);return p;}
string Hash(string path)=>Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path)));
var video=new StreamInfo(0,"video","h264",320,180,"yuv420p",8,0,"bt709","bt709","bt709","tv","25/1",0,false,false);
var audio=new StreamInfo(1,"audio","aac",0,0,"",0,0,"","","","","",2,false,false);
var media=new MediaInfo(10,[video,audio],0);
var profile=new CompressionProfile();
try
{
 await Check("Defaults match the handoff",()=>Unit(()=>Assert(profile.Crf==20 && profile.Preset=="slow" && profile.Encoder==EncoderKind.Cpu && !profile.KeepLarger && profile.FastStart)));
 await Check("Mirrored nested paths with Unicode, spaces and apostrophes",()=>Unit(()=>
 {
   var path=Folder("Father's café 礼拝");var plan=PathPlan.Create(path);var nested=Path.Combine(path,"2026","Acts","x.MP4");
   Assert(plan.Destination(nested)==Path.Combine(testRoot,"smaller","Father's café 礼拝","2026","Acts","x.MP4"));
 }));
 await Check("Reject drive/root selection",()=>Unit(()=>Throws(()=>PathPlan.Create(Path.GetPathRoot(testRoot)!))));
 await Check("Reject output folder and descendants",()=>Unit(()=>{var p=Folder("smaller/library/child");Throws(()=>PathPlan.Create(p));}));
 await Check("Reject marked outputs even when renamed",()=>Unit(()=>{var p=Folder("renamed-output");File.WriteAllText(Path.Combine(p,PathPlan.Marker),"{}");Throws(()=>PathPlan.Create(p));}));
 await Check("Containment is separator aware and Windows case insensitive",()=>Unit(()=>{Assert(PathPlan.IsWithin(Path.Combine(testRoot,"VIDEOS","a.mp4"),Path.Combine(testRoot,"videos")));Assert(!PathPlan.IsWithin(Path.Combine(testRoot,"videos-other"),Path.Combine(testRoot,"videos")));}));
 await Check("Reject source traversal",()=>Unit(()=>{var plan=PathPlan.Create(Folder("traversal"));Throws(()=>plan.Destination(Path.Combine(testRoot,"elsewhere.mp4")));Throws(()=>plan.AssertDestination(plan.SourceRoot));}));
 await Check("Reject output junction/symlink ancestor",()=>Unit(()=>
 {
   var root=Folder("link-source");var output=Path.Combine(testRoot,"smaller","link-source");
   Directory.CreateSymbolicLink(output,root);Throws(()=>PathPlan.Create(root));Directory.Delete(output);
 }));
 await Check("Microsecond progress and clamp",()=>Unit(()=>{var p=new ProgressParser(10);p.Feed("out_time_ms=5000000");p.Feed("total_size=1234");p.Feed("speed=2.5x");var r=p.Feed("progress=continue")!;Assert(r.Fraction==.5 && r.Bytes==1234 && r.Speed=="2.5x");p.Feed("out_time_us=90000000");Assert(p.Feed("progress=continue")!.Fraction==1);}));
 await Check("Progress time fallback, invalid data and end",()=>Unit(()=>{var p=new ProgressParser(10);p.Feed("out_time=00:00:02.5000000");Assert(p.Feed("progress=continue")!.Fraction==.25);p.Feed("out_time_us=N/A");Assert(p.Feed("nonsense")==null);Assert(p.Feed("progress=end")!.Fraction==1);}));
 await Check("CPU argument list preserves filenames and streams",()=>Unit(()=>
 {
   var args=CompressionArguments.Build("weird ' 礼 $file.mp4","temp.mp4",media,profile);
   Assert(args[args.IndexOf("-i")+1]=="weird ' 礼 $file.mp4");Assert(args.Contains("0:a?") && args.Contains("0:s?") && args.Contains("+faststart"));Assert(!args.Contains("-y") && args.Contains("-n"));Assert(args.Contains("-noautorotate"));
 }));
 await Check("All audio tracks receive codec options",()=>Unit(()=>{var args=CompressionArguments.Build("a","b",media with{Streams=[video,audio,audio with{Index=2}]},profile);Assert(args.Contains("-c:a:0")&&args.Contains("-c:a:1"));}));
 await Check("Compatible copy mode and incompatible copy rejection",()=>Unit(()=>{Assert(CompressionArguments.Build("a","b",media,profile with{Audio=AudioMode.CopyCompatible}).Contains("copy"));Throws(()=>CompressionArguments.Build("a","b",media with{Streams=[video,audio with{Codec="vorbis"}]},profile with{Audio=AudioMode.CopyCompatible}));}));
 await Check("Hardware uses distinct rate-control options",()=>Unit(()=>{foreach(var encoder in new[]{EncoderKind.Nvidia,EncoderKind.Intel,EncoderKind.Amd}) {var a=CompressionArguments.VideoOptions(profile with{Encoder=encoder},false);Assert(!a.Contains("-crf"));} }));
 await Check("10-bit SDR stays 10-bit",()=>Unit(()=>{var a=CompressionArguments.Build("a","b",media with{Streams=[video with{BitDepth=10,PixelFormat="yuv420p10le"}]},profile);Assert(a.Contains("yuv420p10le"));}));
 await Check("HDR, subtitles and extra streams cannot silently disappear",()=>Unit(()=>
 {
   Throws(()=>CompressionArguments.CheckMedia(media with{Streams=[video with{ColorTransfer="smpte2084"}]}));
   Throws(()=>CompressionArguments.CheckMedia(media with{Streams=[video,audio with{Type="subtitle",Codec="dvd_subtitle"}]}));
   Throws(()=>CompressionArguments.CheckMedia(media with{Streams=[video,video with{Index=2}]}));
   Throws(()=>CompressionArguments.CheckMedia(media with{Streams=[video,audio with{Type="data",Codec="tmcd"}]}));
 }));
 await Check("Unknown duration and unusual pixel format fail safely",()=>Unit(()=>{Throws(()=>CompressionArguments.CheckMedia(media with{Duration=0}));Throws(()=>CompressionArguments.CheckMedia(media with{Streams=[video with{PixelFormat="yuv444p"}]}));}));
 await Check("Only-smaller safeguard",()=>Unit(()=>{Assert(OutputValidator.ShouldKeep(100,99,false));Assert(!OutputValidator.ShouldKeep(100,100,false));Assert(!OutputValidator.ShouldKeep(100,101,false));Assert(OutputValidator.ShouldKeep(100,101,true));Assert(!OutputValidator.ShouldKeep(100,0,true));}));
 var valid=media with{Streams=[video with{Codec="hevc"},audio]};
 await Check("Output duration tolerance",()=>Unit(()=>{OutputValidator.Validate(media,valid with{Duration=10.3});Throws(()=>OutputValidator.Validate(media,valid with{Duration=7}));}));
 await Check("Reject missing audio, changed channels and rotation",()=>Unit(()=>{Throws(()=>OutputValidator.Validate(media,valid with{Streams=[video with{Codec="hevc"}]}));Throws(()=>OutputValidator.Validate(media,valid with{Streams=[video with{Codec="hevc"},audio with{Channels=1}]}));Throws(()=>OutputValidator.Validate(media,valid with{Streams=[video with{Codec="hevc",Rotation=90},audio]}));}));
 await Check("Settings roundtrip and corrupt-file fallback",()=>Unit(()=>{var s=new StateStore(Folder("settings"));s.SaveSettings(new(){SourceFolder="café",Profile=profile with{Crf=18}});Assert(s.LoadSettings().Profile.Crf==18);File.WriteAllText(Path.Combine(s.Root,"settings.json"),"{broken");Assert(s.LoadSettings().Profile.Crf==20);}));
 await Check("Reject invalid quality and preset injection",()=>Unit(()=>{Throws(()=>(profile with{Crf=50}).Validate());Throws(()=>(profile with{Preset="slow -y"}).Validate());}));
 await Check("History detects changed input, output and settings",()=>Unit(()=>
 {
  var source=Path.Combine(Folder("history-source"),"a.mp4");File.WriteAllText(source,"source");var output=Path.Combine(Folder("history-output"),"a.mp4");File.WriteAllText(output,"out");var fi=new FileInfo(source);
  var job=new VideoJob{SourcePath=source,DestinationPath=output,RelativePath="a.mp4",SourceBytes=fi.Length,SourceModifiedUtc=fi.LastWriteTimeUtc};var state=new StateStore(Folder("history-state"));state.SaveHistory(job,profile,"libx265");var record=state.History(job)!;
  Assert(record.Matches(job,profile));Assert(!record.Matches(job,profile with{Crf=22}));job.SourceBytes++;Assert(!record.Matches(job,profile));job.SourceBytes--;File.AppendAllText(output,"changed");Assert(!record.Matches(job,profile));
 }));
 await Check("Recovery only removes a registered owned temporary",()=>Unit(()=>
 {
  var plan=PathPlan.Create(Folder("recovery"));Directory.CreateDirectory(plan.OutputRoot);var temp=Path.Combine(plan.OutputRoot,"a.mp4.__mp4shrinker_"+Guid.NewGuid().ToString("N")+"__.mp4");var unrelated=Path.Combine(plan.OutputRoot,"a.__mp4shrinker_encoding__.mp4");File.WriteAllText(temp,"temp");File.WriteAllText(unrelated,"do not delete");var state=new StateStore(Folder("recovery-state"));state.RegisterTemp(plan,temp);Assert(state.CleanStale(plan)==1);Assert(File.Exists(unrelated));state.RegisterTemp(plan,unrelated);Throws(()=>state.CleanStale(plan));
 }));
 await Check("Shutdown is disallowed after failures/cancellation",()=>Unit(()=>{Assert(new BatchSummary(1,0,0,0,false,100,50).CanShutdown);Assert(!new BatchSummary(1,0,0,1,false,100,50).CanShutdown);Assert(!new BatchSummary(1,0,0,0,true,100,50).CanShutdown);Assert(!new BatchSummary(0,0,0,0,false,0,0).CanShutdown);}));

 if(args.Contains("--integration"))
 {
  var tools=ToolPaths.Locate(AppContext.BaseDirectory);var runner=new ProcessRunner();var probe=new FfprobeService(tools.Ffprobe,runner);
  async Task Ffmpeg(params string[] arguments) {var r=await runner.RunAsync(tools.Ffmpeg,new[]{"-hide_banner","-v","error","-nostdin","-y"}.Concat(arguments),CancellationToken.None);if(r.ExitCode!=0) throw new Exception(r.Error);}
  var source=Folder("integration/Originals 礼");Directory.CreateDirectory(Path.Combine(source,"Sermons","2026"));
  var clip=Path.Combine(source,"Sermons","2026","Father's test.MP4");
  await Ffmpeg("-f","lavfi","-i","testsrc2=size=320x180:rate=25","-f","lavfi","-i","sine=frequency=440:sample_rate=48000","-f","lavfi","-i","sine=frequency=660:sample_rate=48000","-t","2","-map","0:v","-map","1:a","-map","2:a","-c:v","libx264","-crf","12","-c:a","aac",clip);
  var noAudio=Path.Combine(source,"silent.mp4");await Ffmpeg("-i",clip,"-an","-c:v","copy",noAudio);
  var portrait=Path.Combine(source,"rotated.mp4");await Ffmpeg("-i",clip,"-map","0","-c","copy","-metadata:s:v:0","rotate=90",portrait);
  var subtitle=Path.Combine(testRoot,"caption.srt");File.WriteAllText(subtitle,"1\n00:00:00,000 --> 00:00:01,500\nA subtitle to preserve.\n");
  var subtitled=Path.Combine(source,"subtitles.mp4");await Ffmpeg("-i",clip,"-i",subtitle,"-map","0","-map","1","-c","copy","-c:s","mov_text",subtitled);
  File.WriteAllText(Path.Combine(source,"broken.mp4"),"not a video");
  var hashes=Directory.EnumerateFiles(source,"*",SearchOption.AllDirectories).ToDictionary(p=>p,Hash);
  var state=new StateStore(Folder("integration-state"));using var log=new LoggingService(state);
  var scanner=new FileScanner(probe);var batch=new BatchProcessor(tools,state,log);
  var fast=profile with{Crf=26,Preset="ultrafast",KeepLarger=true};
  var scan=await scanner.ScanAsync(source,null,CancellationToken.None);
  await Check("Real scanning handles uppercase MP4 and corrupt media",()=>Unit(()=>{Assert(scan.Jobs.Count==5);Assert(scan.Jobs.Count(j=>j.Status==JobStatus.Failed)==1);}));
  BatchSummary? summary=null;
  await Check("Real FFmpeg batch: multiple audio, silent, rotation, subtitles and corrupt-file isolation",async()=>
  {
   summary=await batch.RunAsync(scan,fast,null,null,CancellationToken.None);
   Assert(summary.Completed==4 && summary.Failed==1,summary.ToString()+"\n"+string.Join("\n",scan.Jobs.Select(j=>j.Message)));
   foreach(var j in scan.Jobs.Where(j=>j.Status==JobStatus.Completed)) OutputValidator.Validate(j.Media!,await probe.ProbeAsync(j.DestinationPath,CancellationToken.None));
  });
  await Check("All originals remain byte-for-byte unchanged",()=>Unit(()=>{foreach(var (path,hash) in hashes) Assert(Hash(path)==hash,path);}));
  await Check("No temporary outputs masquerade as final files",()=>Unit(()=>Assert(!Directory.EnumerateFiles(scan.Paths.OutputRoot,"*__mp4shrinker*",SearchOption.AllDirectories).Any())));
  await Check("Second batch validates and skips completed outputs",async()=>{var again=await scanner.ScanAsync(source,null,CancellationToken.None);var result=await batch.RunAsync(again,fast,null,null,CancellationToken.None);Assert(result.Skipped==4 && result.Failed==1,result.ToString());});
  await Check("Changed quality causes explicit existing-output conflicts",async()=>{var again=await scanner.ScanAsync(source,null,CancellationToken.None);var result=await batch.RunAsync(again,fast with{Crf=24},null,null,CancellationToken.None);Assert(result.Failed==5 && result.Completed==0);});
  await Check("Unverified existing output is not overwritten",async()=>
  {
   var path=Folder("unverified");File.Copy(noAudio,Path.Combine(path,"x.mp4"));var s=await scanner.ScanAsync(path,null,CancellationToken.None);Directory.CreateDirectory(s.Paths.OutputRoot);File.WriteAllText(s.Jobs[0].DestinationPath,"untouchable existing file");var h=Hash(s.Jobs[0].DestinationPath);var r=await batch.RunAsync(s,fast,null,null,CancellationToken.None);Assert(r.Failed==1 && Hash(s.Jobs[0].DestinationPath)==h);
  });
  await Check("Explicit overwrite replaces only the output",async()=>{var s=await scanner.ScanAsync(source,null,CancellationToken.None);var r=await batch.RunAsync(s,fast with{Existing=ExistingMode.Overwrite},null,null,CancellationToken.None);Assert(r.Completed==4 && r.Failed==1);foreach(var (p,h) in hashes) Assert(Hash(p)==h);});
  await Check("Scanner ignores directory links",async()=>
  {
   var link=Path.Combine(source,"cycle");Directory.CreateSymbolicLink(link,source);var s=await scanner.ScanAsync(source,null,CancellationToken.None);Assert(s.Jobs.Count==5 && s.Warnings.Count==1);Directory.Delete(link);
  });
  await Check("Cancellation kills FFmpeg and drains streams",async()=>
  {
   using var cancel=new CancellationTokenSource(600);var sw=System.Diagnostics.Stopwatch.StartNew();bool cancelled=false;
   try {await runner.RunAsync(tools.Ffmpeg,["-hide_banner","-re","-f","lavfi","-i","color=s=128x128:r=25","-t","60","-f","null","-"],cancel.Token);}catch(OperationCanceledException){cancelled=true;}
   Assert(cancelled && sw.Elapsed.TotalSeconds<10);
  });
  await Check("Cancel mid-batch removes temporary and leaves next file pending",async()=>
  {
   var folder=Folder("cancel-source");await Ffmpeg("-f","lavfi","-i","testsrc2=size=640x360:rate=25","-t","8","-c:v","libx264","-preset","ultrafast",Path.Combine(folder,"a.mp4"));File.Copy(noAudio,Path.Combine(folder,"z.mp4"));var s=await scanner.ScanAsync(folder,null,CancellationToken.None);
   using var ct=new CancellationTokenSource();var progress=new InlineProgress<BatchUpdate>(u=>{if(u.Job.Status==JobStatus.Encoding && u.Job.Fraction>0) ct.Cancel();});
   var result=await batch.RunAsync(s,fast with{Preset="slow"},progress,null,ct.Token);
   Assert(result.Cancelled);Assert(!File.Exists(s.Jobs[0].DestinationPath));Assert(s.Jobs[1].Status==JobStatus.Ready);Assert(!Directory.EnumerateFiles(s.Paths.OutputRoot,"*__mp4shrinker*",SearchOption.AllDirectories).Any());
  });
  await Check("Larger encode is discarded by default",async()=>
  {
   var folder=Folder("efficient");await Ffmpeg("-f","lavfi","-i","color=s=160x90:r=10","-f","lavfi","-i","sine=sample_rate=48000","-t","3","-c:v","libx265","-x265-params","pools=2","-crf","40","-preset","ultrafast","-c:a","aac","-b:a","24k",Path.Combine(folder,"tiny.mp4"));
   var s=await scanner.ScanAsync(folder,null,CancellationToken.None);var r=await batch.RunAsync(s,fast with{KeepLarger=false,Audio=AudioMode.Aac192,Crf=18},null,null,CancellationToken.None);Assert(r.NotSmaller==1,r.ToString()+s.Jobs[0].Message);Assert(!File.Exists(s.Jobs[0].DestinationPath));
  });
  await Check("Source changed since scan is refused",async()=>{var folder=Folder("changed");var path=Path.Combine(folder,"x.mp4");File.Copy(noAudio,path);var s=await scanner.ScanAsync(folder,null,CancellationToken.None);File.SetLastWriteTimeUtc(path,DateTime.UtcNow.AddMinutes(2));var r=await batch.RunAsync(s,fast,null,null,CancellationToken.None);Assert(r.Failed==1 && !File.Exists(s.Jobs[0].DestinationPath));});
  await Check("CPU encoder is tested with a real encode",async()=>Assert(await new EncoderDetector(tools.Ffmpeg,runner).VerifyAsync(fast,CancellationToken.None)=="libx265"));
  await Check("Chapter titles and counts survive a real encode",async()=>
  {
   var folder=Folder("chapter-source");var metadata=Path.Combine(testRoot,"chapters.txt");File.WriteAllText(metadata,";FFMETADATA1\n[CHAPTER]\nTIMEBASE=1/1000\nSTART=0\nEND=1000\ntitle=Opening\n[CHAPTER]\nTIMEBASE=1/1000\nSTART=1000\nEND=2000\ntitle=Closing\n");
   await Ffmpeg("-i",clip,"-i",metadata,"-map","0","-map_metadata","1","-map_chapters","1","-c","copy",Path.Combine(folder,"chapters.mp4"));
   var s=await scanner.ScanAsync(folder,null,CancellationToken.None);var r=await batch.RunAsync(s,fast,null,null,CancellationToken.None);Assert(r.Completed==1,r.ToString()+s.Jobs[0].Message);Assert((await probe.ProbeAsync(s.Jobs[0].DestinationPath,CancellationToken.None)).Chapters==2);
   var inspect=await runner.RunAsync(tools.Ffprobe,["-v","error","-show_chapters","-of","json",s.Jobs[0].DestinationPath],CancellationToken.None);Assert(inspect.Output.Contains("Opening")&&inspect.Output.Contains("Closing"));
  });
  await Check("720p, 1080p, 4K and 10-bit SDR dimensions are retained",async()=>
  {
   var folder=Folder("resolutions");
   foreach(var size in new[]{"1280x720","1920x1080","3840x2160"}) await Ffmpeg("-f","lavfi","-i","color=c=blue:s="+size+":r=5","-t","1","-c:v","libx264","-preset","ultrafast",Path.Combine(folder,size+".mp4"));
   await Ffmpeg("-f","lavfi","-i","testsrc2=size=320x180:rate=5","-t","1","-pix_fmt","yuv420p10le","-c:v","libx265","-x265-params","pools=2","-preset","ultrafast",Path.Combine(folder,"tenbit.mp4"));
   var s=await scanner.ScanAsync(folder,null,CancellationToken.None);var r=await batch.RunAsync(s,fast,null,null,CancellationToken.None);Assert(r.Completed==4,r.ToString()+string.Join("\n",s.Jobs.Select(j=>j.Message)));
  });
  await Check("Long nested Unicode paths are processed",async()=>
  {
   var folder=Folder("long-paths");var path=folder;for(int i=0;i<5;i++)path=Path.Combine(path,"A long folder with spaces and Unicode 礼拝_"+i);Directory.CreateDirectory(path);File.Copy(noAudio,Path.Combine(path,"video.mp4"));
   var s=await scanner.ScanAsync(folder,null,CancellationToken.None);var r=await batch.RunAsync(s,fast,null,null,CancellationToken.None);Assert(r.Completed==1,r.ToString()+s.Jobs[0].Message);
  });
  await Check("Failed overwrite leaves previous valid output untouched",async()=>
  {
   var folder=Folder("failed-overwrite");File.Copy(noAudio,Path.Combine(folder,"x.mp4"));var s=await scanner.ScanAsync(folder,null,CancellationToken.None);await batch.RunAsync(s,fast,null,null,CancellationToken.None);var hash=Hash(s.Jobs[0].DestinationPath);
   var again=await scanner.ScanAsync(folder,null,CancellationToken.None);again.Jobs[0].Media=again.Jobs[0].Media! with{Streams=[video with{PixelFormat="bad-format"}]};var r=await batch.RunAsync(again,fast with{Existing=ExistingMode.Overwrite},null,null,CancellationToken.None);Assert(r.Failed==1 && Hash(s.Jobs[0].DestinationPath)==hash);
  });
 }
 Console.WriteLine($"\nRESULT: {passed} passed, {failed} failed. Platform: {System.Runtime.InteropServices.RuntimeInformation.OSDescription}");
 Console.WriteLine("Test data: "+testRoot);
}
finally { if(failed==0) Directory.Delete(testRoot,true); }
return failed==0?0:1;

sealed class InlineProgress<T>(Action<T> handler) : IProgress<T> { public void Report(T value)=>handler(value); }
