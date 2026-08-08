using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

/// <summary>
/// 2D board renderer — port of src/pages/Board2D.tsx (chessground web component).
///
/// Renders a 2D chess position as a flat image using UnityEngine.UI.Image
/// components. Used as:
///   - Fallback when 3D is toggled off
///   - Preview board in AnalyzeScreen landing panel
///   - Mini boards in WatchDetailsScreen
///   - Board in StatsScreen (last puzzle)
///
/// Layout: 8×8 grid of square images with piece icons (TextMeshPro Unicode glyphs
/// or Sprite images). Arrows rendered as UI Line elements.
/// </summary>
public class Board2DRenderer : MonoBehaviour
{
    [Header("Square")]
    [SerializeField] private Color lightSquareColor = new Color(0.93f, 0.84f, 0.67f);
    [SerializeField] private Color darkSquareColor  = new Color(0.71f, 0.53f, 0.39f);
    [SerializeField] private Color selectedColor    = new Color(0.18f, 0.71f, 0.18f, 0.6f);
    [SerializeField] private Color legalMoveColor   = new Color(0f, 0f, 0f, 0.2f);

    [Header("Piece Sprites (optional — falls back to Unicode)")]
    [SerializeField] private Sprite[] pieceSprites; // wK wQ wR wB wN wP bK bQ bR bB bN bP

    [Header("Refs")]
    [SerializeField] private RectTransform squareGrid;
    [SerializeField] private GameObject    squarePrefab;
    [SerializeField] private GameObject    piecePrefab;

    // ── State ─────────────────────────────────────────────────────────────────
    private readonly Image[]    _squares    = new Image[64];
    private readonly TMP_Text[] _pieceTexts = new TMP_Text[64];
    private string   _fen;
    private bool     _whiteAtBottom = true;

    // ── Arrow type ─────────────────────────────────────────────────────────────
    public class Arrow
    {
        public string From;
        public string To;
        public Color  Color = Color.green;
    }

    // ── Init ───────────────────────────────────────────────────────────────────
    private void Awake()
    {
        BuildGrid();
    }

    private void BuildGrid()
    {
        if (squareGrid == null) return;

        // 8×8 squares
        for (int rank = 0; rank < 8; rank++)
        {
            for (int file = 0; file < 8; file++)
            {
                int idx = rank * 8 + file;
                var obj = Instantiate(squarePrefab, squareGrid);
                var img = obj.GetComponent<Image>();
                img.color = (rank + file) % 2 == 0 ? darkSquareColor : lightSquareColor;
                _squares[idx] = img;

                var text = obj.GetComponentInChildren<TMP_Text>();
                if (text != null) _pieceTexts[idx] = text;
            }
        }
    }

    // ── Public API ────────────────────────────────────────────────────────────

    public void SetPosition(string fen, bool whiteAtBottom, List<Arrow> arrows)
    {
        _fen          = fen;
        _whiteAtBottom = whiteAtBottom;
        RenderFen(fen);
        // TODO: render arrows as UI line graphics
    }

    // ── FEN → Board ────────────────────────────────────────────────────────────
    private void RenderFen(string fen)
    {
        if (string.IsNullOrEmpty(fen)) return;
        string placement = fen.Split(' ')[0];

        // Clear
        for (int i = 0; i < 64; i++)
            if (_pieceTexts[i] != null) _pieceTexts[i].text = "";

        // Parse
        int rank = 7, file = 0;
        foreach (char c in placement)
        {
            if (c == '/') { rank--; file = 0; continue; }
            if (char.IsDigit(c)) { file += c - '0'; continue; }

            // Map to display rank/file
            int dispRank = _whiteAtBottom ? rank : 7 - rank;
            int dispFile = _whiteAtBottom ? file : 7 - file;
            int idx = dispRank * 8 + dispFile;

            if (idx >= 0 && idx < 64 && _pieceTexts[idx] != null)
                _pieceTexts[idx].text = PieceToUnicode(c);

            file++;
        }
    }

    // ── Unicode chess pieces ───────────────────────────────────────────────────
    private static string PieceToUnicode(char c) => c switch
    {
        'K' => "♔", 'Q' => "♕", 'R' => "♖", 'B' => "♗", 'N' => "♘", 'P' => "♙",
        'k' => "♚", 'q' => "♛", 'r' => "♜", 'b' => "♝", 'n' => "♞", 'p' => "♟",
        _   => ""
    };
}
