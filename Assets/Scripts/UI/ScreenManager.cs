using UnityEngine;

/// <summary>
/// Singleton that owns all top-level screens and drives which one is visible.
/// Mirrors the React Router / page-component pattern from App.tsx.
/// 
/// Screens are child GameObjects of this manager. At most one is active at a time.
/// Sub-screens (Puzzles, Import, Stats, FreePractice) are children of PracticeScreen.
/// </summary>
public class ScreenManager : MonoBehaviour
{
    // ── Singleton ────────────────────────────────────────────────────────────
    public static ScreenManager Instance { get; private set; }

    // ── Screen References (assigned in Inspector / by AppController) ─────────
    [Header("Top-Level Screens")]
    [SerializeField] private GameObject watchScreen;
    [SerializeField] private GameObject analyzeScreen;
    [SerializeField] private GameObject practiceScreen;

    // ── State ────────────────────────────────────────────────────────────────
    public enum Screen { Watch, Analyze, Practice }

    private Screen _currentScreen = Screen.Watch;
    public Screen CurrentScreen => _currentScreen;

    // ── Events ───────────────────────────────────────────────────────────────
    public event System.Action<Screen> OnScreenChanged;

    // ── Lifecycle ────────────────────────────────────────────────────────────
    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;
        DontDestroyOnLoad(gameObject);
    }

    private void Start()
    {
        // Force landscape orientation — never unlocked
        Screen.orientation = ScreenOrientation.AutoRotation;
        Screen.autorotateToPortrait = false;
        Screen.autorotateToPortraitUpsideDown = false;
        Screen.autorotateToLandscapeLeft = true;
        Screen.autorotateToLandscapeRight = true;

        ShowScreen(Screen.Watch); // Default tab (mirrors App.tsx → /watch)
    }

    // ── Public API ────────────────────────────────────────────────────────────
    public void ShowWatch()   => ShowScreen(Screen.Watch);
    public void ShowAnalyze() => ShowScreen(Screen.Analyze);
    public void ShowPractice() => ShowScreen(Screen.Practice);

    public void ShowScreen(Screen target)
    {
        _currentScreen = target;

        SetActive(watchScreen,    target == Screen.Watch);
        SetActive(analyzeScreen,  target == Screen.Analyze);
        SetActive(practiceScreen, target == Screen.Practice);

        OnScreenChanged?.Invoke(target);
    }

    // ── Helpers ───────────────────────────────────────────────────────────────
    private static void SetActive(GameObject go, bool active)
    {
        if (go != null) go.SetActive(active);
    }
}
