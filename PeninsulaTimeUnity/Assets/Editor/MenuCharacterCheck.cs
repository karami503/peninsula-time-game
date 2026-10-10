using System;
using System.Reflection;
using UnityEditor;
using UnityEngine;
using Object=UnityEngine.Object;

namespace PeninsulaTime
{
    // Regression check for the menu pointer barrier and the shared block-player wardrobe model.
    public static class MenuCharacterCheck
    {
        const BindingFlags Flags=BindingFlags.Instance|BindingFlags.NonPublic|BindingFlags.Public;
        static int failures;
        static void Expect(bool ok,string message){if(!ok){failures++;Debug.LogError("MenuCharacterCheck: "+message);}}
        static object Call(object target,string method,params object[] args){return target.GetType().GetMethod(method,Flags).Invoke(target,args);}
        static void Set(object target,string field,object value){target.GetType().GetField(field,Flags).SetValue(target,value);}
        public static void Run()
        {
            failures=0;var host=new GameObject("Menu character check");host.SetActive(false);
            var game=host.AddComponent<GameController>();var camera=new GameObject("Camera").AddComponent<Camera>();
            var world=new GameObject("World").AddComponent<WorldBuilder>();world.root=new GameObject("Root");world.worldCamera=camera;
            try
            {
                Set(game,"world",world);Set(game,"viewCamera",camera);Set(game,"streetMenu",true);Set(game,"mode","district");
                var eye=new GameObject("Player eye").transform;eye.rotation=Quaternion.Euler(12,34,0);Set(game,"eye",eye);
                Expect((bool)Call(game,"PointerMenuOpen"),"street menu does not capture the pointer");
                Expect(!(bool)Call(game,"PointerOverWorld"),"menu click leaks through to the 3D world");
                var before=eye.rotation;Call(game,"UpdateLook",.1f);Expect(Quaternion.Angle(before,eye.rotation)<.01f,"camera rotates while a menu is open");

                Transform[] legs,arms;var player=world.CreatePlayerCharacter(Vector3.zero,out legs,out arms);
                Expect(player!=null&&legs.Length==2&&arms.Length==2,"block player rig is incomplete");
                Expect(player.transform.Find("Torso")!=null&&player.transform.Find("Head")!=null&&player.transform.Find("Block hair")!=null,"block player body is incomplete");
                int cubes=player.GetComponentsInChildren<Renderer>().Length;
                var leftLeg=legs[0].Find("Trouser leg");var torso=player.transform.Find("Torso");
                Expect(cubes>=14&&torso!=null&&leftLeg!=null,"player is not the shared block wardrobe model: cubes="+cubes+" torso="+(torso!=null)+" leg="+(leftLeg!=null));
                world.TintPerson(player,1,1);var shirtBlock=new MaterialPropertyBlock();var trouserBlock=new MaterialPropertyBlock();
                torso.GetComponent<Renderer>().GetPropertyBlock(shirtBlock,0);leftLeg.GetComponent<Renderer>().GetPropertyBlock(trouserBlock,0);
                Expect(shirtBlock.GetColor("_Color")==WorldBuilder.ShirtColor(1)&&trouserBlock.GetColor("_Color")==WorldBuilder.TrouserColor(1),"wardrobe selection did not update the shared player renderers");
            }
            catch(Exception e){failures++;Debug.LogException(e);}
            finally{Object.DestroyImmediate(host);Object.DestroyImmediate(world.gameObject);Object.DestroyImmediate(camera.gameObject);}
            Debug.Log("MenuCharacterCheck: "+(failures==0?"passed":failures+" failed")+"; pointer capture, no camera leak, shared block player and live wardrobe");
            EditorApplication.Exit(failures==0?0:1);
        }
    }
}
