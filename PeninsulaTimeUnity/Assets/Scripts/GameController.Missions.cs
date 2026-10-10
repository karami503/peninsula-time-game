using System.Collections.Generic;
using UnityEngine;
namespace PeninsulaTime
{
    // City missions in the 3D district, driven in the car. Delivery (X): stop inside one yellow disc.
    // Checkpoint run (Z): drive through three discs in order. Taxi fare (Y): stop at a pickup, then at a drop-off.
    // Rewards go to the city budget, so play feeds city management. Missions are not saved.
    public partial class GameController
    {
        const float MissionRadius=10f,MissionStopSpeed=1.5f,MissionMinDistance=120f,MissionLegDistance=60f,MissionDiscHeight=.15f;
        const float DeliverySeconds=180f,CheckpointSeconds=150f,FareSeconds=200f;
        const int DeliveryReward=200,CheckpointReward=150,FareReward=180,CheckpointCount=3,MissionPickAttempts=30;
        // stopAtEach: each disc needs a stop (delivery, fare); otherwise the car only drives through (checkpoint run).
        sealed class CityMission
        {
            public bool stopAtEach;
            public int reward,next;
            public float timeLeft;
            public string finishMessage;
            public readonly List<Vector3> stops=new List<Vector3>();
        }
        CityMission mission;
        GameObject missionMarker;

        // A stop ends when the car is stopped inside the disc. Height is ignored.
        public static bool DeliveryDone(Vector3 car,Vector3 target,float speed)
        {
            var offset=car-target;offset.y=0;
            return offset.magnitude<=MissionRadius&&Mathf.Abs(speed)<=MissionStopSpeed;
        }
        // A checkpoint is passed by driving inside the disc; no stop is needed.
        public static bool CheckpointReached(Vector3 car,Vector3 checkpoint)
        {
            var offset=car-checkpoint;offset.y=0;
            return offset.magnitude<=MissionRadius;
        }
        bool CanStartMission(out TrafficGraph roads)
        {
            roads=world==null?null:world.DistrictRoads;
            if(mission!=null){Toast("미션이 이미 진행 중입니다.");return false;}
            if(roads==null||roads.nodes.Count<2){Toast("이 지역에는 미션용 도로가 없습니다.");return false;}
            return true;
        }
        // A random road crossing at least `minimum` metres from `from`; the last pick is kept if none qualifies.
        Vector3 PickRoadPoint(TrafficGraph roads,Vector3 from,float minimum)
        {
            var point=roads.nodes[UnityEngine.Random.Range(0,roads.nodes.Count)];
            for(int attempt=1;attempt<MissionPickAttempts&&Vector3.Distance(point,from)<minimum;attempt++)
                point=roads.nodes[UnityEngine.Random.Range(0,roads.nodes.Count)];
            return point;
        }
        void StartDeliveryMission()
        {
            TrafficGraph roads;if(!CanStartMission(out roads))return;
            var started=new CityMission{stopAtEach=true,timeLeft=DeliverySeconds,reward=DeliveryReward,finishMessage="배달 완료!"};
            started.stops.Add(PickRoadPoint(roads,eye.position,MissionMinDistance));
            BeginMission(started,"배달 미션 시작: 노란 원에 차를 세우세요.");
        }
        void StartCheckpointRun()
        {
            TrafficGraph roads;if(!CanStartMission(out roads))return;
            var started=new CityMission{stopAtEach=false,timeLeft=CheckpointSeconds,reward=CheckpointReward,finishMessage="순찰 완료!"};
            var from=eye.position;
            for(int i=0;i<CheckpointCount;i++)
            {
                var point=PickRoadPoint(roads,from,i==0?MissionMinDistance:MissionLegDistance);
                started.stops.Add(point);from=point;
            }
            BeginMission(started,"순찰 미션 시작: 노란 원 3곳을 차로 순서대로 통과하세요.");
        }
        // Taxi: stop at the pickup disc, then at the drop-off disc.
        void StartTaxiFare()
        {
            TrafficGraph roads;if(!CanStartMission(out roads))return;
            var started=new CityMission{stopAtEach=true,timeLeft=FareSeconds,reward=FareReward,finishMessage="택시 손님 도착!"};
            var pickup=PickRoadPoint(roads,eye.position,MissionMinDistance);
            started.stops.Add(pickup);
            started.stops.Add(PickRoadPoint(roads,pickup,MissionMinDistance));
            BeginMission(started,"택시 호출: 노란 원에서 손님을 태우고 다음 원에 내려 주세요.");
        }
        void BeginMission(CityMission started,string message)
        {
            mission=started;
            ShowMissionMarker(mission.stops[0]);
            Toast(message+(InVehicle()?"":" 먼저 차에 타세요."));
        }
        // Runs every frame: counts down, advances the mission in the car and clears it when leaving the district.
        void UpdateDeliveryMission()
        {
            if(mission==null)return;
            if(mode!="district"){ClearDeliveryMission();return;}
            mission.timeLeft-=Time.deltaTime;
            if(InVehicle())AdvanceMission(ridingCar.transform.position,ridingCar.Speed);
            if(mission!=null&&mission.timeLeft<=0){Toast("미션 시간 초과.");ClearDeliveryMission();}
        }
        void AdvanceMission(Vector3 car,float speed)
        {
            bool reached=mission.stopAtEach?DeliveryDone(car,mission.stops[mission.next],speed):CheckpointReached(car,mission.stops[mission.next]);
            if(!reached)return;
            mission.next++;
            if(mission.next>=mission.stops.Count){FinishMission(mission.reward,mission.finishMessage);return;}
            ShowMissionMarker(mission.stops[mission.next]);
            Toast((mission.stopAtEach?"다음 정차지 ":"체크포인트 ")+mission.next+"/"+mission.stops.Count+" 통과");
        }
        void FinishMission(int reward,string message)
        {
            Economy("seoul").budget+=reward;
            Log("미션 완료 · 도시 예산 +"+reward);
            Toast(message+" 도시 예산 +"+reward);
            ClearDeliveryMission();Save();
        }
        void ClearDeliveryMission()
        {
            mission=null;
            if(missionMarker!=null)Destroy(missionMarker);
            missionMarker=null;
        }
        // A flat yellow disc on the road, with no collider so cars and clicks pass through it.
        void ShowMissionMarker(Vector3 target)
        {
            if(missionMarker!=null)Destroy(missionMarker);
            missionMarker=GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            missionMarker.name="MissionMarker";
            Destroy(missionMarker.GetComponent<Collider>());
            missionMarker.transform.position=new Vector3(target.x,target.y+MissionDiscHeight,target.z);
            missionMarker.transform.localScale=new Vector3(MissionRadius*2f,MissionDiscHeight,MissionRadius*2f);
            var paint=missionMarker.GetComponent<Renderer>();
            paint.shadowCastingMode=UnityEngine.Rendering.ShadowCastingMode.Off;
            paint.material.color=new Color(1f,.8f,.1f,1f);
        }
        // Task, distance to the next disc and time left, shown under the top bar while a mission runs.
        void DrawDeliveryMission()
        {
            if(mission==null)return;
            var from=InVehicle()?ridingCar.transform.position:eye.position;
            var offset=mission.stops[mission.next]-from;offset.y=0;
            string task=!InVehicle()?"차에 타고 노란 원으로 가세요"
                :!mission.stopAtEach?"순찰 "+(mission.next+1)+"/"+mission.stops.Count+": 노란 원을 통과하세요"
                :mission.stops.Count==1?"배달: 노란 원에 차를 세우세요"
                :"택시 "+(mission.next+1)+"/"+mission.stops.Count+": 노란 원에 차를 세우세요";
            // GTA V: the objective box sits top-left inside the safe zone, translucent, with white text.
            float sx=ObjectiveLeft(),sy=hudHeight*SafeZone;
            UiTexture(new Rect(sx,sy,660,32),softTexture);
            UiLabel(new Rect(sx+10,sy+6,640,24),task+" · "+Mathf.RoundToInt(offset.magnitude)+" m · 남은 "+Mathf.CeilToInt(mission.timeLeft)+"초",smallStyle);
        }
    }
}
