// debug view what channels contain UV data

Shader "UnityLibrary/Debug/UVChannelDebug"
{
    Properties
    {
        _MainTex ("Texture", 2D) = "white" {}
        _ColorUV0 ("UV0 Color", Color) = (1,0,0,1)
        _ColorUV1 ("UV1 Color", Color) = (0,1,0,1)
        _ColorUV2 ("UV2 Color", Color) = (0,0,1,1)
        _ColorUV3 ("UV3 Color", Color) = (1,1,0,1)
        _ColorUV4 ("UV4 Color", Color) = (1,0,1,1)
        _ColorUV5 ("UV5 Color", Color) = (0,1,1,1)
        _ColorUV6 ("UV6 Color", Color) = (1,1,1,1)
        _ColorUV7 ("UV7 Color", Color) = (0,0,0,1)

        [Toggle] _EnableUV0 ("Enable UV0", Float) = 1
        [Toggle] _EnableUV1 ("Enable UV1", Float) = 1
        [Toggle] _EnableUV2 ("Enable UV2", Float) = 1
        [Toggle] _EnableUV3 ("Enable UV3", Float) = 1
        [Toggle] _EnableUV4 ("Enable UV4", Float) = 1
        [Toggle] _EnableUV5 ("Enable UV5", Float) = 1
        [Toggle] _EnableUV6 ("Enable UV6", Float) = 1
        [Toggle] _EnableUV7 ("Enable UV7", Float) = 1
    }
    SubShader
    {
        Tags { "RenderType"="Opaque" }
        LOD 100

        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag

            #include "UnityCG.cginc"

            struct appdata
            {
                float4 vertex : POSITION;
                float2 uv0 : TEXCOORD0;
                float2 uv1 : TEXCOORD1;
                float2 uv2 : TEXCOORD2;
                float2 uv3 : TEXCOORD3;
                float2 uv4 : TEXCOORD4;
                float2 uv5 : TEXCOORD5;
                float2 uv6 : TEXCOORD6;
                float2 uv7 : TEXCOORD7;
            };

            struct v2f
            {
                float2 uv0 : TEXCOORD0;
                float2 uv1 : TEXCOORD1;
                float2 uv2 : TEXCOORD2;
                float2 uv3 : TEXCOORD3;
                float2 uv4 : TEXCOORD4;
                float2 uv5 : TEXCOORD5;
                float2 uv6 : TEXCOORD6;
                float2 uv7 : TEXCOORD7;
                float4 vertex : SV_POSITION;
            };

            sampler2D _MainTex;
            float4 _MainTex_ST;
            float4 _ColorUV0;
            float4 _ColorUV1;
            float4 _ColorUV2;
            float4 _ColorUV3;
            float4 _ColorUV4;
            float4 _ColorUV5;
            float4 _ColorUV6;
            float4 _ColorUV7;

            float _EnableUV0;
            float _EnableUV1;
            float _EnableUV2;
            float _EnableUV3;
            float _EnableUV4;
            float _EnableUV5;
            float _EnableUV6;
            float _EnableUV7;

            fixed UVChecker(float2 uv)
            {
                float2 cell = floor(uv * 8.0);
                return fmod(cell.x + cell.y, 2.0);
            }

            fixed HasUVData(float2 uv)
            {
                float variation = fwidth(uv.x) + fwidth(uv.y);
                float magnitude = abs(uv.x) + abs(uv.y);
                return step(1e-5, max(variation, magnitude));
            }

            v2f vert (appdata v)
            {
                v2f o;
                o.vertex = UnityObjectToClipPos(v.vertex);

                o.uv0 = TRANSFORM_TEX(v.uv0, _MainTex);
                o.uv1 = TRANSFORM_TEX(v.uv1, _MainTex);
                o.uv2 = TRANSFORM_TEX(v.uv2, _MainTex);
                o.uv3 = TRANSFORM_TEX(v.uv3, _MainTex);
                o.uv4 = TRANSFORM_TEX(v.uv4, _MainTex);
                o.uv5 = TRANSFORM_TEX(v.uv5, _MainTex);
                o.uv6 = TRANSFORM_TEX(v.uv6, _MainTex);
                o.uv7 = TRANSFORM_TEX(v.uv7, _MainTex);

                return o;
            }

            fixed4 frag (v2f i) : SV_Target
            {
                fixed4 col0 = tex2D(_MainTex, i.uv0) * _ColorUV0;
                fixed4 col1 = tex2D(_MainTex, i.uv1) * _ColorUV1;
                fixed4 col2 = tex2D(_MainTex, i.uv2) * _ColorUV2;
                fixed4 col3 = tex2D(_MainTex, i.uv3) * _ColorUV3;
                fixed4 col4 = tex2D(_MainTex, i.uv4) * _ColorUV4;
                fixed4 col5 = tex2D(_MainTex, i.uv5) * _ColorUV5;
                fixed4 col6 = tex2D(_MainTex, i.uv6) * _ColorUV6;
                fixed4 col7 = tex2D(_MainTex, i.uv7) * _ColorUV7;

                float e0 = _EnableUV0 * HasUVData(i.uv0);
                float e1 = _EnableUV1 * HasUVData(i.uv1);
                float e2 = _EnableUV2 * HasUVData(i.uv2);
                float e3 = _EnableUV3 * HasUVData(i.uv3);
                float e4 = _EnableUV4 * HasUVData(i.uv4);
                float e5 = _EnableUV5 * HasUVData(i.uv5);
                float e6 = _EnableUV6 * HasUVData(i.uv6);
                float e7 = _EnableUV7 * HasUVData(i.uv7);

                fixed4 col =
                    col0 * e0 +
                    col1 * e1 +
                    col2 * e2 +
                    col3 * e3 +
                    col4 * e4 +
                    col5 * e5 +
                    col6 * e6 +
                    col7 * e7;

                return saturate(col);
            }
            ENDCG
        }
    }
}
