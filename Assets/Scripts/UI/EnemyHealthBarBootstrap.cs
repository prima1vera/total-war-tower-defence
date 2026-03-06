using UnityEngine;
using UnityEngine.SceneManagement;

public static class EnemyHealthBarBootstrap
{
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void EnsureHealthBarSystem()
    {
        Scene activeScene = SceneManager.GetActiveScene();
        if (!activeScene.IsValid() || activeScene.name != "FirstScene")
            return;

        _ = EnemyHealthBarSystem.Instance;
    }
}
