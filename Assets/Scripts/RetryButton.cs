using UdonSharp;
using UnityEngine;

[UdonBehaviourSyncMode(BehaviourSyncMode.NoVariableSync)]
public class RetryButton : UdonSharpBehaviour
{
    public ResultPanel panel;

    public override void Interact()
    {
        if (panel != null) panel.TryRetry();
    }
}
