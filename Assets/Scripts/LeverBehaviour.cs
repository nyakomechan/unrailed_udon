using UdonSharp;
using UnityEngine;
using UnityEngine.UI;
using VRC.SDKBase;
using VRC.Udon.Common.Interfaces;

[UdonBehaviourSyncMode(BehaviourSyncMode.Manual)]
public class LeverBehaviour : UdonSharpBehaviour
{
    public GameManager gameManager;
    public Text ownerLabel;

    private float _denyUntil = -999f;

    public override void Interact()
    {
        if (gameManager == null) return;
        if (!Networking.IsOwner(gameManager.gameObject))
        {
            _denyUntil = Time.time + 1.5f;
            RefreshLabel();
            return;
        }
        if (gameManager.runState == GameManager.StateIdle)
        {
            gameManager.SendCustomNetworkEvent(NetworkEventTarget.Owner, nameof(GameManager.RequestStartRun));
        }
    }

    void Update()
    {
        if (Time.frameCount % 15 != 0) return;
        if (ownerLabel != null && Time.time > _denyUntil && ownerLabel.color != Color.white)
            ownerLabel.color = Color.white;
        RefreshLabel();
    }

    private void RefreshLabel()
    {
        if (ownerLabel == null || gameManager == null) return;
        VRCPlayerApi owner = Networking.GetOwner(gameManager.gameObject);
        string name = owner != null && owner.IsValid() ? owner.displayName : "?";
        if (Time.time < _denyUntil)
        {
            ownerLabel.color = Color.red;
            ownerLabel.text = "Owner only: " + name;
        }
        else
        {
            ownerLabel.text = "Owner: " + name;
        }
    }
}
