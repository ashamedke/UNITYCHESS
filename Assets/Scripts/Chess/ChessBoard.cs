using System;
using System.Collections.Generic;

/// <summary>
/// Pure C# port of chess.js — all chess rules, move generation, validation.
/// No Unity dependencies — can be unit-tested standalone.
/// 
/// Ported from:  chess.js by Jeff Hlywa
/// Web source:   src/engine/game.js  (wraps chess.js)
/// </summary>
public class ChessBoard
{
    // ── Constants ─────────────────────────────────────────────────────────────
    public const string START_FEN = "rnbqkbnr/pppppppp/8/8/8/8/PPPPPPPP/RNBQKBNR w KQkq - 0 1";

    public const char WHITE = 'w';
    public const char BLACK = 'b';

    // Piece type chars (uppercase = white, lowercase = black in FEN)
    public const char PAWN   = 'p';
    public const char KNIGHT = 'n';
    public const char BISHOP = 'b';
    public const char ROOK   = 'r';
    public const char QUEEN  = 'q';
    public const char KING   = 'k';

    // Move flags
    public const int FLAG_NORMAL    = 0x01;
    public const int FLAG_CAPTURE   = 0x02;
    public const int FLAG_BIG_PAWN  = 0x04;
    public const int FLAG_EP        = 0x08; // en passant
    public const int FLAG_PROMO     = 0x10;
    public const int FLAG_KSIDE     = 0x20; // kingside castle
    public const int FLAG_QSIDE     = 0x40; // queenside castle

    // ── Board Representation ──────────────────────────────────────────────────
    // 0x88 board: 128 squares, upper 4 bits = rank, lower 4 bits = file
    // Valid squares: index & 0x88 == 0
    private readonly char[]   _board      = new char[128]; // piece at each square ('\0' = empty)
    private readonly char[]   _boardColor = new char[128]; // 'w','b' or '\0'

    // ── Game State ────────────────────────────────────────────────────────────
    public  char   Turn           { get; private set; }  = WHITE;
    public  int    HalfMoves      { get; private set; }  = 0;
    public  int    MoveNumber     { get; private set; }  = 1;
    public  int    EpSquare       { get; private set; }  = -1;
    private string _castlingRights = "KQkq"; // current castling availability
    private int    _castlingFlags; // bitmask

    // King positions (cached for fast check detection)
    private int _wKingPos;
    private int _bKingPos;

    // History stack for undo
    private readonly Stack<HistoryEntry> _history = new Stack<HistoryEntry>();

    // ── Algebraic ↔ 0x88 lookup ───────────────────────────────────────────────
    // "a1" = 0, "h8" = 119 in 0x88
    private static readonly Dictionary<string, int> _algToIdx = BuildAlgToIdx();
    private static readonly string[] _idxToAlg = BuildIdxToAlg();

    // ── Piece move offsets ────────────────────────────────────────────────────
    private static readonly int[] KNIGHT_OFFSETS = { -33, -31, -18, -14, 14, 18, 31, 33 };
    private static readonly int[] BISHOP_OFFSETS = { -17, -15,  17,  15 };
    private static readonly int[] ROOK_OFFSETS   = { -16,   1,  16,  -1 };
    private static readonly int[] QUEEN_OFFSETS  = { -17, -16, -15,   1,  17,  16,  15,  -1 };
    private static readonly int[] KING_OFFSETS   = { -17, -16, -15,   1,  17,  16,  15,  -1 };

    // ── Constructor ───────────────────────────────────────────────────────────
    public ChessBoard() { Load(START_FEN); }
    public ChessBoard(string fen) { Load(fen); }

    // ── Load FEN ──────────────────────────────────────────────────────────────
    public void Load(string fen)
    {
        Array.Clear(_board, 0, 128);
        Array.Clear(_boardColor, 0, 128);
        _history.Clear();
        EpSquare = -1;
        _castlingFlags = 0;

        var parts = fen.Trim().Split(' ');
        if (parts.Length < 4) throw new ArgumentException("Invalid FEN: " + fen);

        // 1. Piece placement
        int rank = 7, file = 0;
        foreach (char c in parts[0])
        {
            if (c == '/')
            {
                rank--;
                file = 0;
            }
            else if (char.IsDigit(c))
            {
                file += c - '0';
            }
            else
            {
                int sq = rank * 16 + file;
                char type  = char.ToLower(c);
                char color = char.IsUpper(c) ? WHITE : BLACK;
                _board[sq]      = type;
                _boardColor[sq] = color;
                if (type == KING)
                {
                    if (color == WHITE) _wKingPos = sq;
                    else                _bKingPos = sq;
                }
                file++;
            }
        }

        // 2. Active color
        Turn = parts[1][0];

        // 3. Castling
        _castlingRights = parts[2];
        if (_castlingRights.Contains("K")) _castlingFlags |= 0x01;
        if (_castlingRights.Contains("Q")) _castlingFlags |= 0x02;
        if (_castlingRights.Contains("k")) _castlingFlags |= 0x04;
        if (_castlingRights.Contains("q")) _castlingFlags |= 0x08;

        // 4. En passant
        if (parts[3] != "-") EpSquare = AlgToIdx(parts[3]);

        // 5-6. Halfmove clock + fullmove number
        HalfMoves  = parts.Length > 4 ? int.Parse(parts[4]) : 0;
        MoveNumber = parts.Length > 5 ? int.Parse(parts[5]) : 1;
    }

    // ── Get FEN ───────────────────────────────────────────────────────────────
    public string Fen()
    {
        var sb = new System.Text.StringBuilder();

        // Piece placement
        for (int rank = 7; rank >= 0; rank--)
        {
            int empty = 0;
            for (int file = 0; file < 8; file++)
            {
                int sq = rank * 16 + file;
                if (_board[sq] == '\0')
                {
                    empty++;
                }
                else
                {
                    if (empty > 0) { sb.Append(empty); empty = 0; }
                    char p = _boardColor[sq] == WHITE
                        ? char.ToUpper(_board[sq])
                        : _board[sq];
                    sb.Append(p);
                }
            }
            if (empty > 0) sb.Append(empty);
            if (rank > 0)  sb.Append('/');
        }

        // Active color
        sb.Append(' '); sb.Append(Turn);

        // Castling
        sb.Append(' ');
        string castle = "";
        if ((_castlingFlags & 0x01) != 0) castle += "K";
        if ((_castlingFlags & 0x02) != 0) castle += "Q";
        if ((_castlingFlags & 0x04) != 0) castle += "k";
        if ((_castlingFlags & 0x08) != 0) castle += "q";
        sb.Append(castle.Length > 0 ? castle : "-");

        // En passant
        sb.Append(' ');
        sb.Append(EpSquare >= 0 ? IdxToAlg(EpSquare) : "-");

        // Halfmove + fullmove
        sb.Append(' '); sb.Append(HalfMoves);
        sb.Append(' '); sb.Append(MoveNumber);

        return sb.ToString();
    }

    // ── Move Generation ───────────────────────────────────────────────────────
    public List<ChessMove> GenerateMoves(bool onlyLegal = true)
    {
        var moves = new List<ChessMove>();

        for (int sq = 0; sq < 128; sq++)
        {
            if ((sq & 0x88) != 0) continue;           // off-board square
            if (_boardColor[sq] != Turn)  continue;   // wrong color

            char piece = _board[sq];
            switch (piece)
            {
                case PAWN:   GeneratePawnMoves(sq, moves);   break;
                case KNIGHT: GenerateLeaperMoves(sq, KNIGHT_OFFSETS, moves); break;
                case BISHOP: GenerateSlidingMoves(sq, BISHOP_OFFSETS, moves); break;
                case ROOK:   GenerateSlidingMoves(sq, ROOK_OFFSETS,   moves); break;
                case QUEEN:  GenerateSlidingMoves(sq, QUEEN_OFFSETS,  moves); break;
                case KING:   GenerateKingMoves(sq, moves); break;
            }
        }

        if (!onlyLegal) return moves;

        // Filter pseudo-legal moves: remove any that leave own king in check
        var legal = new List<ChessMove>();
        foreach (var m in moves)
        {
            ApplyMove(m);
            if (!IsInCheck(Flip(Turn))) // after move, previous turn must not be in check
                legal.Add(m);
            UndoMove();
        }
        return legal;
    }

    // ── Make Move (by ChessMove) ──────────────────────────────────────────────
    /// <summary>
    /// Applies a legal move. Returns false if the move is illegal.
    /// </summary>
    public bool MakeMove(ChessMove move)
    {
        var legal = GenerateMoves();
        ChessMove? matched = null;
        foreach (var m in legal)
        {
            if (m.From == move.From && m.To == move.To &&
                (move.Promotion == '\0' || m.Promotion == move.Promotion))
            {
                matched = m;
                break;
            }
        }
        if (matched == null) return false;
        ApplyMove(matched.Value);
        return true;
    }

    /// <summary>
    /// Convenience: make move from UCI string e.g. "e2e4", "e7e8q".
    /// </summary>
    public bool MakeMove(string uci)
    {
        if (uci == null || uci.Length < 4) return false;
        string from = uci.Substring(0, 2);
        string to   = uci.Substring(2, 2);
        char promo  = uci.Length > 4 ? uci[4] : '\0';
        return MakeMove(new ChessMove
        {
            From = AlgToIdx(from), To = AlgToIdx(to), Promotion = promo
        });
    }

    // ── Undo ──────────────────────────────────────────────────────────────────
    public bool UndoMove()
    {
        if (_history.Count == 0) return false;
        var entry = _history.Pop();

        // Restore moved piece to its origin
        _board[entry.Move.From]      = entry.PieceMoved;
        _boardColor[entry.Move.From] = entry.ColorMoved;
        _board[entry.Move.To]        = '\0';
        _boardColor[entry.Move.To]   = '\0';

        // Restore captured piece
        if ((entry.Move.Flags & FLAG_CAPTURE) != 0)
        {
            int capSq = (entry.Move.Flags & FLAG_EP) != 0
                ? entry.Move.To + (entry.ColorMoved == WHITE ? -16 : 16)
                : entry.Move.To;
            _board[capSq]      = entry.Captured;
            _boardColor[capSq] = Flip(entry.ColorMoved);
        }

        // Undo promotion: pawn was promoted, now restore pawn
        if ((entry.Move.Flags & FLAG_PROMO) != 0)
            _board[entry.Move.From] = PAWN;

        // Undo castling rook
        if ((entry.Move.Flags & FLAG_KSIDE) != 0)
            UndoCastleRook(entry.ColorMoved, true);
        if ((entry.Move.Flags & FLAG_QSIDE) != 0)
            UndoCastleRook(entry.ColorMoved, false);

        // Restore king position cache
        if (entry.PieceMoved == KING)
        {
            if (entry.ColorMoved == WHITE) _wKingPos = entry.Move.From;
            else                           _bKingPos = entry.Move.From;
        }

        // Restore global state
        Turn           = entry.Turn;
        EpSquare       = entry.EpSquare;
        _castlingFlags = entry.CastlingFlags;
        HalfMoves      = entry.HalfMoves;
        MoveNumber     = entry.MoveNumber;
        return true;
    }

    // ── Check / Mate / Draw ───────────────────────────────────────────────────
    public bool IsInCheck()   => IsInCheck(Turn);
    public bool IsCheckmate() => IsInCheck() && GenerateMoves().Count == 0;
    public bool IsStalemate() => !IsInCheck() && GenerateMoves().Count == 0;
    public bool IsDraw()      => IsStalemate() || HalfMoves >= 100;

    // ── Square queries ────────────────────────────────────────────────────────
    public char PieceAt(string alg)      => _board[AlgToIdx(alg)];
    public char PieceColorAt(string alg) => _boardColor[AlgToIdx(alg)];

    // ── SAN → Move ────────────────────────────────────────────────────────────
    /// <summary>Converts a SAN string (e.g. "Nf3", "exd5", "O-O") to a ChessMove.</summary>
    public ChessMove? SanToMove(string san)
    {
        var legal = GenerateMoves();
        foreach (var m in legal)
        {
            if (MoveToSan(m) == san) return m;
        }
        return null;
    }

    /// <summary>Converts an internal ChessMove to SAN notation.</summary>
    public string MoveToSan(ChessMove m)
    {
        if ((m.Flags & FLAG_KSIDE) != 0) return IsCheckOrMateAfter(m, "O-O");
        if ((m.Flags & FLAG_QSIDE) != 0) return IsCheckOrMateAfter(m, "O-O-O");

        string san = "";
        if (m.Piece != PAWN)
            san += char.ToUpper(m.Piece);

        // Disambiguation
        string disambig = Disambiguate(m);
        san += disambig;

        if ((m.Flags & FLAG_CAPTURE) != 0 || (m.Flags & FLAG_EP) != 0)
        {
            if (m.Piece == PAWN) san += IdxToAlg(m.From)[0]; // file letter
            san += "x";
        }

        san += IdxToAlg(m.To);

        if ((m.Flags & FLAG_PROMO) != 0)
            san += "=" + char.ToUpper(m.Promotion);

        return IsCheckOrMateAfter(m, san);
    }

    // ── Algebraic Helpers ─────────────────────────────────────────────────────
    public static int AlgToIdx(string alg)
    {
        if (alg == null || alg.Length < 2) return -1;
        int file = alg[0] - 'a';
        int rank = alg[1] - '1';
        return rank * 16 + file;
    }

    public static string IdxToAlg(int idx)
    {
        int file = idx & 7;
        int rank = idx >> 4;
        return "" + (char)('a' + file) + (char)('1' + rank);
    }

    public char Flip(char color) => color == WHITE ? BLACK : WHITE;

    // ═════════════════════════════════════════════════════════════════════════
    // PRIVATE HELPERS
    // ═════════════════════════════════════════════════════════════════════════

    private bool IsInCheck(char color)
    {
        int kingSq = color == WHITE ? _wKingPos : _bKingPos;
        return IsAttacked(kingSq, Flip(color));
    }

    private bool IsAttacked(int sq, char byColor)
    {
        // Pawns
        int pawnDir = byColor == WHITE ? 1 : -1;
        int[] pawnAttacks = { sq - 16 * pawnDir - 1, sq - 16 * pawnDir + 1 };
        foreach (int a in pawnAttacks)
        {
            if ((a & 0x88) == 0 && _board[a] == PAWN && _boardColor[a] == byColor)
                return true;
        }

        // Knights
        foreach (int off in KNIGHT_OFFSETS)
        {
            int t = sq + off;
            if ((t & 0x88) == 0 && _board[t] == KNIGHT && _boardColor[t] == byColor)
                return true;
        }

        // Bishops / Queens (diagonals)
        foreach (int off in BISHOP_OFFSETS)
        {
            int t = sq + off;
            while ((t & 0x88) == 0)
            {
                if (_board[t] != '\0')
                {
                    if (_boardColor[t] == byColor && (_board[t] == BISHOP || _board[t] == QUEEN))
                        return true;
                    break;
                }
                t += off;
            }
        }

        // Rooks / Queens (straight)
        foreach (int off in ROOK_OFFSETS)
        {
            int t = sq + off;
            while ((t & 0x88) == 0)
            {
                if (_board[t] != '\0')
                {
                    if (_boardColor[t] == byColor && (_board[t] == ROOK || _board[t] == QUEEN))
                        return true;
                    break;
                }
                t += off;
            }
        }

        // King (adjacent squares)
        foreach (int off in KING_OFFSETS)
        {
            int t = sq + off;
            if ((t & 0x88) == 0 && _board[t] == KING && _boardColor[t] == byColor)
                return true;
        }

        return false;
    }

    private void GeneratePawnMoves(int sq, List<ChessMove> moves)
    {
        int dir     = Turn == WHITE ? 16 : -16;
        int startRk = Turn == WHITE ? 1 : 6;
        int promoRk = Turn == WHITE ? 7 : 0;

        int rank = sq >> 4;

        // Single push
        int fwd = sq + dir;
        if ((fwd & 0x88) == 0 && _board[fwd] == '\0')
        {
            int destRank = fwd >> 4;
            if (destRank == promoRk)
                AddPromos(sq, fwd, FLAG_NORMAL | FLAG_PROMO, moves);
            else
            {
                AddMove(sq, fwd, FLAG_NORMAL, moves);
                // Double push from starting rank
                if (rank == startRk)
                {
                    int dbl = sq + dir * 2;
                    if (_board[dbl] == '\0')
                        AddMove(sq, dbl, FLAG_BIG_PAWN, moves);
                }
            }
        }

        // Captures
        int[] captureOffsets = { dir - 1, dir + 1 };
        foreach (int cOff in captureOffsets)
        {
            int t = sq + cOff;
            if ((t & 0x88) == 0)
            {
                if (_boardColor[t] == Flip(Turn))
                {
                    int destRank = t >> 4;
                    if (destRank == promoRk)
                        AddPromos(sq, t, FLAG_CAPTURE | FLAG_PROMO, moves);
                    else
                        AddMove(sq, t, FLAG_CAPTURE, moves);
                }
                else if (t == EpSquare)
                {
                    AddMove(sq, t, FLAG_EP | FLAG_CAPTURE, moves);
                }
            }
        }
    }

    private void GenerateLeaperMoves(int sq, int[] offsets, List<ChessMove> moves)
    {
        foreach (int off in offsets)
        {
            int t = sq + off;
            if ((t & 0x88) != 0) continue;
            if (_boardColor[t] == Turn) continue;
            int flags = _board[t] != '\0' ? FLAG_CAPTURE : FLAG_NORMAL;
            AddMove(sq, t, flags, moves);
        }
    }

    private void GenerateSlidingMoves(int sq, int[] offsets, List<ChessMove> moves)
    {
        foreach (int off in offsets)
        {
            int t = sq + off;
            while ((t & 0x88) == 0)
            {
                if (_board[t] != '\0')
                {
                    if (_boardColor[t] != Turn)
                        AddMove(sq, t, FLAG_CAPTURE, moves);
                    break;
                }
                AddMove(sq, t, FLAG_NORMAL, moves);
                t += off;
            }
        }
    }

    private void GenerateKingMoves(int sq, List<ChessMove> moves)
    {
        GenerateLeaperMoves(sq, KING_OFFSETS, moves);

        // Castling — king-side
        if (Turn == WHITE && (_castlingFlags & 0x01) != 0)
        {
            if (_board[0x05] == '\0' && _board[0x06] == '\0' &&
                !IsAttacked(0x04, BLACK) && !IsAttacked(0x05, BLACK) && !IsAttacked(0x06, BLACK))
                AddMove(0x04, 0x06, FLAG_KSIDE, moves);
        }
        else if (Turn == BLACK && (_castlingFlags & 0x04) != 0)
        {
            if (_board[0x75] == '\0' && _board[0x76] == '\0' &&
                !IsAttacked(0x74, WHITE) && !IsAttacked(0x75, WHITE) && !IsAttacked(0x76, WHITE))
                AddMove(0x74, 0x76, FLAG_KSIDE, moves);
        }

        // Castling — queen-side
        if (Turn == WHITE && (_castlingFlags & 0x02) != 0)
        {
            if (_board[0x03] == '\0' && _board[0x02] == '\0' && _board[0x01] == '\0' &&
                !IsAttacked(0x04, BLACK) && !IsAttacked(0x03, BLACK) && !IsAttacked(0x02, BLACK))
                AddMove(0x04, 0x02, FLAG_QSIDE, moves);
        }
        else if (Turn == BLACK && (_castlingFlags & 0x08) != 0)
        {
            if (_board[0x73] == '\0' && _board[0x72] == '\0' && _board[0x71] == '\0' &&
                !IsAttacked(0x74, WHITE) && !IsAttacked(0x73, WHITE) && !IsAttacked(0x72, WHITE))
                AddMove(0x74, 0x72, FLAG_QSIDE, moves);
        }
    }

    private void AddMove(int from, int to, int flags, List<ChessMove> moves)
    {
        moves.Add(new ChessMove
        {
            From      = from,
            To        = to,
            Piece     = _board[from],
            Color     = _boardColor[from],
            Captured  = _board[to],
            Promotion = '\0',
            Flags     = flags
        });
    }

    private void AddPromos(int from, int to, int flags, List<ChessMove> moves)
    {
        foreach (char promo in new[] { QUEEN, ROOK, BISHOP, KNIGHT })
        {
            moves.Add(new ChessMove
            {
                From = from, To = to,
                Piece = _board[from], Color = _boardColor[from],
                Captured = _board[to], Promotion = promo, Flags = flags
            });
        }
    }

    private void ApplyMove(ChessMove m)
    {
        // Save state for undo
        _history.Push(new HistoryEntry
        {
            Move          = m,
            PieceMoved    = _board[m.From],
            ColorMoved    = _boardColor[m.From],
            Captured      = _board[m.To],
            Turn          = Turn,
            EpSquare      = EpSquare,
            CastlingFlags = _castlingFlags,
            HalfMoves     = HalfMoves,
            MoveNumber    = MoveNumber
        });

        // Move the piece
        _board[m.To]      = m.Piece;
        _boardColor[m.To] = m.Color;
        _board[m.From]    = '\0';
        _boardColor[m.From] = '\0';

        // En passant capture
        if ((m.Flags & FLAG_EP) != 0)
        {
            int capSq = m.To + (m.Color == WHITE ? -16 : 16);
            _board[capSq]      = '\0';
            _boardColor[capSq] = '\0';
        }

        // Promotion
        if ((m.Flags & FLAG_PROMO) != 0)
            _board[m.To] = m.Promotion;

        // Castling rook
        if ((m.Flags & FLAG_KSIDE) != 0) MoveCastleRook(m.Color, true);
        if ((m.Flags & FLAG_QSIDE) != 0) MoveCastleRook(m.Color, false);

        // Update king position cache
        if (m.Piece == KING)
        {
            if (m.Color == WHITE) _wKingPos = m.To;
            else                  _bKingPos = m.To;

            // Revoke all castling rights for this color
            if (m.Color == WHITE) _castlingFlags &= ~0x03;
            else                  _castlingFlags &= ~0x0c;
        }

        // Revoke castling if rook moved or was captured
        RevokeCastling(m.From);
        RevokeCastling(m.To);

        // Set en passant square
        EpSquare = (m.Flags & FLAG_BIG_PAWN) != 0
            ? m.To + (m.Color == WHITE ? -16 : 16)
            : -1;

        // Halfmove clock
        HalfMoves = (m.Piece == PAWN || (m.Flags & FLAG_CAPTURE) != 0) ? 0 : HalfMoves + 1;

        // Full move number
        if (Turn == BLACK) MoveNumber++;
        Turn = Flip(Turn);
    }

    private void MoveCastleRook(char color, bool kingSide)
    {
        int rookFrom, rookTo;
        if (color == WHITE)
        {
            rookFrom = kingSide ? 0x07 : 0x00;
            rookTo   = kingSide ? 0x05 : 0x03;
        }
        else
        {
            rookFrom = kingSide ? 0x77 : 0x70;
            rookTo   = kingSide ? 0x75 : 0x73;
        }
        _board[rookTo]      = _board[rookFrom];
        _boardColor[rookTo] = _boardColor[rookFrom];
        _board[rookFrom]    = '\0';
        _boardColor[rookFrom] = '\0';
    }

    private void UndoCastleRook(char color, bool kingSide)
    {
        int rookFrom, rookTo;
        if (color == WHITE)
        {
            rookFrom = kingSide ? 0x07 : 0x00;
            rookTo   = kingSide ? 0x05 : 0x03;
        }
        else
        {
            rookFrom = kingSide ? 0x77 : 0x70;
            rookTo   = kingSide ? 0x75 : 0x73;
        }
        _board[rookFrom]    = _board[rookTo];
        _boardColor[rookFrom] = _boardColor[rookTo];
        _board[rookTo]      = '\0';
        _boardColor[rookTo] = '\0';
    }

    private void RevokeCastling(int sq)
    {
        // a1 rook
        if (sq == 0x00) _castlingFlags &= ~0x02;
        // h1 rook
        else if (sq == 0x07) _castlingFlags &= ~0x01;
        // a8 rook
        else if (sq == 0x70) _castlingFlags &= ~0x08;
        // h8 rook
        else if (sq == 0x77) _castlingFlags &= ~0x04;
    }

    private string IsCheckOrMateAfter(ChessMove m, string san)
    {
        ApplyMove(m);
        bool check = IsInCheck();
        bool mate  = check && GenerateMoves().Count == 0;
        UndoMove();

        if (mate)  return san + "#";
        if (check) return san + "+";
        return san;
    }

    private string Disambiguate(ChessMove m)
    {
        var legal = GenerateMoves();
        bool sameFile = false, sameRank = false, needsDisambig = false;

        foreach (var other in legal)
        {
            if (other.To != m.To || other.From == m.From || other.Piece != m.Piece)
                continue;
            needsDisambig = true;
            if ((other.From & 7) == (m.From & 7)) sameFile = true;
            if ((other.From >> 4) == (m.From >> 4)) sameRank = true;
        }

        if (!needsDisambig) return "";
        if (!sameFile) return "" + (char)('a' + (m.From & 7));
        if (!sameRank) return "" + (char)('1' + (m.From >> 4));
        return IdxToAlg(m.From);
    }

    // ── Static Init ───────────────────────────────────────────────────────────
    private static Dictionary<string, int> BuildAlgToIdx()
    {
        var d = new Dictionary<string, int>(64);
        for (int r = 0; r < 8; r++)
            for (int f = 0; f < 8; f++)
                d["" + (char)('a' + f) + (char)('1' + r)] = r * 16 + f;
        return d;
    }

    private static string[] BuildIdxToAlg()
    {
        var a = new string[128];
        for (int r = 0; r < 8; r++)
            for (int f = 0; f < 8; f++)
                a[r * 16 + f] = "" + (char)('a' + f) + (char)('1' + r);
        return a;
    }

    // ── History entry ──────────────────────────────────────────────────────────
    private struct HistoryEntry
    {
        public ChessMove Move;
        public char      PieceMoved;
        public char      ColorMoved;
        public char      Captured;
        public char      Turn;
        public int       EpSquare;
        public int       CastlingFlags;
        public int       HalfMoves;
        public int       MoveNumber;
    }
}
