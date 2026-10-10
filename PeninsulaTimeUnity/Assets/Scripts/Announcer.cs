using System.IO;
using UnityEngine;

namespace PeninsulaTime
{
    // Transit announcements: a chime, then the line read out. The scripts follow the wording pattern of Seoul subway,
    // Seoul city bus, Korail and Korean airline cabin announcements (our own text, no recordings). On macOS the line is
    // spoken by the system's Korean voice through `say`; elsewhere, and if that fails, the chime and the on-screen toast remain.
    public static class Announcer
    {
        const string SayPath="/usr/bin/say",KoreanVoice="Yuna";
        const int ChimeSilenceMs=1100,WordsPerMinute=185;
        static System.Diagnostics.Process speaking;static bool speechBroken;

        public static string MetroNext(string station){return "이번 역은 "+station+", "+station+"역입니다. 내리실 문은 열차가 멈춘 뒤 열립니다. 내리실 분은 잊으신 물건이 없는지 확인하시기 바랍니다.";}
        public static string TrainNext(string service,string station){return "이번 역은 "+station+"역입니다. "+service+" 열차를 이용해 주셔서 감사합니다. 내리실 고객께서는 잊으신 물건이 없는지 다시 한 번 확인하시기 바랍니다.";}
        public static string BusNext(string stop,string next)
        {
            return "이번 정류장은 "+stop+"입니다."+(string.IsNullOrEmpty(next)?"":" 다음 정류장은 "+next+"입니다.")+" 내리실 분은 미리 하차벨을 눌러 주시기 바랍니다.";
        }
        public static string BusTerminus(){return "이번 정류장은 이 버스의 종점입니다. 두고 내리시는 물건이 없도록 다시 한 번 확인하시기 바랍니다.";}
        public static string FlightBoarding(string flight,string destination,int gate){return destination+"로 가는 "+flight+"편 탑승을 시작하겠습니다. 탑승객 여러분께서는 "+gate+"번 탑승구로 오시기 바랍니다.";}
        public static string FlightDeparture(string flight,string destination){return "손님 여러분, 안녕하십니까. 이 비행기는 "+destination+"까지 가는 "+flight+"편입니다. 이륙을 위해 좌석 등받이를 세워 주시고 좌석 벨트를 매 주시기 바랍니다.";}
        public static string FlightLanding(string airport){return "손님 여러분, 우리 비행기는 잠시 후 "+airport+"에 도착하겠습니다. 좌석 벨트를 매 주시고 휴대 전화는 비행기 모드로 유지해 주시기 바랍니다.";}

        // Chime, then the spoken line; a new announcement cuts off the one still playing.
        public static void Say(string chime,string text)
        {
            Debug.Log("Announce: "+text);
            if(Application.isBatchMode)return;
            Sfx.Play(chime,.45f);
            if(speechBroken||(Application.platform!=RuntimePlatform.OSXPlayer&&Application.platform!=RuntimePlatform.OSXEditor)||!File.Exists(SayPath))return;
            try
            {
                Stop();
                // The text goes through a file so no part of it is ever parsed as a command-line argument.
                string file=Path.Combine(Application.temporaryCachePath,"announce.txt");
                string volume=Mathf.Clamp01(Sfx.Volume*.8f).ToString("F2",System.Globalization.CultureInfo.InvariantCulture);
                File.WriteAllText(file,"[[slnc "+ChimeSilenceMs+"]][[volm "+volume+"]]"+text);
                var info=new System.Diagnostics.ProcessStartInfo(SayPath,"-v "+KoreanVoice+" -r "+WordsPerMinute+" -f \""+file+"\""){UseShellExecute=false,CreateNoWindow=true};
                speaking=System.Diagnostics.Process.Start(info);
            }
            catch(System.Exception e){speechBroken=true;Debug.LogWarning("Announcement speech unavailable, chime and toast only: "+e.Message);}
        }
        // Cuts off speech still playing (a newer announcement, or the game quitting).
        public static void Stop()
        {
            try{if(speaking!=null&&!speaking.HasExited)speaking.Kill();}
            catch(System.Exception e){Debug.LogWarning("Announcement stop failed: "+e.Message);}
        }
    }
}
