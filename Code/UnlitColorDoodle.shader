//
//A simple unlit shader with a little bit of cartoonish wiggle for Unity3D.
//
//https://unity.com/
//

//
//UnlitColorDoodle.shader by Pelle "Stupid++" Bruinsma
//
//To the extent possible under law, the person who associated CC0 with
//UnlitColorDoodle.shader has waived all copyright and related or neighboring rights
//to UnlitColorDoodle.shader.
//
//You should have received a copy of the CC0 legalcode along with this
//work.  If not, see http://creativecommons.org/publicdomain/zero/1.0/.
//

Shader "Unlit/ColorDoodle"
{
    Properties{
        _Color("Main Color", Color) = (1,1,1,1)
        _NoiseScale("Noise Scale", Float) = 0.025
        _NoiseSnap("Noise Snap", Float) = 0.25
    }

        SubShader{
            Tags { "RenderType" = "Opaque" }
            LOD 100

            Pass {
                CGPROGRAM
                    #pragma vertex vert
                    #pragma fragment frag
                    #pragma target 2.0
                    #pragma multi_compile_fog

                    #include "UnityCG.cginc"

                    struct appdata_t {
                        float4 vertex : POSITION;
                        UNITY_VERTEX_INPUT_INSTANCE_ID
                    };

                    struct v2f {
                        float4 vertex : SV_POSITION;
                        UNITY_FOG_COORDS(0)
                        UNITY_VERTEX_OUTPUT_STEREO
                    };

                    fixed4 _Color;
                    float _NoiseScale;
                    float _NoiseSnap;

                    float rand(float2 co)
                    {
                        return frac(sin(dot(co.xy, float2(12.9898, 78.233))) * 43758.5453);
                    }
                    float3 random3(float3 co) //returns a random float3 between -1 and 1
                    {
                        float tot = co.x + co.y + co.z;
                        return float3(
                            rand(float2(tot, 0)) * 2 - 1,
                            rand(float2(tot, 0)) * 2 - 1,
                            rand(float2(tot, 0)) * 2 - 1
                            );
                    }
                    inline float snap(float x, float snap)
                    {
                        return snap * round(x / snap);
                    }

                    v2f vert(appdata_t v)
                    {
                        float time = snap(_Time.y, _NoiseSnap);
                        float3 noise = random3(v.vertex.xyz + float3(time, time, time)) * _NoiseScale;
                        v.vertex.xyz += noise;

                        v2f o;
                        UNITY_SETUP_INSTANCE_ID(v);
                        UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
                        o.vertex = UnityObjectToClipPos(v.vertex);
                        UNITY_TRANSFER_FOG(o,o.vertex);
                        return o;
                    }

                    fixed4 frag(v2f i) : COLOR
                    {
                        fixed4 col = _Color;
                        UNITY_APPLY_FOG(i.fogCoord, col);
                        UNITY_OPAQUE_ALPHA(col.a);
                        return col;
                    }
                ENDCG
            }
    }
}