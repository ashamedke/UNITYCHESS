using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

/// <summary>
/// Square highlight overlay — renders colored dots/rings on 3D board squares.
/// Used for: selected square, legal move dots, hint glow, last-move highlight.
/// Port of chessground's highlight system.
/// </summary>
public class SquareHighlight : MonoBehaviour
{
    public enum Type { Selected, LegalMove, LegalCapture, LastMove, HintFrom, HintTo }

    [Header("Highlight Prefabs")]
    [SerializeField] private GameObject selectedPrefab;     // solid colored square
    [SerializeField] private GameObject legalMovePrefab;    // small dot
    [SerializeField] private GameObject legalCapturePrefab; // ring on capture square
    [SerializeField] private GameObject lastMovePrefab;     // subtle tint
    [SerializeField] private GameObject hintPrefab;         // green glow

    [Header("References")]
    [SerializeField] private BoardScene3D boardScene;

    private readonly List<GameObject> _active = new List<GameObject>();

    // ── Public API ────────────────────────────────────────────────────────────
    public void HighlightSquare(int sq88, Type type)
    {
        GameObject prefab = type switch
        {
            Type.Selected     => selectedPrefab,
            Type.LegalMove    => legalMovePrefab,
            Type.LegalCapture => legalCapturePrefab,
            Type.LastMove     => lastMovePrefab,
            Type.HintFrom     => hintPrefab,
            Type.HintTo       => hintPrefab,
            _                 => legalMovePrefab
        };

        if (prefab == null || boardScene == null) return;

        Vector3 pos = boardScene.SquareCenter(sq88);
        var go = Instantiate(prefab, pos, Quaternion.identity, transform);
        _active.Add(go);
    }

    public void ClearHighlights()
    {
        foreach (var go in _active)
            if (go != null) Destroy(go);
        _active.Clear();
    }
}

/// <summary>
/// Ghost piece — semi-transparent copy of a piece that follows the user's finger
/// during drag-and-drop input. Sits above the board in world space.
/// </summary>
public class GhostPiece : MonoBehaviour
{
    [SerializeField] private Camera  uiCamera;
    [SerializeField] private float   elevation = 2f; // world units above board

    private GameObject _ghost;
    private bool       _visible;

    public bool IsVisible => _visible;

    public void Show(GameObject sourcePiece, Vector2 screenPos)
    {
        Hide();
        if (sourcePiece == null) return;

        _ghost = Instantiate(sourcePiece, transform);
        _ghost.transform.localScale = sourcePiece.transform.localScale * 1.15f;

        // Make semi-transparent
        foreach (var r in _ghost.GetComponentsInChildren<Renderer>())
        {
            var mat = r.material;
            Color c = mat.color; c.a = 0.6f; mat.color = c;
        }

        _visible = true;
        SetScreenPosition(screenPos);
    }

    public void SetScreenPosition(Vector2 screenPos)
    {
        if (!_visible || _ghost == null) return;

        Ray ray = Camera.main.ScreenPointToRay(screenPos);
        // Place ghost on a horizontal plane at board height + elevation
        float t = elevation / Mathf.Max(0.001f, ray.direction.y == 0 ? 0.001f : -ray.direction.y);
        _ghost.transform.position = ray.GetPoint(t > 0 ? t : 5f);
    }

    public void Hide()
    {
        if (_ghost != null) Destroy(_ghost);
        _ghost   = null;
        _visible = false;
    }
}

/// <summary>
/// Promotion dialog — appears when a pawn reaches the last rank.
/// Shows 4 piece options (Q/R/B/N) as buttons.
/// </summary>
public class PromotionDialog : MonoBehaviour
{
    public static PromotionDialog Instance { get; private set; }

    [SerializeField] private GameObject panel;
    [SerializeField] private Button     btnQueen;
    [SerializeField] private Button     btnRook;
    [SerializeField] private Button     btnBishop;
    [SerializeField] private Button     btnKnight;

    private System.Action<string, string, char> _callback;
    private string _from, _to;

    private void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
        panel?.SetActive(false);
    }

    public void Show(string from, string to, char color, System.Action<string, string, char> callback)
    {
        _from = from; _to = to; _callback = callback;
        panel?.SetActive(true);

        btnQueen?.onClick.AddListener(() => Choose(ChessBoard.QUEEN));
        btnRook?.onClick.AddListener(() => Choose(ChessBoard.ROOK));
        btnBishop?.onClick.AddListener(() => Choose(ChessBoard.BISHOP));
        btnKnight?.onClick.AddListener(() => Choose(ChessBoard.KNIGHT));
    }

    private void Choose(char piece)
    {
        panel?.SetActive(false);
        btnQueen?.onClick.RemoveAllListeners();
        btnRook?.onClick.RemoveAllListeners();
        btnBishop?.onClick.RemoveAllListeners();
        btnKnight?.onClick.RemoveAllListeners();
        _callback?.Invoke(_from, _to, piece);
        _callback = null;
    }
}

/// <summary>
/// Captured piece rack — shows captured pieces beside the board.
/// Matches the web app's captured pieces display.
/// </summary>
public class CapturedRack : MonoBehaviour
{
    [SerializeField] private Transform rackRoot;
    [SerializeField] private float     spacing = 0.25f;

    private int _count;

    public Vector3 NextSlotPosition()
    {
        return rackRoot != null
            ? rackRoot.position + rackRoot.right * (_count * spacing)
            : transform.position;
    }

    public void AddCapturedPiece(GameObject piece)
    {
        if (piece == null) return;
        piece.transform.SetParent(rackRoot ?? transform, worldPositionStays: true);
        piece.transform.position = NextSlotPosition();
        piece.transform.localScale *= 0.5f;
        _count++;
    }
}

/// <summary>
/// Evaluation bar — vertical bar showing the engine's centipawn assessment.
/// White advantage = bar tips toward white. Mirrors the eval bar in Analyze.tsx.
/// </summary>
public class EvalBar : MonoBehaviour
{
    [SerializeField] private RectTransform whiteBar;
    [SerializeField] private TMP_Text      evalText;

    private const float MAX_CP = 800f; // clamp to ±8 pawns

    public void SetEval(int cp, bool isMate, int mateIn, char sideToMove)
    {
        string label;
        float  fraction; // 0=black wins, 1=white wins, 0.5=even

        if (isMate)
        {
            bool whiteWins = (mateIn > 0 && sideToMove == ChessBoard.WHITE) ||
                             (mateIn < 0 && sideToMove == ChessBoard.BLACK);
            fraction = whiteWins ? 1f : 0f;
            label    = whiteWins ? $"M{Mathf.Abs(mateIn)}" : $"-M{Mathf.Abs(mateIn)}";
        }
        else
        {
            float clamped = Mathf.Clamp(cp, -MAX_CP, MAX_CP);
            fraction = (clamped + MAX_CP) / (MAX_CP * 2f);
            label    = cp >= 0 ? $"+{cp / 100f:F1}" : $"{cp / 100f:F1}";
        }

        if (whiteBar != null)
        {
            var anchor = whiteBar.anchorMax;
            anchor.y = fraction;
            whiteBar.anchorMax = anchor;
        }

        if (evalText != null) evalText.text = label;
    }
}

/// <summary>
/// Engine lines panel — displays top N PVs from Stockfish.
/// Each line shows depth, eval, and the first 4-5 moves in SAN.
/// </summary>
public class TopEngineLinesPanel : MonoBehaviour
{
    [SerializeField] private Transform container;
    [SerializeField] private GameObject linePrefab;

    public void SetLines(System.Collections.Generic.List<StockfishBridge.PvInfo> pvs,
                         ChessBoard board)
    {
        foreach (Transform child in container)
            Destroy(child.gameObject);

        foreach (var pv in pvs)
        {
            var obj = Instantiate(linePrefab, container);
            var ui  = obj.GetComponent<EngineLineUI>();
            if (ui != null)
            {
                string evalStr = pv.IsMate
                    ? (pv.MateMoves > 0 ? $"M{pv.MateMoves}" : $"-M{Mathf.Abs(pv.MateMoves)}")
                    : (pv.CentiPawns >= 0 ? $"+{pv.CentiPawns / 100f:F1}" : $"{pv.CentiPawns / 100f:F1}");

                // Convert first 5 UCI moves to SAN for display
                string sanLine = UciToSan(pv.PvUci, board, 5);
                ui.Set(evalStr, $"d{pv.Depth}", sanLine);
            }
        }
    }

    private string UciToSan(string pvUci, ChessBoard startBoard, int maxMoves)
    {
        if (string.IsNullOrEmpty(pvUci)) return "";

        var tempBoard = new ChessBoard(startBoard.Fen());
        var ucis = pvUci.Split(' ');
        var sans = new System.Text.StringBuilder();
        int count = 0;

        foreach (string uci in ucis)
        {
            if (count >= maxMoves) break;
            if (string.IsNullOrEmpty(uci)) continue;

            var move = new ChessMove
            {
                From = ChessBoard.AlgToIdx(uci.Substring(0, 2)),
                To   = ChessBoard.AlgToIdx(uci.Substring(2, 2)),
                Promotion = uci.Length > 4 ? uci[4] : '\0'
            };

            // Generate SAN before making the move
            var legalMoves = tempBoard.GenerateMoves();
            ChessMove? matched = null;
            foreach (var m in legalMoves)
            {
                if (m.From == move.From && m.To == move.To &&
                    (move.Promotion == '\0' || m.Promotion == move.Promotion))
                { matched = m; break; }
            }
            if (matched == null) break;

            if (count > 0) sans.Append(' ');
            if (tempBoard.Turn == ChessBoard.WHITE)
                sans.Append(tempBoard.MoveNumber).Append(". ");
            sans.Append(tempBoard.MoveToSan(matched.Value));

            tempBoard.MakeMove(matched.Value);
            count++;
        }

        return sans.ToString();
    }
}

public class EngineLineUI : MonoBehaviour
{
    [SerializeField] private TMP_Text evalText;
    [SerializeField] private TMP_Text depthText;
    [SerializeField] private TMP_Text pvText;

    public void Set(string eval, string depth, string pv)
    {
        if (evalText  != null) evalText.text  = eval;
        if (depthText != null) depthText.text = depth;
        if (pvText    != null) pvText.text    = pv;
    }
}
