Shader "Peninsula/DoubleSidedCity"
{
    Properties { _Color ("Color", Color) = (1,1,1,1) _MainTex ("Texture", 2D) = "white" {} _Metallic ("Metallic", Range(0,1)) = 0 _Smoothness ("Smoothness", Range(0,1)) = 0.28 }
    SubShader
    {
        Tags { "RenderType"="Opaque" }
        Cull Off
        CGPROGRAM
        #pragma surface surf Standard fullforwardshadows addshadow
        #pragma multi_compile_instancing
        sampler2D _MainTex;
        fixed4 _Color;
        half _Metallic, _Smoothness;
        struct Input { float2 uv_MainTex; };
        void surf (Input IN, inout SurfaceOutputStandard o)
        {
            o.Albedo = tex2D(_MainTex, IN.uv_MainTex).rgb * _Color.rgb;
            o.Metallic = _Metallic; o.Smoothness = _Smoothness;
            o.Alpha = 1;
        }
        ENDCG
    }
    Fallback "Diffuse"
}
