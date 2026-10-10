using System.Collections.Generic;
using UnityEngine;

namespace PeninsulaTime
{
    // The real transit network on the national map (F3 교통): line lists, route maps with the trains and buses
    // running now, BIS-style arrival and departure boards for any station or stop, and an editor for adding and
    // deleting stations and lines (kept in the save).
    public partial class GameController
    {
        static readonly string[] NetworkKinds={"metro","ktx","mugunghwa","bus","brt"};
        static readonly string[] MetroRegions={"수도권","부산","대구","대전","광주","수도권 계획","창원 계획","사용자"};
        static readonly string[] LinePalette={"#0052A4","#00A84D","#EF7C1C","#00A5DE","#996CAC","#CD7C2F","#747F00","#E6186C","#BDB092","#D4003B","#1E5BA8","#D1495B"};
        NetLine selectedLine;NetStation selectedStation,hoveredStation;
        bool streetBoard; // the board was opened from a bus stop's BIS screen in the 3D streets
        string networkKind="metro",networkRegion="수도권",newStationName="",newLineName="",newLineKind="metro",pendingDelete="";
        int newLineColour;bool networkEdit,placingStation;
        Rect boardRect;Vector2 boardScroll;
        string stationSearch="";
        Texture2D dotTexture;GUIStyle chipStyle,chipButtonStyle,stationLabelStyle,boardLineStyle,boardTitleStyle;
        readonly List<Rect> labelRects=new List<Rect>();

        bool NetworkMapVisible(){return mode=="map"&&(tab=="지도"||tab=="교통");}
        // "서면역을", "강남을": the object particle that fits the last syllable's final consonant.
        static string WithObject(string word)
        {
            char last=word.Length>0?word[word.Length-1]:'가';
            bool batchim=last>='가'&&last<='힣'&&(last-'가')%28!=0;
            return word+(batchim?"을":"를");
        }
        static string Hex(Color c){return "#"+ColorUtility.ToHtmlStringRGB(c);}
        static string Chip(NetLine line){return "<color="+Hex(line.color)+">●</color> "+line.name;}
        static string StationTitle(NetStation s){return s.name.EndsWith("역")||s.lines.TrueForAll(l=>l.kind=="bus"||l.kind=="brt")?s.name:s.name+"역";}
        static bool IsRail(NetStation s){return s.lines.Exists(l=>l.kind=="ktx"||l.kind=="mugunghwa");}
        static bool IsMetro(NetStation s){return s.lines.Exists(l=>l.kind=="metro");}

        void EnsureNetworkStyles()
        {
            if(chipStyle!=null)return;
            chipStyle=new GUIStyle(bodyStyle){richText=true};
            boardTitleStyle=new GUIStyle(chipStyle){fontSize=19};
            chipButtonStyle=new GUIStyle(buttonStyle){richText=true,alignment=TextAnchor.MiddleLeft,wordWrap=true};
            boardLineStyle=new GUIStyle(smallStyle){richText=true,wordWrap=true};
            stationLabelStyle=new GUIStyle(GUI.skin.label){font=koreanBoldFont,fontSize=11,alignment=TextAnchor.MiddleLeft,wordWrap=false,clipping=TextClipping.Overflow,padding=new RectOffset(3,3,0,0)};
            stationLabelStyle.normal.textColor=new Color(.08f,.09f,.10f);stationLabelStyle.normal.background=Solid(new Color(1f,1f,1f,.82f));
            const int size=32;dotTexture=new Texture2D(size,size,TextureFormat.RGBA32,false);
            for(int y=0;y<size;y++)for(int x=0;x<size;x++)
            {
                float d=Vector2.Distance(new Vector2(x+.5f,y+.5f),new Vector2(size*.5f,size*.5f))/(size*.5f);
                // White disc with a dark ring, anti-aliased at the rim.
                var c=d<.62f?Color.white:new Color(.12f,.13f,.15f);c.a=Mathf.Clamp01((1f-d)*size*.5f);
                dotTexture.SetPixel(x,y,c);
            }
            dotTexture.Apply();
        }

        // ---------------------------------------------------------------- left panel (F3)
        // Buttons in rows of three so the tab strips fit the side panel; returns the clicked index or -1.
        int ButtonRows(string[] labels,System.Func<int,bool> selected)
        {
            int clicked=-1;
            for(int i=0;i<labels.Length;i++)
            {
                if(i%3==0)GUILayout.BeginHorizontal();
                if(Button(labels[i],selected(i)))clicked=i;
                if(i%3==2||i==labels.Length-1)GUILayout.EndHorizontal();
            }
            return clicked;
        }
        void DrawNetworkPanel()
        {
            EnsureNetworkStyles();
            Label("03  /  TRANSIT",kickerStyle);Label("교통망 · 노선도",titleStyle);
            Label("전국 역 검색 · 이름 입력 후 3D 방문",smallStyle);
            stationSearch=GUILayout.TextField(stationSearch,textFieldStyle);
            if(!string.IsNullOrWhiteSpace(stationSearch))
            {
                int matches=0;
                foreach(var station in TransitNetwork.Stations)
                    if(station.name.Contains(stationSearch.Trim()))
                    {
                        if(Button(station.name+" · 3D 방문"))VisitNetworkStation(station);
                        if(++matches>=8)break;
                    }
                if(matches==0)Label("일치하는 역이 없습니다.",smallStyle);
            }
            Label("지도에서 선택한 역 방문: F8 · 미선택 시 서울역",smallStyle);
            Label("노선과 정류장: OpenStreetMap. 시간표는 배차 간격과 운행 시간으로 만든 시뮬레이션이며 현재 시각("+TransitSchedule.Clock(TransitSchedule.Now)+")에 맞춰 움직입니다. 지도에서 역을 누르면 실시간 도착 정보가 뜹니다.",smallStyle);
            int pickedKind=ButtonRows(System.Array.ConvertAll(NetworkKinds,TransitNetwork.KindLabel),i=>networkKind==NetworkKinds[i]);
            if(pickedKind>=0){networkKind=NetworkKinds[pickedKind];SelectLine(null,false);}
            if(selectedLine!=null){DrawRouteMap(selectedLine);return;}
            if(networkKind=="metro")
            {
                int pickedRegion=ButtonRows(MetroRegions,i=>networkRegion==MetroRegions[i]);
                if(pickedRegion>=0)networkRegion=MetroRegions[pickedRegion];
            }
            if(networkKind=="brt")Label("BRT·중앙버스전용차로 "+TransitNetwork.Corridors.Count+"구간이 지도에 빨간 선으로 표시됩니다. BRT 노선은 편집에서 만들 수 있습니다.",smallStyle);
            int shown=0;
            foreach(var line in TransitNetwork.Lines)
            {
                if(line.kind!=networkKind)continue;
                if(networkKind=="metro"&&(networkRegion=="사용자"?!line.custom:line.region!=networkRegion))continue;
                if(GUILayout.Button(Chip(line)+"  <size=11>"+line.stops.Count+"개 "+(line.kind=="bus"||line.kind=="brt"?"정류장":"역")+"</size>",chipButtonStyle))SelectLine(line,true);
                shown++;
            }
            if(shown==0)Label(networkKind=="bus"?"이 지역 버스 노선 자료가 없습니다.":"노선이 없습니다.",smallStyle);
            GUILayout.Space(12);
            if(Button(networkEdit?"편집 끝내기":"노선·역 편집 (추가·삭제)",networkEdit)){networkEdit=!networkEdit;placingStation=false;pendingDelete="";}
            if(networkEdit)DrawNetworkEditor();
            GUILayout.Space(18);
        }

        void SelectLine(NetLine line,bool focus)
        {
            selectedLine=line;world.Highlight(line);scroll=Vector2.zero;placingStation=placingStation&&line!=null;
            if(line!=null&&focus)FocusMapOn(line.stops);
        }
        void SelectStation(NetStation s,bool focus)
        {
            selectedStation=s;pendingDelete="";boardScroll=Vector2.zero;
            if(s!=null&&focus)FocusMapOn(new List<NetStation>{s});
        }
        // Centres the national map on the stations and zooms so they all fit.
        void FocusMapOn(List<NetStation> stops)
        {
            if(stops.Count==0||mode!="map")return;
            var min=stops[0].MapPosition(0);var max=min;
            foreach(var s in stops){var p=s.MapPosition(0);min=Vector3.Min(min,p);max=Vector3.Max(max,p);}
            var centre=(min+max)*.5f;
            viewCamera.transform.position=centre+new Vector3(0,155,-50);viewCamera.transform.LookAt(centre);
            float aspect=Mathf.Max(.5f,viewCamera.aspect);
            viewCamera.orthographicSize=Mathf.Clamp(Mathf.Max(max.z-min.z,(max.x-min.x)/aspect)*.62f+.4f,stops.Count==1?2.5f:1.2f,80f);
        }

        // 노선도: stops top to bottom with the vehicles running now (▼ toward the last stop, ▲ toward the first).
        void DrawRouteMap(NetLine line)
        {
            if(Button("← 노선 목록")){SelectLine(null,false);return;}
            BeginVerticalDump(cardStyle);
            GUILayout.Label("<b>"+Chip(line)+"</b>",chipStyle);
            float total=line.offsets[line.offsets.Length-1];
            Label(line.KindLabel+" · 첫차 "+TransitSchedule.Clock(line.firstMinute*60)+" · 막차 "+TransitSchedule.Clock(line.lastMinute*60),smallStyle);
            Label("배차 출퇴근 "+line.peakMinutes+"분 · 평시 "+line.offPeakMinutes+"분 · 전 구간 약 "+Mathf.RoundToInt(total/60f)+"분",smallStyle);
            if(Button("지도에서 보기"))FocusMapOn(line.stops);
            EndVerticalDump();
            var here=new Dictionary<int,string>();
            var running=TransitSchedule.Running(line,TransitSchedule.Now);
            foreach(var v in running)
            {
                int direction=(int)v.x;float t=v.y*total;int order=0;
                for(int k=1;k<line.stops.Count;k++)if(TransitSchedule.Offset(line,direction,direction==0?k:line.stops.Count-1-k)<=t)order=k;
                int index=direction==0?order:line.stops.Count-1-order;
                string mark=direction==0?"▼":"▲";string had;
                here[index]=here.TryGetValue(index,out had)?(had.Contains(mark)?had:had+mark):mark;
            }
            bool bus=line.kind=="bus"||line.kind=="brt";
            Label("운행 중인 "+(bus?"버스":"열차")+" "+running.Count+"대 · ▼ "+TransitSchedule.Toward(line,0)+" · ▲ "+TransitSchedule.Toward(line,1),smallStyle);
            string colour=Hex(line.color);
            for(int i=0;i<line.stops.Count;i++)
            {
                var s=line.stops[i];string mark;here.TryGetValue(i,out mark);
                GUILayout.BeginHorizontal();
                GUILayout.Label("<color=#f2c14e>"+(mark??"")+"</color>",chipStyle,GUILayout.Width(26));
                GUILayout.Label("<color="+colour+">"+(i==0||i==line.stops.Count-1?"◉":"●")+"</color>",chipStyle,GUILayout.Width(18));
                string transfers="";
                foreach(var other in s.lines)if(other!=line&&other.kind!="bus"&&transfers.Length<60)transfers+=" <color="+Hex(other.color)+">●</color>";
                if(GUILayout.Button(s.name+"<size=10>"+transfers+"</size>",chipButtonStyle))SelectStation(s,true);
                if(networkEdit&&GUILayout.Button("✕",buttonStyle,GUILayout.Width(34)))RemoveStop(line,i);
                GUILayout.EndHorizontal();
            }
            if(networkEdit){GUILayout.Space(8);DrawNetworkEditor();}
        }

        // ---------------------------------------------------------------- editor
        // Line colour swatches, two rows of six; the chosen one has a white frame.
        void DrawPalette()
        {
            for(int row=0;row<2;row++)
            {
                GUILayout.BeginHorizontal();
                for(int i=row*6;i<Mathf.Min(LinePalette.Length,row*6+6);i++)
                {
                    var r=GUILayoutUtility.GetRect(34,28,GUILayout.Width(34),GUILayout.Height(28));
                    Color c;ColorUtility.TryParseHtmlString(LinePalette[i],out c);
                    var previous=GUI.color;
                    if(i==newLineColour)UiTexture(r,Texture2D.whiteTexture);
                    GUI.color=c;UiTexture(new Rect(r.x+3,r.y+3,r.width-6,r.height-6),Texture2D.whiteTexture);GUI.color=previous;
                    if(UiButton(r,GUIContent.none,GUIStyle.none))newLineColour=i;
                }
                GUILayout.EndHorizontal();
            }
        }
        void DrawNetworkEditor()
        {
            BeginVerticalDump(cardStyle);
            Label("노선·역 편집",headingStyle);
            Label("바꾼 내용은 저장 파일에 남고 노선도·시간표·지도에 바로 반영됩니다.",smallStyle);
            if(selectedLine!=null)
            {
                Label("편집 중: "+selectedLine.name,bodyStyle);
                Label("새 역 이름",smallStyle);newStationName=GUILayout.TextField(newStationName,20,textFieldStyle);
                if(Button(placingStation?"역 추가 끝내기":"역 추가 (지도 클릭)",!placingStation))placingStation=!placingStation;
                if(placingStation)Label("지도에서 기존 역을 누르면 그 역을, 빈 곳을 누르면 새 역을 만들어 노선 끝에 붙입니다. 이름을 비우면 '새 역 N'이 됩니다.",smallStyle);
                if(selectedStation!=null&&!selectedLine.stops.Contains(selectedStation)&&Button(StationTitle(selectedStation)+" 노선 끝에 붙이기"))AppendStop(selectedLine,selectedStation);
                if(ConfirmButton("노선 삭제","line:"+selectedLine.id))DeleteLine(selectedLine);
            }
            else
            {
                Label("새 노선",kickerStyle);
                newLineName=GUILayout.TextField(newLineName,24,textFieldStyle);
                int kind=ButtonRows(System.Array.ConvertAll(NetworkKinds,TransitNetwork.KindLabel),i=>newLineKind==NetworkKinds[i]);
                if(kind>=0)newLineKind=NetworkKinds[kind];
                DrawPalette();
                if(Button("노선 만들기",true))CreateLine();
                Label("새 노선은 지도에서 고른 역(없으면 지도 가운데 가까운 역)에서 시작합니다. 노선을 고르면 역을 붙이거나 노선도에서 ✕로 뺄 수 있습니다. 역을 지우려면 지도에서 역을 눌러 안내판의 '역 삭제'를 쓰세요.",smallStyle);
            }
            var e=state.network;
            Label("추가한 역 "+e.stations.Count+" · 바꾸거나 만든 노선 "+e.lines.Count+" · 지운 역 "+e.removedStations.Count+" · 지운 노선 "+e.removedLines.Count,smallStyle);
            if(ConfirmButton("편집 모두 되돌리기","reset")){state.network=new NetworkEdits();placingStation=false;ApplyNetworkEdits("교통망을 원래대로 되돌렸습니다.");}
            EndVerticalDump();
        }
        // A destructive button that asks for a second click.
        bool ConfirmButton(string label,string key)
        {
            if(pendingDelete==key){if(Button("한 번 더 눌러 확인: "+label,true)){pendingDelete="";return true;}return false;}
            if(Button(label))pendingDelete=key;
            return false;
        }
        // The saved copy of a line to edit: a custom line, or an override holding a base line's current stops.
        CustomLine EditableLine(NetLine line)
        {
            var edit=state.network.lines.Find(l=>l.id==line.id);
            if(edit!=null)return edit;
            edit=new CustomLine{id=line.id,kind=line.kind,name=line.name,color=Hex(line.color),peak=line.peakMinutes,offPeak=line.offPeakMinutes};
            foreach(var s in line.stops)edit.stops.Add(s.id);
            state.network.lines.Add(edit);return edit;
        }
        void ApplyNetworkEdits(string message)
        {
            string lineId=selectedLine!=null?selectedLine.id:null,stationId=selectedStation!=null?selectedStation.id:null;
            TransitNetwork.Build(state.network);
            selectedLine=TransitNetwork.Line(lineId);selectedStation=TransitNetwork.Station(stationId);
            if(selectedLine==null)placingStation=false;
            if(mode=="map"){world.BuildNetworkLayer();world.Highlight(selectedLine);}
            Save();Toast(message);
        }
        NetStation MapCentreStation()
        {
            var ray=viewCamera.ViewportPointToRay(new Vector3(.5f,.5f,0));float distance;
            if(!new Plane(Vector3.up,Vector3.zero).Raycast(ray,out distance))return null;
            var geo=GeoProjection.ToGeo(ray.GetPoint(distance));
            return TransitNetwork.Nearest(geo.x,geo.y,500f);
        }
        void CreateLine()
        {
            string name=newLineName.Trim();
            if(name.Length==0)name=TransitNetwork.KindLabel(newLineKind)+" 새 노선 "+(TransitNetwork.Lines.FindAll(l=>l.custom).Count+1);
            bool rail=newLineKind=="ktx"||newLineKind=="mugunghwa";
            var line=new CustomLine{id=TransitNetwork.NewId("u"),kind=newLineKind,name=name,color=LinePalette[newLineColour],peak=newLineKind=="ktx"?30:newLineKind=="mugunghwa"?60:4,offPeak=newLineKind=="ktx"?60:newLineKind=="mugunghwa"?120:8};
            // A line needs two stops: start it at the picked station (or the one nearest the map centre) and its nearest neighbour.
            var first=selectedStation??MapCentreStation();
            if(first==null){Toast("지도에 역이 없습니다.");return;}
            var second=TransitNetwork.Nearest(first.lon,first.lat,120f,s=>s!=first&&(rail?IsRail(s)||IsMetro(s):true));
            line.stops.Add(first.id);if(second!=null)line.stops.Add(second.id);
            state.network.lines.Add(line);newLineName="";
            ApplyNetworkEdits(name+" 노선을 만들었습니다 · 지도를 눌러 역을 붙이세요.");
            var made=TransitNetwork.Line(line.id);
            if(made!=null){networkKind=made.kind;if(made.kind=="metro")networkRegion="사용자";SelectLine(made,true);placingStation=true;}
        }
        void AppendStop(NetLine line,NetStation s)
        {
            var edit=EditableLine(line);
            if(edit.stops.Count>0&&edit.stops[edit.stops.Count-1]==s.id){Toast("이미 노선 끝에 있는 역입니다.");return;}
            edit.stops.Add(s.id);ApplyNetworkEdits(line.name+"에 "+StationTitle(s)+" 추가");
        }
        void RemoveStop(NetLine line,int index)
        {
            if(line.stops.Count<=2){Toast("노선에는 역이 두 개 이상 있어야 합니다. 노선을 지우려면 '노선 삭제'를 쓰세요.");return;}
            var edit=EditableLine(line);var name=line.stops[index].name;
            edit.stops.Clear();for(int i=0;i<line.stops.Count;i++)if(i!=index)edit.stops.Add(line.stops[i].id);
            ApplyNetworkEdits(line.name+"에서 "+name+" 뺌");
        }
        void DeleteLine(NetLine line)
        {
            state.network.lines.RemoveAll(l=>l.id==line.id);
            if(!line.custom&&!state.network.removedLines.Contains(line.id))state.network.removedLines.Add(line.id);
            selectedLine=null;placingStation=false;world.Highlight(null);ApplyNetworkEdits(line.name+" 노선을 지웠습니다.");
        }
        void DeleteStation(NetStation s)
        {
            if(s.custom)state.network.stations.RemoveAll(c=>c.id==s.id);
            else if(!state.network.removedStations.Contains(s.id))state.network.removedStations.Add(s.id);
            foreach(var l in state.network.lines)l.stops.RemoveAll(id=>id==s.id);
            selectedStation=null;ApplyNetworkEdits(WithObject(StationTitle(s))+" 지웠습니다.");
        }
        void PlaceStationAt(Vector2 lonLat)
        {
            if(selectedLine==null)return;
            string name=newStationName.Trim();if(name.Length==0)name="새 역 "+(state.network.stations.Count+1);
            var made=new CustomStation{id=TransitNetwork.NewId("u"),name=name,lon=lonLat.x,lat=lonLat.y};
            state.network.stations.Add(made);newStationName="";
            EditableLine(selectedLine).stops.Add(made.id);
            ApplyNetworkEdits(selectedLine.name+"에 역 추가: "+name);
        }
        // Map click while adding stations: a station under the cursor joins the line (handled in the overlay),
        // empty map makes a new station there.
        bool HandleNetworkMapClick()
        {
            if(!NetworkMapVisible()||!placingStation||selectedLine==null||hoveredStation!=null)return false;
            var ray=viewCamera.ScreenPointToRay(Input.mousePosition);float distance;
            if(!new Plane(Vector3.up,new Vector3(0,.9f,0)).Raycast(ray,out distance))return false;
            PlaceStationAt(GeoProjection.ToGeo(ray.GetPoint(distance)));
            return true;
        }

        // ---------------------------------------------------------------- map overlay: stations, names, boards
        void DrawNetworkOverlay(float w,float h)
        {
            hoveredStation=null;
            // In the 3D streets the board opens from a bus stop's BIS screen.
            if(mode=="district"||mode=="interior")
            {
                if((flightDeskAirline>=0||kioskDesk))DrawFlightDesk(w,h);
                else if(streetBoard&&selectedStation!=null){EnsureNetworkStyles();DrawStationBoard(w,h);}
                else boardRect=new Rect(0,0,0,0);
                return;
            }
            streetBoard=false;flightDeskAirline=-1;kioskDesk=false;
            if(!NetworkMapVisible()||viewCamera==null||!viewCamera.orthographic){boardRect=new Rect(0,0,0,0);return;}
            EnsureNetworkStyles();
            float zoom=viewCamera.orthographicSize,scale=Mathf.Max(.55f,Mathf.Min(Screen.width/1440f,Screen.height/860f));
            var mouse=Event.current.mousePosition;labelRects.Clear();
            // Keep dots and names off the map's zoom and pan boxes (DrawMapControls).
            var controls=new[]{MapControlsRect(w),MapPanRect(w,h)};labelRects.AddRange(controls);
            float bestHover=10f;bool repaint=Event.current.type==EventType.Repaint;
            foreach(var s in TransitNetwork.Stations)
            {
                bool rail=IsRail(s),metro=IsMetro(s),picked=s==selectedStation,onLine=selectedLine!=null&&selectedLine.stops.Contains(s);
                bool show=picked||(onLine&&(selectedLine.kind!="bus"||zoom<6f))||(rail&&zoom<45f)||(metro&&zoom<WorldBuilder.MetroZoom+2f);
                if(!show)continue;
                var screen=viewCamera.WorldToScreenPoint(s.MapPosition(1.15f));
                if(screen.z<0)continue;
                float x=screen.x/scale,y=(Screen.height-screen.y)/scale;
                if(x<WorldLeft()+4||x>Mathf.Min(w-8,WorldRight()-4)||y<20||y>h-WorldBottom()-6||controls[0].Contains(new Vector2(x,y))||controls[1].Contains(new Vector2(x,y)))continue;
                int transfers=0;foreach(var l in s.lines)if(l.kind!="bus")transfers++;
                float size=picked?13:transfers>1?9:7;
                float d=Vector2.Distance(mouse,new Vector2(x,y));
                if(d<bestHover&&!boardRect.Contains(mouse)){bestHover=d;hoveredStation=s;}
                if(!repaint)continue;
                var previous=GUI.color;if(picked)GUI.color=new Color(1f,.82f,.3f);
                UiTexture(new Rect(x-size*.5f,y-size*.5f,size,size),dotTexture);
                GUI.color=previous;
                bool named=picked||(onLine&&zoom<(selectedLine.kind=="bus"?2.5f:12f))||(rail&&zoom<(transfers>1||s.lines.Exists(l=>l.kind=="ktx")?16f:7f))||(metro&&zoom<2.4f);
                if(!named)continue;
                var content=new GUIContent(s.name);var labelSize=stationLabelStyle.CalcSize(content);
                var rect=new Rect(x+size*.5f+2,y-labelSize.y*.5f,labelSize.x,labelSize.y);
                bool overlaps=false;foreach(var r in labelRects)if(r.Overlaps(rect)){overlaps=true;break;}
                if(overlaps&&!picked)continue;
                labelRects.Add(rect);UiLabel(rect,content,stationLabelStyle);
            }
            if(hoveredStation!=null&&repaint)
            {
                var tip=StationTitle(hoveredStation)+(placingStation&&selectedLine!=null?" · 클릭해 "+selectedLine.name+"에 추가":" · 클릭해 실시간 도착 정보");
                var size=stationLabelStyle.CalcSize(new GUIContent(tip));
                UiLabel(new Rect(mouse.x+14,mouse.y-22,size.x,size.y),tip,stationLabelStyle);
            }
            if(selectedStation!=null)DrawStationBoard(w,h);else boardRect=new Rect(0,0,0,0);
            var e=Event.current;
            if(e.type==EventType.MouseDown&&e.button==0&&hoveredStation!=null)
            {
                if(placingStation&&selectedLine!=null)AppendStop(selectedLine,hoveredStation);
                else SelectStation(hoveredStation,false);
                e.Use();
            }
        }

        // Arrival board for the picked station: BIS rows for buses, arrival screens for subway, departures for trains.
        void DrawStationBoard(float w,float h)
        {
            var s=selectedStation;double now=TransitSchedule.Now;
            float width=440,height=Mathf.Min(h-250,580);
            boardRect=new Rect(MapControlsRect(w).xMax+14,112,width,height);
            UiBox(boardRect,"",boxStyle);
            GUILayout.BeginArea(new Rect(boardRect.x+14,boardRect.y+12,width-28,height-24));
            GUILayout.BeginHorizontal();
            GUILayout.Label("<b>"+StationTitle(s)+"</b>",boardTitleStyle);
            bool close=GUILayout.Button("닫기",buttonStyle,GUILayout.Width(64));
            GUILayout.EndHorizontal();
            Label(TransitSchedule.Clock(now)+":"+((int)(now%60)).ToString("00")+" 기준 · 실시간 도착 정보 (시뮬레이션)",smallStyle);
            if(s.lines.Count>0&&Button("이 역·정류장 3D 방문",true))VisitNetworkStation(s);
            boardScroll=GUILayout.BeginScrollView(boardScroll);
            DrawDepartures(s,now,8);
            DrawArrivals(s,now);
            if(networkEdit)
            {
                GUILayout.Space(8);
                if(selectedLine!=null&&!selectedLine.stops.Contains(s)&&Button(selectedLine.name+" 끝에 붙이기"))AppendStop(selectedLine,s);
                if(ConfirmButton("역 삭제","station:"+s.id))DeleteStation(s);
            }
            GUILayout.EndScrollView();
            GUILayout.EndArea();
            if(close){selectedStation=null;streetBoard=false;boardRect=new Rect(0,0,0,0);}
        }
        // Trains: one departure list across KTX and 무궁화호, as on a station's departure screen.
        void DrawDepartures(NetStation s,double now,int rows)
        {
            var departures=TransitSchedule.TrainsFrom(s,now);
            if(departures.Count==0)return;
            Label("열차 출발 안내",headingStyle);
            for(int i=0;i<Mathf.Min(rows,departures.Count);i++)
            {
                var a=departures[i];
                string status=a.delay>=60?"<color=#ff8a65>"+a.delay/60+"분 지연</color>":"정시";
                GUILayout.Label(TransitSchedule.Clock(a.scheduled)+"  <color="+Hex(a.line.color)+">"+TransitSchedule.TrainNumber(a.line,a.direction,a.trip)+"</color>  "+a.line.Terminus(a.direction).name+"행 · "+status+" · "+(a.seconds<=0?"탑승 중":Mathf.CeilToInt((float)a.seconds/60)+"분 후"),boardLineStyle);
            }
            GUILayout.Space(8);
        }
        // Subway and buses: per line and direction, the next two arrivals.
        void DrawArrivals(NetStation s,double now)
        {
            foreach(var line in s.lines)
            {
                if(line.kind=="ktx"||line.kind=="mugunghwa")continue;
                int index=line.IndexOf(s);bool bus=line.kind=="bus"||line.kind=="brt";
                GUILayout.BeginHorizontal();
                GUILayout.Label("<b>"+Chip(line)+"</b>",chipStyle);
                if(GUILayout.Button("노선도",buttonStyle,GUILayout.Width(72))){networkKind=line.kind;if(tab!="교통")tab="교통";SelectLine(line,false);}
                GUILayout.EndHorizontal();
                for(int direction=0;direction<2;direction++)
                {
                    var next=TransitSchedule.Next(line,direction,index,2,now);
                    if(next.Count==0)continue;
                    var toward=TransitSchedule.NextStop(line,direction,index);
                    string head=bus?TransitSchedule.Toward(line,direction):(toward!=null?toward.name+" 방면 · ":"")+TransitSchedule.Toward(line,direction);
                    string rows="";
                    foreach(var a in next)rows+=(rows.Length>0?"  /  다음 ":"")+TransitSchedule.Describe(a);
                    GUILayout.Label("  "+head+"\n    <color=#f2c14e>"+rows+"</color>"+(bus?"  · "+Crowding(next[0]):""),boardLineStyle);
                }
            }
        }
        // 여유 / 보통 / 혼잡, busier at rush hour.
        static string Crowding(Arrival a)
        {
            double hour=(TransitSchedule.Now/3600)%24;bool rush=(hour>=7&&hour<9.5)||(hour>=17.5&&hour<20);
            int h=(a.trip*7+a.line.id.Length*13)%10;
            return rush?(h<6?"혼잡":"보통"):(h<2?"보통":"여유");
        }
    }
}
