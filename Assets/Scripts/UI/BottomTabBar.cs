using UnityEngine;
using UnityEngine.UI;
using TMPro;

/// <summary>
/// Persistent bottom tab bar — W (Watch) | A (Analyze) | P (Practice).
/// The "P" tab opens a slide-up sub-menu (Free Play / Puzzles / Import / Stats).
/// Mirrors TopNav.tsx from the web app, adapted as a landscape mobile bottom bar.
/// </summary>
public class BottomTabBar : MonoBehaviour
{
    // ── Serialized Fields ────────────────────────────────────────────────────
    [Header("Tab Buttons")]
    [SerializeField] private Button btnWatch;
    [SerializeField] private Button btnAnalyze;
    [SerializeField] private Button btnPractice;

    [Header("Tab Active Indicators (thin line under active tab)")]
    [SerializeField] private Image indicatorWatch;
    [SerializeField] private Image indicatorAnalyze;
    [SerializeField] private Image indicatorPractice;

    [Header("Practice Sub-Menu Panel")]
    [SerializeField] private GameObject practiceSubMenu;
    [SerializeField] private Button btnFreePractice;
    [SerializeField] private Button btnPuzzles;
    [SerializeField] private Button btnImport;
    [SerializeField] private Button btnStats;

    [Header("Colors")]
    [SerializeField] private Color activeColor   = new Color(1f, 1f, 1f, 1f);
    [SerializeField] private Color inactiveColor = new Color(1f, 1f, 1f, 0.45f);

    // ── State ────────────────────────────────────────────────────────────────
    private bool _subMenuOpen = false;

    // ── Lifecycle ────────────────────────────────────────────────────────────
    private void Start()
    {
        // Wire up top-level tabs
        btnWatch.onClick.AddListener(OnWatchClicked);
        btnAnalyze.onClick.AddListener(OnAnalyzeClicked);
        btnPractice.onClick.AddListener(OnPracticeClicked);

        // Wire up sub-menu items
        btnFreePractice?.onClick.AddListener(OnFreePracticeClicked);
        btnPuzzles?.onClick.AddListener(OnPuzzlesClicked);
        btnImport?.onClick.AddListener(OnImportClicked);
        btnStats?.onClick.AddListener(OnStatsClicked);

        // Hide sub-menu initially
        if (practiceSubMenu != null) practiceSubMenu.SetActive(false);

        // Subscribe to screen changes to keep indicators in sync
        ScreenManager.Instance.OnScreenChanged += OnScreenChanged;

        // Init to Watch tab
        RefreshIndicators(ScreenManager.Screen.Watch);
    }

    private void OnDestroy()
    {
        if (ScreenManager.Instance != null)
            ScreenManager.Instance.OnScreenChanged -= OnScreenChanged;
    }

    // ── Tab Handlers ─────────────────────────────────────────────────────────

    private void OnWatchClicked()
    {
        CloseSubMenu();
        ScreenManager.Instance.ShowWatch();
    }

    private void OnAnalyzeClicked()
    {
        CloseSubMenu();
        ScreenManager.Instance.ShowAnalyze();
    }

    private void OnPracticeClicked()
    {
        // Toggle the sub-menu; also navigate to Practice screen so
        // the background changes even before a sub-item is chosen.
        _subMenuOpen = !_subMenuOpen;
        if (practiceSubMenu != null) practiceSubMenu.SetActive(_subMenuOpen);

        ScreenManager.Instance.ShowPractice();
    }

    // ── Sub-menu Handlers ────────────────────────────────────────────────────

    private void OnFreePracticeClicked()
    {
        CloseSubMenu();
        PracticeScreen.Instance?.ShowFreePractice();
    }

    private void OnPuzzlesClicked()
    {
        CloseSubMenu();
        PracticeScreen.Instance?.ShowPuzzles();
    }

    private void OnImportClicked()
    {
        CloseSubMenu();
        PracticeScreen.Instance?.ShowImport();
    }

    private void OnStatsClicked()
    {
        CloseSubMenu();
        PracticeScreen.Instance?.ShowStats();
    }

    // ── Screen Change Sync ───────────────────────────────────────────────────

    private void OnScreenChanged(ScreenManager.Screen screen)
    {
        RefreshIndicators(screen);

        // Close sub-menu whenever we navigate away from Practice
        if (screen != ScreenManager.Screen.Practice)
            CloseSubMenu();
    }

    private void RefreshIndicators(ScreenManager.Screen active)
    {
        SetIndicator(indicatorWatch,    active == ScreenManager.Screen.Watch);
        SetIndicator(indicatorAnalyze,  active == ScreenManager.Screen.Analyze);
        SetIndicator(indicatorPractice, active == ScreenManager.Screen.Practice);

        SetTabColor(btnWatch,    active == ScreenManager.Screen.Watch);
        SetTabColor(btnAnalyze,  active == ScreenManager.Screen.Analyze);
        SetTabColor(btnPractice, active == ScreenManager.Screen.Practice);
    }

    private void SetIndicator(Image indicator, bool on)
    {
        if (indicator == null) return;
        indicator.gameObject.SetActive(on);
    }

    private void SetTabColor(Button btn, bool isActive)
    {
        if (btn == null) return;
        var img = btn.GetComponent<Image>();
        if (img != null) img.color = isActive ? activeColor : inactiveColor;

        // Also tint all child TMP labels and images
        foreach (var tmp in btn.GetComponentsInChildren<TextMeshProUGUI>())
            tmp.color = isActive ? activeColor : inactiveColor;
        foreach (var child in btn.GetComponentsInChildren<Image>())
            if (child.gameObject != btn.gameObject)
                child.color = isActive ? activeColor : inactiveColor;
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private void CloseSubMenu()
    {
        _subMenuOpen = false;
        if (practiceSubMenu != null) practiceSubMenu.SetActive(false);
    }

    /// <summary>
    /// Close the sub-menu when touching anywhere outside of it.
    /// Call this from an invisible full-screen blocker panel.
    /// </summary>
    public void OnOutsideTap() => CloseSubMenu();
}
