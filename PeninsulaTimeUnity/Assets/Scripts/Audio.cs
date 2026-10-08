using System.Collections.Generic;
using UnityEngine;

namespace PeninsulaTime
{
    // Sound effects and ambience, synthesised once at start (no audio files): card taps, door chimes, footsteps,
    // engines, the street by day and night, station halls and shop music. Loops are cross-faded so they repeat
    // without a click.
    public static class Sfx
    {
        const int Rate=22050;
        static readonly Dictionary<string,AudioClip> clips=new Dictionary<string,AudioClip>();
        static GameObject host;static AudioSource[] voices;static int nextVoice;
        static AudioSource[] ambience;static string ambienceKey;static int ambienceSide;
        public static float Volume=1f;

        static void Init()
        {
            if(host!=null)return;
            host=new GameObject("Sound");Object.DontDestroyOnLoad(host);
            voices=new AudioSource[10];
            for(int i=0;i<voices.Length;i++){voices[i]=host.AddComponent<AudioSource>();voices[i].playOnAwake=false;}
            ambience=new AudioSource[2];
            for(int i=0;i<2;i++){ambience[i]=host.AddComponent<AudioSource>();ambience[i].loop=true;ambience[i].playOnAwake=false;ambience[i].volume=0;}
        }
        public static AudioClip Clip(string key)
        {
            AudioClip clip;if(clips.TryGetValue(key,out clip))return clip;
            var data=Make(key);if(data==null)return null;
            clip=AudioClip.Create(key,data.Length,1,Rate,false);clip.SetData(data,0);clips[key]=clip;return clip;
        }
        // A one-shot at the listener (interface sounds, the player's own steps).
        public static void Play(string key,float volume=1f,float pitch=1f)
        {
            if(Application.isBatchMode)return;
            Init();var clip=Clip(key);if(clip==null)return;
            var v=voices[nextVoice=(nextVoice+1)%voices.Length];
            v.pitch=pitch*(key.StartsWith("step")?Random.Range(.9f,1.1f):1f);v.PlayOneShot(clip,volume*Volume);
        }
        // A one-shot in the world (doors, bells), quieter with distance.
        public static void PlayAt(string key,Vector3 position,float volume=1f)
        {
            if(Application.isBatchMode)return;
            var clip=Clip(key);if(clip==null)return;
            var o=new GameObject("Sound "+key);o.transform.position=position;
            var s=o.AddComponent<AudioSource>();s.clip=clip;s.spatialBlend=1;s.minDistance=3;s.maxDistance=45;s.rolloffMode=AudioRolloffMode.Linear;
            s.volume=volume*Volume;s.Play();Object.Destroy(o,clip.length+.1f);
        }
        // A looping sound carried by a vehicle (engine, train rumble).
        public static AudioSource Attach(GameObject owner,string key,float volume,float reach=40f)
        {
            if(Application.isBatchMode||owner==null)return null;
            var s=owner.AddComponent<AudioSource>();s.clip=Clip(key);s.loop=true;s.spatialBlend=1;s.minDistance=2;s.maxDistance=reach;
            s.rolloffMode=AudioRolloffMode.Linear;s.volume=volume*Volume;s.dopplerLevel=0;s.time=Random.Range(0,s.clip.length*.9f);s.Play();
            return s;
        }
        // The background for where the player is; changing key cross-fades over a second.
        public static void Ambience(string key,float volume)
        {
            if(Application.isBatchMode)return;
            Init();
            if(key!=ambienceKey)
            {
                ambienceKey=key;ambienceSide=1-ambienceSide;
                var s=ambience[ambienceSide];s.clip=key!=null?Clip(key):null;
                if(s.clip!=null){s.time=0;s.Play();}
            }
            float step=Time.unscaledDeltaTime;
            for(int i=0;i<2;i++)
            {
                var s=ambience[i];float target=i==ambienceSide&&s.clip!=null?volume*Volume:0;
                s.volume=Mathf.MoveTowards(s.volume,target,step*.8f);
                if(s.volume<=0&&i!=ambienceSide&&s.isPlaying)s.Stop();
            }
        }

        // ---------- synthesis ----------
        static System.Random noise=new System.Random(7);
        static float N(){return (float)noise.NextDouble()*2f-1f;}
        static float[] Buffer(float seconds){return new float[Mathf.Max(1,(int)(seconds*Rate))];}
        static float Env(float t,float attack,float decay){return t<attack?t/attack:Mathf.Exp(-(t-attack)/decay);}
        static void Tone(float[] b,float start,float seconds,float freq,float amp,float decay,float harmonics=0)
        {
            int s0=(int)(start*Rate),n=(int)(seconds*Rate);
            for(int i=0;i<n&&s0+i<b.Length;i++)
            {
                float t=(float)i/Rate,e=Env(t,.005f,decay)*amp;
                float v=Mathf.Sin(2*Mathf.PI*freq*t)+harmonics*.5f*Mathf.Sin(4*Mathf.PI*freq*t)+harmonics*.25f*Mathf.Sin(6.02f*Mathf.PI*freq*t);
                b[s0+i]+=v*e;
            }
        }
        // Noise shaped by a one-pole low-pass (cutoff 0..1) and an envelope.
        static void Noise(float[] b,float start,float seconds,float amp,float cutoff,float attack,float decay)
        {
            int s0=(int)(start*Rate),n=(int)(seconds*Rate);float y=0;
            for(int i=0;i<n&&s0+i<b.Length;i++){float t=(float)i/Rate;y+=cutoff*(N()-y);b[s0+i]+=y*amp*Env(t,attack,decay);}
        }
        // Makes the last `fade` seconds blend into the start so the clip loops seamlessly.
        static float[] Loop(float[] b,float fade)
        {
            int f=(int)(fade*Rate),n=b.Length-f;var r=new float[n];
            for(int i=0;i<n;i++)r[i]=b[i];
            for(int i=0;i<f;i++){float w=(float)i/f;r[i]=b[i]*w+b[n+i]*(1-w);}
            return r;
        }
        static float[] Make(string key)
        {
            float[] b;
            switch(key)
            {
                case "tap":b=Buffer(.16f);Tone(b,0,.16f,1320,.35f,.06f);Tone(b,0,.16f,1980,.15f,.05f);return b;
                case "scan":b=Buffer(.1f);Tone(b,0,.1f,2100,.3f,.05f);return b;
                case "deny":b=Buffer(.42f);Tone(b,0,.16f,420,.35f,.09f,1);Tone(b,.22f,.18f,380,.35f,.09f,1);return b;
                case "coin":b=Buffer(.5f);Tone(b,0,.5f,1568,.25f,.12f);Tone(b,.08f,.42f,2093,.25f,.15f);return b;
                case "chime":b=Buffer(1.6f);Tone(b,0,1f,659,.3f,.35f,1);Tone(b,.45f,1.15f,523,.3f,.45f,1);return b;
                case "bell":b=Buffer(.9f);Tone(b,0,.9f,988,.28f,.2f,1);Tone(b,.18f,.7f,784,.28f,.25f,1);return b;
                case "shop-door":b=Buffer(1.1f);Tone(b,0,.6f,1175,.22f,.2f);Tone(b,.25f,.85f,880,.22f,.3f);return b;
                case "door-chime":b=Buffer(1.2f);{float[] f={988,880,784,659};for(int i=0;i<4;i++)Tone(b,i*.16f,.5f,f[i],.25f,.12f,1);}return b;
                case "arrival":b=Buffer(2.6f);{float[] f={523,659,784,659,880,784};for(int i=0;i<6;i++)Tone(b,i*.32f,.7f,f[i],.22f,.25f,1);}return b;
                case "psd":b=Buffer(1.0f);Noise(b,0,1f,.25f,.08f,.25f,.25f);Tone(b,.85f,.1f,180,.2f,.03f);return b;
                case "air":b=Buffer(.9f);Noise(b,0,.9f,.45f,.5f,.01f,.28f);Tone(b,.65f,.15f,140,.25f,.04f);return b;
                case "step":b=Buffer(.09f);Noise(b,0,.09f,.7f,.12f,.002f,.025f);return b;
                case "step-hard":b=Buffer(.07f);Noise(b,0,.07f,.6f,.45f,.001f,.015f);Tone(b,0,.05f,220,.15f,.015f);return b;
                case "land":b=Buffer(.18f);Noise(b,0,.18f,.9f,.08f,.002f,.05f);return b;
                case "jump":b=Buffer(.18f);Noise(b,0,.18f,.25f,.3f,.04f,.05f);return b;
                case "city-day":
                    b=Buffer(9f);Noise(b,0,9f,.5f,.02f,.01f,1e6f);
                    for(int k=0;k<5;k++){float s=k*1.7f+.4f;Noise(b,s,2.2f,.25f,.05f,.9f,.6f);} // passing cars
                    Tone(b,3.1f,.25f,880,.05f,.08f);Tone(b,3.35f,.25f,880,.05f,.08f); // distant horn
                    return Loop(b,1f);
                case "city-night":
                    b=Buffer(9f);Noise(b,0,9f,.25f,.015f,.01f,1e6f);
                    for(int k=0;k<36;k++){float s=k*.24f+(k%3)*.03f;Tone(b,s,.06f,4300,.03f,.015f);} // crickets
                    Noise(b,4f,2.5f,.18f,.05f,1f,.7f);
                    return Loop(b,1f);
                case "station":
                    b=Buffer(7f);Noise(b,0,7f,.35f,.03f,.01f,1e6f);
                    for(int i=0;i<b.Length;i++)b[i]+=.03f*Mathf.Sin(2*Mathf.PI*60*i/Rate);
                    return Loop(b,1f);
                case "shop-music":
                    b=Buffer(8.5f);
                    {float[][] chords={new[]{262f,330,392},new[]{220f,262,330},new[]{175f,220,262},new[]{196f,247,294}};
                     for(int c=0;c<4;c++)for(int k=0;k<8;k++)Tone(b,c*2f+k*.25f,.5f,chords[c][k%3]*(k>=3?2:1),.08f,.18f,1);}
                    return Loop(b,.5f);
                case "engine":case "bus-engine":
                    b=Buffer(3f);
                    {float f=key=="engine"?52:38;
                     for(int i=0;i<b.Length;i++){float t=(float)i/Rate;b[i]=.18f*Mathf.Sin(2*Mathf.PI*f*t)+.1f*Mathf.Sin(2*Mathf.PI*f*2*t)+.06f*Mathf.Sin(2*Mathf.PI*f*3.01f*t);}
                     Noise(b,0,3f,.12f,.06f,.01f,1e6f);}
                    return Loop(b,.5f);
                case "rumble":
                    b=Buffer(4.6f);Noise(b,0,4.6f,.5f,.03f,.01f,1e6f);
                    for(float s=.2f;s<4.4f;s+=.9f){Noise(b,s,.12f,.35f,.3f,.002f,.03f);Noise(b,s+.16f,.12f,.3f,.3f,.002f,.03f);} // wheels over rail joints
                    return Loop(b,.4f);
            }
            return null;
        }
    }
}
