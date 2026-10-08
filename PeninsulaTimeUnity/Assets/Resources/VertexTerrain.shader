Shader "Peninsula/VertexTerrain" {
    SubShader {
        Tags { "RenderType"="Opaque" }
        LOD 200
        CGPROGRAM
        #pragma surface surf Lambert vertex:vert
        struct Input { float4 vertexColor : COLOR; };
        void vert(inout appdata_full v, out Input o) { UNITY_INITIALIZE_OUTPUT(Input,o); o.vertexColor=v.color; }
        void surf(Input IN, inout SurfaceOutput o) { o.Albedo=IN.vertexColor.rgb; o.Alpha=1; }
        ENDCG
    }
    Fallback "Diffuse"
}
