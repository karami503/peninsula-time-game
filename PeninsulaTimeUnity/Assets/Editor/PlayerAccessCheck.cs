using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using UnityEditor;
using UnityEngine;
using Object=UnityEngine.Object;

namespace PeninsulaTime
{
    // Run with --save-directory <disposable-check-directory>: shopping exercises the actual save path.
    public static class PlayerAccessCheck
    {
        static int failures;
        const BindingFlags Flags=BindingFlags.Instance|BindingFlags.NonPublic|BindingFlags.Public;
        static void Expect(bool ok,string message){if(!ok){failures++;Debug.LogError("PlayerAccessCheck: "+message);}}
        static object Call(object target,string method,params object[] args){return target.GetType().GetMethod(method,Flags).Invoke(target,args);}
        static void Set(object target,string name,object value){target.GetType().GetField(name,Flags).SetValue(target,value);}
        static T Add<T>(Transform root,string name) where T:Component
        {var go=new GameObject(name);go.transform.SetParent(root,false);return go.AddComponent<T>();}
        static void CheckBalances(GameController game,int original)
        {Expect(game.state.wallet==original&&game.cardBalance==original,"retired wallet/card fields were changed by a player action");}
        public static void Run()
        {
            failures=0;
            string[] args=Environment.GetCommandLineArgs();string saveDir=null;
            for(int i=0;i+1<args.Length;i++)if(args[i]=="--save-directory")saveDir=args[i+1];
            if(string.IsNullOrEmpty(saveDir)||File.Exists(Path.Combine(saveDir,"save-v1.json")))
            {Debug.LogError("PlayerAccessCheck requires --save-directory pointing to a disposable directory without an existing save-v1.json.");EditorApplication.Exit(1);return;}
            var host=new GameObject("Inactive player access check");host.SetActive(false);
            var game=host.AddComponent<GameController>();game.state=new GameState();
            // Keep only the controller inactive to avoid Awake/save loading. Vehicles must be active,
            // as in the real world: GetComponentInParent intentionally ignores inactive ancestors.
            var fixtures=new GameObject("Active player access fixtures");
            var eye=new GameObject("Eye").transform;eye.SetParent(fixtures.transform,false);Set(game,"eye",eye);
            try
            {
                TransitNetwork.Build(null);
                foreach(int legacyBalance in new[]{0,-1000})
                {
                    game.state.wallet=legacyBalance;game.cardBalance=legacyBalance;
                    var resources=(int[])game.state.resources.Clone();
                    var gate=Add<Barrier>(fixtures.transform,"Automatic gate");gate.kind="fare";gate.blocker=gate.gameObject.AddComponent<BoxCollider>();
                    gate.transform.localPosition=new Vector3(legacyBalance==0?0:10,0,0);eye.position=gate.transform.position+Vector3.back;Set(game,"farePaid",false);game.TapBarrier(gate);
                    Expect(gate.IsOpen&&!gate.blocker.enabled,"empty/negative legacy balance blocked gate");
                    game.TapBarrier(gate);CheckBalances(game,legacyBalance);
                    eye.position=gate.transform.position+Vector3.forward;game.TapBarrier(gate);Expect(gate.IsOpen,"exit gate blocked");

                    var shop=Add<Fixture>(fixtures.transform,"Shop item");shop.kind="shop";shop.hint="생수 구매";shop.detail="생수";
                    int purchases=game.state.purchases.Count;game.UseFixture(shop);
                    Expect(game.state.purchases.Count==purchases+1&&game.state.purchases[purchases]=="생수","empty balance blocked immediate item purchase");
                    var oldShop=Add<Fixture>(fixtures.transform,"Legacy priced item");oldShop.kind="shop";oldShop.hint="음료 구매";oldShop.detail="음료";oldShop.number=3500;game.UseFixture(oldShop);
                    Expect(game.state.purchases.Contains("음료"),"legacy priced fixture no longer purchases");
                    var leisure=Add<Fixture>(fixtures.transform,"Arcade");leisure.kind="leisure";
                    var economy=(CityEconomy)Call(game,"Economy",game.state.selectedCity);int happy=economy.happiness,budget=economy.budget;
                    game.UseFixture(leisure);Expect(economy.happiness==Mathf.Min(100,happy+2)&&economy.budget==budget,"arcade stopped working or charged city budget");
                    var kiosk=Add<Fixture>(fixtures.transform,"Legacy card kiosk");kiosk.kind="card";game.UseFixture(kiosk);CheckBalances(game,legacyBalance);
                    string status=(string)Call(game,"TransitStatus");Expect(!status.Contains("잔액")&&!status.Contains("지갑")&&!status.Contains("교통카드"),"balance display remains in player status");

                    var bus=Add<Cabin>(fixtures.transform,"Bus");bus.kind="bus";
                    Expect((bool)Call(game,"MayBoard",bus),"empty balance blocked bus");Call(game,"BoardBus",new object[]{null});Call(game,"LeaveBus");CheckBalances(game,legacyBalance);
                    var metro=Add<SubwayTrain>(fixtures.transform,"Metro");metro.line=new StationLine("2호선","시청",Color.green,0);
                    var consist=Add<TrainConsist>(metro.transform,"Consist");consist.train=metro;consist.cabin=consist.gameObject.AddComponent<Cabin>();consist.cabin.kind="metro";
                    Set(game,"farePaid",false);Expect((bool)Call(game,"MayBoard",consist.cabin),"metro still requires card authorization");

                    foreach(string kind in new[]{"metro","ktx","mugunghwa","bus"})
                    {
                        var service=Add<StationJourney>(fixtures.transform,"Network "+kind);service.line=new NetLine{kind=kind};service.train=service.transform;
                        service.stops.Add(new NetStation{name="출발"});service.stops.Add(new NetStation{name="도착"});
                        service.doors=service.gameObject.AddComponent<VehicleDoors>();service.doors.cabin=service.gameObject.AddComponent<Cabin>();service.doors.cabin.kind="networkrail";service.doors.Set(1);
                        Expect((bool)Call(game,"MayBoard",service.doors.cabin),"empty balance blocked network "+kind);
                        service.doors.Set(0);Expect(!(bool)Call(game,"MayBoard",service.doors.cabin),"closed-door boarding guard removed for "+kind);
                    }
                    var rail=Add<RailVehicle>(fixtures.transform,"Local KTX");rail.manual=true;rail.stopAt=0;
                    rail.doors=rail.gameObject.AddComponent<VehicleDoors>();rail.doors.cabin=rail.gameObject.AddComponent<Cabin>();rail.doors.cabin.kind="ktx";rail.doors.Set(1);
                    var train=rail.gameObject.AddComponent<KtxTrain>();train.rail=rail;
                    Expect((bool)Call(game,"MayBoardRail",rail.doors.cabin),"empty balance blocked local KTX");Call(game,"BoardRail",rail.doors.cabin);CheckBalances(game,legacyBalance);Call(game,"ClearRail");
                    var checkin=Add<Fixture>(fixtures.transform,"Checkin");checkin.kind="checkin";checkin.number=0;game.UseFixture(checkin);
                    Expect(game.BoardingGate>0,"empty balance blocked flight check-in");CheckBalances(game,legacyBalance);
                    for(int i=0;i<resources.Length;i++)Expect(game.state.resources[i]==resources[i],"player purchases changed city simulation resources");
                }
                var reloaded=JsonUtility.FromJson<GameState>(File.ReadAllText(Path.Combine(saveDir,"save-v1.json")));
                Expect(reloaded!=null&&reloaded.purchases.Contains("생수")&&reloaded.purchases.Contains("음료"),"purchases did not survive save/reload");
            }
            catch(Exception e){failures++;Debug.LogException(e);}
            finally{Object.DestroyImmediate(fixtures);Object.DestroyImmediate(host);}
            Debug.Log("PlayerAccessCheck: "+(failures==0?"passed":failures+" failed")+"; zero/negative legacy balances, gates, shops, leisure, bus, metro, national rail, KTX and check-in");
            EditorApplication.Exit(failures==0?0:1);
        }
    }
}
