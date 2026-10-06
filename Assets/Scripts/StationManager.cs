using UdonSharp;
using UnityEngine;
using VRC.SDKBase;

[UdonBehaviourSyncMode(BehaviourSyncMode.NoVariableSync)]
public class StationManager : UdonSharpBehaviour
{
    public GameManager gameManager;
    public ChunkManager chunkManager;
    public Transform[] stationObjects = new Transform[3];

    private int _appliedStationCount = -1;

    void Update()
    {
        if (gameManager == null || chunkManager == null) return;
        int sc = gameManager.stationCount;
        if (sc == _appliedStationCount) return;
        _appliedStationCount = sc;
        for (int i = 0; i < stationObjects.Length; i++)
        {
            Transform st = stationObjects[i];
            if (st == null) continue;
            int k = sc + 1 + i;
            st.position = new Vector3(k * chunkManager.stationTiles + chunkManager.origin.x, 0f, chunkManager.origin.z + 3.5f);
        }
    }
}
