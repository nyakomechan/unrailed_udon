// Samplers
UNITY_DECLARE_TEX2D(_MainTex);
#if defined(USE_EMISSION_MAP)
sampler2D   _EmissionMap;
#endif
#if defined(USE_OCCLUSION_MAP)
sampler2D   _OcclusionMap;
#endif
#if defined(USE_NORMAL_MAPS)
sampler2D   _BumpMap;
#endif
#if defined(USE_SPECULAR)
sampler2D   _MetallicMap;
sampler2D   _GlossMap;
#endif
#if defined(USE_DETAIL_MAPS)
sampler2D   _DetailAlbedoMap;
sampler2D   _DetailMask;
    #if defined(USE_NORMAL_MAPS)
sampler2D   _DetailNormalMap;
    #endif
#endif
#if defined(USE_MATCAP)
sampler2D   _Matcap;
sampler2D   _MatcapMask;
#endif
#if defined(USE_AUDIOLINK)
UNITY_DECLARE_TEX2D_NOSAMPLER(_AudioLinkMask);
#endif
sampler2D   _HueShiftMask;
sampler2D   _Ramp;
sampler2D   _ColorMask;

// Properties
VRCHAT_DEFINE_ATLAS_PROPERTY(half4, _MainTex_ST);
VRCHAT_DEFINE_ATLAS_PROPERTY(half4, _Ramp_ST);

#if defined(USE_EMISSION_MAP)
    VRCHAT_DEFINE_ATLAS_PROPERTY(half4, _EmissionMap_ST);
#endif

#if defined(USE_OCCLUSION_MAP)
    VRCHAT_DEFINE_ATLAS_PROPERTY(half4, _OcclusionMap_ST);
    VRCHAT_DEFINE_ATLAS_PROPERTY(uint, _OcclusionMapChannel);
#endif

#if defined(USE_NORMAL_MAPS)
    VRCHAT_DEFINE_ATLAS_PROPERTY(half4, _BumpMap_ST);
#endif

#if defined(USE_SPECULAR)
    VRCHAT_DEFINE_ATLAS_PROPERTY(half4, _MetallicMap_ST);
    VRCHAT_DEFINE_ATLAS_PROPERTY(uint, _MetallicMapChannel);
    VRCHAT_DEFINE_ATLAS_PROPERTY(half4, _GlossMap_ST);
    VRCHAT_DEFINE_ATLAS_PROPERTY(uint, _GlossMapChannel);
#endif

#if defined(USE_DETAIL_MAPS)
    VRCHAT_DEFINE_ATLAS_PROPERTY(half4, _DetailMask_ST);
    VRCHAT_DEFINE_ATLAS_PROPERTY(uint, _DetailMaskChannel);
    VRCHAT_DEFINE_ATLAS_PROPERTY(half4, _DetailAlbedoMap_ST);
    VRCHAT_DEFINE_ATLAS_PROPERTY(half, _DetailMode);
    VRCHAT_DEFINE_ATLAS_PROPERTY(uint, _DetailUV);
    #if defined(USE_NORMAL_MAPS)
        VRCHAT_DEFINE_ATLAS_PROPERTY(half4, _DetailNormalMap_ST);
    #endif
    VRCHAT_DEFINE_ATLAS_PROPERTY(half, _DetailHueShift);
#endif

#if defined(USE_MATCAP)
    VRCHAT_DEFINE_ATLAS_PROPERTY(half4, _MatcapMask_ST);
    VRCHAT_DEFINE_ATLAS_PROPERTY(uint, _MatcapMaskChannel);
    VRCHAT_DEFINE_ATLAS_PROPERTY(uint, _MatcapType);
    VRCHAT_DEFINE_ATLAS_PROPERTY(half, _MatcapStrength);
#endif

#if defined(USE_AUDIOLINK)
VRCHAT_DEFINE_ATLAS_PROPERTY(uint, _AudioLinkMode);
VRCHAT_DEFINE_ATLAS_PROPERTY(uint, _ALEnableFallback);
VRCHAT_DEFINE_ATLAS_PROPERTY(float, _ALFallbackSpeed);
VRCHAT_DEFINE_ATLAS_PROPERTY(half4, _AudioLinkMask_ST);
VRCHAT_DEFINE_ATLAS_PROPERTY(uint, _ALBlendMode);
VRCHAT_DEFINE_ATLAS_PROPERTY(half4, _ALTint);
VRCHAT_DEFINE_ATLAS_PROPERTY(half, _ALIntensity);
VRCHAT_DEFINE_ATLAS_PROPERTY(uint, _ALMaskUVChannel);
VRCHAT_DEFINE_ATLAS_PROPERTY(uint, _ALMaskByEmission);
VRCHAT_DEFINE_ATLAS_PROPERTY(uint, _ALMaskChannel);
VRCHAT_DEFINE_ATLAS_PROPERTY(half, _ALRimMaskStrength);
VRCHAT_DEFINE_ATLAS_PROPERTY(half, _ALRimMaskSmoothing);
VRCHAT_DEFINE_ATLAS_PROPERTY(uint, _ALBand);
VRCHAT_DEFINE_ATLAS_PROPERTY(half, _ALSmoothing);
VRCHAT_DEFINE_ATLAS_PROPERTY(uint, _ALScrollCenterOut);
VRCHAT_DEFINE_ATLAS_PROPERTY(half, _ALScrollScale);
VRCHAT_DEFINE_ATLAS_PROPERTY(uint, _ALEffectUseMask);
VRCHAT_DEFINE_ATLAS_PROPERTY(uint, _ALEffectMaskChannel);
VRCHAT_DEFINE_ATLAS_PROPERTY(uint, _ALEffectUVChannel);
VRCHAT_DEFINE_ATLAS_PROPERTY(half, _ALBarSmoothing);
#endif

VRCHAT_DEFINE_ATLAS_PROPERTY(half4, _ColorMask_ST);

VRCHAT_DEFINE_ATLAS_PROPERTY(half4, _HueShiftMask_ST);
VRCHAT_DEFINE_ATLAS_PROPERTY(uint, _HueShiftMaskChannel);

VRCHAT_DEFINE_ATLAS_PROPERTY(half4, _Color);
VRCHAT_DEFINE_ATLAS_PROPERTY(half, _BumpScale);
VRCHAT_DEFINE_ATLAS_PROPERTY(half, _VertexColor);

VRCHAT_DEFINE_ATLAS_PROPERTY(half, _ShadowBoost);
VRCHAT_DEFINE_ATLAS_PROPERTY(half, _ShadowAlbedo);
VRCHAT_DEFINE_ATLAS_PROPERTY(half, _MinBrightness);
VRCHAT_DEFINE_ATLAS_PROPERTY(half, _LimitBrightness);

VRCHAT_DEFINE_ATLAS_PROPERTY(half, _HueShift);

VRCHAT_DEFINE_ATLAS_PROPERTY(half3, _RimColor);
VRCHAT_DEFINE_ATLAS_PROPERTY(half, _RimAlbedoTint);
VRCHAT_DEFINE_ATLAS_PROPERTY(half, _RimIntensity);
VRCHAT_DEFINE_ATLAS_PROPERTY(half, _RimRange);
VRCHAT_DEFINE_ATLAS_PROPERTY(half, _RimSharpness);
VRCHAT_DEFINE_ATLAS_PROPERTY(half, _RimEnvironmental);

VRCHAT_DEFINE_ATLAS_PROPERTY(half4, _ColorMaskColor1);
VRCHAT_DEFINE_ATLAS_PROPERTY(half, _ColorMaskEmissionStrength1);
VRCHAT_DEFINE_ATLAS_PROPERTY(half4, _ColorMaskColor2);
VRCHAT_DEFINE_ATLAS_PROPERTY(half, _ColorMaskEmissionStrength2);
VRCHAT_DEFINE_ATLAS_PROPERTY(half4, _ColorMaskColor3);
VRCHAT_DEFINE_ATLAS_PROPERTY(half, _ColorMaskEmissionStrength3);
VRCHAT_DEFINE_ATLAS_PROPERTY(half4, _ColorMaskColor4);
VRCHAT_DEFINE_ATLAS_PROPERTY(half, _ColorMaskEmissionStrength4);
VRCHAT_DEFINE_ATLAS_PROPERTY(uint, _ColorMaskBlendMode);

#define COLORMASK_BLEND_MULTIPLY 0
#define COLORMASK_BLEND_ADDITIVE 1

//VRCHAT_DEFINE_ATLAS_PROPERTY(half, _Cutoff);
//VRCHAT_DEFINE_ATLAS_PROPERTY(half, _AlphaToMask);

#if defined(USE_EMISSION_MAP)
    VRCHAT_DEFINE_ATLAS_PROPERTY(half4, _EmissionColor);
    VRCHAT_DEFINE_ATLAS_PROPERTY(half, _EmissionStrength);
    VRCHAT_DEFINE_ATLAS_PROPERTY(uint, _EmissionUV);
    VRCHAT_DEFINE_ATLAS_PROPERTY(half, _EmissionHueShift);
#endif

#if defined(USE_OCCLUSION_MAP)
    VRCHAT_DEFINE_ATLAS_PROPERTY(half, _OcclusionStrength);
#endif

#if defined(USE_SPECULAR)
    VRCHAT_DEFINE_ATLAS_PROPERTY(half, _MetallicStrength);
    VRCHAT_DEFINE_ATLAS_PROPERTY(half, _GlossStrength);
    VRCHAT_DEFINE_ATLAS_PROPERTY(half, _SpecularSharpness);
    VRCHAT_DEFINE_ATLAS_PROPERTY(half, _Reflectance);
#endif

#if defined(USE_DETAIL_MAPS)
    VRCHAT_DEFINE_ATLAS_PROPERTY(half, _DetailNormalMapScale);
#endif

// Atlas Texture Modes
#if defined(VRCHAT_ATLASING_ENABLED)
    VRCHAT_DEFINE_ATLAS_TEXTUREMODE(_MainTex);
    VRCHAT_DEFINE_ATLAS_TEXTUREMODE(_Ramp);

    #if defined(USE_EMISSION_MAP)
        VRCHAT_DEFINE_ATLAS_TEXTUREMODE(_EmissionMap);
    #endif

    #if defined(USE_OCCLUSION_MAP)
        VRCHAT_DEFINE_ATLAS_TEXTUREMODE(_OcclusionMap);
    #endif

    #if defined(USE_NORMAL_MAPS)
        VRCHAT_DEFINE_ATLAS_TEXTUREMODE(_BumpMap);
    #endif

    #if defined(USE_SPECULAR)
        VRCHAT_DEFINE_ATLAS_TEXTUREMODE(_MetallicMap);
        VRCHAT_DEFINE_ATLAS_TEXTUREMODE(_GlossMap);
    #endif

    #if defined(USE_DETAIL_MAPS)
        VRCHAT_DEFINE_ATLAS_TEXTUREMODE(_DetailAlbedoMap);
        VRCHAT_DEFINE_ATLAS_TEXTUREMODE(_DetailMask);
        #if defined(USE_NORMAL_MAPS)
            VRCHAT_DEFINE_ATLAS_TEXTUREMODE(_DetailNormalMap);
        #endif
    #endif

    #if defined(USE_MATCAP)
        VRCHAT_DEFINE_ATLAS_TEXTUREMODE(_MatcapMask);
    #endif

    VRCHAT_DEFINE_ATLAS_TEXTUREMODE(_HueShiftMask);

    #if defined(USE_AUDIOLINK)
        VRCHAT_DEFINE_ATLAS_TEXTUREMODE(_AudioLinkMask);
    #endif
#endif