
using System.Collections.Generic;
using UnityEngine;
using VRC.SDKBase;

namespace VRC.SDK3.ClientSim
{
    /// <summary>
    /// System responsible for highlighting objects.
    /// </summary>
    [AddComponentMenu("")]
    public class ClientSimHighlightManager : ClientSimBehaviour, IClientSimHighlightManager
    {
        [SerializeField]
        private Mesh cubeMesh;
        [SerializeField]
        private Mesh capsuleMesh;
        [SerializeField]
        private Mesh sphereMesh;
        
        [SerializeField]
        private GameObject proxyHighlightPrefab;
        
        private HighlightsFX _highlightsFX;
        
        private readonly Dictionary<GameObject, ClientSimHighlightProxy> _objectRenderProxies =
            new Dictionary<GameObject, ClientSimHighlightProxy>();

        private readonly Queue<ClientSimHighlightProxy> _proxyMeshQueue = new Queue<ClientSimHighlightProxy>();
        
        public void Initialize(Camera playerCamera)
        {
            _highlightsFX = playerCamera.gameObject.AddComponent<HighlightsFX>();
        }

        public void EnableObjectHighlight(GameObject obj)
        {
            List<Renderer> renderers = GatherRenderers(obj, false);
            if (renderers.Count == 0)
            {
                Renderer rend = GetProxyHighlight(obj);
                if (rend != null)
                {
                    renderers.Add(rend);
                }
            }
            
            foreach (var rend in renderers)
            {
                EnableObjectHighlight(rend, true);
            }
        }

        public void DisableObjectHighlight(GameObject obj)
        {
            if (_objectRenderProxies.TryGetValue(obj, out ClientSimHighlightProxy proxy))
            {
                _objectRenderProxies.Remove(obj);
                EnableObjectHighlight(proxy.Renderer, false);

                _proxyMeshQueue.Enqueue(proxy);
                proxy.DisableProxy();
            }

            // Disable across ALL renderers on the object in case OutlineRenderers changed while we were holding it.
            List<Renderer> renderers = GatherRenderers(obj, true);
            foreach (var rend in renderers)
            {
                EnableObjectHighlight(rend, false);
            }
        }

        public void EnableObjectHighlight(Renderer rend, bool isEnabled)
        {
            _highlightsFX.EnableOutline(rend, isEnabled);
        }

        private List<Renderer> GatherRenderers(GameObject obj, bool forDisabling)
        {
            List<Renderer> results = new List<Renderer>();

            if(obj == null)
            {
                return results;
            }

            if (!forDisabling)
            {
                // Use manually defined outline renderers for activating the highlight if given.
                // This option allows disabled renderers.
                if (obj.TryGetComponent(out VRC_Pickup pickup) && pickup.OutlineRenderers != null)
                {
                    foreach (Renderer customRend in pickup.OutlineRenderers)
                    {
                        if (customRend != null  && customRend.transform.IsChildOf(obj.transform) && IsRendererValid(customRend, true))
                        {
                            results.Add(customRend);
                        }
                    }
                }
            }

            if (results.Count == 0)
            {
                foreach (var rend in obj.GetComponentsInChildren<Renderer>(forDisabling))
                {
                    if (IsRendererValid(rend, forDisabling))
                    {
                        results.Add(rend);
                    }
                }
            }

            return results;

            bool IsRendererValid(Renderer rend, bool allowDisabled)
            {
                bool canProcess = !rend.isPartOfStaticBatch && (allowDisabled || (rend.enabled && rend.gameObject.activeInHierarchy));
                if (!canProcess)
                {
                    return false;
                }

                if (rend is MeshRenderer)
                {
                    if (!rend.TryGetComponent(out MeshFilter filter) || filter.sharedMesh == null)
                    {
                        return false;
                    }
                }
                else if (rend is SkinnedMeshRenderer skinnedRend)
                {
                    if (skinnedRend.sharedMesh == null)
                    {
                        return false;
                    }
                }
                else
                {
                    return false;
                }

                return true;
            }
        }

        private Renderer GetProxyHighlight(GameObject obj)
        {
            ClientSimHighlightProxy proxy = GetUnusedProxy();
            
            Collider objCollider = obj.GetComponent<Collider>();
            _objectRenderProxies.Add(obj, proxy);
            proxy.EnableProxy(obj.transform, objCollider);
            
            return proxy.Renderer;
        }

        private ClientSimHighlightProxy GetUnusedProxy()
        {
            if (_proxyMeshQueue.Count == 0)
            {
                GameObject tooltipObj = Instantiate(proxyHighlightPrefab, transform);
                ClientSimHighlightProxy tooltip = tooltipObj.GetComponent<ClientSimHighlightProxy>();
                tooltip.Initialize(cubeMesh, capsuleMesh, sphereMesh);
                return tooltip;
            }

            return _proxyMeshQueue.Dequeue();
        }
    }
}
