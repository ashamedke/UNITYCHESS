using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

/// <summary>
/// Puzzle screen — port of src/pages/Puzzles.tsx.
///
/// Controls the full puzzle-solving UI:
///   - Download prompt if no puzzles DB exists
///   - Loading a random unsolved puzzle
///   - Hint system (level 1 = from-square glow, level 2 = full arrow)
///   - Auto-next toggle
///   - Retry on failure
///   - "Practice vs Computer" bridge to FreePracticeScreen
///   - 2D/3D board toggle
/// </summary>
public class PuzzleScreen : MonoBehaviour
{
    // ── Singleton ─────────────────────────────────────────────────────────────
    public static PuzzleScreen Instance { get; private set; }

    // ── UI References (assign in Inspector) ───────────────────────────────────
    [Header("Panels")]
    [SerializeField] private GameObject downloadPanel;
    [SerializeField] private GameObject puzzlePanel;
    [SerializeField] private TMP_Text   statusText;
    [SerializeField] private TMP_Text   downloadProgressText;

    [Header("Download Panel")]
    [SerializeField] private Button   btn1K;
    [SerializeField] private Button   btn5K;
    [SerializeField] private Button   btn10K;
    [SerializeField] private Button   btn50K;
    [SerializeField] private Slider   downloadProgress;

    [Header("Controls")]
    [SerializeField] private Button   btnHint;
    [SerializeField] private Button   btnNext;
    [SerializeField] private Button   btnRetry;
    [SerializeField] private Button   btnStats;
    [SerializeField] private Button   btnPracticeVsComputer;
    [SerializeField] private Toggle   toggleAutoNext;
    [SerializeField] private Button   btn2D;
    [SerializeField] private Button   btn3D;

    [Header("Board")]
    [SerializeField] private Board2DRenderer board2D;
    [SerializeField] private GameObject      board3DRoot;
    [SerializeField] private PieceManager    pieceManager3D;
    [SerializeField] private TouchPieceInput touchInput;
    [SerializeField] private SquareHighlight squareHighlight;

    // ── State ─────────────────────────────────────────────────────────────────
    private ChessBoard          _board;
    private PuzzleDatabase.Puzzle _currentPuzzle;
    private string[]            _puzzleMoves;     // moves the user must play
    private int                 _moveIndex;
    private bool                _isSolved;
    private bool                _isFailed;
    private bool                _autoNext;
    private bool                _attemptRecorded;
    private int                 _hintLevel;       // 0=none, 1=from-square, 2=full arrow
    private float               _startTime;
    private bool                _is3D;
    private string              _orientation;     // "white" | "black"

    // ── Lifecycle ──────────────────────────────────────────────────────────────
    private void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
    }

    private void OnEnable()
    {
        // Wire buttons
        btnHint?.onClick.AddListener(HandleHint);
        btnNext?.onClick.AddListener(LoadPuzzle);
        btnRetry?.onClick.AddListener(HandleRetry);
        btnStats?.onClick.AddListener(() => PracticeScreen.Instance?.ShowStats());
        btnPracticeVsComputer?.onClick.AddListener(HandlePracticeVsComputer);
        toggleAutoNext?.onValueChanged.AddListener(v => _autoNext = v);
        btn2D?.onClick.AddListener(() => SetBoardType(false));
        btn3D?.onClick.AddListener(() => SetBoardType(true));
        btn1K?.onClick.AddListener(() => BeginDownload(1000));
        btn5K?.onClick.AddListener(() => BeginDownload(5000));
        btn10K?.onClick.AddListener(() => BeginDownload(10000));
        btn50K?.onClick.AddListener(() => BeginDownload(50000));

        // Subscribe to puzzle DB events
        if (PuzzleDatabase.Instance != null)
        {
            PuzzleDatabase.Instance.OnDatabaseReady   += OnDatabaseReady;
            PuzzleDatabase.Instance.OnDownloadProgress += OnDownloadProgress;
        }

        // Subscribe to board moves
        if (touchInput != null) touchInput.OnMove += HandleUserMove;

        CheckDatabaseState();
    }

    private void OnDisable()
    {
        btnHint?.onClick.RemoveAllListeners();
        btnNext?.onClick.RemoveAllListeners();
        btnRetry?.onClick.RemoveAllListeners();
        btnStats?.onClick.RemoveAllListeners();
        btnPracticeVsComputer?.onClick.RemoveAllListeners();

        if (PuzzleDatabase.Instance != null)
        {
            PuzzleDatabase.Instance.OnDatabaseReady   -= OnDatabaseReady;
            PuzzleDatabase.Instance.OnDownloadProgress -= OnDownloadProgress;
        }

        if (touchInput != null) touchInput.OnMove -= HandleUserMove;
    }

    // ── Init ───────────────────────────────────────────────────────────────────
    private void CheckDatabaseState()
    {
        if (PuzzleDatabase.Instance == null || !PuzzleDatabase.Instance.IsReady)
        {
            ShowDownloadPanel();
            return;
        }

        if (PuzzleDatabase.Instance.TotalPuzzles == 0)
        {
            ShowDownloadPanel();
            return;
        }

        ShowPuzzlePanel();
        LoadPuzzle();
    }

    private void OnDatabaseReady()
    {
        if (PuzzleDatabase.Instance.TotalPuzzles == 0)
        {
            ShowDownloadPanel();
            return;
        }
        ShowPuzzlePanel();
        if (_currentPuzzle == null) LoadPuzzle();
    }

    private void OnDownloadProgress(string msg)
    {
        if (downloadProgressText != null) downloadProgressText.text = msg;
    }

    // ── Download Panel ────────────────────────────────────────────────────────
    private void ShowDownloadPanel()
    {
        downloadPanel?.SetActive(true);
        puzzlePanel?.SetActive(false);
    }

    private void ShowPuzzlePanel()
    {
        downloadPanel?.SetActive(false);
        puzzlePanel?.SetActive(true);
    }

    private void BeginDownload(int count)
    {
        downloadProgressText?.gameObject.SetActive(true);
        btn1K?.gameObject.SetActive(false);
        btn5K?.gameObject.SetActive(false);
        btn10K?.gameObject.SetActive(false);
        btn50K?.gameObject.SetActive(false);
        PuzzleDatabase.Instance?.DownloadPuzzles(count, () =>
        {
            UnityMainThreadDispatcher.Enqueue(OnDatabaseReady);
        });
    }

    // ── Load Puzzle ────────────────────────────────────────────────────────────
    private void LoadPuzzle()
    {
        _isSolved = _isFailed = _attemptRecorded = false;
        _hintLevel   = 0;
        _moveIndex   = 0;

        var puzzle = PuzzleDatabase.Instance?.GetRandomUnsolvedPuzzle();
        if (puzzle == null)
        {
            SetStatus("No puzzles available. Download more!");
            return;
        }

        _currentPuzzle = puzzle;
        _board = new ChessBoard(puzzle.fen);

        // Play the opponent's first move
        if (puzzle.moves.Length > 0)
        {
            _board.MakeMove(puzzle.moves[0]);
            _puzzleMoves = new string[puzzle.moves.Length - 1];
            System.Array.Copy(puzzle.moves, 1, _puzzleMoves, 0, _puzzleMoves.Length);
        }
        else
        {
            _puzzleMoves = new string[0];
        }

        _orientation = _board.Turn == ChessBoard.WHITE ? "white" : "black";
        _startTime   = Time.realtimeSinceStartup;

        RefreshBoard();
        SetStatus(_board.Turn == ChessBoard.WHITE ? "White to move" : "Black to move");

        // Show/hide controls
        btnRetry?.gameObject.SetActive(false);
        btnPracticeVsComputer?.gameObject.SetActive(false);
    }

    // ── User Move ─────────────────────────────────────────────────────────────
    private void HandleUserMove(string from, string to, char promotion)
    {
        if (_isSolved || _isFailed) return;

        string uci = from + to + (promotion != '\0' ? promotion.ToString() : "");

        if (_moveIndex >= _puzzleMoves.Length) return;
        string expected = _puzzleMoves[_moveIndex];

        bool correct = uci == expected;

        bool isCapture = _board.PieceAt(to) != '\0';
        _board.MakeMove(uci);
        ChessAudioManager.Instance?.PlayMoveSound(
            new ChessMove { Flags = isCapture ? ChessBoard.FLAG_CAPTURE : 0 },
            _board.IsInCheck());

        if (correct)
        {
            _hintLevel = 0;
            _moveIndex++;

            if (_moveIndex >= _puzzleMoves.Length)
            {
                // Puzzle solved!
                _isSolved = true;
                SetStatus("Puzzle Solved! ✓");
                RecordAttempt("solved");
                btnPracticeVsComputer?.gameObject.SetActive(!_autoNext);
                btnRetry?.gameObject.SetActive(false);

                if (_autoNext) StartCoroutine(AutoNextDelay());
            }
            else
            {
                // Play opponent's response
                SetStatus("Correct! Opponent is moving...");
                StartCoroutine(PlayOpponentMove());
            }
        }
        else
        {
            _isFailed = true;
            SetStatus("Incorrect move ✗");
            RecordAttempt("failed");
            btnRetry?.gameObject.SetActive(true);
            btnPracticeVsComputer?.gameObject.SetActive(false);
        }

        RefreshBoard();
    }

    private IEnumerator PlayOpponentMove()
    {
        yield return new WaitForSeconds(0.3f);

        if (_moveIndex < _puzzleMoves.Length)
        {
            string opponentMove = _puzzleMoves[_moveIndex];
            _board.MakeMove(opponentMove);
            _moveIndex++;
            ChessAudioManager.Instance?.PlayMove();
            RefreshBoard();
            SetStatus(_board.Turn == ChessBoard.WHITE ? "White to move" : "Black to move");
        }
    }

    private IEnumerator AutoNextDelay()
    {
        SetStatus("Solved! Next puzzle in 1s...");
        yield return new WaitForSeconds(1f);
        LoadPuzzle();
    }

    // ── Hint ──────────────────────────────────────────────────────────────────
    private void HandleHint()
    {
        if (_isSolved || _isFailed) return;
        _hintLevel = Mathf.Min(_hintLevel + 1, 2);

        if (_moveIndex >= _puzzleMoves.Length) return;
        string expected = _puzzleMoves[_moveIndex];
        string from = expected.Substring(0, 2);
        string to   = expected.Substring(2, 2);

        // Level 1: highlight from-square only
        // Level 2: highlight both from and to (full arrow)
        squareHighlight?.ClearHighlights();
        squareHighlight?.HighlightSquare(ChessBoard.AlgToIdx(from), SquareHighlight.Type.HintFrom);
        if (_hintLevel >= 2)
            squareHighlight?.HighlightSquare(ChessBoard.AlgToIdx(to), SquareHighlight.Type.HintTo);
    }

    // ── Retry ─────────────────────────────────────────────────────────────────
    private void HandleRetry()
    {
        if (_currentPuzzle == null) return;

        _board = new ChessBoard(_currentPuzzle.fen);
        if (_currentPuzzle.moves.Length > 0)
        {
            _board.MakeMove(_currentPuzzle.moves[0]);
            _puzzleMoves = new string[_currentPuzzle.moves.Length - 1];
            System.Array.Copy(_currentPuzzle.moves, 1, _puzzleMoves, 0, _puzzleMoves.Length);
        }
        _moveIndex = 0;
        _isFailed  = _isSolved = false;
        _hintLevel = 0;
        _attemptRecorded = false;

        RefreshBoard();
        SetStatus(_board.Turn == ChessBoard.WHITE ? "White to move" : "Black to move");
        btnRetry?.gameObject.SetActive(false);
    }

    // ── Practice vs Computer ──────────────────────────────────────────────────
    private void HandlePracticeVsComputer()
    {
        // Navigate to FreePractice with the puzzle start position
        // The player keeps their color; computer plays the other side
        PracticeScreen.Instance?.ShowFreePracticeFromPuzzle(
            _board.Fen(),
            _orientation == "white" ? ChessBoard.WHITE : ChessBoard.BLACK
        );
    }

    // ── Board Type Toggle ─────────────────────────────────────────────────────
    private void SetBoardType(bool use3D)
    {
        _is3D = use3D;
        board3DRoot?.SetActive(use3D);
        board2D?.gameObject.SetActive(!use3D);
        RefreshBoard();
    }

    // ── Board Refresh ─────────────────────────────────────────────────────────
    private void RefreshBoard()
    {
        bool whiteAtBottom = _orientation == "white";
        if (_is3D)
        {
            pieceManager3D?.LoadPosition(_board, !whiteAtBottom);
            touchInput?.SetBoard(_board);
            touchInput?.SetEnabled(!_isSolved && !_isFailed);
        }
        else
        {
            board2D?.SetPosition(_board.Fen(), whiteAtBottom, GetHintArrows());
        }
    }

    private System.Collections.Generic.List<Board2DRenderer.Arrow> GetHintArrows()
    {
        var arrows = new System.Collections.Generic.List<Board2DRenderer.Arrow>();
        if (_hintLevel > 0 && _moveIndex < _puzzleMoves.Length)
        {
            string move = _puzzleMoves[_moveIndex];
            string from = move.Substring(0, 2);
            string to   = move.Substring(2, 2);
            arrows.Add(new Board2DRenderer.Arrow
            {
                From  = from,
                To    = _hintLevel >= 2 ? to : from,
                Color = Color.green
            });
        }
        return arrows;
    }

    // ── Status ────────────────────────────────────────────────────────────────
    private void SetStatus(string msg)
    {
        if (statusText != null) statusText.text = msg;
    }

    // ── Record Attempt ────────────────────────────────────────────────────────
    private void RecordAttempt(string status)
    {
        if (_attemptRecorded || _currentPuzzle == null) return;
        _attemptRecorded = true;
        int timeTaken = Mathf.RoundToInt(Time.realtimeSinceStartup - _startTime);
        PuzzleDatabase.Instance?.RecordAttempt(
            _currentPuzzle.id, _currentPuzzle.rating, timeTaken, status);
    }
}
