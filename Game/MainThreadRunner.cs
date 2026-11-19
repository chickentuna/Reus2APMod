using System;
using UnityEngine;

namespace Reus2Archipelago.Game;

/// <summary>
/// Drives the plugin's main-thread work from a hidden, persistent GameObject, since a BepInEx
/// plugin's own Update never ticks in Reus 2.
/// </summary>
internal sealed class MainThreadRunner : MonoBehaviour
{
    private static Action onStart;

    internal static void Create(Action startAction)
    {
        onStart = startAction;
        var host = new GameObject("Reus2Archipelago") { hideFlags = HideFlags.HideAndDontSave };
        DontDestroyOnLoad(host);
        host.AddComponent<MainThreadRunner>();
    }

    private void Start() => onStart?.Invoke();

    private void Update()
    {
        ApState.DrainNetworkEvents();
        ItemApplier.Tick();
        ApNotices.Tick();
        BioticaBadges.Tick();
        ProjectBadges.Tick();
        TurningPointStarBadges.Tick();
        GameProbes.Tick();
        ConnectionUI.Tick();
    }

    private void OnGUI()
    {
        GoalCounter.Draw();
        ConnectionUI.Draw();
    }
}
