// Cartesian visibility filtering. The beam falloff is never blurred.
Shader "Hidden/FlashlightOcclusion"
{
    Properties { _MainTex ("Beam", 2D) = "white" {} _OcclusionMap ("Distances", 2D) = "white" {} }
    SubShader
    {
        Cull Off ZWrite Off ZTest Always
        CGINCLUDE
        #include "UnityCG.cginc"
        sampler2D _MainTex, _OcclusionMap, _VisibilityMask, _BlurredMask, _WallMap;
        float4 _OcclusionMap_TexelSize, _Bounds, _BlurAxis, _WallMapRect, _WallMap_TexelSize;
        float _Rotation, _Softness, _SpreadDistance, _ShadowStrength, _Inset, _PixelsPerUnit, _WallFaceWrap;
        float4 _Origin;
        float2 BeamToWorld(float2 p)
        {
            float s = sin(_Rotation), c = cos(_Rotation);
            return float2(c*p.x - s*p.y, s*p.x + c*p.y) + _Origin.xy;
        }
        float2 WorldToUV(float2 w)
        {
            float s = sin(_Rotation), c = cos(_Rotation);
            float2 d = w - _Origin.xy;
            return (float2(c*d.x + s*d.y, -s*d.x + c*d.y) - _Bounds.xy) / _Bounds.zw;
        }
        // Wall map texel (FlashlightWallMapJob): 0 = floor, 1-254 = wall face (cells down to its base), 255 = other wall art.
        float WallCode(float2 w)
        {
            return round(tex2Dlod(_WallMap, float4((w - _WallMapRect.xy) / _WallMapRect.zw, 0, 0)).r * 255);
        }
        // Beam-local point -> center of its world pixel. Keeps shadow edges on the sprite pixel grid
        // while the beam texture moves and rotates with the light.
        float2 SnapToWorldPixel(float2 p)
        {
            float s = sin(_Rotation), c = cos(_Rotation);
            float2 w = float2(c*p.x - s*p.y, s*p.x + c*p.y) + _Origin.xy;
            w = (floor(w * _PixelsPerUnit) + 0.5) / _PixelsPerUnit - _Origin.xy;
            return float2(c*w.x + s*w.y, -s*w.x + c*w.y);
        }
        // Occlusion map: distance to the first shadow plate per angle bin (FlashlightOcclusionJob).
        float OcclusionRow(float bin, float row)
        {
            return tex2Dlod(_OcclusionMap, float4((bin + 0.5) * _OcclusionMap_TexelSize.x,
                                                  (row + 0.5) * _OcclusionMap_TexelSize.y, 0, 0)).r;
        }
        float BinCoord(float2 p)
        {
            float angle = atan2(p.x, p.y) - _Rotation;
            return (angle / 6.28318530718 + 0.5) * _OcclusionMap_TexelSize.z - 0.5;
        }
        float ShadowStart(float2 p)
        {
            float x = BinCoord(p);
            float bin = floor(x);
            float a = OcclusionRow(bin, 0);
            float b = OcclusionRow(bin + 1, 0);
            // Interpolate continuous faces only. At corners choose the actual bin,
            // rather than spreading the nearest wall to both sides.
            return abs(a-b) < max(0.25, min(a,b)*0.1) ? lerp(a,b,frac(x)) : (frac(x)<0.5 ? a : b);
        }
        // x, y = lit and covered*contact on the floor only, z = 1 on a wall, w = lit.
        // Floor values are blurred apart from walls so wall edges stay sharp.
        float4 MaskSample(float2 p, float aa)
        {
            float r = length(p);
            // Plate shadows fall on the floor only: wall pixels (DarknessManager.UpdateWallMap) are never covered here.
            // Wall faces are shaded in Compose.
            float onFloor = WallCode(BeamToWorld(p)) < 0.5 ? 1 : 0;
            float start = onFloor > 0 ? ShadowStart(p) : 1000;
            float behind = r - start + _Inset;
            // Each sample covers half a pixel.
            float covered = start < 999 ? smoothstep(-0.75 * aa, -0.25 * aa, behind) : 0;
            float contact = saturate(max(0, behind) / max(0.01, _SpreadDistance));
            float lit = 1 - covered;
            return float4(lit*onFloor, covered*contact*onFloor, 1-onFloor, lit);
        }
        float4 Mask(v2f_img i) : SV_Target
        {
            float2 p = _Bounds.xy + i.uv * _Bounds.zw;
            float exit = ShadowStart(p);
            // Pixel coverage depends on the wall boundary, not just radial distance.
            // Ignore derivatives across corners/no-wall bins, where exit is discontinuous.
            float2 exitDelta = float2(ddx(exit), ddy(exit));
            float2 radialDelta = float2(ddx(length(p)), ddy(length(p)));
            float faceTolerance = max(0.25, min(exit, 20.0) * 0.1);
            float2 continuous = step(abs(exitDelta), float2(faceTolerance, faceTolerance));
            float2 edgeDelta = radialDelta - exitDelta * continuous;
            float aa = max(abs(edgeDelta.x) + abs(edgeDelta.y), 0.0001);
            float2 px = ddx(p) * 0.25;
            float2 py = ddy(p) * 0.25;
            // Pixel-art mode: one decision per world pixel (supersampling would bring back sliding edges).
            // Branch only after the derivative ops, which must stay outside flow control.
            [branch] if (_PixelsPerUnit > 0)
                return MaskSample(SnapToWorldPixel(p), 0.0001);
            float4 mask = (MaskSample(p-px-py, aa) + MaskSample(p+px-py, aa)
                         + MaskSample(p-px+py, aa) + MaskSample(p+px+py, aa)) * 0.25;
            return mask;
        }
        float4 Blur(v2f_img i) : SV_Target
        {
            float4 sum = 0;
            float total = 0;
            // Taps ~2-3 texels apart; sparser taps copy hard edges into visible stripes.
            [unroll] for (int k=-24; k<=24; k++)
            {
                float t = k / 24.0;
                float weight = exp(-4.5*t*t);
                float2 uv = i.uv + _BlurAxis.xy * (2*_Softness*t);
                // Beyond the cookie bounds there is no occlusion; clamping would smear border shadows.
                float4 value = (all(uv>=0) && all(uv<=1)) ? tex2D(_MainTex, uv) : float4(1,0,0,1);
                sum += value * weight;
                total += weight;
            }
            return sum / total;
        }
        float SurfaceLit(float2 uv)
        {
            float4 hard = tex2Dlod(_VisibilityMask,float4(uv,0,0));
            float4 soft = tex2Dlod(_BlurredMask,float4(uv,0,0));
            // Blur over floor pixels only.
            float floorWeight = max(1-soft.b, 0.001);
            float softLit = saturate(soft.r / floorWeight);
            float covered = 1-softLit;
            // Contact remains attached to the wall. Farther shadow edges become symmetric.
            float centerCovered = 1-hard.a;
            float centerContact = hard.g / max(centerCovered,0.001);
            float neighborContact = soft.g / floorWeight / max(covered,0.001);
            float contact = lerp(neighborContact, centerContact, centerCovered);
            float lit = lerp(hard.a, softLit, saturate(contact));
            // Walls are never blurred.
            return lerp(lit, hard.a, hard.b);
        }
        // A wall face is a vertical face standing on its base line and facing down (-y).
        // It dims smoothly as the light turns away from it, and takes the shadow of the floor right in front of it.
        float WallFaceLit(float2 w, float run)
        {
            float cell = _WallMapRect.z * _WallMap_TexelSize.x;
            float row = floor((w.y - _WallMapRect.y) / cell);
            float2 base = float2(w.x, _WallMapRect.y + (row - run + 1) * cell);
            float2 toLight = _Origin.xy - base;
            float facing = -toLight.y / max(length(toLight), 0.0001);
            float lit = saturate((facing + _WallFaceWrap) / (1 + _WallFaceWrap));
            // Only while facing the light: behind it, the floor in front lies in this wall's own shadow,
            // which facing already accounts for.
            [branch] if (facing > 0)
            {
                float2 front = WorldToUV(base - float2(0, 0.5 * cell));
                if (all(front >= 0) && all(front <= 1)) lit *= SurfaceLit(front);
            }
            return lit;
        }
        float4 Compose(v2f_img i) : SV_Target
        {
            float alpha = tex2D(_MainTex,i.uv).a;
            float lit = SurfaceLit(i.uv);
            float2 p = _Bounds.xy + i.uv * _Bounds.zw;
            if (_PixelsPerUnit > 0) p = SnapToWorldPixel(p);
            float2 w = BeamToWorld(p);
            float code = WallCode(w);
            [branch] if (code > 0.5 && code < 254.5) lit = WallFaceLit(w, code);
            return float4(1,1,1,alpha*lerp(1,lit,_ShadowStrength));
        }
        ENDCG
        Pass { CGPROGRAM
            #pragma target 3.0
            #pragma vertex vert_img
            #pragma fragment Mask
        ENDCG }
        Pass { CGPROGRAM
            #pragma target 3.0
            #pragma vertex vert_img
            #pragma fragment Blur
        ENDCG }
        Pass { CGPROGRAM
            #pragma target 3.0
            #pragma vertex vert_img
            #pragma fragment Compose
        ENDCG }
    }
}
