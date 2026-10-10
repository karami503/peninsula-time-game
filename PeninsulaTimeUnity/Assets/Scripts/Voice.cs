using UnityEngine;

namespace PeninsulaTime
{
    // NPC speech as a garbled, sing-song babble (the "Animalese" idea): every Hangul syllable of the line becomes one short
    // pitched blip whose vowel colour comes from the syllable's vowel and whose attack comes from its first consonant.
    // Each person has a fixed base pitch, so the same person always sounds the same. Synthesised per line, no audio files.
    public static class Voice
    {
        const int Rate=22050;
        const float SyllableSeconds=.072f,SpacePause=.05f,StopPause=.16f;
        // Formants (F1, F2 in Hz) for the 21 Hangul vowels ㅏㅐㅑㅒㅓㅔㅕㅖㅗㅘㅙㅚㅛㅜㅝㅞㅟㅠㅡㅢㅣ.
        static readonly float[,] Formants={
            {800,1300},{580,1800},{760,1450},{560,1850},{600,1000},{500,1900},{580,1150},{480,1950},{450,800},{700,1150},{580,1650},
            {480,1700},{420,850},{330,900},{560,1050},{480,1650},{340,1800},{320,1000},{360,1350},{350,1900},{280,2250}};
        // Base pitch (Hz) for each Dialogue.Personas entry.
        static readonly float[] PersonaPitch={180,230,270,140,220,170,210,240,200,190,250,260};

        // Pure: (initial consonant 0-18, vowel 0-20, final consonant 0-27) of a Hangul syllable, or all -1 for anything else.
        public static void Jamo(char c,out int initial,out int vowel,out int final)
        {
            int index=c-0xAC00;
            if(index<0||index>=11172){initial=-1;vowel=-1;final=-1;return;}
            initial=index/588;vowel=index%588/28;final=index%28;
        }
        // Pure: a person's base pitch, their persona's pitch nudged by up to ±10% so two office workers differ.
        public static float PitchFor(int persona,int seed)
        {
            int p=((persona%PersonaPitch.Length)+PersonaPitch.Length)%PersonaPitch.Length;
            return PersonaPitch[p]*(1f+(((seed%9)+9)%9-4)*.025f);
        }

        // Pure: the babble samples for one line at the given base pitch. A question rises over its last syllables.
        public static float[] Synthesize(string line,float pitch)
        {
            line=line??"";
            float seconds=.05f;int total=0;
            foreach(char c in line){float d=Duration(c);seconds+=d;if(d==SyllableSeconds)total++;}
            var b=new float[Mathf.Max(1,(int)(seconds*Rate))];
            var rng=new System.Random(line.GetHashCode());
            bool question=line.TrimEnd('”','"',' ').EndsWith("?");
            float at=0;int count=0;
            foreach(char c in line)
            {
                float d=Duration(c);
                if(d==SyllableSeconds)
                {
                    int initial,vowel,final;Jamo(c,out initial,out vowel,out final);
                    if(vowel<0){vowel=char.ToLowerInvariant(c)%21;initial=c%19;final=0;}
                    float rise=question&&count>=total-3?1.25f:1f;
                    float f0=pitch*rise*(1f+(float)(rng.NextDouble()-.5)*.18f);
                    Blip(b,at,final>0?d*.8f:d,f0,Formants[vowel,0],Formants[vowel,1],initial,rng);
                    count++;
                }
                at+=d;
            }
            return b;
        }
        static float Duration(char c)
        {
            if(char.IsWhiteSpace(c))return SpacePause;
            if(c=='.'||c==','||c=='!'||c=='?'||c=='…')return StopPause;
            return char.IsLetterOrDigit(c)?SyllableSeconds:0;
        }
        // One syllable: harmonics of f0 shaped by two formant peaks, with a consonant attack.
        static void Blip(float[] b,float start,float seconds,float f0,float f1,float f2,int initial,System.Random rng)
        {
            int s0=(int)(start*Rate),n=(int)(seconds*Rate);
            int harmonics=Mathf.Clamp((int)(2600f/f0),1,24);
            var gains=new float[harmonics+1];
            for(int k=1;k<=harmonics;k++)
            {
                float f=k*f0,a=(f-f1)/160f,c=(f-f2)/220f;
                gains[k]=Mathf.Exp(-a*a)+.6f*Mathf.Exp(-c*c)+.04f;
            }
            // Fricatives (ㅅㅆㅈㅉㅊㅎ) hiss, stops (ㄱㄲㄷㄸㅂㅃㅋㅌㅍ) click, nasals, ㄹ and ㅇ start soft.
            bool hiss=initial==9||initial==10||initial==12||initial==13||initial==14||initial==18;
            bool click=initial==0||initial==1||initial==3||initial==4||initial==7||initial==8||initial==15||initial==16||initial==17;
            float noise=0;
            for(int i=0;i<n&&s0+i<b.Length;i++)
            {
                float t=(float)i/Rate,env=Mathf.Min(1,t/.008f)*Mathf.Clamp01((seconds-t)/.02f);
                float v=0;
                for(int k=1;k<=harmonics;k++)v+=gains[k]*Mathf.Sin(2*Mathf.PI*k*f0*t);
                v*=.11f;
                if(hiss&&t<.02f){noise+=.6f*((float)rng.NextDouble()*2-1-noise);v=v*(t/.02f)+noise*.18f*(1-t/.02f);}
                else if(click&&t<.006f)v+=((float)rng.NextDouble()*2-1)*.25f*(1-t/.006f);
                b[s0+i]+=Mathf.Clamp(v*env,-1,1);
            }
        }

        // Plays the babble from a person's head; the clip is freed when it ends.
        public static void Speak(string line,float pitch,Vector3 position)
        {
            if(Application.isBatchMode)return;
            var data=Synthesize(line,pitch);
            var clip=AudioClip.Create("voice",data.Length,1,Rate,false);clip.SetData(data,0);
            var o=new GameObject("Voice");o.transform.position=position;
            var s=o.AddComponent<AudioSource>();s.clip=clip;s.spatialBlend=.8f;s.minDistance=2;s.maxDistance=25;
            s.rolloffMode=AudioRolloffMode.Linear;s.volume=.8f*Sfx.Volume;s.Play();
            Object.Destroy(o,clip.length+.1f);Object.Destroy(clip,clip.length+.2f);
        }
    }
}
