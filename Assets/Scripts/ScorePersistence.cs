using UdonSharp;
using UnityEngine;
using VRC.SDK3.Persistence;
using VRC.SDKBase;

[UdonBehaviourSyncMode(BehaviourSyncMode.NoVariableSync)]
public class ScorePersistence : UdonSharpBehaviour
{
    public ScoreBoard scoreBoard;
    public int bestScore;
    public int lastRunScore;

    private bool _restored;

    public override void OnPlayerRestored(VRCPlayerApi player)
    {
        if (player == null || !player.isLocal) return;
        bestScore = PlayerData.GetInt(player, "unrailedBest");
        _restored = true;
        if (scoreBoard != null) scoreBoard.Refresh();
    }

    public void ReportScore(int score)
    {
        lastRunScore = score;
        if (scoreBoard != null) scoreBoard.Refresh();
        if (!_restored) return;
        if (score > bestScore)
        {
            bestScore = score;
            PlayerData.SetInt("unrailedBest", score);
            if (scoreBoard != null) scoreBoard.Refresh();
        }
    }
}
