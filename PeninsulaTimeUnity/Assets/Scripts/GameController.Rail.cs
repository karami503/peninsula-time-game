using UnityEngine;

namespace PeninsulaTime
{
    // The local Seoul Station train becomes a manual service while the player is aboard; past the end of its mapped
    // track it carries on as a national-network ride (GameController.NetRide) to its first stop.
    public partial class GameController
    {
        RailVehicle railRide;
        Arrival railTicket;
        float railClock;
        bool railAtDestination;
        string railDestination;

        bool MayBoardRail(Cabin c)
        {
            var train=c.GetComponentInParent<KtxTrain>();
            if(train==null||train.rail==null||!train.rail.Dwelling)return Refuse("열차가 승강장에 정차하면 열린 문으로 들어가세요",false);
            Arrival trip;
            if(!KtxTrain.NextTrip(out trip,"ktx"))return Refuse("지금 출발하는 열차가 없습니다",false);
            return true;
        }
        void BoardRail(Cabin c)
        {
            var train=c.GetComponentInParent<KtxTrain>();
            railRide=train!=null?train.rail:null;
            if(railRide==null||!KtxTrain.NextTrip(out railTicket,"ktx")){cabin=null;return;}
            railDestination=TransitNetwork.Bare(railTicket.line.Terminus(railTicket.direction).name);
            railRide.manual=true;railRide.manualOffset=0;
            railClock=0;railAtDestination=false;
            Beep.Play("tap");Toast(TransitSchedule.TrainNumber(railTicket.line,railTicket.direction,railTicket.trip)+" 승차 · "+railDestination+"행 · 출발 후에는 내릴 수 없습니다");
        }
        bool MayLeaveRail()
        {
            if(railAtDestination)return true;
            if(railRide!=null&&railClock<4f&&railRide.doors!=null&&railRide.doors.amount>.8f)return true;
            return Refuse("열차가 달리고 있습니다 · 도착역에서 내리세요",false);
        }
        void LeftRail()
        {
            if(railRide!=null)railRide.manual=false;
            railRide=null;railClock=0;
            if(railAtDestination)Toast(railDestination+"역에 도착했습니다 · 지도로 돌아가 다른 도시를 선택할 수 있습니다");
            else Toast("KTX에서 내렸습니다.");
        }
        void ClearRail()
        {
            if(railRide!=null)railRide.manual=false;
            railRide=null;railClock=0;railAtDestination=false;
        }
        void UpdateRailRide(float dt)
        {
            if(railRide==null||cabin==null||railAtDestination)return;
            railClock+=dt;
            // Keep the doors open long enough to board, then accelerate away from the buffer end.
            float travel=Mathf.Max(0,railClock-4.8f)*TransitSpeed.RailMultiplier;
            if(railRide.doors!=null)railRide.doors.Move(railClock<4f,dt);
            float run=travel*travel*.62f;
            var before=railRide.transform.position;
            railRide.manualOffset=railRide.doors!=null&&railRide.doors.amount>.001f?0:Mathf.Min(railRide.reach+30f,run);
            railRide.Step(0);
            CarryInCabin();
            if(railRide.manualOffset<railRide.reach+30f)return;
            // Past the end of the mapped track: the same train runs on along the line to its first stop.
            var line=railTicket.line;int direction=railTicket.direction;
            var start=line.stops.Find(s=>TransitNetwork.Bare(s.name)=="서울")??line.stops[0];
            var rail=railRide;railRide=null;
            var heading=rail.transform.position-before;if(heading.sqrMagnitude<1e-6f)heading=-rail.Direction;
            StartNetRideFromRail(rail,line,direction,start,heading,2f*.62f*travel*TransitSpeed.RailMultiplier);
        }
    }
}
