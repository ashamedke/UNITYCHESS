using UnityEngine;
using UnityEngine.UI;
using TMPro;

/// <summary>
/// Practice screen container — hosts Free Practice / Puzzles / Import / Stats
/// as child panels switched by BottomTabBar sub-menu.
/// </summary>
public class PracticeScreen : MonoBehaviour
{
    // ── Singleton ─────────────────────────────────────────────────────────────
    public static PracticeScreen Instance { get; private set; }

    [Header("Sub-screen panels")]
    [SerializeField] private GameObject freePracticeRoot;
    [SerializeField] private GameObject puzzlesRoot;
    [SerializeField] private GameObject importRoot;
    [SerializeField] private GameObject statsRoot;

    private enum SubScreen { FreePractice, Puzzles, Import, Stats }
    private SubScreen _current = SubScreen.FreePractice;

    // ── Lifecycle ──────────────────────────────────────────────────────────────
    private void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
    }

    private void OnEnable() => ShowFreePractice();

    // ── Navigation ────────────────────────────────────────────────────────────
    public void ShowFreePractice() => ShowSub(SubScreen.FreePractice);
    public void ShowPuzzles()      => ShowSub(SubScreen.Puzzles);
    public void ShowImport()       => ShowSub(SubScreen.Import);
    public void ShowStats()        => ShowSub(SubScreen.Stats);

    private void ShowSub(SubScreen sub)
    {
        _current = sub;
        freePracticeRoot?.SetActive(sub == SubScreen.FreePractice);
        puzzlesRoot?.SetActive(sub == SubScreen.Puzzles);
        importRoot?.SetActive(sub == SubScreen.Import);
        statsRoot?.SetActive(sub == SubScreen.Stats);
    }
}
