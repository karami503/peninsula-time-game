using System;
using System.Collections;
using System.Reflection;
using UnityEditor;
using UnityEngine;

namespace PeninsulaTime
{
    // Headless run of the player's loop, without the GUI: each district's delivery and checkpoint missions,
    // demolition, and city growth. It calls the game's own private methods by reflection.
    // Writes save-v1.json, so it refuses to run unless --save-directory points away from the player's real save.
    // Unity -batchmode -quit -projectPath . -executeMethod PeninsulaTime.SimulationCheck.Run -save-directory <scratch>
    public static class SimulationCheck
    {
        const BindingFlags Any = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
        const int CheckpointReward = 150, DeliveryReward = 200, FareReward = 180;
        static int failures;

        public static void Run()
        {
            if (!UsesScratchSaveDirectory())
            {
                Debug.LogError("SimulationCheck: pass --save-directory <scratch folder>; the real save is never written");
                EditorApplication.Exit(1);
                return;
            }
            try
            {
                var camera = new GameObject("Sim camera").AddComponent<Camera>();
                var world = new GameObject("Sim world").AddComponent<WorldBuilder>();
                world.worldCamera = camera;
                var holder = new GameObject("Sim player");
                holder.SetActive(false);
                var game = holder.AddComponent<GameController>();
                game.state = new GameState();
                game.world = world;
                Set(game, "eye", new GameObject("Sim eye").transform);
                for (int district = 0; district < 4; district++)
                {
                    world.BuildDistrict(district);
                    Physics.SyncTransforms();
                    Set(game, "mode", "district");
                    CheckMissions(game, district);
                }
                CheckDemolish(game);
                CheckGrowth(game);
                CheckCityClock(game);
            }
            catch (Exception error)
            {
                failures++;
                Debug.LogError("SimulationCheck exception: " + error);
            }
            Debug.Log("SimulationCheck: " + (failures == 0 ? "passed" : failures + " failed"));
            EditorApplication.Exit(failures == 0 ? 0 : 1);
        }

        static void CheckMissions(GameController game, int district)
        {
            string area = "구역 " + district + " ";
            int budget = Budget(game);
            Call(game, "StartDeliveryMission");
            var delivery = Get(game, "mission");
            Rule(delivery != null && (bool)Get(delivery, "stopAtEach") && ((IList)Get(delivery, "stops")).Count == 1, area + "배달 미션 시작");
            Rule(Get(game, "missionMarker") != null, area + "배달 원 표시");
            var stops = (IList)Get(delivery, "stops");
            Call(game, "AdvanceMission", stops[0], 0f);
            Rule(Get(game, "mission") == null && Budget(game) == budget + DeliveryReward, area + "배달 완료 보상 +" + DeliveryReward);

            Call(game, "StartCheckpointRun");
            var run = Get(game, "mission");
            stops = (IList)Get(run, "stops");
            Rule(stops.Count == 3 && (int)Get(run, "next") == 0 && !(bool)Get(run, "stopAtEach"), area + "순찰 미션 체크포인트 3곳");
            budget = Budget(game);
            for (int i = 0; i < stops.Count; i++) Call(game, "AdvanceMission", stops[i], 0f);
            Rule(Get(game, "mission") == null && Budget(game) == budget + CheckpointReward, area + "순찰 완료 보상 +" + CheckpointReward);

            // Taxi fare: stop at the pickup, then at the drop-off.
            Call(game, "StartTaxiFare");
            var fare = Get(game, "mission");
            stops = (IList)Get(fare, "stops");
            budget = Budget(game);
            Call(game, "AdvanceMission", stops[0], 0f);
            Rule(Get(game, "mission") != null && (int)Get(fare, "next") == 1, area + "택시 손님 태우기");
            Call(game, "AdvanceMission", stops[1], 0f);
            Rule(Get(game, "mission") == null && Budget(game) == budget + FareReward, area + "택시 도착 보상 +" + FareReward);

            Call(game, "StartDeliveryMission");
            Set(Get(game, "mission"), "timeLeft", 0f);
            Call(game, "UpdateDeliveryMission");
            Rule(Get(game, "mission") == null && Get(game, "missionMarker") == null, area + "시간 초과 시 미션·표시 정리");
        }

        static void CheckDemolish(GameController game)
        {
            var state = game.state;
            state.buildings.Add(new PlacedBuilding { id = "park", city = "seoul" });
            int index = state.buildings.Count - 1, count = state.buildings.Count, wood = state.resources[0];
            Call(game, "DemolishBuilding", index);
            Rule(state.buildings.Count == count - 1, "철거: 시설 목록 감소");
            Rule(state.resources[0] == wood + 3, "철거: 목재 절반 환급 (공원 목재 6의 절반)");
        }

        static void CheckGrowth(GameController game)
        {
            var state = game.state;
            foreach (var id in new[] { "house", "house", "house", "school", "park", "hospital" })
                state.buildings.Add(new PlacedBuilding { id = id, city = "seoul" });
            var economy = (CityEconomy)Call(game, "Economy", "seoul");
            for (int turn = 0; turn < 30; turn++) Call(game, "UpdateEconomy", economy);
            Rule(economy.population >= 0 && economy.happiness >= 0 && economy.happiness <= 100, "성장 30기: 인구·행복도 범위 안");
            float coverage = (float)Call(game, "CityServiceCoverage", economy);
            Rule(coverage > 0f && coverage <= 1f, "성장: 서비스 충족 0~1 (" + Mathf.RoundToInt(coverage * 100) + "%)");
        }

        // City clock: 1배 passes a turn every 20 s, 2배 twice as fast, 정지 passes none, and a long frame loses no turns.
        static void CheckCityClock(GameController game)
        {
            var state = game.state;
            Set(game, "mode", "map");
            Set(game, "turnTimer", 0f);
            int turn = state.turn;
            Set(game, "gameSpeed", 0);
            Call(game, "UpdateCityTime", 100f);
            Rule(state.turn == turn, "시계 정지: 시간이 흐르지 않음");
            Set(game, "gameSpeed", 1);
            Call(game, "UpdateCityTime", 20f);
            Rule(state.turn == turn + 1, "1배: 20초에 한 시기");
            Set(game, "gameSpeed", 2);
            Call(game, "UpdateCityTime", 10f);
            Rule(state.turn == turn + 2, "2배: 10초에 한 시기");
            Set(game, "gameSpeed", 3);
            Call(game, "UpdateCityTime", 100f);
            Rule(state.turn == turn + 2 + 20, "4배: 100초에 20시기 (긴 프레임도 누락 없음)");
            Set(game, "gameSpeed", 0);
            Set(game, "turnTimer", 0f);
        }

        static int Budget(GameController game)
        {
            return ((CityEconomy)Call(game, "Economy", "seoul")).budget;
        }

        static bool UsesScratchSaveDirectory()
        {
            var args = Environment.GetCommandLineArgs();
            for (int i = 0; i + 1 < args.Length; i++)
                if (args[i] == "--save-directory")
                    return System.IO.Path.GetFullPath(args[i + 1]) != System.IO.Path.GetFullPath(Application.persistentDataPath);
            return false;
        }

        static void Rule(bool ok, string name)
        {
            Debug.Log("SimulationCheck rule: " + (ok ? "OK   " : "FAIL ") + name);
            if (!ok) failures++;
        }

        static object Call(object target, string name, params object[] args)
        {
            var method = target.GetType().GetMethod(name, Any);
            if (method == null) throw new MissingMethodException(target.GetType().Name, name);
            return method.Invoke(target, args);
        }

        static object Get(object target, string name)
        {
            if (target == null) return null;
            var field = target.GetType().GetField(name, Any);
            if (field == null) throw new MissingFieldException(target.GetType().Name, name);
            return field.GetValue(target);
        }

        static void Set(object target, string name, object value)
        {
            var field = target.GetType().GetField(name, Any);
            if (field == null) throw new MissingFieldException(target.GetType().Name, name);
            field.SetValue(target, value);
        }
    }
}
