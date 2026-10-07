// Copies one eye slice of Meta's _EnvironmentDepthTexture (Texture2DArray, NDC depth) into a 2D target.
// Encoding 0 (Gray8):   RGB = Kinect scale (meters / 8.191) or normalized _MinDepth.._MaxDepth.
// Encoding 1 (Packed16): R = high byte, G = low byte, B = valid mask (normalized range).
// Encoding 2 (Detail):  gray codes decoded by the TouchDesigner quest3 panel:
//                       0 = unknown, 5 = too close, 10 = too far, 16..255 = min..max (linear),
//                       plus a black|white calibration patch at the left edge, vertical center.
// _Align = 1: resample depth from the color camera's viewpoint (pixel-aligned with Quest3_Color)
//             by casting each color pixel's ray into Meta's depth (2 refinement steps for parallax).
Shader "Hidden/Quest3Bridge/DepthArrayToRT"
{
    Properties
    {
        _MainTex ("Unused (Blit source)", 2D) = "black" {}
    }
    SubShader
    {
        Cull Off ZWrite Off ZTest Always

        Pass
        {
            CGPROGRAM
            #pragma vertex vert_img
            #pragma fragment frag
            #pragma require 2darray
            #include "UnityCG.cginc"

            UNITY_DECLARE_TEX2DARRAY(_EnvironmentDepthTexture);
            float4 _EnvironmentDepthZBufferParams;
            float _Slice;
            float _MinDepth;
            float _MaxDepth;
            float _Invert;
            float _Mirror;
            float _TooClose;
            float _TooFar;
            float _Unknown;
            float _Encoding;
            float _KinectScale;
            float4x4 _EnvironmentDepthReprojectionMatrices[2];
            float _Align;
            float4 _AlignOrigin;                          // color camera position (world)
            float4 _AlignC00, _AlignC10, _AlignC01, _AlignC11; // corner rays, scaled to 1 m forward

            float Linearize(float d)
            {
                float ndc = d * 2.0 - 1.0;
                return (1.0 / (ndc + _EnvironmentDepthZBufferParams.y)) * _EnvironmentDepthZBufferParams.x;
            }

            // Depth (meters) seen along a color-camera pixel ray; < 0 if unknown
            float AlignedMeters(float2 uv)
            {
                float3 dir = lerp(lerp(_AlignC00.xyz, _AlignC10.xyz, uv.x), lerp(_AlignC01.xyz, _AlignC11.xyz, uv.x), uv.y);
                float t = 1.5; // initial guess, refined by sampling
                [unroll] for (int k = 0; k < 3; k++)
                {
                    float4 c = mul(_EnvironmentDepthReprojectionMatrices[(int)_Slice], float4(_AlignOrigin.xyz + dir * t, 1.0));
                    float2 duv = (c.xy / c.w + 1.0) * 0.5;
                    if (c.w <= 0 || any(duv < 0) || any(duv > 1)) return -1;
                    float d = UNITY_SAMPLE_TEX2DARRAY(_EnvironmentDepthTexture, float3(duv, _Slice)).r;
                    if (d <= 0.0 || d >= 1.0) return -1;
                    t = Linearize(d);
                }
                return t;
            }

            #define KIND_VALID 0
            #define KIND_UNKNOWN 1
            #define KIND_TOOCLOSE 2
            #define KIND_TOOFAR 3

            float4 Gray(float v) { return float4(v, v, v, 1); }

            float4 Output(int kind, float meters)
            {
                float v = (meters - _MinDepth) / max(_MaxDepth - _MinDepth, 1e-4);

                if (_Encoding > 1.5) // Detail
                {
                    if (kind == KIND_UNKNOWN)  return Gray(0);
                    if (kind == KIND_TOOCLOSE) return Gray(5.0 / 255.0);
                    if (kind == KIND_TOOFAR)   return Gray(10.0 / 255.0);
                    return Gray((16.0 + saturate(v) * 239.0) / 255.0);
                }

                float fallback = kind == KIND_UNKNOWN ? _Unknown : kind == KIND_TOOCLOSE ? _TooClose : _TooFar;

                if (_Encoding > 0.5) // Packed16
                {
                    if (kind != KIND_VALID) return float4(fallback, fallback, 0, 1);
                    float q = round(saturate(lerp(v, 1.0 - v, _Invert)) * 65535.0);
                    float hi = floor(q / 256.0);
                    return float4(hi / 255.0, (q - hi * 256.0) / 255.0, 1, 1);
                }

                // Gray8
                if (kind != KIND_VALID) return Gray(fallback);
                // Kinect TOP convention: linear, 1.0 = 8.191 m. Otherwise normalized min..max.
                if (_KinectScale > 0.5) return Gray(saturate(meters / 8.191));
                return Gray(lerp(v, 1.0 - v, _Invert));
            }

            float4 frag (v2f_img i) : SV_Target
            {
                // Detail calibration patch (unmirrored, vertically symmetric so flips don't matter)
                if (_Encoding > 1.5 && i.uv.x < 0.05 && abs(i.uv.y - 0.5) < 0.0125)
                    return Gray(i.uv.x < 0.025 ? 0 : 1);

                float2 uv = i.uv;
                uv.x = lerp(uv.x, 1.0 - uv.x, _Mirror);

                float meters;
                if (_Align > 0.5)
                {
                    meters = AlignedMeters(uv);
                    if (meters < 0) return Output(KIND_UNKNOWN, 0);
                }
                else
                {
                    float d = UNITY_SAMPLE_TEX2DARRAY(_EnvironmentDepthTexture, float3(uv, _Slice)).r;
                    if (d <= 0.0 || d >= 1.0) return Output(KIND_UNKNOWN, 0);
                    meters = Linearize(d); // same linearization as Meta's EnvironmentOcclusion.cginc
                }
                if (meters < _MinDepth) return Output(KIND_TOOCLOSE, meters);
                if (meters > _MaxDepth) return Output(KIND_TOOFAR, meters);
                return Output(KIND_VALID, meters);
            }
            ENDCG
        }
    }
}
