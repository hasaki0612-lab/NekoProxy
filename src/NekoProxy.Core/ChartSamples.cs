namespace NekoProxy.Core;

public enum MonitorMetric {Cpu,Memory,Network,Disk,SshConnection}
public sealed record ChartPoint(DateTimeOffset Timestamp,double Value,MonitoringSample Raw);
public static class ChartSamples
{
    // Monotone cubic derivatives: only presentation geometry is interpolated.
    // A curve never overshoots a local measured range; hover uses Raw as before.
    public static double[] Tangents(IReadOnlyList<ChartPoint> points)
    {
        var m=new double[points.Count];if(points.Count<2)return m;
        var d=Enumerable.Range(0,points.Count-1).Select(i=>(points[i+1].Value-points[i].Value)/Math.Max(1,(points[i+1].Timestamp-points[i].Timestamp).TotalMilliseconds)).ToArray();
        m[0]=d[0];m[^1]=d[^1];
        for(var i=1;i<m.Length-1;i++)m[i]=d[i-1]*d[i]<=0?0:2/(1/d[i-1]+1/d[i]);
        for(var i=0;i<d.Length;i++)
        {
            if(d[i]==0){m[i]=m[i+1]=0;continue;}
            var a=m[i]/d[i];var b=m[i+1]/d[i];var norm=a*a+b*b;
            if(norm>9){var factor=3/Math.Sqrt(norm);m[i]=factor*a*d[i];m[i+1]=factor*b*d[i];}
        }
        return m;
    }
    public static double? Value(MonitoringSample sample,MonitorMetric metric,bool secondary=false)=>!sample.Snapshot.Success?null:metric switch
    {
        MonitorMetric.Cpu=>sample.Snapshot.CpuPercent,
        MonitorMetric.Memory=>sample.Snapshot.MemoryPercent,
        MonitorMetric.Network=>(secondary?sample.Snapshot.UploadBytesPerSecond:sample.Snapshot.DownloadBytesPerSecond)/1048576d,
        MonitorMetric.Disk=>sample.Snapshot.DiskPercent,
        _=>sample.Snapshot.SshConnectMilliseconds
    };
    public static TimeSpan Gap(MonitoringSample sample)=>TimeSpan.FromSeconds(Math.Max(5,sample.ExpectedIntervalSeconds*1.8));
    public static IReadOnlyList<IReadOnlyList<ChartPoint>> Segments(IEnumerable<MonitoringSample> samples,MonitorMetric metric,bool secondary=false)
    {
        var groups=new List<IReadOnlyList<ChartPoint>>();var current=new List<ChartPoint>();MonitoringSample? last=null;
        foreach(var s in samples.OrderBy(x=>x.Timestamp))
        {
            var value=Value(s,metric,secondary);
            if(value is null||!double.IsFinite(value.Value)){if(current.Count>0)groups.Add(current);current=[];last=null;continue;}
            if(last is not null&&s.Timestamp-last.Timestamp>Gap(last)){if(current.Count>0)groups.Add(current);current=[];}
            current.Add(new(s.Timestamp,value.Value,s));last=s;
        }
        if(current.Count>0)groups.Add(current);return groups;
    }
    // First/min/max/last per pixel bucket: extrema survive; raw samples remain immutable.
    public static IReadOnlyList<ChartPoint> Reduce(IReadOnlyList<ChartPoint> points,DateTimeOffset start,DateTimeOffset end,int width)
    {
        width=Math.Max(1,width);if(points.Count<=width*2)return points;
        var span=Math.Max(1,(end-start).TotalMilliseconds);var result=new List<ChartPoint>();
        foreach(var group in points.GroupBy(p=>(int)Math.Floor((p.Timestamp-start).TotalMilliseconds/span*width)))
        {
            var rows=group.ToArray();result.AddRange(new[]{rows[0],rows.MinBy(p=>p.Value)!,rows.MaxBy(p=>p.Value)!,rows[^1]}.DistinctBy(p=>p.Timestamp).OrderBy(p=>p.Timestamp));
        }
        return result;
    }
    public static MonitoringSample? Nearest(IReadOnlyList<MonitoringSample> samples,DateTimeOffset at,MonitorMetric metric)
    {
        if(samples.Count==0||at<samples[0].Timestamp)return null;
        var index=0;while(index<samples.Count&&samples[index].Timestamp<at)index++;
        var left=samples[Math.Max(0,index-1)];var right=samples[Math.Min(samples.Count-1,index)];
        if(at>samples[^1].Timestamp+Gap(samples[^1]))return null;
        if(left.Timestamp<at&&right.Timestamp>at&&(right.Timestamp-left.Timestamp>Gap(left)||Value(left,metric) is null||Value(right,metric) is null))return null;
        var closest=(at-left.Timestamp).Duration()<=(right.Timestamp-at).Duration()?left:right;
        return Value(closest,metric) is {} value&&double.IsFinite(value)?closest:null;
    }
}
