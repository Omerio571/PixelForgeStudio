namespace PixelForge.Core;

public sealed class PlaybackTimeline
{
    private IReadOnlyList<int> durations;
    private double elapsed;
    public PlaybackTimeline(IReadOnlyList<int> durations){Validate(durations);this.durations=durations.ToArray();}
    public int TotalDurationMs=>durations.Sum();
    public int CurrentFrame { get; private set; }
    public bool Completed { get; private set; }
    public static int FrameDuration(int index,int fps)=>(int)Math.Round((index+1)*1000.0/fps)-(int)Math.Round(index*1000.0/fps);
    public void SetDurations(IReadOnlyList<int> updated)
    {
        Validate(updated);
        if(updated.Count!=durations.Count)throw new ArgumentException("Retiming must preserve the frame count.");
        double fraction=elapsed/durations[CurrentFrame];
        durations=updated.ToArray();elapsed=fraction*durations[CurrentFrame];
    }
    private static void Validate(IReadOnlyList<int> values){if(values.Count==0||values.Any(v=>v<=0))throw new ArgumentException("Playback needs positive frame durations.");}
    public void Seek(int index){CurrentFrame=Math.Clamp(index,0,durations.Count-1);elapsed=0;Completed=false;}
    public void Advance(double milliseconds,bool loop)
    {
        if(durations.Count==0||milliseconds<=0||Completed&&!loop)return;
        elapsed+=milliseconds;
        if(loop)elapsed%=durations.Sum();
        while(elapsed>=durations[CurrentFrame])
        {
            elapsed-=durations[CurrentFrame];
            if(CurrentFrame+1<durations.Count)CurrentFrame++;
            else if(loop)CurrentFrame=0;
            else{Completed=true;elapsed=0;break;}
        }
    }
}
