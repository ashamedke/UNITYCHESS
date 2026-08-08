using System.Collections;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

/// <summary>
/// Free practice screen — port of src/pages/PracticeSetup.tsx + Play mode.
///
/// Lets the user:
///   - Choose their color (white / black / random)
///   - Choose opponent: Stockfish at skill 1-20, or just Free (no engine)
///   - Set time control (Bullet / Blitz / Rapid / Classic / Unlimited)
///   - Start playing against the engine, with a live clock
/// </summary>
public class FreePracticeScreen : MonoBehaviour
{
    // ── Singleton ─────────────────────────────────────────────────────────────
    public static FreePracticeScreen Instance { get; private set; }

    // ── UI ────────────────────────────────────────────────────────────────────
    [Header("Setup Panel")]
    [SerializeField] private GameObject     setupPanel;
    [SerializeField] private Button         btnWhite, btnBlack, btnRandom;
    [SerializeField] private Slider         skillSlider;
    [SerializeField] private TMP_Text       skillLabel;
    [SerializeField] private Button         btnBullet, btnBlitz, btnRapid, btnClassic, btnUnlimited;
    [SerializeField] private Button         btnStart;
    [SerializeField] private Toggle         toggleEngine;

    [Header("Play Panel")]
    [SerializeField] private GameObject     playPanel;
    [SerializeField] private Board2DRenderer board2D;
    [SerializeField] private GameObject     board3DRoot;
    [SerializeField] private PieceManager   pieceManager3D;
    [SerializeField] private TouchPieceInput touchInput;

    [Header("Clock UI")]
    [SerializeField] private TMP_Text       whiteClockText;
    [SerializeField] private TMP_Text       blackClockText;
    [SerializeField] private TMP_Text       statusText;

    [Header("Controls")]
    [SerializeField] private Button         btnResign;
    [SerializeField] private Button         btnOfferDraw;
    [SerializeField] private Button         btnFlip;
    [SerializeField] private Button         btnBack;

    // ── Time controls (minutes,increment in seconds) ───────────────────────────
    private readonly (int min, int inc, string label)[] _timeControls = new[]
    {
        (1, 0,  "Bullet 1+0"),
        (3, 0,  "Blitz 3+0"),
        (5, 3,  "Blitz 5+3"),
        (10, 5, "Rapid 10+5"),
        (30, 0, "Classic 30+0"),
        (0, 0,  "Unlimited")
    };
    private int _timeIndex = 2; // Default: Blitz 5+3

    // ── Game state ────────────────────────────────────────────────────────────
    private ChessBoard _board;
    private ChessClock _clock;
    private char       _playerColor;
    private int        _skillLevel = 10;
    private bool       _useEngine = true;
    private bool       _gameOver;
    private bool       _boardFlipped;
    private bool       _is3D;

    // ── Lifecycle ──────────────────────────────────────────────────────────────
    private void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
    }

    private void OnEnable()
    {
        btnWhite?.onClick.AddListener(() => _playerColor = ChessBoard.WHITE);
        btnBlack?.onClick.AddListener(() => _playerColor = ChessBoard.BLACK);
        btnRandom?.onClick.AddListener(() => _playerColor = Random.value > 0.5f ? ChessBoard.WHITE : ChessBoard.BLACK);
        btnStart?.onClick.AddListener(StartGame);
        btnResign?.onClick.AddListener(Resign);
        btnFlip?.onClick.AddListener(FlipBoard);
        btnBack?.onClick.AddListener(ShowSetup);
        skillSlider?.onValueChanged.AddListener(v => UpdateSkillLabel((int)v));
        if (touchInput != null) touchInput.OnMove += HandleUserMove;

        _playerColor = ChessBoard.WHITE;
        ShowSetup();
    }

    private void OnDisable()
    {
        _clock?.Dispose();
        btnStart?.onClick.RemoveAllListeners();
        btnResign?.onClick.RemoveAllListeners();
        if (touchInput != null) touchInput.OnMove -= HandleUserMove;
    }

    private void UpdateSkillLabel(int level)
    {
        _skillLevel = level;
        if (skillLabel != null)
        {
            string label = level switch
            {
                <= 3  => $"Level {level} — Beginner",
                <= 6  => $"Level {level} — Intermediate",
                <= 10 => $"Level {level} — Advanced",
                <= 15 => $"Level {level} — Expert",
                _     => $"Level {level} — Master"
            };
            skillLabel.text = label;
        }
    }

    // ── Setup ──────────────────────────────────────────────────────────────────
    private void ShowSetup()
    {
        setupPanel?.SetActive(true);
        playPanel?.SetActive(false);
        _clock?.Dispose();
    }

    // ── Start Game ────────────────────────────────────────────────────────────
    public void StartGame()
    {
        _board   = new ChessBoard();
        _gameOver = false;
        _boardFlipped = _playerColor == ChessBoard.BLACK;

        setupPanel?.SetActive(false);
        playPanel?.SetActive(true);

        RefreshBoard();

        // Clock
        if (_timeControls[_timeIndex].min > 0)
        {
            long ms = _timeControls[_timeIndex].min * 60000L;
            _clock = new ChessClock(ms, ms);
            _clock.OnTick  += OnClockTick;
            _clock.OnFlag  += OnFlag;
            _clock.Start(ChessBoard.WHITE);
        }

        SetStatus(_board.Turn == ChessBoard.WHITE ? "White to move" : "Black to move");

        // If player is black, trigger engine move first
        if (_playerColor == ChessBoard.BLACK)
            StartCoroutine(RequestEngineMove());
    }

    /// <summary>Called from PuzzleScreen to continue playing from a puzzle position.</summary>
    public void StartFromPosition(string fen, char playerColor)
    {
        _playerColor = playerColor;
        _board = new ChessBoard(fen);
        _gameOver = false;
        _boardFlipped = playerColor == ChessBoard.BLACK;

        setupPanel?.SetActive(false);
        playPanel?.SetActive(true);

        RefreshBoard();
        SetStatus("Free practice — continue from puzzle");

        // If it's already the computer's turn
        if (_board.Turn != _playerColor)
            StartCoroutine(RequestEngineMove());
    }

    // ── User Move ─────────────────────────────────────────────────────────────
    private void HandleUserMove(string from, string to, char promotion)
    {
        if (_gameOver || _board.Turn != _playerColor) return;

        var move = new ChessMove
        {
            From = ChessBoard.AlgToIdx(from),
            To   = ChessBoard.AlgToIdx(to),
            Promotion = promotion
        };

        if (!_board.MakeMove(move)) return;

        ChessAudioManager.Instance?.PlayMoveSound(move, _board.IsInCheck());
        _clock?.OnMove(_board.Turn, _timeControls[_timeIndex].inc * 1000L);
        pieceManager3D?.AnimateMove(move);
        board2D?.SetPosition(_board.Fen(), !_boardFlipped, null);

        if (CheckGameOver()) return;

        if (_useEngine) StartCoroutine(RequestEngineMove());
    }

    // ── Engine Move ───────────────────────────────────────────────────────────
    private IEnumerator RequestEngineMove()
    {
        if (!StockfishBridge.Instance.IsReady)
            yield return new WaitUntil(() => StockfishBridge.Instance.IsReady);

        bool moveDone = false;
        string engineMove = null;

        // movetime scales with skill (weaker = shorter think time)
        int movetime = 200 + _skillLevel * 100;

        StockfishBridge.Instance.GetBestMove(
            _board.Fen(), movetime,
            bestMove => { engineMove = bestMove; moveDone = true; },
            skillLevel: _skillLevel
        );

        yield return new WaitUntil(() => moveDone);

        if (engineMove != null && !_gameOver)
        {
            _board.MakeMove(engineMove);
            ChessAudioManager.Instance?.PlayMove();
            _clock?.OnMove(_board.Turn, _timeControls[_timeIndex].inc * 1000L);
            RefreshBoard();
            CheckGameOver();
        }
    }

    // ── Game Over ─────────────────────────────────────────────────────────────
    private bool CheckGameOver()
    {
        if (_board.IsCheckmate())
        {
            _gameOver = true;
            char winner = _board.Turn == ChessBoard.WHITE ? ChessBoard.BLACK : ChessBoard.WHITE;
            SetStatus(winner == ChessBoard.WHITE ? "White wins by checkmate!" : "Black wins by checkmate!");
            _clock?.Pause();
            return true;
        }
        if (_board.IsStalemate())
        {
            _gameOver = true;
            SetStatus("Stalemate — Draw!");
            _clock?.Pause();
            return true;
        }
        if (_board.IsDraw())
        {
            _gameOver = true;
            SetStatus("50-move rule — Draw!");
            _clock?.Pause();
            return true;
        }
        SetStatus(_board.Turn == ChessBoard.WHITE ? "White to move" : "Black to move");
        return false;
    }

    private void Resign()
    {
        if (_gameOver) return;
        _gameOver = true;
        _clock?.Pause();
        SetStatus(_playerColor == ChessBoard.WHITE ? "Black wins — White resigned." : "White wins — Black resigned.");
    }

    // ── Clock callbacks ───────────────────────────────────────────────────────
    private void OnClockTick(long wMs, long bMs, char active)
    {
        UnityMainThreadDispatcher.Enqueue(() =>
        {
            if (whiteClockText != null) whiteClockText.text = ChessClock.FormatMs(wMs);
            if (blackClockText != null) blackClockText.text = ChessClock.FormatMs(bMs);
        });
    }

    private void OnFlag(char flaggedSide)
    {
        UnityMainThreadDispatcher.Enqueue(() =>
        {
            _gameOver = true;
            char winner = flaggedSide == ChessBoard.WHITE ? ChessBoard.BLACK : ChessBoard.WHITE;
            SetStatus(winner == ChessBoard.WHITE ? "White wins on time!" : "Black wins on time!");
        });
    }

    // ── Refresh ────────────────────────────────────────────────────────────────
    private void RefreshBoard()
    {
        board2D?.SetPosition(_board.Fen(), !_boardFlipped, null);
        pieceManager3D?.LoadPosition(_board, _boardFlipped);
        touchInput?.SetBoard(_board);
        touchInput?.SetEnabled(!_gameOver && _board.Turn == _playerColor);
    }

    private void FlipBoard()
    {
        _boardFlipped = !_boardFlipped;
        RefreshBoard();
    }

    private void SetStatus(string msg)
    {
        if (statusText != null) statusText.text = msg;
    }
}
