using UnityEngine;
namespace PeninsulaTime
{
    // Demolish a built facility from the build panel: half of its materials and half of its budget cost come back.
    public partial class GameController
    {
        const int DemolishShownLimit=8;
        public static int[] DemolishRefund(int[] cost)
        {
            var refund=new int[cost.Length];
            for(int i=0;i<cost.Length;i++)refund[i]=cost[i]/2;
            return refund;
        }
        void DemolishBuilding(int index)
        {
            if(maintenance||index<0||index>=state.buildings.Count)return;
            var placed=state.buildings[index];var info=GameContent.Building(placed.id);
            state.buildings.RemoveAt(index);
            if(info==null){Save();return;}
            var refund=DemolishRefund(info.cost);
            for(int i=0;i<7;i++)state.resources[i]+=refund[i];
            Economy(placed.city).budget+=MoneyCost(info)/2;
            Log(GameContent.City(placed.city).name+" "+info.name+" 철거 · 자재 절반 환급");
            if(mode=="city")world.BuildCityPlot(state);else world.BuildMap(state);
            Save();
        }
        // Newest facilities first; capped so the panel stays readable. The list changes only after it is drawn.
        void DrawDemolishList(CityInfo city)
        {
            GUILayout.Space(12);BeginVerticalDump(cardStyle);Label("건설된 시설 · 철거",headingStyle);
            int shown=0,demolish=-1;
            for(int i=state.buildings.Count-1;i>=0&&shown<DemolishShownLimit;i--)
            {
                var placed=state.buildings[i];if(placed.city!=city.id)continue;
                var info=GameContent.Building(placed.id);if(info==null)continue;
                shown++;
                GUILayout.BeginHorizontal();Label(info.name,bodyStyle);
                GUI.enabled=!maintenance;if(Button("철거"))demolish=i;
                GUILayout.EndHorizontal();
            }
            if(shown==0)Label("아직 건설된 시설이 없습니다.",smallStyle);
            EndVerticalDump();
            if(demolish>=0)DemolishBuilding(demolish);
        }
    }
}
