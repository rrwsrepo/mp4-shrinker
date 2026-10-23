using MP4Shrinker.Core;

namespace MP4Shrinker.App;
public sealed class JobRow(JobUpdate initial) : Observable
{
    public Guid Id=>value.Id;
    private JobUpdate value=initial;
    public string File=>value.RelativePath;
    public string Original=>Sizes.Format(value.SourceBytes);
    public string Output=>value.OutputBytes is { } n ? Sizes.Format(n) : "—";
    public string Status=>value.Status==JobStatus.NotSmaller ? "Not smaller" : value.Status.ToString();
    public string Details=>value.Message+"\n"+value.Media;
    public bool Failed=>value.Status==JobStatus.Failed;
    public bool Kept=>value.Status is JobStatus.Completed or JobStatus.Skipped;
    public long SourceBytes=>value.SourceBytes;
    public long OutputBytes=>value.OutputBytes??0;
    public double Percentage=>100*value.Fraction;
    public void Apply(JobUpdate update) { value=update; Changed(""); }
}
