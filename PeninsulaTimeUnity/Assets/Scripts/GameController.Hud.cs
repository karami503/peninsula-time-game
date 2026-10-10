using UnityEngine;
namespace PeninsulaTime
{
    // Two HUD layouts, each modelled on a reference game.
    // Street views (3D districts, interiors, rail platforms, rides, city streets) follow the GTA V HUD (GTA Base HUD guide):
    // no plates, the radar bottom-left and the money readout floating top-right inside a 2.5 % safe zone (measured on 1080p
    // GTA V screenshots), the objective box top-left, the last spend in red under the balance, and H for the menu.
    // City building follows the Cities: Skylines HUD (official user manual, "User Interface"): no top bar, a bottom dock with
    // the category toolbar over a status strip (time, city name, bank balance, population and happiness), 1/2/3 and Space for
    // the clock, and info panels floating over the map.
    public partial class GameController
    {
        // City side panel: the Cities: Skylines policies panel - on the right, 19.7% of the width, 5.8% from the top, down to the dock.
        const float CityDockHeight=96f,CityPanelRightGap=4f,CityPanelTop=52f,CityPanelWidth=284f,CityPanelDockGap=4f;
        // Street menu: the GTA V interaction menu measured at 1080p - 40% of the screen height wide, a 10% banner,
        // a 3.4% subtitle row and 3.5% rows; it starts at the radar's left edge, as GTA's does.
        const float MenuTop=14f,MenuWidth=360f,MenuBannerHeight=90f,MenuSubtitleHeight=31f,MenuRowHeight=31.5f;
        const float CityTabWidth=56f,CityTabHeight=44f,CityTabGap=49f,CityStatusHeight=26f;
        const float SecondsPerCityTurn=20f,MetresPerSecondToKmh=3.6f,SafeZone=.025f,SpendShownSeconds=6f;
        static readonly string[] SpeedLabels={"정지","1배","2배","4배"};
        // Cities: Skylines speeds: normal, fast (2x) and very fast (4x).
        static readonly int[] SpeedMultipliers={0,1,2,4};
        int gameSpeed=0,pausedSpeed=1;
        float turnTimer=0f;
        bool streetMenu=false;
        // HUD size in GUI units, set by OnGUI each frame; draw helpers that take no size read it for the safe zone.
        float hudWidth=1440f,hudHeight=900f;
        int spendLast=int.MinValue,spendDelta;
        float spendUntil;
        string incomeCity;
        int incomeTurn=-1,incomeBudget,incomePopulation,budgetChange,populationChange;
        GUIStyle moneyStyle,speedStyle,spendStyle,gainStyle,clockStyle,cityNameStyle,dockValueStyle,deltaGainStyle,deltaLossStyle;
        GUIStyle toolStyle,toolSelectedStyle,dockButtonStyle,dockAccentStyle,menuBannerStyle,menuSubtitleStyle,menuRowStyle,menuRowSelectedStyle;

        // Street views: 3D districts, station and airport interiors, rail platforms, rides and city streets.
        bool StreetHud(){return mode=="district"||mode=="interior"||mode=="carved"||mode=="rail"||mode=="ride"||(mode=="city"&&cityStreet);}
        // Edges of the 3D view in GUI units. Street views fill the screen; city views leave room for the panel and the dock.
        float WorldLeft(){return 0f;}
        float WorldRight(){return StreetHud()?hudWidth:CityPanelLeft()-12f;}
        float CityPanelLeft(){return hudWidth-CityPanelWidth-CityPanelRightGap;}
        float WorldTop(){return 0f;}
        float WorldBottom(){return StreetHud()?0f:CityDockHeight;}
        // Left edge of the objective and ticket boxes: the safe zone, or right of the menu panel while H is open.
        float ObjectiveLeft(){return streetMenu?MenuLeft()+MenuWidth+16f:hudWidth*SafeZone;}
        float MenuLeft(){return Mathf.Round(hudHeight*.04f);}
        // Map buttons sit on the left, opposite the side panel.
        Rect MapControlsRect(float w){return new Rect(18f,116f,79f,223f);}
        Rect MapPanRect(float w,float h){return new Rect(26f,h-CityDockHeight-152f,176f,140f);}

        void EnsureHudStyles()
        {
            if(moneyStyle!=null)return;
            moneyStyle=new GUIStyle(metricStyle){fontSize=30,alignment=TextAnchor.MiddleRight};
            moneyStyle.normal.textColor=Color.white;
            speedStyle=new GUIStyle(metricStyle){fontSize=32,alignment=TextAnchor.MiddleRight};
            speedStyle.normal.textColor=Color.white;
            spendStyle=new GUIStyle(metricStyle){fontSize=18,alignment=TextAnchor.MiddleRight};
            spendStyle.normal.textColor=new Color(.93f,.27f,.27f);
            gainStyle=new GUIStyle(spendStyle);
            gainStyle.normal.textColor=new Color(.55f,.95f,.6f);
            clockStyle=new GUIStyle(smallStyle){alignment=TextAnchor.MiddleRight};
            cityNameStyle=new GUIStyle(metricStyle){fontSize=14,alignment=TextAnchor.MiddleCenter};
            menuBannerStyle=new GUIStyle(titleStyle){fontSize=34,alignment=TextAnchor.MiddleLeft,wordWrap=false};
            menuBannerStyle.normal.textColor=new Color(.06f,.08f,.09f);
            menuSubtitleStyle=new GUIStyle(kickerStyle){fontSize=13,alignment=TextAnchor.MiddleLeft};
            menuRowStyle=new GUIStyle(navStyle){margin=new RectOffset(0,0,0,0)};
            menuRowSelectedStyle=new GUIStyle(navSelectedStyle){margin=new RectOffset(0,0,0,0)};
            dockValueStyle=new GUIStyle(metricStyle){fontSize=15};
            deltaGainStyle=new GUIStyle(smallStyle){font=koreanBoldFont};
            deltaGainStyle.normal.textColor=new Color(.55f,.95f,.6f);
            deltaLossStyle=new GUIStyle(deltaGainStyle);
            deltaLossStyle.normal.textColor=new Color(.93f,.4f,.35f);
            toolStyle=new GUIStyle(navStyle){alignment=TextAnchor.MiddleCenter,fontSize=14,padding=new RectOffset(2,2,0,0)};
            // Figma "Tab/Toolbar" selected: gold fill with dark text.
            toolSelectedStyle=new GUIStyle(accentButtonStyle){alignment=TextAnchor.MiddleCenter,fontSize=14,padding=new RectOffset(2,2,0,0)};
            dockButtonStyle=new GUIStyle(buttonStyle){fontSize=12,padding=new RectOffset(2,2,0,0)};
            dockAccentStyle=new GUIStyle(accentButtonStyle){fontSize=12,padding=new RectOffset(2,2,0,0)};
        }
        // GTA text sits straight on the scene with a dark drop shadow instead of a plate.
        void ShadowLabel(Rect rect,string text,GUIStyle style)
        {
            var color=style.normal.textColor;
            style.normal.textColor=new Color(0,0,0,.85f);
            UiLabel(new Rect(rect.x+1.5f,rect.y+1.5f,rect.width,rect.height),text,style);
            style.normal.textColor=color;
            UiLabel(rect,text,style);
        }
        // The balance change since the last frame it moved, shown for a few seconds under the money readout.
        void TrackSpend(int budget)
        {
            if(spendLast==int.MinValue){spendLast=budget;return;}
            if(budget==spendLast)return;
            spendDelta=budget-spendLast;spendLast=budget;spendUntil=Time.time+SpendShownSeconds;
        }
        // Per-turn change in budget and population for the dock, as Cities: Skylines shows weekly income and population growth.
        void TrackCityStats(string cityId,CityEconomy economy)
        {
            if(incomeCity!=cityId||incomeTurn<0)
            {
                incomeCity=cityId;incomeTurn=state.turn;incomeBudget=economy.budget;incomePopulation=economy.population;
                budgetChange=0;populationChange=0;return;
            }
            if(incomeTurn==state.turn)return;
            budgetChange=economy.budget-incomeBudget;populationChange=economy.population-incomePopulation;
            incomeTurn=state.turn;incomeBudget=economy.budget;incomePopulation=economy.population;
        }

        void DrawStreetHud(float w,float h)
        {
            EnsureHudStyles();
            float sx=w*SafeZone,sy=h*SafeZone;
            int budget=Economy("seoul").budget;
            TrackSpend(budget);
            var money=new Rect(w-sx-340f,sy-4f,340f,40f);
            ShadowLabel(money,budget.ToString("N0"),moneyStyle);
            float y=money.yMax-4f;
            if(Time.time<spendUntil)
            {
                ShadowLabel(new Rect(money.x,y,money.width,24f),(spendDelta>0?"+":"-")+Mathf.Abs(spendDelta).ToString("N0"),spendDelta>0?gainStyle:spendStyle);
                y+=24f;
            }
            ShadowLabel(new Rect(money.x,y,money.width,20f),DayCycle.Clock,clockStyle);
            if(InVehicle())
                ShadowLabel(new Rect(w-sx-340f,h-sy-44f,340f,44f),Mathf.RoundToInt(Mathf.Abs(ridingCar.Speed)*MetresPerSecondToKmh)+" km/h",speedStyle);
            string hint=(InVehicle()?"Esc 차에서 내리기 · H 메뉴":"H 메뉴 · Esc 지도")+" · M 레이더";
            UiTexture(new Rect(w*.5f-300,h-44,600,30),softTexture);
            UiLabel(new Rect(w*.5f-290,h-40,580,24),hint,smallStyle);
            DrawDeliveryMission();
            DrawTicketTrip(w);
            if(streetMenu)DrawStreetMenu(h);
        }
        // The menu lists the tabs and shows the active tab's panel. A tab pick is applied after the layout pass.
        void DrawStreetMenu(float h)
        {
            var panel=new Rect(MenuLeft(),MenuTop,MenuWidth,h-MenuTop*2);
            UiBox(panel,"",boxStyle);
            var banner=new Rect(panel.x,panel.y,panel.width,MenuBannerHeight);
            UiTexture(banner,goldTexture);
            UiLabel(new Rect(banner.x+20,banner.y,banner.width-40,banner.height),"메뉴",menuBannerStyle);
            UiTexture(new Rect(panel.x,banner.yMax,panel.width,MenuSubtitleHeight),inkTexture);
            UiLabel(new Rect(panel.x+16,banner.yMax,panel.width-32,MenuSubtitleHeight),"H 닫기",menuSubtitleStyle);
            float top=banner.yMax+MenuSubtitleHeight;
            float rows=tabs.Length*MenuRowHeight;
            GUILayout.BeginArea(new Rect(panel.x,top,panel.width,rows));
            int picked=-1;
            for(int i=0;i<tabs.Length;i++)
                if(DumpButton(tabs[i],tab==tabs[i]?menuRowSelectedStyle:menuRowStyle,GUILayout.Height(MenuRowHeight)))picked=i;
            GUILayout.EndArea();
            GUILayout.BeginArea(new Rect(panel.x+16,top+rows+10,panel.width-32,panel.yMax-top-rows-24));
            scroll=BeginScrollDump(scroll);
            DrawActiveTab();
            EndScrollDump();GUILayout.EndArea();
            if(picked>=0)SelectTab(picked);
        }

        // Bottom dock (toolbar row over status row), floating info panel on the left, resources at the top centre.
        void DrawCityHud(float w,float h)
        {
            EnsureHudStyles();
            var city=GameContent.City(state.selectedCity);
            var economy=Economy(city.id);
            TrackCityStats(city.id,economy);
            TrackSpend(Economy("seoul").budget);
            float dockTop=h-CityDockHeight;

            var panel=new Rect(CityPanelLeft(),CityPanelTop,CityPanelWidth,dockTop-CityPanelTop-CityPanelDockGap);
            UiBox(panel,"",boxStyle);
            GUILayout.BeginArea(new Rect(panel.x+16,panel.y+14,panel.width-32,panel.height-28));
            scroll=BeginScrollDump(scroll);
            DrawActiveTab();
            EndScrollDump();GUILayout.EndArea();

            // Top centre is where Cities: Skylines puts the Chirper; the stockpile goes here.
            string resources=ResourceLine().TrimEnd();
            var resourceSize=smallStyle.CalcSize(new GUIContent(resources));
            UiTexture(new Rect(w*.5f-resourceSize.x*.5f-12f,12f,resourceSize.x+24f,26f),softTexture);
            UiLabel(new Rect(w*.5f-resourceSize.x*.5f,16f,resourceSize.x+8f,20f),resources,smallStyle);
            // The advisor's line, just above the dock.
            float logLeft=MapPanRect(w,h).xMax+12f;
            UiLabel(new Rect(logLeft,dockTop-28f,WorldRight()-logLeft-8f,22f),state.log.Count>0?state.log[0]:"대한민국의 도시를 설계하세요.",smallStyle);

            UiTexture(new Rect(0,dockTop,w,CityDockHeight),inkTexture);
            UiTexture(new Rect(0,dockTop,w,1),goldTexture);
            // Toolbar row: the categories, centred a little right of the middle; slot spacing puts the icon row at 32-78% of the width as in Cities: Skylines.
            float toolbarWidth=tabs.Length*CityTabWidth+(tabs.Length-1)*CityTabGap;
            float toolbarLeft=w*.55f-toolbarWidth*.5f;
            // Icons, as in Cities: Skylines; the category name shows as a tooltip while the pointer is over the icon.
            if(tabIcons==null)LoadTabIcons();
            for(int i=0;i<tabs.Length;i++)
            {
                var slot=new Rect(toolbarLeft+i*(CityTabWidth+CityTabGap),dockTop+8f,CityTabWidth,CityTabHeight);
                if(UiButton(slot,new GUIContent("",tabs[i]),tab==tabs[i]?toolSelectedStyle:toolStyle))SelectTab(i);
                UiTexture(new Rect(slot.center.x-TabIconSize*.5f,slot.center.y-TabIconSize*.5f,TabIconSize,TabIconSize),tabIcons[i],ScaleMode.ScaleToFit,true);
            }
            if(!string.IsNullOrEmpty(GUI.tooltip))
            {
                var tip=new GUIContent(GUI.tooltip);var tipSize=smallStyle.CalcSize(tip);
                var tipRect=new Rect(Mathf.Clamp(Event.current.mousePosition.x-tipSize.x*.5f-10f,4f,w-tipSize.x-24f),dockTop-34f,tipSize.x+20f,24f);
                UiTexture(tipRect,softTexture);UiLabel(new Rect(tipRect.x+10f,tipRect.y+2f,tipSize.x+4f,20f),tip,smallStyle);
            }

            // Status row, left to right: time controls and date, city name, bank balance, population and happiness, actions.
            float rowY=dockTop+CityTabHeight+14f;
            for(int speed=0;speed<SpeedLabels.Length;speed++)
                if(UiButton(new Rect(16f+speed*48f,rowY,44f,CityStatusHeight),SpeedLabels[speed],gameSpeed==speed?dockAccentStyle:dockButtonStyle))gameSpeed=speed;
            UiLabel(new Rect(16f+SpeedLabels.Length*48f+6f,rowY+3f,70f,20f),"시기 "+state.turn,smallStyle);
            // Plate positions follow a real Cities: Skylines screenshot: name plate 19-32 % of the width, the three demand bars
            // at 33-37 %, balance centred near 57 % and population near 71 %.
            var namePlate=new Rect(w*.193f,rowY,w*.127f,CityStatusHeight);
            UiTexture(namePlate,cardTexture);
            UiLabel(namePlate,city.name+" · "+GameContent.Modern.name,cityNameStyle);
            DrawDemandBars(w*.333f,rowY,city,economy);
            // The balance plate spans 50-63 % of the width in the screenshot.
            var moneyPlate=new Rect(w*.567f-91.5f,rowY,183f,CityStatusHeight);
            UiTexture(moneyPlate,cardTexture);
            UiLabel(new Rect(moneyPlate.x+8f,rowY+3f,36f,20f),"예산",kickerStyle);
            UiLabel(new Rect(moneyPlate.x+44f,rowY,80f,CityStatusHeight),economy.budget.ToString("N0"),dockValueStyle);
            UiLabel(new Rect(moneyPlate.x+126f,rowY+3f,54f,20f),Signed(budgetChange),budgetChange<0?deltaLossStyle:deltaGainStyle);
            var peoplePlate=new Rect(w*.65f,rowY,180f,CityStatusHeight);
            UiTexture(peoplePlate,cardTexture);
            UiLabel(new Rect(peoplePlate.x+8f,rowY+3f,36f,20f),"인구",kickerStyle);
            UiLabel(new Rect(peoplePlate.x+40f,rowY,56f,CityStatusHeight),economy.population.ToString("N0"),dockValueStyle);
            UiLabel(new Rect(peoplePlate.x+94f,rowY+3f,34f,20f),Signed(populationChange),populationChange<0?deltaLossStyle:deltaGainStyle);
            // Happiness: a coloured dot and the score, where the manual has the smiley.
            GUI.color=economy.happiness>=60?new Color(.55f,.95f,.6f):economy.happiness>=40?new Color(.95f,.78f,.3f):new Color(.93f,.35f,.3f);
            UiTexture(new Rect(peoplePlate.x+132f,rowY+8f,10f,10f),Texture2D.whiteTexture);
            GUI.color=Color.white;
            UiLabel(new Rect(peoplePlate.x+148f,rowY+3f,30f,20f),economy.happiness.ToString(),smallStyle);
            if(UiButton(new Rect(w-318f,rowY,150f,CityStatusHeight),"다음 시기 →",dockAccentStyle))
            {Tick();Toast("도시 운영이 한 시기 진행되었습니다.");}
            if(UiButton(new Rect(w-158f,rowY,140f,CityStatusHeight),"지금 저장",dockButtonStyle)){Save();Toast("저장했습니다.");}

            if(tab=="세계·외교")DrawWorldAtlas(w,h);
            if((mode=="map"||mode=="city")&&(tab=="지도"||tab=="교통")&&!cityStreet)DrawMapControls(w,h);
        }
        const float TabIconSize=32f;
        Texture2D[] tabIcons;
        void LoadTabIcons()
        {
            tabIcons=new Texture2D[tabs.Length];
            for(int i=0;i<tabs.Length;i++)tabIcons[i]=Resources.Load<Texture2D>("UI/tab-"+i)??Texture2D.whiteTexture;
        }
        static string Signed(int value){return (value>0?"+":"")+value;}
        // Zoning demand bars, as left of the balance in Cities: Skylines: residential green, commercial blue, industrial amber.
        // The game has no zones, so each bar reads a figure it does have: free building plots, happiness, and room for more people.
        static readonly Color[] DemandColors={new Color(.35f,.78f,.3f),new Color(.25f,.62f,.95f),new Color(.95f,.62f,.25f)};
        void DrawDemandBars(float x,float y,CityInfo city,CityEconomy economy)
        {
            int used=0;
            foreach(var placed in state.buildings)if(placed.city==city.id)used++;
            var levels=new[]{1f-used/(float)CityCapacity(city),economy.happiness/100f,1f-economy.population/200f};
            for(int i=0;i<levels.Length;i++)
            {
                var bar=new Rect(x+i*16f,y,13f,CityStatusHeight);
                UiTexture(bar,cardTexture);
                float fill=Mathf.Max(2f,Mathf.Clamp01(levels[i])*bar.height);
                GUI.color=DemandColors[i];UiTexture(new Rect(bar.x,bar.yMax-fill,bar.width,fill),Texture2D.whiteTexture);GUI.color=Color.white;
            }
        }
        void DrawActiveTab()
        {
            if(tab=="지도")DrawMapPanel();else if(tab=="생산·건설")DrawBuildPanel();else if(tab=="교통")DrawTransitPanel();else if(tab=="세계·외교")DrawWorldPanel();else if(tab=="온라인")DrawOnlinePanel();else DrawWalkPanel();
        }
        string ResourceLine()
        {
            string line="";
            for(int i=0;i<7;i++)line+=GameContent.ResourceNames[i]+" "+state.resources[i].ToString("N0")+"   ";
            return line;
        }

        // H opens the street menu in 3D views; leaving 3D closes it. In the city views 1, 2 and 3 pick the clock speed and
        // Space pauses and resumes, as in Cities: Skylines.
        void UpdateHudInput()
        {
            if(StreetHud())
            {
                if(GUIUtility.keyboardControl==0&&Input.GetKeyDown(KeyCode.H))streetMenu=!streetMenu;
                return;
            }
            streetMenu=false;
            if(maintenance||GUIUtility.keyboardControl!=0||(mode!="map"&&mode!="city"))return;
            for(int speed=1;speed<SpeedLabels.Length;speed++)
                if(Input.GetKeyDown((KeyCode)((int)KeyCode.Alpha1+speed-1)))gameSpeed=speed;
            if(Input.GetKeyDown(KeyCode.Space))
            {
                if(gameSpeed>0){pausedSpeed=gameSpeed;gameSpeed=0;}
                else gameSpeed=pausedSpeed;
            }
        }
        // Time controls: at 1배 one city turn passes every SecondsPerCityTurn; 2배 and 4배 pass turns faster.
        // seconds is real time since the last call (Time.deltaTime in Update); SimulationCheck passes it directly.
        void UpdateCityTime(float seconds)
        {
            if(gameSpeed==0||maintenance||StreetHud())return;
            if(mode!="map"&&mode!="city")return;
            turnTimer+=seconds*SpeedMultipliers[gameSpeed];
            while(turnTimer>=SecondsPerCityTurn){turnTimer-=SecondsPerCityTurn;Tick();}
        }
    }
}
