/// <summary>
/// Represents a single chess move — both the source/destination squares
/// and all contextual flags needed for animation, SAN generation, and undo.
/// Mirrors the move objects produced by chess.js's verbose move API.
/// </summary>
public struct ChessMove
{
    /// <summary>Source square index in 0x88 notation.</summary>
    public int  From;
    /// <summary>Destination square index in 0x88 notation.</summary>
    public int  To;
    /// <summary>Piece type char ('p','n','b','r','q','k').</summary>
    public char Piece;
    /// <summary>Color of the moving piece ('w' or 'b').</summary>
    public char Color;
    /// <summary>Captured piece type ('\0' if no capture).</summary>
    public char Captured;
    /// <summary>Promotion piece type ('\0' if not a promotion).</summary>
    public char Promotion;
    /// <summary>Bitmask of ChessBoard.FLAG_* constants.</summary>
    public int  Flags;

    // ── Convenience properties ────────────────────────────────────────────────

    public string FromAlg => ChessBoard.IdxToAlg(From);
    public string ToAlg   => ChessBoard.IdxToAlg(To);

    /// <summary>UCI string e.g. "e2e4", "e7e8q".</summary>
    public string Uci => FromAlg + ToAlg + (Promotion != '\0' ? Promotion.ToString() : "");

    public bool IsCapture   => (Flags & ChessBoard.FLAG_CAPTURE) != 0;
    public bool IsPromotion => (Flags & ChessBoard.FLAG_PROMO)   != 0;
    public bool IsEnPassant => (Flags & ChessBoard.FLAG_EP)      != 0;
    public bool IsKingside  => (Flags & ChessBoard.FLAG_KSIDE)   != 0;
    public bool IsQueenside => (Flags & ChessBoard.FLAG_QSIDE)   != 0;
    public bool IsCastle    => IsKingside || IsQueenside;

    public override string ToString() => Uci;
}
