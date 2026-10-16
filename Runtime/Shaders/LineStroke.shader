Shader "UnityLines/LineStroke"
{
    Properties { [Enum(UnityEngine.Rendering.CompareFunction)] _ZTest ("Depth test", Float) = 4 }
    SubShader
    {
        Tags { "RenderPipeline"="UniversalPipeline" "RenderType"="Transparent" "Queue"="Transparent" }
        Pass
        {
            Name "LineStroke"
            Tags { "LightMode"="SRPDefaultUnlit" }
            Cull Off ZWrite Off ZTest [_ZTest]
            Blend SrcAlpha OneMinusSrcAlpha
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma target 4.0
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            CBUFFER_START(UnityPerMaterial)
            float4 _LineViewport;
            float _MiterLimit;
            CBUFFER_END
            struct Attributes
            {
                float3 positionOS : POSITION;
                float3 previous : TEXCOORD0;
                float3 next : TEXCOORD1;
                float4 data : TEXCOORD2;
                float4 color : COLOR;
            };
            struct Varyings { float4 positionCS : SV_POSITION; noperspective float4 stroke : TEXCOORD0; half4 color : COLOR; };
            float2 Screen(float4 clip) { return clip.xy / max(clip.w, 1e-7) * _LineViewport.xy * .5; }
            float2 Direction(float2 delta, float2 fallback) { float d = dot(delta, delta); return d > 1e-12 ? delta * rsqrt(d) : fallback; }
            Varyings Vert(Attributes input)
            {
                Varyings output;
                float4 currentClip = TransformWorldToHClip(input.positionOS);
                float2 current = Screen(currentClip);
                float2 previous = Screen(TransformWorldToHClip(input.previous));
                float2 next = Screen(TransformWorldToHClip(input.next));
                uint flags = (uint)input.data.w;
                bool first = (flags & 1u) != 0, last = (flags & 2u) != 0;
                float2 incoming = Direction(current - previous, float2(1, 0));
                float2 outgoing = Direction(next - current, incoming);
                if (first) incoming = outgoing;
                if (last) outgoing = incoming;
                float2 normal = float2(-outgoing.y, outgoing.x);
                float2 miter = Direction(float2(-incoming.y, incoming.x) + normal, normal);
                float radius = input.data.y * .5;
                if (input.data.z > .5)
                {
                    float3 right = float3(UNITY_MATRIX_I_V._m00, UNITY_MATRIX_I_V._m10, UNITY_MATRIX_I_V._m20);
                    radius = length(Screen(TransformWorldToHClip(input.positionOS + right * radius)) - current);
                }
                float extent = radius + 1;
                float scale = min(extent / max(.0001, dot(miter, normal)), extent * _MiterLimit);
                float2 offset = input.data.x * miter * scale;
                float fromStart = first ? -1 : ((flags & 4u) != 0 ? length(current - previous) : 1e6);
                float fromEnd = last ? -1 : ((flags & 8u) != 0 ? length(next - current) : 1e6);
                if (first) { offset -= outgoing; fromEnd += 1; }
                if (last) { offset += incoming; fromStart += 1; }
                currentClip.xy += offset * (2 / _LineViewport.xy) * currentClip.w;
                output.positionCS = currentClip;
                output.stroke = float4(input.data.x * extent, radius, fromStart, fromEnd);
                output.color = input.color;
                return output;
            }
            half4 Frag(Varyings input) : SV_Target
            {
                float edge = max(abs(input.stroke.x) - input.stroke.y, max(-input.stroke.z, -input.stroke.w));
                float aa = max(fwidth(edge), .0001);
                return half4(input.color.rgb, input.color.a * saturate(.5 - edge / aa));
            }
            ENDHLSL
        }
    }
}
