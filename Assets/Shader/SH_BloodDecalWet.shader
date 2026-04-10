Shader "Custom/Sprite/Blood Decal Wet"
{
    Properties
    {
        [PerRendererData] _MainTex ("Sprite Texture", 2D) = "white" {}
        _Color ("Tint", Color) = (1,1,1,1)
        _BaseColor ("Base Color", Color) = (1,1,1,1)
        _BloodWetness ("Blood Wetness", Range(0,1)) = 0
        _BloodSheenStrength ("Wet Sheen Strength", Range(0,1)) = 0.08
        _BloodSheenBoost ("Wet Richness Boost", Range(0,0.25)) = 0.03

        [MaterialToggle] PixelSnap ("Pixel snap", Float) = 0
        [HideInInspector] _RendererColor ("RendererColor", Color) = (1,1,1,1)
        [HideInInspector] _Flip ("Flip", Vector) = (1,1,1,1)
        [PerRendererData] _AlphaTex ("External Alpha", 2D) = "white" {}
        [PerRendererData] _EnableExternalAlpha ("Enable External Alpha", Float) = 0
    }

    SubShader
    {
        Tags
        {
            "Queue" = "Transparent"
            "IgnoreProjector" = "True"
            "RenderType" = "Transparent"
            "PreviewType" = "Plane"
            "CanUseSpriteAtlas" = "True"
        }

        Cull Off
        Lighting Off
        ZWrite Off
        Blend One OneMinusSrcAlpha

        Pass
        {
            CGPROGRAM
            #pragma vertex SpriteVert
            #pragma fragment Frag
            #pragma target 2.0
            #pragma multi_compile_instancing
            #pragma multi_compile _ PIXELSNAP_ON
            #pragma multi_compile _ ETC1_EXTERNAL_ALPHA

            #include "UnitySprites.cginc"

            fixed4 _BaseColor;
            half _BloodWetness;
            half _BloodSheenStrength;
            half _BloodSheenBoost;

            fixed4 Frag(v2f IN) : SV_Target
            {
                fixed4 tex = SampleSpriteTexture(IN.texcoord);
                fixed4 tint = IN.color * _BaseColor;

                fixed4 c;
                c.rgb = tex.rgb * tint.rgb;
                c.a = tex.a * tint.a;

                clip(c.a - 0.001f);

                half wet = saturate(_BloodWetness);
                half2 uv = IN.texcoord;
                half directional = saturate(0.62h * (1.0h - uv.y) + 0.38h * uv.x);
                half edge = saturate((0.85h - tex.a) * 3.0h);
                half interior = saturate(tex.a * 1.3h - 0.15h);
                half sheenMask = saturate(lerp(interior * directional, edge * (0.35h + 0.65h * directional), 0.32h));
                half sheen = wet * _BloodSheenStrength * sheenMask;

                c.rgb *= (1.0h + wet * _BloodSheenBoost);
                c.rgb += sheen;
                c.rgb *= c.a;
                return c;
            }
            ENDCG
        }
    }
}
