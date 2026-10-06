using UnityEngine;
using System.Collections.Generic;
using UnityEngine.Rendering;

[RequireComponent(typeof(Camera))]
[AddComponentMenu("")]
public class HighlightsFX : PostEffectsBase
{
    public static HighlightsFX Instance { get; protected set; }

    protected struct OutlineData
    {
        public readonly Renderer Renderer;
        public readonly int SubMeshCount;
        public readonly Color Color;

        public OutlineData(Renderer renderer, Mesh sharedMesh, Color color)
        {
            Renderer = renderer;
            SubMeshCount = sharedMesh.subMeshCount;
            Color = color;
        }

        public bool IsValid()
        {
            return Renderer != null;
        }
    }

    protected readonly Dictionary<Renderer, OutlineData> objectsToRender = new();
    protected Shader highlightShader;
    protected Material highlightMaterial;

    protected CommandBuffer _commandBuffer;

    #region Public Methods

    public static void EnableObjectHighlight(Renderer outlineRenderer, bool enable)
    {
        if(Instance != null)
        {
            Instance.EnableOutline(outlineRenderer, enable);
        }
    }

    public void EnableOutline(Renderer outlineRenderer, bool enable)
    {
        if(outlineRenderer == null)
        {
            return;
        }

        Mesh sharedMesh = null;
        if (outlineRenderer is MeshRenderer outlineMeshRenderer && outlineMeshRenderer.TryGetComponent(out MeshFilter meshFilter))
        {
            sharedMesh = meshFilter.sharedMesh;
        }
        else if (outlineRenderer is SkinnedMeshRenderer outlineSkinnedMeshRenderer)
        {
            sharedMesh = outlineSkinnedMeshRenderer.sharedMesh;
        }

        if (sharedMesh == null)
        {
            return;
        }

        OutlineData data = new OutlineData(
            outlineRenderer,
            sharedMesh,
            highlightColor
        );
        EnableOutline(outlineRenderer, data, enable);
    }

    private void EnableOutline(Renderer outlineRenderer, OutlineData outlineData, bool enable)
    {
        if(outlineRenderer == null)
        {
            return;
        }

        if(enable)
        {
            objectsToRender[outlineRenderer] = outlineData;
        }
        else
        {
            objectsToRender.Remove(outlineRenderer);
        }
    }

    #endregion
    
    #region Constants

    private const int DRAW_DEPTH_PREPASS = 0;
    private const int DRAW_HIGHLIGHT_PASS = 1;

    private const string CommandBufferName = "HighlightsFXMobile";
    private const string HighlightShaderName = "Hidden/VRChat/MobileHighlight";

    #endregion
    
    #region Unity Serialized Fields

    [SerializeField]
    private Color highlightColor = new Color(0.0f, 0.573f, 1.0f, 1.0f);

    [SerializeField]
    [Range(0.0f, 10.0f)]
    private float mobilePulseSpeed = 2.0f;

    [SerializeField]
    [Range(0.0f, 1.0f)]
    private float mobileMinimumOpacity = 0.2f;

    [SerializeField]
    [Range(0.0f, 1.0f)]
    private float mobileMaximumOpacity = 0.4f;

    #endregion

    #region Private Fields

    private static readonly int highlightColorId = Shader.PropertyToID("_HighlightColor");

    #endregion

    #region Unity Methods

    protected virtual void Awake()
    {
        if(Instance != null)
        {
            Debug.LogError("HighlightsFX - More than one instance detected.");
            return;
        }

        Instance = this;
        
        highlightShader = Shader.Find(HighlightShaderName);
        SetupCommandBuffer();
    }

    private void OnDisable()
    {
        ClearCommandBuffer();
    }
    
    private void OnPreRender()
    {
        ClearCommandBuffer();

        if(objectsToRender.Count == 0)
        {
            return;
        }

        if (_commandBuffer != null)
        {
            PopulateCommandBuffer();
        }
    }

    protected override void OnDestroy()
    {
        DisposeCommandBuffer();
        base.OnDestroy();
        Instance = null;
    }

    #endregion
    
    #region Command Buffer Management

    private void SetupCommandBuffer()
    {
        if (_commandBuffer == null)
        {
            _commandBuffer = new CommandBuffer { name = CommandBufferName };
        }

        GetComponent<Camera>().AddCommandBuffer(CameraEvent.AfterImageEffectsOpaque, _commandBuffer);
    }

    private void DisposeCommandBuffer()
    {
        if (_commandBuffer != null)
        {
            GetComponent<Camera>().RemoveCommandBuffer(CameraEvent.AfterImageEffectsOpaque, _commandBuffer);
            _commandBuffer.Dispose();

            _commandBuffer = null;
        }
    }

    private void PopulateCommandBuffer()
    {
        float highlightAlpha = Mathf.Lerp(mobileMinimumOpacity, mobileMaximumOpacity, 0.5f * Mathf.Sin(mobilePulseSpeed * Time.timeSinceLevelLoad) + 0.5f);

        // Depth prepass for all objects
        foreach (var (component, outlineData) in objectsToRender)
        {
            AddDepthPrepassCommands(outlineData);
        }

        // Highlight pass for all objects
        foreach (var (component, outlineData) in objectsToRender)
        {
            AddHighlightPassCommands(outlineData, highlightAlpha);
        }
    }

    private void ClearCommandBuffer()
    {
        if (_commandBuffer != null)
        {
            _commandBuffer.Clear();
        }
    }

    #endregion


    #region PostEffectBase Methods

    public override bool CheckResources()
    {
        CheckSupport(false);
        highlightMaterial = CheckShaderAndCreateMaterial(highlightShader, highlightMaterial);

        if(!isSupported)
        {
            ReportAutoDisable();
        }

        return isSupported;
    }

    #endregion


    #region Mobile Highlight

    private void AddDepthPrepassCommands(OutlineData outlineData)
    {
        if (!outlineData.IsValid())
        {
            return;
        }

        for (int i = 0; i < outlineData.SubMeshCount; i++)
        {
            _commandBuffer.DrawRenderer(outlineData.Renderer, highlightMaterial, i, DRAW_DEPTH_PREPASS);
        }
    }

    private void AddHighlightPassCommands(OutlineData outlineData, float highlightAlpha)
    {
        if (!outlineData.IsValid())
        {
            return;
        }

        Color color = outlineData.Color;
        color.a *= highlightAlpha;
        _commandBuffer.SetGlobalColor(highlightColorId, color);

        for (int i = 0; i < outlineData.SubMeshCount; i++)
        {
            _commandBuffer.DrawRenderer(outlineData.Renderer, highlightMaterial, i, DRAW_HIGHLIGHT_PASS);
        }
    }

    #endregion
}
