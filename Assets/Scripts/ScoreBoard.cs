using UdonSharp;
using UnityEngine;
using UnityEngine.UI;

[UdonBehaviourSyncMode(BehaviourSyncMode.NoVariableSync)]
public class ScoreBoard : UdonSharpBehaviour
{
    public GameManager gameManager;
    public ScorePersistence persistence;
    public Text text;

    private string _lastText = "";
    private int _lastStationCount = -1;
    private float _flashUntil = -999f;

    void Start()
    {
        Refresh();
    }

    void Update()
    {
        if (gameManager != null)
        {
            int sc = gameManager.stationCount;
            if (sc != _lastStationCount)
            {
                if (_lastStationCount >= 0 && sc > _lastStationCount) _flashUntil = Time.time + 3f;
                _lastStationCount = sc;
            }
        }
        if (Time.frameCount % 15 == 0) Refresh();
    }

    public void Refresh()
    {
        if (text == null || gameManager == null) return;

        float dist = gameManager.trainController != null ? gameManager.trainController.trackDistance : 0f;
        string line1 = "Dist " + Mathf.FloorToInt(dist) + "m"
            + "  Station " + gameManager.stationCount
            + "  Speed " + (gameManager.trainController != null ? gameManager.trainController.CurrentSpeed().ToString("F2") : "0");
        string line2 = "Wood " + gameManager.woodCount + "/" + gameManager.woodMax
            + "  Iron " + gameManager.ironCount + "/" + gameManager.ironMax
            + "  Rail " + gameManager.railStock + "/" + gameManager.railStockMax
            + "  Water " + gameManager.waterLevel + "/" + gameManager.waterMax;

        string line3;
        Color col = Color.white;
        int st = gameManager.runState;
        if (st == GameManager.StateIdle)
        {
            line3 = "Pull the lever to start!";
        }
        else if (st == GameManager.StateCountdown)
        {
            line3 = "Get ready...";
        }
        else if (st == GameManager.StateStationStop)
        {
            line3 = "STATION " + gameManager.stationCount + "!";
        }
        else if (st == GameManager.StateCrashed)
        {
            line3 = "DERAILED!  Score " + gameManager.score;
            col = new Color(1f, 0.45f, 0.4f);
        }
        else
        {
            int remain = Mathf.CeilToInt((gameManager.stationCount + 1) * gameManager.stationTiles - dist);
            if (remain < 0) remain = 0;
            line3 = "Next station in " + remain + "m";
        }
        if (Time.time < _flashUntil) col = new Color(1f, 0.9f, 0.3f);
        if ((st == GameManager.StateRunning || st == GameManager.StateStationStop) && gameManager.waterLevel <= 1)
        {
            line3 = "LOW WATER!";
            col = new Color(1f, 0.7f, 0.2f);
        }
        if (gameManager.boilerOnFire)
        {
            line3 = "FIRE!! REFILL NOW!";
            col = new Color(1f, 0.3f, 0.2f);
        }

        string line4 = "Last Score " + (persistence != null ? persistence.lastRunScore : gameManager.score)
            + "  Best " + (persistence != null ? persistence.bestScore : 0);

        string t = line1 + "\n" + line2 + "\n" + line3 + "\n" + line4;
        if (t != _lastText)
        {
            _lastText = t;
            text.text = t;
        }
        if (text.color != col) text.color = col;
    }
}
