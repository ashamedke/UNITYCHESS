using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

/// <summary>
/// Watch details screen — port of src/pages/WatchDetails.tsx.
///
/// Streams a live/past broadcast round PGN and shows multi-board layout.
/// Landscape: board list left side, active board right side.
/// Uses ChessClock for live clock sync from %clk annotations.
/// </summary>
public class WatchDetailsScreen : MonoBehaviour
{
    [Header("UI")]
    [SerializeField] private TMP_Text   headerTitle;
    [SerializeField] private Button     btnBack;
    [SerializeField] private Transform  boardListContainer;
    [SerializeField] private GameObject boardCardPrefab;
    [SerializeField] private GameObject singleBoardPanel;
    [SerializeField] private Board2DRenderer activeBoardRenderer;
    [SerializeField] private TMP_Text   whiteName;
    [SerializeField] private TMP_Text   blackName;
    [SerializeField] private TMP_Text   whiteClk;
    [SerializeField] private TMP_Text   blackClk;
    [SerializeField] private TMP_Text   resultText;
    [SerializeField] private MoveListPanel moveList;
    [SerializeField] private GameObject    loadingSpinner;

    // ── State ──────────────────────────────────────────────────────────────────
    private string   _tourId;
    private string   _roundId;
    private System.Action _onBack;

    // game id → board state
    private readonly Dictionary<string, WatchedGame> _games = new Dictionary<string, WatchedGame>();
    private WatchedGame _activeGame;
    private Coroutine   _streamCoroutine;
    private ChessClock  _clock;

    private class WatchedGame
    {
        public string        GameId;
        public string        White, Black, WhiteElo, BlackElo, Result;
        public string        CurrentFen;
        public MoveTree      Tree;
        public PgnParser.PgnGame PgnGame;
        public string        LastNodeId;
        public long          WhiteMs, BlackMs;
    }

    // ── Open ───────────────────────────────────────────────────────────────────
    public void OpenRound(string tourId, string roundId, string title, System.Action onBack)
    {
        _tourId = tourId;
        _roundId = roundId;
        _onBack = onBack;

        if (headerTitle != null) headerTitle.text = title;
        btnBack?.onClick.RemoveAllListeners();
        btnBack?.onClick.AddListener(HandleBack);

        _games.Clear();
        _activeGame = null;
        _clock?.Dispose();

        ClearBoardList();
        if (loadingSpinner != null) loadingSpinner.SetActive(true);

        _streamCoroutine = LichessClient.Instance?.StreamBroadcastPgn(
            roundId,
            onChunk: HandlePgnChunk,
            onDone:  () => { },
            onError: err => Debug.LogWarning("[WatchDetails] Stream error: " + err)
        );
    }

    private void HandleBack()
    {
        _streamCoroutine?.GetHashCode(); // stop would need ref to client
        LichessClient.Instance?.StopStream();
        _clock?.Dispose();
        _onBack?.Invoke();
    }

    private void OnDisable()
    {
        LichessClient.Instance?.StopStream();
        _clock?.Dispose();
        _clock = null;
    }

    // ── PGN Chunk Handler ─────────────────────────────────────────────────────
    private void HandlePgnChunk(LichessClient.PgnChunk chunk)
    {
        if (loadingSpinner != null) loadingSpinner.SetActive(false);

        // Parse all games in this chunk
        var games = PgnParser.ParseMulti(chunk.PgnText);

        foreach (var game in games)
        {
            string gameId = game.Headers.GetValueOrDefault("Site", game.White + "_" + game.Black);

            var wg = new WatchedGame
            {
                GameId    = gameId,
                White     = game.White,
                Black     = game.Black,
                Result    = game.Result,
                PgnGame   = game,
                CurrentFen = game.Headers.GetValueOrDefault("FEN", ChessBoard.START_FEN),
                Tree      = MoveTree.FromPgn(game)
            };

            // Get last node for current position display
            var lastNode = wg.Tree.GetLast("root");
            wg.LastNodeId = lastNode?.Id ?? "root";
            wg.CurrentFen = lastNode?.Fen ?? wg.CurrentFen;

            // Extract clock from last move %clk annotation
            ExtractClocks(wg, game);

            _games[gameId] = wg;
            UpdateBoardCard(gameId, wg);

            // Auto-select first game or keep current
            if (_activeGame == null)
                ShowGame(wg);
        }
    }

    // ── Clock Extraction ──────────────────────────────────────────────────────
    private void ExtractClocks(WatchedGame wg, PgnParser.PgnGame game)
    {
        long whiteMs = 0, blackMs = 0;
        foreach (var move in game.Moves)
        {
            if (move.ClkAnnotation != null)
            {
                long ms = PgnParser.ClkToSeconds(move.ClkAnnotation) * 1000L;
                // Alternate assignment: odd moves = white, even = black
                // Actually: last %clk for white = white's remaining time after their move
                // We take the last values seen for each color
            }
        }

        // Simpler: take last two moves' clocks
        if (game.Moves.Count >= 2)
        {
            var lastMove = game.Moves[game.Moves.Count - 1];
            var prevMove = game.Moves[game.Moves.Count - 2];

            // Determine which is white's and which is black's based on move parity
            bool lastIsBlack = game.Moves.Count % 2 == 0;
            if (lastMove.ClkAnnotation != null)
            {
                long ms = PgnParser.ClkToSeconds(lastMove.ClkAnnotation) * 1000L;
                if (lastIsBlack) blackMs = ms; else whiteMs = ms;
            }
            if (prevMove.ClkAnnotation != null)
            {
                long ms = PgnParser.ClkToSeconds(prevMove.ClkAnnotation) * 1000L;
                if (lastIsBlack) whiteMs = ms; else blackMs = ms;
            }
        }

        wg.WhiteMs = whiteMs;
        wg.BlackMs = blackMs;
    }

    // ── Board List ────────────────────────────────────────────────────────────
    private void ClearBoardList()
    {
        foreach (Transform child in boardListContainer)
            Destroy(child.gameObject);
    }

    private void UpdateBoardCard(string gameId, WatchedGame wg)
    {
        // Find or create card
        Transform existing = boardListContainer.Find(gameId);
        if (existing == null)
        {
            var card = Instantiate(boardCardPrefab, boardListContainer);
            card.name = gameId;
            existing  = card.transform;
        }

        var ui = existing.GetComponent<WatchBoardCardUI>();
        ui?.Set(wg.White, wg.Black, wg.CurrentFen, wg.Result, () => ShowGame(wg));
    }

    // ── Show Game ─────────────────────────────────────────────────────────────
    private void ShowGame(WatchedGame wg)
    {
        _activeGame = wg;

        if (whiteName != null) whiteName.text = wg.White;
        if (blackName != null) blackName.text = wg.Black;
        if (resultText != null) resultText.text = wg.Result;

        activeBoardRenderer?.SetPosition(wg.CurrentFen, true, null);
        moveList?.BuildFrom(wg.Tree, wg.LastNodeId, null);

        // Start/update clock
        _clock?.Dispose();
        if (wg.WhiteMs > 0 || wg.BlackMs > 0)
        {
            var board = new ChessBoard(wg.CurrentFen);
            _clock = new ChessClock(wg.WhiteMs, wg.BlackMs);
            _clock.OnTick += OnClockTick;

            if (wg.Result == "*") // game in progress
                _clock.Start(board.Turn);
        }

        UpdateClockDisplay(wg.WhiteMs, wg.BlackMs);
    }

    private void OnClockTick(long wMs, long bMs, char active)
    {
        UnityMainThreadDispatcher.Enqueue(() => UpdateClockDisplay(wMs, bMs));
    }

    private void UpdateClockDisplay(long wMs, long bMs)
    {
        if (whiteClk != null) whiteClk.text = wMs > 0 ? ChessClock.FormatMs(wMs) : "--:--";
        if (blackClk != null) blackClk.text = bMs > 0 ? ChessClock.FormatMs(bMs) : "--:--";
    }
}

/// <summary>Mini board card for the board list panel.</summary>
public class WatchBoardCardUI : MonoBehaviour
{
    [SerializeField] private Board2DRenderer miniBoard;
    [SerializeField] private TMP_Text        whiteLabel;
    [SerializeField] private TMP_Text        blackLabel;
    [SerializeField] private TMP_Text        resultLabel;
    [SerializeField] private Button          clickArea;

    public void Set(string white, string black, string fen, string result, System.Action onClick)
    {
        if (whiteLabel  != null) whiteLabel.text  = white;
        if (blackLabel  != null) blackLabel.text  = black;
        if (resultLabel != null) resultLabel.text = result != "*" ? result : "";
        miniBoard?.SetPosition(fen, true, null);
        clickArea?.onClick.RemoveAllListeners();
        clickArea?.onClick.AddListener(() => onClick?.Invoke());
    }
}
