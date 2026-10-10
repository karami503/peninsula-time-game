using System.Collections.Generic;
using UnityEngine;
namespace PeninsulaTime
{
    // City services reach homes on the city plot. A home counts as served for a need when a school, hospital or
    // leisure site (park or market) stands within ServiceReachSlots cells of it (same grid as WorldBuilder.BuildCityPlot).
    // Coverage is the average of the three served shares (0 to 1). It lifts happiness, so placement matters.
    public partial class GameController
    {
        const int ServiceHappinessPoints=25;
        const float ServiceReachSlots=1.5f;

        // Plot cell of the facility in slot i, in cell units: 6 columns, centred on the plot.
        public static Vector2 PlotCell(int slot){return new Vector2(slot%6-2.5f,slot/6-1.5f);}

        // Pure: share of homes that have a service of each kind in reach, averaged over the three kinds.
        public static float ReachCoverage(IList<int> homes,IList<int> schools,IList<int> hospitals,IList<int> leisure)
        {
            if(homes.Count==0)return 0f;
            return (ServedShare(homes,schools)+ServedShare(homes,hospitals)+ServedShare(homes,leisure))/3f;
        }
        static float ServedShare(IList<int> homes,IList<int> services)
        {
            int served=0;
            foreach(int home in homes)
                foreach(int service in services)
                    if(Vector2.Distance(PlotCell(home),PlotCell(service))<=ServiceReachSlots){served++;break;}
            return served/(float)homes.Count;
        }
        float CityServiceCoverage(CityEconomy economy)
        {
            var homes=new List<int>();var schools=new List<int>();var hospitals=new List<int>();var leisure=new List<int>();
            int slot=0;
            foreach(var placed in state.buildings)
            {
                if(placed.city!=economy.city)continue;
                var info=GameContent.Building(placed.id);
                if(info!=null&&info.category=="주거")homes.Add(slot);
                else if(placed.id=="school")schools.Add(slot);
                else if(placed.id=="hospital")hospitals.Add(slot);
                else if(placed.id=="park"||placed.id=="market")leisure.Add(slot);
                slot++;
            }
            return ReachCoverage(homes,schools,hospitals,leisure);
        }
    }
}
