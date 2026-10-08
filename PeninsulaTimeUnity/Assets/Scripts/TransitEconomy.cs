using UnityEngine;

namespace PeninsulaTime
{
    // Transit operation rules inspired by Cities in Motion 2: demand comes from the
    // served cities, fares trade revenue against ridership, and every vehicle costs upkeep.
    // These are this game's own numbers, not the original game's formulas.
    public static class TransitEconomy
    {
        public const int BaseFare=14; // 1,400원 in game units of 100원
        public const int MaxVehicles=40;
        public static int Capacity(string type){return type=="ktx"?700:type=="metro"?300:type=="brt"?90:40;}
        public static int VehicleCost(string type){return type=="ktx"?35:type=="metro"?15:type=="brt"?7:3;}
        public static int VehiclePurchase(string type){return type=="ktx"?130:type=="metro"?60:type=="brt"?24:12;}
        public static int Demand(string type,int populationA,int populationB,int happinessA,int happinessB,int fare)
        {
            float share=type=="ktx"?.18f:type=="metro"?.35f:type=="brt"?.28f:.2f;
            float mood=Mathf.Clamp((happinessA+happinessB)/100f,.4f,1.6f);
            float price=Mathf.Clamp(2f-fare/(float)BaseFare,0f,1.5f);
            return Mathf.RoundToInt((populationA+populationB)*share*mood*price);
        }
        public static int Riders(string type,int vehicles,int demand){return Mathf.Min(demand,vehicles*Capacity(type));}
        public static int Profit(string type,int vehicles,int riders,int fare){return riders*fare/10-vehicles*VehicleCost(type);}
    }
}
