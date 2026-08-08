using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using UnityEngine.Networking;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

/// <summary>
/// Puzzle system — port of src/hooks/usePuzzles.ts.
///
/// Replaces IndexedDB with PlayerPrefs (progress) + a JSON file in persistentDataPath.
/// Puzzle data is user-downloaded on demand — NOT bundled in the APK.
///
/// DB Schema (mimics usePuzzles.ts):
///   puzzles:  { id, fen, moves[], rating, themes[], status }
///   attempts: { puzzleId, rating, date, timeTaken, status }
///   streaks:  { length, date }
///
/// Storage strategy:
///   - Puzzles are stored as a JSON array in persistentDataPath/puzzles.db.json
///   - Progress (status flags) stored in PlayerPrefs (key = puzzle id)
///   - Attempts + streaks stored as JSON in persistentDataPath/attempts.json
/// </summary>
public class PuzzleDatabase : MonoBehaviour
{
    // ── Singleton ─────────────────────────────────────────────────────────────
    public static PuzzleDatabase Instance { get; private set; }

    // ── Puzzle model (mirrors usePuzzles.ts) ──────────────────────────────────
    [Serializable]
    public class Puzzle
    {
        public string   id;
        public string   fen;
        public string[] moves;
        public int      rating;
        public string[] themes;
        [NonSerialized] public string status; // "unsolved" | "solved" | "failed"
    }

    [Serializable]
    public class Attempt
    {
        public string puzzleId;
        public int    rating;
        public string date;
        public int    timeTaken;
        public string status; // "solved" | "failed"
    }

    // ── Events ────────────────────────────────────────────────────────────────
    public event Action<string> OnDownloadProgress; // e.g. "Downloading: 4523/10000"
    public event Action         OnDatabaseReady;
    public event Action<string> OnError;

    // ── State ─────────────────────────────────────────────────────────────────
    private List<Puzzle>  _puzzles  = new List<Puzzle>();
    private List<Attempt> _attempts = new List<Attempt>();
    private bool          _isReady;
    private int           _currentStreak;

    private string PuzzlesPath  => Path.Combine(Application.persistentDataPath, "puzzles.json");
    private string AttemptsPath => Path.Combine(Application.persistentDataPath, "attempts.json");

    // ── Stats (mirrors analytics in usePuzzles.ts) ────────────────────────────
    public int TotalPuzzles  => _puzzles.Count;
    public int SolvedPuzzles => _puzzles.FindAll(p => p.status == "solved").Count;
    public int FailedPuzzles => _puzzles.FindAll(p => p.status == "failed").Count;
    public int CurrentStreak => _currentStreak;
    public bool IsReady      => _isReady;

    // ── Lifecycle ──────────────────────────────────────────────────────────────
    private void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
        DontDestroyOnLoad(gameObject);
    }

    private void Start()
    {
        _currentStreak = PlayerPrefs.GetInt("puzzleStreak", 0);
        StartCoroutine(LoadExistingData());
    }

    // ── Load existing puzzles from disk ───────────────────────────────────────
    private IEnumerator LoadExistingData()
    {
        if (File.Exists(PuzzlesPath))
        {
            OnDownloadProgress?.Invoke("Loading puzzle database...");
            yield return null;

            try
            {
                string json = File.ReadAllText(PuzzlesPath);
                _puzzles = JsonConvert.DeserializeObject<List<Puzzle>>(json) ?? new List<Puzzle>();

                // Restore status from PlayerPrefs
                foreach (var p in _puzzles)
                {
                    string saved = PlayerPrefs.GetString("pz_" + p.id, "unsolved");
                    p.status = saved;
                }

                LoadAttempts();
                _isReady = true;
                OnDatabaseReady?.Invoke();
            }
            catch (Exception ex)
            {
                OnError?.Invoke("Failed to load puzzles: " + ex.Message);
            }
        }
        else
        {
            // No database yet — show download prompt in PuzzleScreen
            _isReady = true;
            OnDatabaseReady?.Invoke();
        }
    }

    // ── Download Puzzles (user-initiated) ─────────────────────────────────────

    /// <summary>
    /// Downloads puzzles from Lichess DB CSV and stores locally.
    /// count: how many puzzles the user wants (e.g. 1000, 5000, 10000, 50000).
    /// Mirrors the download_puzzles.py script logic.
    /// </summary>
    public void DownloadPuzzles(int count, Action onDone)
    {
        StartCoroutine(DoDownloadPuzzles(count, onDone));
    }

    private IEnumerator DoDownloadPuzzles(int count, Action onDone)
    {
        // Lichess puzzle DB is available as a compressed CSV.
        // We'll download the CSV, parse it, and store a JSON subset.
        const string PUZZLE_CSV_URL =
            "https://database.lichess.org/lichess_db_puzzle.csv.zst";

        // For a more practical approach: use the Lichess puzzle API to fetch
        // puzzles one at a time or in batches from /api/puzzle/next
        // We'll use the /api/puzzle/next endpoint for each puzzle.

        _puzzles.Clear();
        OnDownloadProgress?.Invoke($"Starting download of {count} puzzles...");

        int downloaded = 0;
        int batchSize  = 50;

        while (downloaded < count)
        {
            int remaining = count - downloaded;
            int batch     = Mathf.Min(batchSize, remaining);

            for (int i = 0; i < batch; i++)
            {
                string url = "https://lichess.org/api/puzzle/next";
                using var req = UnityWebRequest.Get(url);
                yield return req.SendWebRequest();

                if (req.result == UnityWebRequest.Result.Success)
                {
                    try
                    {
                        var obj = JObject.Parse(req.downloadHandler.text);
                        var pz  = ParsePuzzleFromApi(obj);
                        if (pz != null) { _puzzles.Add(pz); downloaded++; }
                    }
                    catch { /* skip malformed */ }
                }

                OnDownloadProgress?.Invoke($"Downloaded {downloaded}/{count} puzzles");

                // Small delay to respect rate limits
                yield return new WaitForSeconds(0.1f);
            }

            // Save progress periodically
            SavePuzzlesToDisk();
        }

        SavePuzzlesToDisk();
        _isReady = true;
        OnDatabaseReady?.Invoke();
        onDone?.Invoke();
    }

    private Puzzle ParsePuzzleFromApi(JObject obj)
    {
        var puzzle = obj["puzzle"];
        if (puzzle == null) return null;

        string movesStr = puzzle["solution"]?.ToString() ?? "";
        var moves = movesStr.Split(new[]{' '}, StringSplitOptions.RemoveEmptyEntries);

        var themes = new List<string>();
        foreach (var t in puzzle["themes"] ?? new JArray())
            themes.Add(t.ToString());

        return new Puzzle
        {
            id     = puzzle["id"]?.ToString() ?? Guid.NewGuid().ToString(),
            fen    = obj["game"]?["fen"]?.ToString() ?? ChessBoard.START_FEN,
            moves  = moves,
            rating = puzzle["rating"]?.Value<int>() ?? 1500,
            themes = themes.ToArray(),
            status = "unsolved"
        };
    }

    // ── Get Random Unsolved Puzzle ────────────────────────────────────────────
    /// <summary>Returns a random unsolved puzzle. Mirrors getRandomUnsolvedPuzzle() in usePuzzles.ts.</summary>
    public Puzzle GetRandomUnsolvedPuzzle()
    {
        var unsolved = _puzzles.FindAll(p => p.status == "unsolved");
        if (unsolved.Count == 0)
        {
            // All solved — reset 20% to give more practice
            int resetCount = Mathf.Max(1, _puzzles.Count / 5);
            for (int i = 0; i < resetCount && i < _puzzles.Count; i++)
            {
                _puzzles[i].status = "unsolved";
                PlayerPrefs.SetString("pz_" + _puzzles[i].id, "unsolved");
            }
            unsolved = _puzzles.FindAll(p => p.status == "unsolved");
        }
        if (unsolved.Count == 0) return null;
        return unsolved[UnityEngine.Random.Range(0, unsolved.Count)];
    }

    // ── Record Attempt ────────────────────────────────────────────────────────
    /// <summary>Records a puzzle attempt. Mirrors recordAttempt() in usePuzzles.ts.</summary>
    public void RecordAttempt(string puzzleId, int rating, int timeTaken, string status)
    {
        // Update puzzle status
        var puzzle = _puzzles.Find(p => p.id == puzzleId);
        if (puzzle != null)
        {
            puzzle.status = status;
            PlayerPrefs.SetString("pz_" + puzzleId, status);
        }

        // Record attempt
        _attempts.Add(new Attempt
        {
            puzzleId  = puzzleId,
            rating    = rating,
            date      = DateTime.UtcNow.ToString("o"),
            timeTaken = timeTaken,
            status    = status
        });

        // Streak logic
        if (status == "solved")
        {
            _currentStreak++;
            PlayerPrefs.SetInt("puzzleStreak", _currentStreak);
        }
        else
        {
            if (_currentStreak > 0)
                SaveStreak(_currentStreak);
            _currentStreak = 0;
            PlayerPrefs.SetInt("puzzleStreak", 0);
        }

        SaveAttempts();
        PlayerPrefs.Save();
    }

    // ── Analytics (mirrors analytics state in usePuzzles.ts) ──────────────────
    public int GetSolvedToday()
    {
        string today = DateTime.UtcNow.ToString("yyyy-MM-dd");
        return _attempts.FindAll(a => a.status == "solved" && a.date.StartsWith(today)).Count;
    }

    public int GetSolvedThisWeek()
    {
        var weekAgo = DateTime.UtcNow.AddDays(-7);
        return _attempts.FindAll(a => a.status == "solved" &&
            DateTime.Parse(a.date) >= weekAgo).Count;
    }

    public float GetAvgTimeSolved()
    {
        var solved = _attempts.FindAll(a => a.status == "solved");
        if (solved.Count == 0) return 0f;
        float total = 0;
        foreach (var a in solved) total += a.timeTaken;
        return total / solved.Count;
    }

    // ── Persistence ───────────────────────────────────────────────────────────
    private void SavePuzzlesToDisk()
    {
        try
        {
            string json = JsonConvert.SerializeObject(_puzzles, Formatting.None);
            File.WriteAllText(PuzzlesPath, json);
        }
        catch (Exception ex) { Debug.LogError("[PuzzleDB] Save error: " + ex.Message); }
    }

    private void LoadAttempts()
    {
        if (!File.Exists(AttemptsPath)) return;
        try
        {
            string json = File.ReadAllText(AttemptsPath);
            _attempts = JsonConvert.DeserializeObject<List<Attempt>>(json) ?? new List<Attempt>();
        }
        catch { _attempts = new List<Attempt>(); }
    }

    private void SaveAttempts()
    {
        try
        {
            // Keep only last 10,000 attempts to avoid unbounded growth
            if (_attempts.Count > 10000)
                _attempts = _attempts.GetRange(_attempts.Count - 10000, 10000);
            File.WriteAllText(AttemptsPath, JsonConvert.SerializeObject(_attempts));
        }
        catch { }
    }

    private void SaveStreak(int length)
    {
        string key = "topStreaks";
        string raw = PlayerPrefs.GetString(key, "[]");
        var streaks = JsonConvert.DeserializeObject<List<JObject>>(raw) ?? new List<JObject>();
        streaks.Add(JObject.FromObject(new { length, date = DateTime.UtcNow.ToString("o") }));
        streaks.Sort((a, b) => b["length"].Value<int>().CompareTo(a["length"].Value<int>()));
        if (streaks.Count > 10) streaks = streaks.GetRange(0, 10);
        PlayerPrefs.SetString(key, JsonConvert.SerializeObject(streaks));
        PlayerPrefs.Save();
    }
}
