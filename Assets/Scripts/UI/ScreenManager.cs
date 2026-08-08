using UnityEngine;
using UnityEngine.UIElements;

/// <summary>
/// Singleton that owns all top-level screens and drives which one is visible.
/// Rewritten to use UI Toolkit (UXML/USS) since we have no visual editor.
/// </summary>
public class ScreenManager : MonoBehaviour
{
    public static ScreenManager Instance { get; private set; }

    public enum Screen { Watch, Analyze, Practice }

    private Screen _currentScreen = Screen.Watch;
    public Screen CurrentScreen => _currentScreen;

    public event System.Action<Screen> OnScreenChanged;

    // UI Elements
    private VisualElement _watchScreen;
    private VisualElement _analyzeScreen;
    private VisualElement _practiceScreen;

    private Button _navWatch;
    private Button _navAnalyze;
    private Button _navPractice;

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
        UnityEngine.Screen.orientation = ScreenOrientation.AutoRotation;
        UnityEngine.Screen.autorotateToPortrait = false;
        UnityEngine.Screen.autorotateToPortraitUpsideDown = false;
        UnityEngine.Screen.autorotateToLandscapeLeft = true;
        UnityEngine.Screen.autorotateToLandscapeRight = true;
    }

    public void InitializeUI(UIDocument doc)
    {
        var root = doc.rootVisualElement;

        _watchScreen = root.Q<VisualElement>("watch-screen");
        _analyzeScreen = root.Q<VisualElement>("analyze-screen");
        _practiceScreen = root.Q<VisualElement>("practice-screen");

        _navWatch = root.Q<Button>("nav-watch");
        _navAnalyze = root.Q<Button>("nav-analyze");
        _navPractice = root.Q<Button>("nav-practice");

        _navWatch?.RegisterCallback<ClickEvent>(ev => ShowWatch());
        _navAnalyze?.RegisterCallback<ClickEvent>(ev => ShowAnalyze());
        _navPractice?.RegisterCallback<ClickEvent>(ev => ShowPractice());

        ShowScreen(Screen.Watch);
    }

    public void ShowWatch()   => ShowScreen(Screen.Watch);
    public void ShowAnalyze() => ShowScreen(Screen.Analyze);
    public void ShowPractice() => ShowScreen(Screen.Practice);

    public void ShowScreen(Screen target)
    {
        _currentScreen = target;

        SetDisplay(_watchScreen, target == Screen.Watch);
        SetDisplay(_analyzeScreen, target == Screen.Analyze);
        SetDisplay(_practiceScreen, target == Screen.Practice);

        UpdateNavClass(_navWatch, target == Screen.Watch);
        UpdateNavClass(_navAnalyze, target == Screen.Analyze);
        UpdateNavClass(_navPractice, target == Screen.Practice);

        OnScreenChanged?.Invoke(target);
    }

    private void SetDisplay(VisualElement el, bool show)
    {
        if (el != null)
            el.style.display = show ? DisplayStyle.Flex : DisplayStyle.None;
    }

    private void UpdateNavClass(Button btn, bool active)
    {
        if (btn == null) return;
        if (active) btn.AddToClassList("nav-btn-active");
        else btn.RemoveFromClassList("nav-btn-active");
    }
}
