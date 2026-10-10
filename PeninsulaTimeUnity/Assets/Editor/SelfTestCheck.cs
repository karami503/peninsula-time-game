using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
namespace PeninsulaTime
{
    // Runs the in-game self-test (GameController.RunSelfTest) on every district and fails on any FAIL line.
    // Unity -batchmode -quit -projectPath . -executeMethod PeninsulaTime.SelfTestCheck.Run
    public static class SelfTestCheck
    {
        public static void Run()
        {
            var camera = new GameObject("SelfTest camera").AddComponent<Camera>();
            var world = new GameObject("SelfTest world").AddComponent<WorldBuilder>();
            world.worldCamera = camera;
            // Inactive, so Awake and Load never touch the player's save.
            var holder = new GameObject("SelfTest player");
            holder.SetActive(false);
            var game = holder.AddComponent<GameController>();
            game.state = new GameState();
            game.world = world;
            int failures = 0;
            // Delivery rule, pure: stopped inside the disc counts; moving or outside does not; height is ignored.
            failures += Rule(GameController.DeliveryDone(new Vector3(3, 0, 4), Vector3.zero, 0f), "배달 원 안 정지");
            failures += Rule(!GameController.DeliveryDone(new Vector3(3, 0, 4), Vector3.zero, 5f), "배달 원 안 주행 불인정");
            failures += Rule(!GameController.DeliveryDone(new Vector3(30, 0, 0), Vector3.zero, 0f), "배달 원 밖 불인정");
            failures += Rule(GameController.DeliveryDone(new Vector3(0, 50, 0), Vector3.zero, 0f), "배달 높이 무시");
            // Checkpoint: passing inside the disc counts; outside does not.
            failures += Rule(GameController.CheckpointReached(new Vector3(4, 0, 0), Vector3.zero), "순찰 체크포인트 통과");
            failures += Rule(!GameController.CheckpointReached(new Vector3(30, 0, 0), Vector3.zero), "순찰 체크포인트 밖 불인정");
            // Services reach homes by plot cell: a service in the same cell or next to a home serves it; far does not.
            failures += Rule(Mathf.Approximately(GameController.ReachCoverage(new List<int> { 0 }, new List<int> { 1 }, new List<int> { 1 }, new List<int> { 1 }), 1f), "서비스 근처 주거 충족");
            // Home 30 is 5 cells away from slot 1, so only home 0 is served: each kind 1/2.
            failures += Rule(Mathf.Approximately(GameController.ReachCoverage(new List<int> { 0, 30 }, new List<int> { 1 }, new List<int> { 1 }, new List<int> { 1 }), 0.5f), "서비스 먼 주거 미충족");
            failures += Rule(Mathf.Approximately(GameController.ReachCoverage(new List<int> { 0 }, new List<int>(), new List<int> { 1 }, new List<int> { 1 }), 2f / 3f), "학교 없음 2/3");
            failures += Rule(Mathf.Approximately(GameController.ReachCoverage(new List<int>(), new List<int> { 1 }, new List<int> { 1 }, new List<int> { 1 }), 0f), "주거 없는 도시 0");
            // Demolish refund, pure: half of each material, rounded down.
            var refund = GameController.DemolishRefund(new[] { 10, 3, 0, 0, 0, 0, 1 });
            failures += Rule(refund[0] == 5 && refund[1] == 1 && refund[6] == 0, "철거 자재 절반 환급");
            // Tickets, pure: buses and BRT have none; the KTX fare is higher than the metro; the trip takes 30 s.
            failures += Rule(GameController.TicketsSold("metro") && GameController.TicketsSold("ktx") && !GameController.TicketsSold("bus") && !GameController.TicketsSold("brt"), "표: 지하철·KTX 판매, 버스·BRT 없음");
            failures += Rule(GameController.TicketFare("ktx") > GameController.TicketFare("metro"), "표 요금: KTX가 지하철보다 비쌈");
            failures += Rule(!GameController.TripDone(29.9f) && GameController.TripDone(30f), "표 이동: 30초에 도착");
            // Every non-bus mode sells tickets: metro, KTX, 무궁화호 and flights. A flight, boarding to arrival, also takes 30 s.
            failures += Rule(GameController.TicketsSold("mugunghwa") && GameController.TicketsSold("flight"), "표: 무궁화호·항공편도 판매");
            failures += Rule(GameController.TicketFare("flight") > 0, "항공권 요금이 있음");
            failures += Rule(Mathf.Abs((66f - GameController.FlightStart(66f, 1.2f)) / GameController.FlightPace(66f, 1.2f) + 1.2f - 30f) < .001f && Mathf.Abs((24f - GameController.FlightStart(24f, 1.2f)) / GameController.FlightPace(24f, 1.2f) + 1.2f - 30f) < .001f, "항공편: 탑승부터 도착까지 30초");
            failures += Rule(GameController.FlightPace(66f, 1.2f) <= 1f && GameController.FlightPace(24f, 1.2f) <= 1f, "항공편: 실제 속도보다 빠르게 재생하지 않음");
            // NPC voice: Hangul splits into consonant/vowel/final; each syllable is one finite, audible blip; personas differ in pitch.
            int ini, vow, fin; Voice.Jamo('한', out ini, out vow, out fin);
            failures += Rule(ini == 18 && vow == 0 && fin == 4, "음성: 한 = ㅎ+ㅏ+ㄴ");
            var babble = Voice.Synthesize("안녕하세요? 반가워요.", 220f); float peak = 0; bool finite = true;
            foreach (var v in babble) { peak = Mathf.Max(peak, Mathf.Abs(v)); finite &= !float.IsNaN(v); }
            failures += Rule(finite && peak > .05f && peak <= 1f && babble.Length > 22050 * .072f * 9, "음성: 음절마다 소리, 클리핑 없음");
            failures += Rule(Voice.PitchFor(3, 0) < Voice.PitchFor(2, 0), "음성: 어르신이 고등학생보다 낮음");
            // Airports: 15 Korean airports with unique codes; kiosk gates stay on Gimpo's counter routes; flight numbers are stable.
            var codes = new HashSet<string>(); foreach (var a in KoreanAirports.All) codes.Add(a.iata);
            failures += Rule(KoreanAirports.All.Length == 15 && codes.Count == 15 && KoreanAirports.ByCode("CJU").cityId == "jeju", "공항: 15곳, 코드 중복 없음");
            failures += Rule(KoreanAirports.ForCity("busan").iata == "PUS" && KoreanAirports.ForCity("seoul").iata == "GMP" && KoreanAirports.ForCity("pohang").iata == "KPO", "공항: 도시별 공항");
            failures += Rule(GameController.KioskGate("GMP", KoreanAirports.ByCode("CJU"), 8) == 1 && GameController.KioskGate("CJU", KoreanAirports.ByCode("PUS"), 4) is int g && g >= 1 && g <= 4, "키오스크: 김포 노선 탑승구 유지, 지방 공항 탑승구 범위");
            failures += Rule(KoreanAirports.FlightNumber("CJU", "PUS") == KoreanAirports.FlightNumber("CJU", "PUS") && KoreanAirports.FlightNumber("CJU", "PUS").Length == 7, "항공편 번호: 같은 노선 같은 번호");
            failures += Rule(Announcer.BusNext("강남역", "신논현역").Contains("다음 정류장은 신논현역") && Announcer.MetroNext("시청").StartsWith("이번 역은 시청, 시청역입니다"), "안내방송: 버스·지하철 문구");
            // Map 3D: a click picks the nearest Seoul district, and only clicks near Seoul open a district 3D.
            failures += Rule(GameController.NearestDistrict(new Vector2(127.0276f, 37.4979f)) == 0 && GameController.NearestDistrict(new Vector2(126.8012f, 37.5583f)) == 3, "지도 3D: 강남·김포공항 클릭이 해당 구역으로");
            failures += Rule(GameController.WithinSeoulReach(new Vector2(127.0276f, 37.4979f)) && !GameController.WithinSeoulReach(new Vector2(129.0756f, 35.1796f)), "지도 3D: 서울 근처만 구역 3D, 부산은 도시 3D");
            for (int district = 0; district < 4; district++)
            {
                world.BuildDistrict(district);
                Physics.SyncTransforms();
                foreach (var line in game.RunSelfTest())
                {
                    Debug.Log("SelfTestCheck district " + district + ": " + line);
                    if (line.StartsWith("FAIL")) failures++;
                }
            }
            Debug.Log("SelfTestCheck: " + (failures == 0 ? "passed" : failures + " failed"));
            EditorApplication.Exit(failures == 0 ? 0 : 1);
        }
        static int Rule(bool ok, string name)
        {
            Debug.Log("SelfTestCheck rule: " + (ok ? "OK   " : "FAIL ") + name);
            return ok ? 0 : 1;
        }
    }
}
