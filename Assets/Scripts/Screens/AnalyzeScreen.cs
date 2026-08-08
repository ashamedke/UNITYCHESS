using System;
using System.Collections;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

/// <summary>
/// Analyze screen — port of src/pages/Analyze.tsx + Playground3D.tsx (analysis mode).
///
/// Landscape layout: 3D board left 60%, panel right 40%.
/// Features:
///   - FEN input + "Proceed" button
///   - Board editor mode (drag piece on/off board)
///   - Stockfish engine lines (top 3 PVs)
///   - Move tree navigation (scrollable list)
///   - Previous/next game nav when PGN list loaded
///   - Camera controls (orbit, flip, white/black view)
///   - Import PGN via ImportScreen bridge
/// </summary>
public class AnalyzeScreen : MonoBehaviour
{
    // ── UI references ─────────────────────────────────────────────────────────
    [Header("Panels")]
    [SerializeField] private GameObject landingPanel;
    [SerializeField] private GameObject analyzePanel;
    [SerializeField] private GameObject editPanel;

    [Header("Landing")]
    [SerializeField] private TMP_InputField fenInput;
    [SerializeField] private Button         btnLoad;
    [SerializeField] private Button         btnProceed;
    [SerializeField] private Button         btnEditBoard;
    [SerializeField] private TMP_Text       errorText;
    [SerializeField] private Board2DRenderer previewBoard;

    [Header("Analyze")]
    [SerializeField] private TopEngineLinesPanel  engineLinesPanel;
    [SerializeField] private MoveListPanel         moveListPanel;
    [SerializeField] private PieceManager          pieceManager;
    [SerializeField] private TouchPieceInput       touchInput;
    [SerializeField] private SquareHighlight       squareHighlight;
    [SerializeField] private EvalBar               evalBar;
    [SerializeField] private Button                btnFlipBoard;
    [SerializeField] private Button                btnToggleEngine;
    [SerializeField] private TMP_Text              modeLabel;

    [Header("Game Navigation (Import mode)")]
    [SerializeField] private GameObject  gameNavPanel;
    [SerializeField] private Button      btnPrevGame;
    [SerializeField] private Button      btnNextGame;
    [SerializeField] private TMP_Text    gameIndexLabel;

    // ── State ─────────────────────────────────────────────────────────────────
    private ChessBoard _board;
    private MoveTree   _tree;
    private string     _currentNodeId = "root";
    private bool       _engineEnabled = true;
    private bool       _boardFlipped;

    // Multi-game import state
    private System.Collections.Generic.List<PgnParser.PgnGame> _gameList;
    private int _gameIndex;

    private enum ViewMode { Landing, Edit, Analyze }
    private ViewMode _viewMode = ViewMode.Landing;

    // ── Lifecycle ──────────────────────────────────────────────────────────────
    private void OnEnable()
    {
        btnLoad?.onClick.AddListener(OnLoadFen);
        btnProceed?.onClick.AddListener(OnProceed);
        btnEditBoard?.onClick.AddListener(OpenEdit);
        btnFlipBoard?.onClick.AddListener(FlipBoard);
        btnToggleEngine?.onClick.AddListener(ToggleEngine);
        btnPrevGame?.onClick.AddListener(PrevGame);
        btnNextGame?.onClick.AddListener(NextGame);

        if (touchInput != null) touchInput.OnMove += HandleUserMove;

        ShowLanding();
    }

    private void OnDisable()
    {
        btnLoad?.onClick.RemoveAllListeners();
        btnProceed?.onClick.RemoveAllListeners();
        btnEditBoard?.onClick.RemoveAllListeners();
        btnFlipBoard?.onClick.RemoveAllListeners();
        btnToggleEngine?.onClick.RemoveAllListeners();
        btnPrevGame?.onClick.RemoveAllListeners();
        btnNextGame?.onClick.RemoveAllListeners();

        if (touchInput != null) touchInput.OnMove -= HandleUserMove;

        StockfishBridge.Instance?.Stop();
    }

    // ── External API (called from ImportScreen) ────────────────────────────────
    public void LoadPgn(PgnParser.PgnGame game)
    {
        _tree = MoveTree.FromPgn(game);
        _board = new ChessBoard(game.Headers.TryGetValue("FEN", out string fen)
            ? fen : ChessBoard.START_FEN);
        _currentNodeId = "root";
        ShowAnalyze();
        RefreshAll();
    }

    public void LoadPgnList(System.Collections.Generic.List<PgnParser.PgnGame> games, int index = 0)
    {
        _gameList  = games;
        _gameIndex = index;
        LoadPgn(games[index]);
        gameNavPanel?.SetActive(games.Count > 1);
        RefreshGameNav();
    }

    // ── Landing ────────────────────────────────────────────────────────────────
    private void ShowLanding()
    {
        _viewMode = ViewMode.Landing;
        landingPanel?.SetActive(true);
        analyzePanel?.SetActive(false);
        editPanel?.SetActive(false);

        if (fenInput != null) fenInput.text = ChessBoard.START_FEN;
        previewBoard?.SetPosition(ChessBoard.START_FEN, true, null);
    }

    private void OnLoadFen()
    {
        string fen = fenInput?.text?.Trim();
        if (string.IsNullOrEmpty(fen)) return;
        try
        {
            var testBoard = new ChessBoard(fen);
            previewBoard?.SetPosition(fen, true, null);
            if (errorText != null) errorText.text = "";
        }
        catch
        {
            if (errorText != null) errorText.text = "Invalid FEN.";
        }
    }

    private void OnProceed()
    {
        string fen = fenInput?.text?.Trim() ?? ChessBoard.START_FEN;
        try
        {
            _board = new ChessBoard(fen);
            _tree  = new MoveTree(fen);
            _currentNodeId = "root";
            ShowAnalyze();
            RefreshAll();
        }
        catch
        {
            if (errorText != null) errorText.text = "Cannot proceed: illegal position.";
        }
    }

    // ── Edit Mode ─────────────────────────────────────────────────────────────
    private void OpenEdit()
    {
        _viewMode = ViewMode.Edit;
        landingPanel?.SetActive(false);
        analyzePanel?.SetActive(false);
        editPanel?.SetActive(true);
    }

    // ── Analyze Mode ──────────────────────────────────────────────────────────
    private void ShowAnalyze()
    {
        _viewMode = ViewMode.Analyze;
        landingPanel?.SetActive(false);
        analyzePanel?.SetActive(true);
        editPanel?.SetActive(false);
    }

    // ── User Move ─────────────────────────────────────────────────────────────
    private void HandleUserMove(string from, string to, char promotion)
    {
        var move = new ChessMove
        {
            From = ChessBoard.AlgToIdx(from),
            To   = ChessBoard.AlgToIdx(to),
            Promotion = promotion
        };

        if (!_board.MakeMove(move)) return;

        string san = _board.MoveToSan(move);
        var node = _tree.AddMove(_currentNodeId, san, move.Uci, _board.Fen(),
                                  _board.MoveNumber, move.Color);
        _currentNodeId = node.Id;

        ChessAudioManager.Instance?.PlayMoveSound(move, _board.IsInCheck());
        pieceManager?.AnimateMove(move);
        RefreshAll();
    }

    // ── Navigation ────────────────────────────────────────────────────────────
    public void NavigateTo(string nodeId)
    {
        var node = _tree.FindById(nodeId);
        if (node == null) return;
        _board = new ChessBoard(node.Fen);
        _currentNodeId = nodeId;
        pieceManager?.LoadPosition(_board, _boardFlipped);
        touchInput?.SetBoard(_board);
        RunEngine();
    }

    private void FlipBoard()
    {
        _boardFlipped = !_boardFlipped;
        pieceManager?.LoadPosition(_board, _boardFlipped);
    }

    private void ToggleEngine()
    {
        _engineEnabled = !_engineEnabled;
        if (_engineEnabled) RunEngine();
        else StockfishBridge.Instance?.Stop();

        engineLinesPanel?.gameObject.SetActive(_engineEnabled);
        evalBar?.gameObject.SetActive(_engineEnabled);
    }

    // ── Multi-game nav ────────────────────────────────────────────────────────
    private void PrevGame()
    {
        if (_gameList == null || _gameIndex <= 0) return;
        _gameIndex--;
        LoadPgn(_gameList[_gameIndex]);
        RefreshGameNav();
    }

    private void NextGame()
    {
        if (_gameList == null || _gameIndex >= _gameList.Count - 1) return;
        _gameIndex++;
        LoadPgn(_gameList[_gameIndex]);
        RefreshGameNav();
    }

    private void RefreshGameNav()
    {
        if (gameIndexLabel != null && _gameList != null)
            gameIndexLabel.text = $"Game {_gameIndex + 1} of {_gameList.Count}";
        btnPrevGame?.GetComponent<Button>().interactable = _gameIndex > 0;
        btnNextGame?.GetComponent<Button>().interactable =
            _gameList != null && _gameIndex < _gameList.Count - 1;
    }

    // ── Engine ────────────────────────────────────────────────────────────────
    private void RunEngine()
    {
        if (!_engineEnabled || StockfishBridge.Instance == null) return;
        var uciPath = _tree.GetUciPath(_currentNodeId);
        StockfishBridge.Instance.Evaluate(
            _board.Fen(), depth: 20, multiPV: 3,
            callback: pvs =>
            {
                engineLinesPanel?.SetLines(pvs, _board);
                if (pvs.Count > 0)
                    evalBar?.SetEval(pvs[0].CentiPawns, pvs[0].IsMate, pvs[0].MateMoves, _board.Turn);
            },
            uciMovesBefore: uciPath
        );
    }

    // ── Refresh ────────────────────────────────────────────────────────────────
    private void RefreshAll()
    {
        pieceManager?.LoadPosition(_board, _boardFlipped);
        touchInput?.SetBoard(_board);
        touchInput?.SetEnabled(true);
        moveListPanel?.BuildFrom(_tree, _currentNodeId, nodeId => NavigateTo(nodeId));
        if (_engineEnabled) RunEngine();
    }
}
