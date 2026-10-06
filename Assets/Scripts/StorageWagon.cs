using UdonSharp;
using UnityEngine;

[UdonBehaviourSyncMode(BehaviourSyncMode.NoVariableSync)]
public class StorageWagon : UdonSharpBehaviour
{
    public GameManager gameManager;
    public GameObject[] woodVisuals = new GameObject[8];
    public GameObject[] ironVisuals = new GameObject[8];

    private int _appliedWood = -1;
    private int _appliedIron = -1;

    void Update()
    {
        if (gameManager == null) return;
        int w = gameManager.woodCount;
        if (w != _appliedWood)
        {
            _appliedWood = w;
            for (int i = 0; i < woodVisuals.Length; i++)
            {
                GameObject g = woodVisuals[i];
                if (g == null) continue;
                bool show = i < w;
                if (g.activeSelf != show) g.SetActive(show);
            }
        }
        int r = gameManager.ironCount;
        if (r != _appliedIron)
        {
            _appliedIron = r;
            for (int i = 0; i < ironVisuals.Length; i++)
            {
                GameObject g = ironVisuals[i];
                if (g == null) continue;
                bool show = i < r;
                if (g.activeSelf != show) g.SetActive(show);
            }
        }
    }
}
