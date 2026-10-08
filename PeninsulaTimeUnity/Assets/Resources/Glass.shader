Shader "Peninsula/Glass"
{
    Properties { _Color ("Color", Color) = (.6,.8,.9,.3) }
    SubShader
    {
        Tags { "Queue"="Transparent" "RenderType"="Transparent" "IgnoreProjector"="True" }
        ZWrite Off
        Cull Off
        CGPROGRAM
        #pragma surface surf BlinnPhong alpha:fade
        fixed4 _Color;
        struct Input { float3 viewDir; };
        void surf (Input IN, inout SurfaceOutput o)
        {
            o.Albedo = _Color.rgb;
            o.Alpha = _Color.a;
            o.Specular = .5;
            o.Gloss = 1;
        }
        ENDCG
    }
}
