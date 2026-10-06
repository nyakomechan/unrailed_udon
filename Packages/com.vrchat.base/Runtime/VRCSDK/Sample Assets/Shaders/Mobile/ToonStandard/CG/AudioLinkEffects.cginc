#if defined(USE_AUDIOLINK)

// These are special wrappers to allow fallback behaviour
static bool audioLinkUseFallback = false;

half GetAudioLinkFallbackData(float offset)
{
    float speed = VRCHAT_GET_ATLAS_PROPERTY(_ALFallbackSpeed);
    float time = (_Time.y * speed + offset) % 1000;
    float fallback = (1.0 - frac(time)) * (sin(time * UNITY_PI) > 0);
    return fallback;
}

half GetAudioLinkData(uint2 uv)
{
    if (!audioLinkUseFallback)
    {
        return AudioLinkData(uv);
    }
    return GetAudioLinkFallbackData(frac(uv.x) + uv.y * 0.5);
}

half GetAudioLinkDataLerped(float2 uv)
{
    if (!audioLinkUseFallback)
    {
        return AudioLinkLerp(uv);
    }
    return GetAudioLinkFallbackData(uv.x / AUDIOLINK_WIDTH + uv.y * 0.25);
}

half3 GetAudioLinkPulse(int band)
{
    uint smoothing = VRCHAT_GET_ATLAS_PROPERTY(_ALSmoothing);
    half alData = GetAudioLinkData(ALPASS_FILTEREDAUDIOLINK + uint2(15 - smoothing, band));
    return alData.rrr;
}

half3 GetAudioLinkScroll(uint band, DotProducts dotProducts, uint maskUVidx, uint effectUVidx, float2 effectUV, half4 maskTexData)
{
    bool centerOut = VRCHAT_GET_ATLAS_PROPERTY(_ALScrollCenterOut);
    float scrollScale = VRCHAT_GET_ATLAS_PROPERTY(_ALScrollScale);
    bool useMask = VRCHAT_GET_ATLAS_PROPERTY(_ALEffectUseMask);
    float2 scrollUV = 0;
        
    UNITY_BRANCH if (centerOut)
    {
        scrollUV = (1.0 - dotProducts.vdn) * 0.5 + 0.5;
    }
    else if (useMask)
    {
        uint effectMaskChannel = VRCHAT_GET_ATLAS_PROPERTY(_ALEffectMaskChannel);
 
        // avoid resampling the mask if the UVs are the same
        UNITY_BRANCH if (maskUVidx != effectUVidx)
        {
            scrollUV.x = UNITY_SAMPLE_TEX2D_SAMPLER(_AudioLinkMask, _MainTex, VRCHAT_TRANSFORM_ATLAS_TEX_MODE(effectUV, _AudioLinkMask))[clamp(effectMaskChannel, 0, 3)];
        } else
        {
            scrollUV.x = maskTexData[clamp(_ALEffectMaskChannel, 0, 3)];
        }
        scrollUV.y = effectUV.y;
    }
    else
    {
        scrollUV = frac(effectUV);
    }
        
    half alData = GetAudioLinkDataLerped(ALPASS_AUDIOBASS + float2(scrollUV.x * scrollScale * AUDIOLINK_WIDTH, band));
    
    return alData.rrr;
}

half3 GetAudioLinkBar(uint band, float2 effectUV)
{
    uint smoothing = VRCHAT_GET_ATLAS_PROPERTY(_ALSmoothing);
    half alData = GetAudioLinkData(ALPASS_FILTEREDAUDIOLINK + uint2(15 - smoothing, band));
        
    half barSmoothing = VRCHAT_GET_ATLAS_PROPERTY(_ALBarSmoothing);
    alData = 1.0 - alData;
    alData = smoothstep(alData - barSmoothing, alData, 1.0 - frac(effectUV.y));
        
    return alData.rrr;
}

void ApplyAudioLinkMix(half3 input, int blendMode, half3 mask, half4 tint, inout half3 emission)
{
    switch (blendMode)
    {
        case 0:
            emission = lerp(emission, input * mask * tint.rgb, tint.a);
            return;
        case 1:
            emission += input * mask * tint.rgb * tint.a;
            return;
        case 2:
            emission = lerp(emission, emission * input * mask * tint.rgb, tint.a);   
            return;
        default:
            return;
    }
}

void ApplyAudioLink(v2f i, DotProducts dotProducts, Surface surface, inout half3 emission)
{
    bool audioLinkPresent = AudioLinkIsAvailable();
    bool useFallbackFlag = VRCHAT_GET_ATLAS_PROPERTY(_ALEnableFallback);
    audioLinkUseFallback = useFallbackFlag && !audioLinkPresent;
    
    UNITY_BRANCH if (!audioLinkPresent && !useFallbackFlag) return;
    
    uint mode = VRCHAT_GET_ATLAS_PROPERTY(_AudioLinkMode);
    uint blendMode = VRCHAT_GET_ATLAS_PROPERTY(_ALBlendMode);
    
    // Allow offsetting the Audio Link mode by Effect UV (X)
    uint effectUVidx = VRCHAT_GET_ATLAS_PROPERTY(_ALEffectUVChannel);
    float2 effectUV = SelectUV(i.uv, i.uv23, effectUVidx);
    
    mode = mode > 2 ? floor(effectUV.x) % 3 : mode;
    
    half3 mask = 1.0;
    uint maskUVidx = VRCHAT_GET_ATLAS_PROPERTY(_ALMaskUVChannel);
    float2 maskUV = SelectUV(i.uv, i.uv23, maskUVidx);
    half4 maskTexData = UNITY_SAMPLE_TEX2D_SAMPLER(_AudioLinkMask, _MainTex, VRCHAT_TRANSFORM_ATLAS_TEX_MODE(maskUV, _AudioLinkMask)); 
    
    // We can either mask by the emission map or by the bespoke AudioLink mask
    if (_ALMaskByEmission)
    {
        mask = _ALMaskChannel == 0 ? surface.emissionMap : surface.emissionMap[clamp(_ALMaskChannel - 1, 0, 3)].rrr;
    }
    else
    {
        mask = _ALMaskChannel == 0 ? maskTexData.rgb : maskTexData[clamp(_ALMaskChannel - 1, 0, 3)].rrr;
    }
    
    half rimMaskStrength = VRCHAT_GET_ATLAS_PROPERTY(_ALRimMaskStrength);
    half rimMaskSmoothing = VRCHAT_GET_ATLAS_PROPERTY(_ALRimMaskSmoothing);
    half rimMask = rimMaskStrength > 0 ? dotProducts.vdn : 1.0 - dotProducts.vdn;
    
    rimMaskSmoothing = rimMaskSmoothing < 0 ? 1.0 / -rimMaskSmoothing : rimMaskSmoothing;
    mask *= lerp(1.0, saturate(pow(rimMask, rimMaskSmoothing)), abs(rimMaskStrength));
    
    half4 tint = VRCHAT_GET_ATLAS_PROPERTY(_ALTint);
    half intensity = VRCHAT_GET_ATLAS_PROPERTY(_ALIntensity);
    tint.rgb *= intensity;
    tint.a = saturate(tint.a * intensity);
    
    // Allow offsetting the Audio Link band by Effect UV (Y)
    uint band = VRCHAT_GET_ATLAS_PROPERTY(_ALBand);
    band = band > 3 ? floor(effectUV.y) % 4 : band;
    
    // Pulse
    UNITY_BRANCH if (mode == 0)
    {
        half3 effect = GetAudioLinkPulse(band);
        ApplyAudioLinkMix(effect, blendMode, mask, tint, /* inout */ emission);
        return;
    }
    
    // Scroll
    UNITY_BRANCH if (mode == 1)
    {
        half3 effect = GetAudioLinkScroll(band, dotProducts, maskUVidx, effectUVidx, effectUV, maskTexData);
        ApplyAudioLinkMix(effect, blendMode, mask, tint, /* inout */ emission);
        return;
    }
    
    // Bar
    UNITY_BRANCH if (mode == 2)
    {
        half3 effect = GetAudioLinkBar(band, effectUV);
        ApplyAudioLinkMix(effect, blendMode, mask, tint, /* inout */ emission);
        return;
    }
}
#endif