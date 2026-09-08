#if UNITY_EDITOR
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Vampire.DropPuzzle
{
    /// <summary>
    /// Makes DropPuzzle playable on its own in the Editor — press Play with DropPuzzle open
    /// and everything works, including the debug hotkeys.
    ///
    /// The persistent managers (PlayerDataManager, UpgradeShop, ProgressionSystem,
    /// DebugUpgradeUI, ...) are authored ONLY on the GameSystems object in FPS_Collect and
    /// reach DropPuzzle through DontDestroyOnLoad. Open DropPuzzle directly and none of them
    /// exist, which quietly breaks more than it looks:
    ///
    ///   - DropperControllerECS.DropAllBallsECS takes ballsToDrop from
    ///     playerData.Inventory; with no PlayerDataManager it falls through to
    ///     DropPuzzleManager and then `yield break`s, so SPACE silently drops nothing.
    ///   - DebugUpgradeUI is the thing that owns the P / G / E / C hotkeys, so none of them
    ///     exist either — there is no way to get riceballs into the inventory by hand.
    ///   - PuzzleEnhancer logs "PlayerDataManager.Instance not found! Using defaults" and
    ///     applies default gate multipliers instead of upgrade-scaled ones.
    ///
    /// Scoping — why this cannot leak into a real playthrough:
    ///   - The whole file is #if UNITY_EDITOR, so it does not exist in a player build.
    ///   - AfterSceneLoad runs once, against the FIRST scene loaded. A normal run starts at
    ///     ComicScene / FPS_Collect, so the scene-name test fails and this no-ops. It can
    ///     only fire when DropPuzzle itself is the scene you pressed Play on.
    ///   - It bails the moment a PlayerDataManager already exists, so it can never race or
    ///     duplicate the real GameSystems object arriving via DontDestroyOnLoad.
    ///
    /// Deliberately does NOT create DayNightCycleManager, TutorialManager or QuestManager:
    /// nothing in DropPuzzle's own systems needs them (CanEnterBallDrop is only consulted by
    /// the FPS_Collect-side entry trigger), and spawning a TutorialManager here would start
    /// tutorial/quest flows that the real game drives from an earlier scene.
    /// </summary>
    internal static class DropPuzzleEditorBootstrap
    {
        private const string DropPuzzleSceneName = "DropPuzzle";

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void CreateGameSystemsIfMissing()
        {
            if (SceneManager.GetActiveScene().name != DropPuzzleSceneName) return;

            // The real GameSystems is already here (entered from FPS_Collect) — leave it alone.
            if (PlayerDataManager.Instance != null) return;

            var go = new GameObject("GameSystems (editor bootstrap — DropPuzzle direct play)");

            // Order matters: PlayerDataManager first, because the others resolve
            // PlayerDataManager.Instance and it is what calls DontDestroyOnLoad on this object.
            // AddComponent runs each Awake immediately, so Instance is live before the next add.
            go.AddComponent<PlayerDataManager>();
            go.AddComponent<UpgradeShop>();       // DebugUpgradeUI.Update() early-outs without this
            go.AddComponent<ProgressionSystem>();
            go.AddComponent<DebugUpgradeUI>();    // owns the P / G / E / C hotkeys

            Debug.Log("[DropPuzzleEditorBootstrap] DropPuzzle opened directly — created a " +
                      "stand-in GameSystems (PlayerDataManager, UpgradeShop, ProgressionSystem, " +
                      "DebugUpgradeUI). Press P to load riceballs, then SPACE to drop. " +
                      "Editor-only; never runs in a build or when entering from FPS_Collect.");
        }
    }
}
#endif
