Shader "Garment/RoiCrop"
{
    Properties
    {
        _MainTex ("Source", 2D) = "black" {}
        _Center ("ROI centre in source UV", Vector) = (0.5, 0.5, 0, 0)
        _Size ("ROI half-extent in source UV", Vector) = (0.5, 0.5, 0, 0)
        _Angle ("ROI rotation in radians", Float) = 0
        _Mirror ("Flip horizontally", Float) = 0
    }

    SubShader
    {
        Tags { "RenderType" = "Opaque" }
        Cull Off ZWrite Off ZTest Always

        Pass
        {
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            TEXTURE2D(_MainTex);
            SAMPLER(sampler_MainTex);

            float4 _Center;
            float4 _Size;
            float _Angle;
            float _Mirror;

            struct Attributes { float4 positionOS : POSITION; float2 uv : TEXCOORD0; };
            struct Varyings { float4 positionCS : SV_POSITION; float2 uv : TEXCOORD0; };

            Varyings Vert(Attributes input)
            {
                Varyings output;
                output.positionCS = TransformObjectToHClip(input.positionOS.xyz);
                output.uv = input.uv;
                return output;
            }

            half4 Frag(Varyings input) : SV_Target
            {
                // Destination UV runs 0..1 over the crop; map it back into the rotated source box.
                float2 centred = input.uv * 2.0 - 1.0;
                if (_Mirror > 0.5) centred.x = -centred.x;

                float s = sin(_Angle);
                float c = cos(_Angle);
                float2 rotated = float2(centred.x * c - centred.y * s, centred.x * s + centred.y * c);

                float2 sourceUv = _Center.xy + rotated * _Size.xy;
                if (any(sourceUv < 0.0) || any(sourceUv > 1.0)) return half4(0, 0, 0, 1);

                return SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, sourceUv);
            }
            ENDHLSL
        }
    }
}
