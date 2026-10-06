using UdonSharp;
using UnityEngine;
using UnityEngine.UI;
using VRC.SDKBase;

[UdonBehaviourSyncMode(BehaviourSyncMode.NoVariableSync)]
public class ResultPanel : UdonSharpBehaviour
{
    public GameManager gameManager;
    public TrackManager trackManager;
    public TrainController trainController;
    public ScorePersistence persistence;
    public GameObject panelRoot;
    public Text scoreText;
    public Text ownerText;

    private float _denyFlashUntil = -999f;

    void Update()
    {
        if (gameManager == null || panelRoot == null) return;
        bool show = gameManager.runState == GameManager.StateCrashed;
        if (panelRoot.activeSelf != show)
        {
            if (show) PositionAtTrain();
            panelRoot.SetActive(show);
        }
        if (show && Time.frameCount % 15 == 0) RefreshTexts();

        if (ownerText != null && Time.time > _denyFlashUntil && ownerText.color != Color.white)
            ownerText.color = Color.white;
    }

    private void PositionAtTrain()
    {
        float d = trainController != null ? trainController.trackDistance : 0f;
        Vector3 pos = trackManager.GetPositionAt(d);
        Vector3 tan = trackManager.GetTangentAt(d);
        if (tan.sqrMagnitude < 0.001f) tan = Vector3.forward;
        panelRoot.transform.position = pos + new Vector3(0f, 2.2f, 0f);
        panelRoot.transform.rotation = Quaternion.LookRotation(tan);
    }

    private void RefreshTexts()
    {
        if (scoreText != null)
        {
            int best = persistence != null ? persistence.bestScore : 0;
            scoreText.text = "DERAILED!\nSCORE " + gameManager.score
                + "   BEST " + best
                + "\nStations " + gameManager.stationCount;
        }
        if (ownerText != null)
        {
            VRCPlayerApi owner = Networking.GetOwner(gameManager.gameObject);
            string name = owner != null && owner.IsValid() ? owner.displayName : "?";
            if (Time.time < _denyFlashUntil)
            {
                ownerText.color = Color.red;
                ownerText.text = "Owner only: " + name;
            }
            else
            {
                ownerText.text = "Retry: Owner (" + name + ")";
            }
        }
    }

    public void TryRetry()
    {
        if (gameManager == null) return;
        if (!Networking.IsOwner(gameManager.gameObject))
        {
            _denyFlashUntil = Time.time + 1.5f;
            RefreshTexts();
            return;
        }
        if (gameManager.runState != GameManager.StateCrashed) return;
        gameManager.OwnerResetRun();
    }
}
