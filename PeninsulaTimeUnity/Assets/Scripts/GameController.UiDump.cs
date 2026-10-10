using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEngine;
namespace PeninsulaTime
{
    // QA: on a capture frame, every label, box, button, texture and layout control of the HUD is written to uidump-N.json in draw order,
    // with the rectangle IMGUI gave it in the 1440-wide GUI units the Figma frames use. The Figma panels are rebuilt from this
    // instead of being drawn by hand. Outside a capture frame the Ui* wrappers only call the plain GUI functions.
    public partial class GameController
    {
        List<string> uiDump;int uiDumpPoint;Vector2 uiOrigin;
        bool UiDumping(){return uiDump!=null&&Event.current.type==EventType.Repaint;}
        float GuiScale(){return Mathf.Max(.55f,Mathf.Min(Screen.width/1440f,Screen.height/860f));}
        static string RgbaHex(Color c){return ((int)(c.r*255+.5f)).ToString("x2")+((int)(c.g*255+.5f)).ToString("x2")+((int)(c.b*255+.5f)).ToString("x2")+((int)(c.a*255+.5f)).ToString("x2");}
        static string Json(string text){return "\""+(text??"").Replace("\\","\\\\").Replace("\"","\\\"").Replace("\n","\\n")+"\"";}
        static string F(float value){return value.ToString("F2",System.Globalization.CultureInfo.InvariantCulture);}
        // Screen origin of the GUI space, measured at the top of OnGUI where no group is open.
        void MarkUiOrigin(){if(UiDumping())uiOrigin=GUIUtility.GUIToScreenPoint(Vector2.zero);}
        string TextureFill(Texture texture)
        {
            var solid=texture as Texture2D;
            if(solid==null)return "";
            try{var pixel=solid.GetPixel(0,0)*GUI.color;return RgbaHex(pixel);}catch{return "";}
        }
        void Append(string kind,string text,GUIStyle style,Rect local,string fill,string name)
        {
            float scale=GuiScale();
            var a=(GUIUtility.GUIToScreenPoint(new Vector2(local.xMin,local.yMin))-uiOrigin)/scale;
            var b=(GUIUtility.GUIToScreenPoint(new Vector2(local.xMax,local.yMax))-uiOrigin)/scale;
            var sb=new StringBuilder();
            sb.Append("{\"k\":\"").Append(kind).Append("\",\"t\":").Append(Json(text)).Append(",\"n\":").Append(Json(name));
            sb.Append(",\"x\":").Append(F(a.x)).Append(",\"y\":").Append(F(a.y)).Append(",\"w\":").Append(F(b.x-a.x)).Append(",\"h\":").Append(F(b.y-a.y));
            if(style!=null)
            {
                sb.Append(",\"fs\":").Append(style.fontSize).Append(",\"bold\":").Append(style.font==koreanBoldFont?1:0);
                sb.Append(",\"al\":\"").Append(style.alignment).Append("\",\"c\":\"").Append(RgbaHex(style.normal.textColor*GUI.color));
                sb.Append("\",\"pl\":").Append(style.padding.left).Append(",\"pr\":").Append(style.padding.right).Append(",\"pt\":").Append(style.padding.top);
                sb.Append(",\"wrap\":").Append(style.wordWrap?1:0).Append(",\"rich\":").Append(style.richText?1:0);
                var back=style.normal.background;
                if(fill==null&&back!=null)fill=TextureFill(back);
                var size=style.CalcSize(new GUIContent(text??""));
                sb.Append(",\"lh\":").Append(F(style.lineHeight)).Append(",\"cw\":").Append(F(size.x)).Append(",\"ch\":").Append(F(size.y));
            }
            sb.Append(",\"fill\":\"").Append(fill??"").Append("\"}");
            uiDump.Add(sb.ToString());
        }
        // Records the control just laid out with GUILayout.
        void DumpControl(string kind,string text,GUIStyle style)
        {
            if(!UiDumping())return;
            Append(kind,text,style,GUILayoutUtility.GetLastRect(),null,"L");
        }
        bool DumpButton(string text,GUIStyle style,params GUILayoutOption[] options)
        {
            bool pressed=GUILayout.Button(text,style,options);DumpControl("button",text,style);return pressed;
        }
        string DumpField(string value,int maxLength,GUIStyle style)
        {
            string result=GUILayout.TextField(value,maxLength,style);DumpControl("field",result,style);return result;
        }
        // Vertical groups (cards) and scroll views are recorded after their children, once their rectangle is known.
        readonly Stack<GUIStyle> uiGroups=new Stack<GUIStyle>();
        void BeginVerticalDump(GUIStyle style,params GUILayoutOption[] options){GUILayout.BeginVertical(style,options);uiGroups.Push(style);}
        void BeginVerticalDump(params GUILayoutOption[] options){GUILayout.BeginVertical(options);uiGroups.Push(null);}
        void EndVerticalDump()
        {
            GUILayout.EndVertical();
            var style=uiGroups.Pop();
            if(UiDumping()&&style!=null)Append("vbox","",style,GUILayoutUtility.GetLastRect(),null,"L");
        }
        Vector2 BeginScrollDump(Vector2 position){return GUILayout.BeginScrollView(position);}
        void EndScrollDump(){GUILayout.EndScrollView();if(UiDumping())Append("scroll","",null,GUILayoutUtility.GetLastRect(),"","L");}
        void UiLabel(Rect rect,string text,GUIStyle style){GUI.Label(rect,text,style);if(UiDumping())Append("label",text,style,rect,null,"");}
        void UiLabel(Rect rect,string text){UiLabel(rect,text,GUI.skin.label);}
        void UiLabel(Rect rect,GUIContent content,GUIStyle style){GUI.Label(rect,content,style);if(UiDumping())Append("label",content.text,style,rect,null,"");}
        void UiBox(Rect rect,string text,GUIStyle style){GUI.Box(rect,text,style);if(UiDumping())Append("box",text,style,rect,null,"");}
        bool UiButton(Rect rect,string text,GUIStyle style){bool pressed=GUI.Button(rect,text,style);if(UiDumping())Append("button",text,style,rect,null,"");return pressed;}
        bool UiButton(Rect rect,GUIContent content,GUIStyle style){bool pressed=GUI.Button(rect,content,style);if(UiDumping())Append("button",content.text,style,rect,null,"");return pressed;}
        bool UiRepeatButton(Rect rect,string text,GUIStyle style){bool pressed=GUI.RepeatButton(rect,text,style);if(UiDumping())Append("button",text,style,rect,null,"");return pressed;}
        // The radar's render texture is saved as radar-N.png so the Figma radar is the real picture without the labels drawn over it.
        void SaveRenderTexture(Texture texture)
        {
            var source=texture as RenderTexture;
            if(source==null)return;
            var previous=RenderTexture.active;RenderTexture.active=source;
            var copy=new Texture2D(source.width,source.height,TextureFormat.RGB24,false);
            copy.ReadPixels(new Rect(0,0,source.width,source.height),0,0);copy.Apply();
            RenderTexture.active=previous;
            File.WriteAllBytes(Path.Combine(SaveDirectory,"radar-"+uiDumpPoint+".png"),copy.EncodeToPNG());
            Destroy(copy);
        }
        // The radar as it lands on screen (filtered and scaled by IMGUI, before the labels are drawn over it) is saved as
        // radar-screen-N.png, so the Figma radar can use the exact screen pixels.
        void SaveScreenRect(Rect local)
        {
            var a=GUIUtility.GUIToScreenPoint(new Vector2(local.xMin,local.yMin));
            var b=GUIUtility.GUIToScreenPoint(new Vector2(local.xMax,local.yMax));
            int x=Mathf.RoundToInt(a.x),y=Mathf.RoundToInt(Screen.height-b.y),w=Mathf.RoundToInt(b.x-a.x),h=Mathf.RoundToInt(b.y-a.y);
            if(w<=0||h<=0)return;
            var copy=new Texture2D(w,h,TextureFormat.RGB24,false);
            copy.ReadPixels(new Rect(x,y,w,h),0,0);copy.Apply();
            File.WriteAllBytes(Path.Combine(SaveDirectory,"radar-screen-"+uiDumpPoint+".png"),copy.EncodeToPNG());
            Destroy(copy);
        }
        void UiTexture(Rect rect,Texture texture){GUI.DrawTexture(rect,texture);if(UiDumping()&&texture!=null){SaveRenderTexture(texture);if(texture is RenderTexture)SaveScreenRect(rect);Append("tex","",null,rect,TextureFill(texture),texture.name);}}
        void UiTexture(Rect rect,Texture texture,ScaleMode mode){GUI.DrawTexture(rect,texture,mode);if(UiDumping()&&texture!=null)Append("tex","",null,rect,TextureFill(texture),texture.name);}
        void UiTexture(Rect rect,Texture texture,ScaleMode mode,bool alpha){GUI.DrawTexture(rect,texture,mode,alpha);if(UiDumping()&&texture!=null)Append("tex","",null,rect,TextureFill(texture),texture.name);}
        void BeginUiDump(int point){uiDump=new List<string>();uiDumpPoint=point;}
        void FlushUiDump()
        {
            if(uiDump==null||Event.current.type!=EventType.Repaint)return;
            File.WriteAllText(Path.Combine(SaveDirectory,"uidump-"+uiDumpPoint+".json"),"["+string.Join(",\n",uiDump)+"]");
            uiDump=null;
        }
    }
}
