using UnityEngine;

namespace PeninsulaTime
{
    // Central gameplay tuning. Scale running time and acceleration together, never station dwell,
    // door animation or the 30/50-second timetable: faster travel must not shorten boarding windows.
    public static class TransitSpeed
    {
        public const float RailMultiplier=4f,WalkwayMultiplier=3f;
        public static bool Bus(string kind){return kind=="bus"||kind=="brt";}
        public static float Multiplier(string kind){return Bus(kind)?1f:RailMultiplier;}
        public static float RunningSeconds(string kind,float kilometres,float speedKmh)
        {return kilometres/Mathf.Max(5f,speedKmh)*3600f/Multiplier(kind);}
        public static float JourneyCruiseSpeed(string kind)
        {return (Bus(kind)?12f:kind=="ktx"?45f:kind=="mugunghwa"?32f:24f)*Multiplier(kind);}
        public static float JourneyAcceleration(string kind)
        {float scale=Multiplier(kind);return (kind=="ktx"?2.6f:kind=="mugunghwa"?2.1f:1.8f)*scale*scale;}
        static void Profile(string kind,float distance,out float acceleration,out float peak,out float ramp,out float cruise,out float duration)
        {
            distance=Mathf.Max(0,distance);acceleration=JourneyAcceleration(kind);
            peak=Mathf.Min(JourneyCruiseSpeed(kind),Mathf.Sqrt(distance*acceleration));
            ramp=peak/acceleration;
            cruise=peak>.001f?Mathf.Max(0,(distance-peak*ramp)/peak):0;
            duration=2*ramp+cruise;
        }
        public static float JourneyDuration(string kind,float distance)
        {float a,v,r,c,d;Profile(kind,distance,out a,out v,out r,out c,out d);return d;}
        // Analytic accelerate/cruise/brake profile, evaluated from elapsed time. The same journey
        // position is obtained at 30/60/144 fps and after a long frame, without overshooting a stop.
        public static float JourneyPosition(string kind,float distance,float elapsed,out float speed)
        {
            float a,v,r,c,d;Profile(kind,distance,out a,out v,out r,out c,out d);
            if(elapsed<=0){speed=0;return 0;}
            if(elapsed>=d){speed=0;return Mathf.Max(0,distance);}
            if(elapsed<r){speed=a*elapsed;return .5f*a*elapsed*elapsed;}
            if(elapsed<r+c){speed=v;return .5f*v*r+v*(elapsed-r);}
            float remaining=d-elapsed;speed=a*remaining;
            return distance-.5f*a*remaining*remaining;
        }
    }
}
