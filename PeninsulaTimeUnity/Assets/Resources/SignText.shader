Shader "Peninsula/SignText"
{
    // Like GUI/Text Shader but single sided, so signs seen from behind are hidden instead of mirrored.
    Properties { _MainTex ("Font Texture", 2D) = "white" {} _Color ("Text Color", Color) = (1,1,1,1) }
    SubShader
    {
        Tags { "Queue"="Transparent" "IgnoreProjector"="True" "RenderType"="Transparent" }
        Lighting Off Cull Back ZWrite Off Blend SrcAlpha OneMinusSrcAlpha
        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_fog
            #include "UnityCG.cginc"
            struct appdata { float4 vertex : POSITION; fixed4 color : COLOR; float2 uv : TEXCOORD0; };
            struct v2f { float4 vertex : SV_POSITION; fixed4 color : COLOR; float2 uv : TEXCOORD0; UNITY_FOG_COORDS(1) };
            sampler2D _MainTex; float4 _MainTex_ST; fixed4 _Color;
            v2f vert (appdata v)
            {
                v2f o; o.vertex = UnityObjectToClipPos(v.vertex); o.color = v.color * _Color;
                o.uv = TRANSFORM_TEX(v.uv, _MainTex); UNITY_TRANSFER_FOG(o, o.vertex); return o;
            }
            fixed4 frag (v2f i) : SV_Target
            {
                fixed4 c = i.color; c.a *= tex2D(_MainTex, i.uv).a;
                UNITY_APPLY_FOG(unity_FogColor, c); return c;
            }
            ENDCG
        }
    }
}
