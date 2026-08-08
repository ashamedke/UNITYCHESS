using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

/// <summary>
/// Import screen — port of src/pages/Import.tsx.
///
/// Accepts PGN text via a TMP_InputField (paste) or file picker.
/// Parses single or multi-game PGN, shows a game list, and sends
/// the selected game(s) to AnalyzeScreen.
/// </summary>
public class ImportScreen : MonoBehaviour
{
    [Header("UI")]
    [SerializeField] private TMP_InputField pgnInput;
    [SerializeField] private Button         btnImport;
    [SerializeField] private Button         btnClear;
    [SerializeField] private Button         btnFilePicker;
    [SerializeField] private TMP_Text       errorText;
    [SerializeField] private GameObject     gameListPanel;
    [SerializeField] private Transform      gameListContainer;
    [SerializeField] private GameObject     gameRowPrefab;
    [SerializeField] private Button         btnAnalyzeAll;
    [SerializeField] private TMP_Text       gameCountText;

    private List<PgnParser.PgnGame> _parsedGames = new List<PgnParser.PgnGame>();

    private void OnEnable()
    {
        btnImport?.onClick.AddListener(HandleImport);
        btnClear?.onClick.AddListener(HandleClear);
        btnAnalyzeAll?.onClick.AddListener(HandleAnalyzeAll);
        btnFilePicker?.onClick.AddListener(HandleFilePicker);
    }

    private void OnDisable()
    {
        btnImport?.onClick.RemoveAllListeners();
        btnClear?.onClick.RemoveAllListeners();
        btnAnalyzeAll?.onClick.RemoveAllListeners();
        btnFilePicker?.onClick.RemoveAllListeners();
    }

    // ── Import ────────────────────────────────────────────────────────────────
    private void HandleImport()
    {
        string pgn = pgnInput?.text?.Trim();
        if (string.IsNullOrEmpty(pgn))
        {
            SetError("Paste a PGN first.");
            return;
        }
        ParsePgn(pgn);
    }

    private void HandleClear()
    {
        if (pgnInput != null) pgnInput.text = "";
        SetError("");
        gameListPanel?.SetActive(false);
        _parsedGames.Clear();
    }

    private void HandleAnalyzeAll()
    {
        if (_parsedGames.Count == 0) return;
        var analyze = Object.FindObjectOfType<AnalyzeScreen>(true);
        if (analyze != null)
        {
            ScreenManager.Instance?.ShowAnalyze();
            analyze.LoadPgnList(_parsedGames, 0);
        }
    }

    // ── File Picker (Android) ─────────────────────────────────────────────────
    private void HandleFilePicker()
    {
#if UNITY_ANDROID && !UNITY_EDITOR
        // Use Android intent to pick a text file
        try
        {
            using var activity = new UnityEngine.AndroidJavaClass(
                "com.unity3d.player.UnityPlayer").GetStatic<UnityEngine.AndroidJavaObject>(
                "currentActivity");
            var intent = new UnityEngine.AndroidJavaObject("android.content.Intent",
                "android.intent.action.GET_CONTENT");
            intent.Call<UnityEngine.AndroidJavaObject>("setType", "text/*");
            activity.Call("startActivityForResult", intent, 1001);
            // Result handled in AndroidActivityResult plugin / OnActivityResult
        }
        catch (System.Exception ex)
        {
            SetError("File picker not available: " + ex.Message);
        }
#else
        SetError("File picker: use paste on this platform.");
#endif
    }

    // ── PGN Parse ─────────────────────────────────────────────────────────────
    private void ParsePgn(string pgn)
    {
        try
        {
            _parsedGames = PgnParser.ParseMulti(pgn);
            if (_parsedGames.Count == 0)
            {
                SetError("No games found in PGN.");
                return;
            }
            SetError("");
            ShowGameList();
        }
        catch (System.Exception ex)
        {
            SetError("Parse error: " + ex.Message);
        }
    }

    private void ShowGameList()
    {
        gameListPanel?.SetActive(true);

        foreach (Transform child in gameListContainer)
            Destroy(child.gameObject);

        if (gameCountText != null)
            gameCountText.text = $"{_parsedGames.Count} game{(_parsedGames.Count != 1 ? "s" : "")} found";

        for (int i = 0; i < _parsedGames.Count; i++)
        {
            var game = _parsedGames[i];
            int idx = i;

            var obj = Instantiate(gameRowPrefab, gameListContainer);
            var row = obj.GetComponent<ImportGameRowUI>();
            row?.Set(idx + 1, game.White, game.Black, game.Result, game.Event_,
                     () => AnalyzeGame(idx));
        }
    }

    private void AnalyzeGame(int index)
    {
        var analyze = Object.FindObjectOfType<AnalyzeScreen>(true);
        if (analyze != null)
        {
            ScreenManager.Instance?.ShowAnalyze();
            analyze.LoadPgn(_parsedGames[index]);
        }
    }

    private void SetError(string msg)
    {
        if (errorText != null) errorText.text = msg;
    }
}

// ── Helper: game row UI ────────────────────────────────────────────────────
public class ImportGameRowUI : MonoBehaviour
{
    [SerializeField] private TMP_Text idxText;
    [SerializeField] private TMP_Text whiteText;
    [SerializeField] private TMP_Text blackText;
    [SerializeField] private TMP_Text resultText;
    [SerializeField] private TMP_Text eventText;
    [SerializeField] private Button   analyzeBtn;

    public void Set(int idx, string white, string black, string result, string evt, System.Action onAnalyze)
    {
        if (idxText    != null) idxText.text    = idx.ToString();
        if (whiteText  != null) whiteText.text  = white;
        if (blackText  != null) blackText.text  = black;
        if (resultText != null) resultText.text = result;
        if (eventText  != null) eventText.text  = evt;
        analyzeBtn?.onClick.AddListener(() => onAnalyze?.Invoke());
    }
}
