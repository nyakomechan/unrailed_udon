// A fairly feature-full shader for toon-style shading in VRChat. Outline variant is PC-only, but placed in `Mobile` folder for consistency.
Shader "VRChat/Mobile/Toon Standard (Outline)"
{
    Properties
    {
        [Enum(Off, 0, Front, 1, Back, 2)] _Culling ("Culling", Int) = 2
        //[Enum(Opaque, 0)] _BlendMode("Blend Mode", Int) = 0
        //[Enum(Opaque, 0, Cutout, 1, Cutout Plus, 2, Transparent, 3, Fade, 4)] _BlendMode("Blend Mode", Int) = 0

        _Color ("Main Color", Color) = (1,1,1,1)
        _MainTex ("Main Texture", 2D) = "white" {}
        //_Cutoff ("Alpha Cutoff", Range(0,1)) = 0.5
        [ToggleUI] _VertexColor ("Apply Vertex Color", Float) = 0.0

        _Ramp ("Shadow Ramp", 2D) = "white" {}
        _ShadowBoost ("Shadow Boost", Range(0,1)) = 0.0
        _ShadowAlbedo ("Shadow Tint", Range(0,1)) = 0.5
        [PowerSlider(2)] _MinBrightness ("Min Brightness", Range(0,0.1)) = 0.0
        [ToggleUI] _LimitBrightness ("Limit Brightness", Float) = 1.0

        [Normal] _BumpMap ("Normal Map", 2D) = "bump" {}
        _BumpScale ("Normal Scale", Float) = 1.0

        _MetallicMap ("Metallic Map", 2D) = "white" {}
        [Enum(Red, 0, Green, 1, Blue, 2, Alpha, 3)] _MetallicMapChannel ("Color Channel", Int) = 0
        _MetallicStrength ("Metallic Strength", Range(0,1)) = 0
        _GlossMap ("Gloss Map", 2D) = "white" {}
        [Enum(Red, 0, Green, 1, Blue, 2, Alpha, 3)] _GlossMapChannel ("Color Channel", Int) = 3
        _GlossStrength ("Gloss Strength", Range(0,1)) = 0.5
        _Reflectance ("Reflectance", Range(0,1)) = 0.5
        _SpecularSharpness("Sharpness", Range(0,1)) = 0

        _Matcap ("Matcap", 2D) = "white" {}
        _MatcapMask ("Matcap Mask", 2D) = "white" {}
        [Enum(Red, 0, Green, 1, Blue, 2, Alpha, 3)] _MatcapMaskChannel ("Color Channel", Int) = 0
        [Enum(Additive, 0, Multiplicative, 1)] _MatcapType ("Matcap Type", Int) = 0
        _MatcapStrength ("Matcap Strength", Range(0,1)) = 1.0

        _RimColor("Color", Color) = (1,1,1,1)
        _RimAlbedoTint("Albedo Tint", Range(0,1)) = 0.0
        _RimIntensity("Intensity", Range(0,1)) = 0.5
        _RimRange("Range", Range(0,1)) = 0.3
        _RimSharpness("Softness", Range(0,1)) = 0.1
        [ToggleUI] _RimEnvironmental("Environmental", Float) = 0.0

        _EmissionMap ("Emission Map", 2D) = "white" {}
        _EmissionColor ("Emission Color", Color) = (0,0,0)
        _EmissionStrength ("Strength", Range(0, 2)) = 1
        [Enum(UV0, 0, UV1, 1)] _EmissionUV ("UV Map", Int) = 0

        _OcclusionMap ("Occlusion Map", 2D) = "white" {}
        [Enum(Red, 0, Green, 1, Blue, 2, Alpha, 3)] _OcclusionMapChannel ("Color Channel", Int) = 1
        _OcclusionStrength ("Occlusion Strength", Range(0,1)) = 1
    
        [Enum(AlphaBlended, 0, Additive, 1, Multiply, 2, MultiplyX2, 3)] _DetailMode ("Detail Mode", Int) = 0
        _DetailMask ("Detail Mask", 2D) = "white" {}
        [Enum(Red, 0, Green, 1, Blue, 2, Alpha, 3)] _DetailMaskChannel ("Color Channel", Int) = 3
        _DetailAlbedoMap ("Detail Texture", 2D) = "black" {}
        _DetailNormalMap ("Detail Normal Map", 2D) = "bump" {}
        _DetailNormalMapScale ("Detail Normal Scale", Float) = 1.0
        [Enum(UV0, 0, UV1, 1)] _DetailUV ("UV Map", Int) = 0

        _HueShift ("Albedo Hue Shift", Range(0,6.283185)) = 0
        _DetailHueShift ("Detail Hue Shift", Range(0,6.283185)) = 0
        _EmissionHueShift ("Emission Hue Shift", Range(0,6.283185)) = 0
        _HueShiftMask ("Hue Shift Mask", 2D) = "white" {}
        [Enum(Red, 0, Green, 1, Blue, 2, Alpha, 3)] _HueShiftMaskChannel ("Color Channel", Int) = 1

        _OutlineMask ("Thickness", 2D) = "white" {}
        [Enum(Red, 0, Green, 1, Blue, 2, Alpha, 3)] _OutlineMaskChannel ("Color Channel", Int) = 0
        [PowerSlider(2)] _OutlineThickness ("Thickness Multiplier", Range(0, 0.5)) = 0.05
        _OutlineColor ("Color", Color) = (0,0,0,1)
        _OutlineFromAlbedo ("Color From Albedo", Range(0,1)) = 0.0
        
        _ColorMask ("Color Mask", 2D) = "black" {}
        _ColorMaskColor1("Color 1", Color) = (1,1,1,1)
        _ColorMaskEmissionStrength1("Color 1 Emission", Range(0,2)) = 0
        _ColorMaskColor2("Color 2", Color) = (1,1,1,1)
        _ColorMaskEmissionStrength2("Color 2 Emission", Range(0,2)) = 0
        _ColorMaskColor3("Color 3", Color) = (1,1,1,1)
        _ColorMaskEmissionStrength3("Color 3 Emission", Range(0,2)) = 0
        _ColorMaskColor4("Color 4", Color) = (1,1,1,1)
        _ColorMaskEmissionStrength4("Color 4 Emission", Range(0,2)) = 0
        
        [Enum(Multiply, 0, Additive, 1)] _ColorMaskBlendMode("Color Mask Blend Mode", Int) = 0
        
        // AudioLink
        [Enum(Pulse, 0, Scroll, 1, Bar, 2, UV Based (X), 3)]_AudioLinkMode("AudioLink Mode", Int) = 0
        [Enum(Replace, 0, Additive, 1, Multiply, 2)]_ALBlendMode("Blend Mode", Int) = 0
        _AudioLinkMask("AudioLink Mask", 2D) = "white" {}
        _ALTint("AudioLink Tint", Color) = (0,0.8980392,0.627451,1)
        _ALIntensity("AudioLink Intensity", Range(0,12)) = 1
        [Enum(UV0, 0, UV1, 1, UV2, 2, UV3, 3)]_ALMaskUVChannel("Mask UV Channel", Int) = 0
        [ToggleUI]_ALMaskByEmission("Mask By Emission Map", Int) = 1
        [Enum(RGB, 0, R, 1, G, 2, B, 3, A, 4)]_ALMaskChannel("Mask Channel", Int) = 0
        _ALRimMaskStrength("Rim Mask Strength", Range(-1,1)) = 0
        _ALRimMaskSmoothing("Rim Mask Smoothing", Float) = 1
        [Enum(Bass, 0, Mids, 1, Mid Highs, 2, Highs, 3, UV Based (Y), 4)]_ALBand("AudioLink Band", Int) = 0
        [IntRange]_ALSmoothing("AudioLink Smoothing", Range(0, 15)) = 7
        [ToggleUI]_ALEffectUseMask("Use Mask for Effects", Int) = 0
        [Enum(R, 0, G, 1, B, 2, A, 3)]_ALEffectMaskChannel("Effect Mask Channel", Int) = 0
        [Enum(UV0, 0, UV1, 1, UV2, 2, UV3, 3)]_ALEffectUVChannel("UV Channel", Int) = 0
        [ToggleUI]_ALScrollCenterOut("Center-Out Scrolling", Int) = 0
        _ALScrollScale("Scroll Scale", Float) = 1
        _ALBarSmoothing("Bar Gradient Smoothing", Range(0.001, 0.99)) = 0.1
        [ToggleUI]_ALEnableFallback("Enable Fallback", Int) = 0
        _ALFallbackSpeed("Fallback Speed", Float) = 1

        //[Enum(UnityEngine.Rendering.BlendMode)]_SrcBlend ("__src", int) = 1
        //[Enum(UnityEngine.Rendering.BlendMode)]_DstBlend ("__dst", int) = 0
        //[Enum(Off,0,On,1)]_ZWrite ("__zw", int) = 1
        //_AlphaToMask ("Alpha To Mask", Int) = 0
    }
    SubShader
    {
        Cull [_Culling]
        //AlphaToMask [_AlphaToMask]
        AlphaToMask Off

        Tags { "VRCFallback" = "toonstandard" "RenderType" = "Opaque" }

        Pass
        {
            Name "FORWARD"
            Tags { "LightMode" = "ForwardBase" }
            
            //Blend [_SrcBlend] [_DstBlend]
            Blend One Zero
            //ZWrite [_ZWrite]
            ZWrite On
            
            CGPROGRAM
            #pragma target 3.0
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_fog
            #pragma multi_compile_fwdbase
            #pragma multi_compile _ VERTEXLIGHT_ON
            //#pragma shader_feature_local_fragment _ _ALPHATEST_ON _ALPHABLEND_ON _ALPHAPREMULTIPLY_ON
            //#pragma multi_compile _ VRCHAT_ATLASING_ENABLED

            #pragma shader_feature_local_fragment _ USE_SPECULAR
            #pragma shader_feature_local_fragment _ USE_MATCAP
            #pragma shader_feature_local_fragment _ USE_DETAIL_MAPS
            #pragma shader_feature_local_fragment _ USE_NORMAL_MAPS
            #pragma shader_feature_local_fragment _ USE_OCCLUSION_MAP
            #pragma shader_feature_local _ USE_AUDIOLINK
            #pragma dynamic_branch_local_fragment _ USE_RIMLIGHT
            #pragma dynamic_branch_local_fragment _ USE_HUE_SHIFT
            #pragma dynamic_branch_local_fragment _ USE_COLOR_MASK

            // this one is practically free in terms of performance, so save some variants by always enabling it for now
            #define USE_EMISSION_MAP

            // disable variants that are not needed, including realtime shadows (not supported)
            #pragma skip_variants LIGHTMAP_ON DYNAMICLIGHTMAP_ON DIRLIGHTMAP_COMBINED STEREO_CUBEMAP_RENDER_ON
            #pragma skip_variants SHADOWS_DEPTH SHADOWS_SCREEN SHADOWS_CUBE SHADOWS_SOFT SHADOWS_SHADOWMASK LIGHTMAP_SHADOW_MIXING

            #ifndef UNITY_PASS_FORWARDBASE
                #define UNITY_PASS_FORWARDBASE
            #endif
            
            #if defined(USE_AUDIOLINK)
                #define NEED_ALL_UV
            #endif

            #include "UnityCG.cginc"
            #include "UnityPBSLighting.cginc"
            #include "AutoLight.cginc"
            #include "Lighting.cginc"
            #include "./CG/VRCAtlasingShim.cginc"
            #include "./CG/DataStructs.cginc"
            #include "./CG/Definitions.cginc"
            #include "./CG/Helpers.cginc"
            #if defined(USE_AUDIOLINK)
            #include "./Dependencies/AudioLink.cginc"
            #include "./CG/AudioLinkEffects.cginc"
            #endif
            #include "./CG/Lighting.cginc"
            #include "./CG/VertexFragment.cginc"
            
            ENDCG
        }

        Pass
        {
            Name "FORWARD_OUTLINE"
            Tags { "LightMode" = "ForwardBase" }
            
            Blend One Zero
            ZWrite On

            Cull Front
            
            CGPROGRAM
            #pragma target 3.0
            #pragma vertex vert_outline
            #pragma fragment frag_outline
            #pragma multi_compile_fog
            #pragma multi_compile_fwdbase
            #pragma multi_compile _ VERTEXLIGHT_ON
            //#pragma multi_compile _ VRCHAT_ATLASING_ENABLED

            // disable variants that are not needed, including realtime shadows (not supported)
            #pragma skip_variants LIGHTMAP_ON DYNAMICLIGHTMAP_ON DIRLIGHTMAP_COMBINED STEREO_CUBEMAP_RENDER_ON
            #pragma skip_variants SHADOWS_DEPTH SHADOWS_SCREEN SHADOWS_CUBE SHADOWS_SOFT SHADOWS_SHADOWMASK LIGHTMAP_SHADOW_MIXING

            #define VRCHAT_PASS_OUTLINE
            #ifndef UNITY_PASS_FORWARDBASE
                #define UNITY_PASS_FORWARDBASE
            #endif

            #include "UnityCG.cginc"
            #include "UnityPBSLighting.cginc"
            #include "AutoLight.cginc"
            #include "Lighting.cginc"
            #include "./CG/VRCAtlasingShim.cginc"
            #include "./CG/DataStructs.cginc"
            #include "./CG/Definitions.cginc"
            #include "./CG/Helpers.cginc"
            #include "./CG/Outlines.cginc"
            
            ENDCG
        }

        Pass
        {
            Name "FWDADD"
            Tags { "LightMode" = "ForwardAdd" }
            
            //Blend [_SrcBlend] One
            Blend One One
            ZWrite Off
            ZTest LEqual

            CGPROGRAM
            #pragma target 3.0
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_fog
            #pragma multi_compile_fwdadd_fullshadow
            //#pragma shader_feature_local_fragment _ _ALPHATEST_ON _ALPHABLEND_ON _ALPHAPREMULTIPLY_ON
            //#pragma multi_compile _ VRCHAT_ATLASING_ENABLED

            #pragma shader_feature_local_fragment _ USE_SPECULAR
            #pragma shader_feature_local_fragment _ USE_MATCAP
            #pragma shader_feature_local_fragment _ USE_DETAIL_MAPS
            #pragma shader_feature_local_fragment _ USE_NORMAL_MAPS
            #pragma shader_feature_local_fragment _ USE_OCCLUSION_MAP
            #pragma dynamic_branch_local_fragment _ USE_RIMLIGHT
            #pragma dynamic_branch_local_fragment _ USE_HUE_SHIFT
            #pragma dynamic_branch_local_fragment _ USE_COLOR_MASK

            // disable variants that are not needed, including realtime shadows (not supported)
            #pragma skip_variants LIGHTMAP_ON DYNAMICLIGHTMAP_ON DIRLIGHTMAP_COMBINED STEREO_CUBEMAP_RENDER_ON
            #pragma skip_variants SHADOWS_DEPTH SHADOWS_SCREEN SHADOWS_CUBE SHADOWS_SOFT SHADOWS_SHADOWMASK LIGHTMAP_SHADOW_MIXING

            #ifndef UNITY_PASS_FORWARDADD
                 #define UNITY_PASS_FORWARDADD
            #endif

            #include "UnityCG.cginc"
            #include "UnityPBSLighting.cginc"
            #include "AutoLight.cginc"
            #include "Lighting.cginc"
            #include "./CG/VRCAtlasingShim.cginc"
            #include "./CG/DataStructs.cginc"
            #include "./CG/Definitions.cginc"
            #include "./CG/Helpers.cginc"
            #include "./CG/Lighting.cginc"
            #include "./CG/VertexFragment.cginc"
            
            ENDCG
        }

        Pass
        {
            Name "SHADOWCASTER"
            Tags { "LightMode" = "ShadowCaster" }

            ZWrite On
            ZTest LEqual

            CGPROGRAM
            #pragma target 3.0
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_shadowcaster
            //#pragma shader_feature_local_fragment _ _ALPHATEST_ON _ALPHABLEND_ON _ALPHAPREMULTIPLY_ON
            //#pragma multi_compile _ VRCHAT_ATLASING_ENABLED

            #ifndef UNITY_PASS_SHADOWCASTER
                #define UNITY_PASS_SHADOWCASTER
            #endif

            // disable variants that are not needed, including realtime shadows (not supported)
            #pragma skip_variants LIGHTMAP_ON DYNAMICLIGHTMAP_ON DIRLIGHTMAP_COMBINED STEREO_CUBEMAP_RENDER_ON
            #pragma skip_variants SHADOWS_DEPTH SHADOWS_SCREEN SHADOWS_CUBE SHADOWS_SOFT SHADOWS_SHADOWMASK LIGHTMAP_SHADOW_MIXING
            #pragma skip_variants FOG_LINEAR FOG_EXP FOG_EXP2

            #include "UnityCG.cginc"
            #include "UnityPBSLighting.cginc"
            #include "AutoLight.cginc"
            #include "Lighting.cginc"
            #include "./CG/VRCAtlasingShim.cginc"
            #include "./CG/DataStructs.cginc"
            #include "./CG/Definitions.cginc"
            #include "./CG/Helpers.cginc"
            #include "./CG/Lighting.cginc"
            #include "./CG/VertexFragment.cginc"
            
            ENDCG
        }
    }
    Fallback "VRChat/Mobile/Diffuse"
    CustomEditor "VRC.ToonStandard.ToonStandardShaderEditor"
}