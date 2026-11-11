using System;
using System.Numerics;
namespace IKnowAGuyVR.SubsystemProbe;

// Metres and a caller-owned monotonic clock. No Unity queries or allocations.
internal sealed class PunchGesture
{
    internal const float ForwardSpeed=2f,Alignment=.64f,RearmSpeed=.8f;
    private const double MaxGap=.1,SettleTime=.06;
    private Vector3 previous,previousAbsolute;
    private double previousTime,settledSince=-1;
    private bool seeded,armed;
    internal Vector3 Velocity {get;private set;}
    internal void Reset(){seeded=armed=false;settledSince=-1;Velocity=default;}
    internal bool Sample(Vector3 position,Vector3 absolute,Vector3 axis,bool grip,double now)
    {
        if(!grip){Reset();return false;}
        double dt=now-previousTime;
        if(!seeded||dt<=0||dt>MaxGap)
        {previous=position;previousAbsolute=absolute;previousTime=now;seeded=true;armed=false;settledSince=-1;Velocity=default;return false;}
        Velocity=(position-previous)/(float)dt;var absoluteVelocity=(absolute-previousAbsolute)/(float)dt;previous=position;previousAbsolute=absolute;previousTime=now;
        float speed=Velocity.Length();
        if(!float.IsFinite(speed)||speed>12||absoluteVelocity.Length()>12){Reset();return false;}
        if(speed<RearmSpeed)
        {
            if(settledSince<0)settledSince=now;
            if(now-settledSince>=SettleTime)armed=true;
            return false;
        }
        settledSince=-1;
        float forward=Vector3.Dot(Velocity,axis);
        if(!armed||forward<ForwardSpeed||forward<speed*Alignment||Vector3.Dot(absoluteVelocity,axis)<ForwardSpeed)return false;
        armed=false;return true;
    }
}
