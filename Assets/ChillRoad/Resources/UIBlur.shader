// Flou des menus (style verre) : flou gaussien séparable à 9 prises, appliqué par Graphics.Blit sur une image du jeu
// réduite (MenuBackdrop). _Dir = pas d'un texel dans la direction du passage.
Shader "Hidden/ChillRoad/UIBlur"
{
    Properties
    {
        _MainTex ("Texture", 2D) = "white" {}
        _Dir ("Direction", Vector) = (0.001, 0, 0, 0)
    }
    SubShader
    {
        Cull Off ZWrite Off ZTest Always
        Pass
        {
            CGPROGRAM
            #pragma vertex vert_img
            #pragma fragment frag
            #include "UnityCG.cginc"

            sampler2D _MainTex;
            float4 _Dir;

            fixed4 frag (v2f_img i) : SV_Target
            {
                float2 d = _Dir.xy;
                fixed4 c = tex2D(_MainTex, i.uv) * 0.2270270;
                c += tex2D(_MainTex, i.uv + d * 1.3846154) * 0.3162162;
                c += tex2D(_MainTex, i.uv - d * 1.3846154) * 0.3162162;
                c += tex2D(_MainTex, i.uv + d * 3.2307692) * 0.0702703;
                c += tex2D(_MainTex, i.uv - d * 3.2307692) * 0.0702703;
                c.a = 1;
                return c;
            }
            ENDCG
        }
    }
}
