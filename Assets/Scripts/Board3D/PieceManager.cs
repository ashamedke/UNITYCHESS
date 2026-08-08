using System.Collections;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Piece manager — port of src/engine/pieces.js.
///
/// Maintains a Dictionary mapping 0x88 square index → piece GameObject.
/// On each FEN change, diffs the board state and:
///   - Instantiates new pieces (PieceManager.CreatePiece)
///   - Removes captured pieces
///   - Triggers MoveAnimator for sliding animations
/// </summary>
public class PieceManager : MonoBehaviour
{
    // ── Prefab references (assign in Inspector) ───────────────────────────────
    [Header("White Piece Prefabs")]
    [SerializeField] private GameObject wKing;
    [SerializeField] private GameObject wQueen;
    [SerializeField] private GameObject wRook;
    [SerializeField] private GameObject wBishop;
    [SerializeField] private GameObject wKnight;
    [SerializeField] private GameObject wPawn;

    [Header("Black Piece Prefabs")]
    [SerializeField] private GameObject bKing;
    [SerializeField] private GameObject bQueen;
    [SerializeField] private GameObject bRook;
    [SerializeField] private GameObject bBishop;
    [SerializeField] private GameObject bKnight;
    [SerializeField] private GameObject bPawn;

    [Header("References")]
    [SerializeField] private BoardScene3D  boardScene;
    [SerializeField] private MoveAnimator  animator;
    [SerializeField] private CapturedRack  whiteRack; // captured white pieces display
    [SerializeField] private CapturedRack  blackRack; // captured black pieces display

    // ── State ──────────────────────────────────────────────────────────────────
    private readonly Dictionary<int, GameObject> _pieces = new Dictionary<int, GameObject>();
    private ChessBoard _previousBoard;
    private bool _isFlipped;

    // ── Public API ────────────────────────────────────────────────────────────

    /// <summary>Clears all pieces and instantiates from a fresh FEN.</summary>
    public void LoadPosition(ChessBoard board, bool flipped = false)
    {
        _isFlipped = flipped;
        ClearAll();
        _previousBoard = null;

        // Place pieces for all 64 squares
        for (int sq = 0; sq < 128; sq++)
        {
            if ((sq & 0x88) != 0) continue;
            string alg   = ChessBoard.IdxToAlg(sq);
            char   piece = board.PieceAt(alg);
            char   color = board.PieceColorAt(alg);
            if (piece == '\0') continue;
            SpawnPiece(sq, piece, color);
        }

        _previousBoard = board;
    }

    /// <summary>
    /// Applies a move with animation.
    /// Call this BEFORE updating the ChessBoard so we can read the old state.
    /// </summary>
    public void AnimateMove(ChessMove move, System.Action onComplete = null)
    {
        GameObject movingPiece = GetPieceAt(move.From);
        if (movingPiece == null) { onComplete?.Invoke(); return; }

        Vector3 targetPos = boardScene.SquareToWorld(move.To, _isFlipped);

        // Handle capture
        GameObject capturedPiece = null;
        if (move.IsCapture)
        {
            int capSq = move.IsEnPassant
                ? move.To + (move.Color == ChessBoard.WHITE ? -16 : 16)
                : move.To;
            capturedPiece = GetPieceAt(capSq);
            if (capturedPiece != null) _pieces.Remove(capSq);
        }

        // Handle castling rook
        GameObject castleRook = null;
        Vector3 rookTarget = Vector3.zero;
        if (move.IsCastle)
        {
            int rookFrom, rookTo;
            if (move.Color == ChessBoard.WHITE)
            {
                rookFrom = move.IsKingside ? 0x07 : 0x00;
                rookTo   = move.IsKingside ? 0x05 : 0x03;
            }
            else
            {
                rookFrom = move.IsKingside ? 0x77 : 0x70;
                rookTo   = move.IsKingside ? 0x75 : 0x73;
            }
            castleRook = GetPieceAt(rookFrom);
            rookTarget = boardScene.SquareToWorld(rookTo, _isFlipped);
            _pieces.Remove(rookFrom);
            _pieces[rookTo] = castleRook;
        }

        // Update piece dictionary
        _pieces.Remove(move.From);
        _pieces[move.To] = movingPiece;

        // Determine capture side rack
        CapturedRack rack = move.Color == ChessBoard.WHITE ? blackRack : whiteRack;

        // Fire animator
        animator?.AnimateMove(
            movingPiece, targetPos,
            capturedPiece, rack,
            castleRook, rookTarget,
            move.IsPromotion ? move.Promotion : '\0',
            GetPrefabForPromo(move.Promotion, move.Color),
            onComplete
        );
    }

    public GameObject GetPieceAt(int sq88) =>
        _pieces.TryGetValue(sq88, out var go) ? go : null;

    public GameObject GetPieceAt(string alg) =>
        GetPieceAt(ChessBoard.AlgToIdx(alg));

    // ── Internal ───────────────────────────────────────────────────────────────

    private void SpawnPiece(int sq88, char type, char color)
    {
        GameObject prefab = GetPrefab(type, color);
        if (prefab == null) return;

        Vector3 pos = boardScene.SquareToWorld(sq88, _isFlipped);
        GameObject go = Instantiate(prefab, pos, Quaternion.identity, transform);
        go.name = color + "" + char.ToUpper(type) + "@" + ChessBoard.IdxToAlg(sq88);

        // Layer for raycasting
        go.layer = LayerMask.NameToLayer("ChessPiece");
        foreach (Transform child in go.transform)
            child.gameObject.layer = go.layer;

        _pieces[sq88] = go;
    }

    private void ClearAll()
    {
        foreach (var go in _pieces.Values)
            if (go != null) Destroy(go);
        _pieces.Clear();
    }

    private GameObject GetPrefab(char type, char color)
    {
        bool isWhite = color == ChessBoard.WHITE;
        return type switch
        {
            ChessBoard.KING   => isWhite ? wKing   : bKing,
            ChessBoard.QUEEN  => isWhite ? wQueen  : bQueen,
            ChessBoard.ROOK   => isWhite ? wRook   : bRook,
            ChessBoard.BISHOP => isWhite ? wBishop : bBishop,
            ChessBoard.KNIGHT => isWhite ? wKnight : bKnight,
            ChessBoard.PAWN   => isWhite ? wPawn   : bPawn,
            _                 => null
        };
    }

    private GameObject GetPrefabForPromo(char type, char color)
        => GetPrefab(type, color);
}
