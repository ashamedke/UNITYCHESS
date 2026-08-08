using System;
using System.Collections.Generic;
using System.Text;
using System.Text.RegularExpressions;

/// <summary>
/// PGN parser — converts PGN strings (single or multi-game) into structured data.
/// Handles: headers, move text, comments, variations, annotations (%clk, %eval),
/// NAG symbols, result tokens.
/// 
/// Ported from: the PGN import logic in src/pages/Import.tsx and the moveTree
/// construction in src/engine/game.js.
/// </summary>
public static class PgnParser
{
    // ── Public types ──────────────────────────────────────────────────────────

    public class PgnGame
    {
        public Dictionary<string, string> Headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        public List<PgnMove>              Moves   = new List<PgnMove>();
        public string                     Result;

        // Convenience header getters
        public string White  => Headers.GetValueOrDefault("White",  "?");
        public string Black  => Headers.GetValueOrDefault("Black",  "?");
        public string Event_ => Headers.GetValueOrDefault("Event",  "?");
        public string Date_  => Headers.GetValueOrDefault("Date",   "?");
    }

    public class PgnMove
    {
        public string San;
        public string Comment;
        public string ClkAnnotation;   // %clk 1:30:00
        public float  EvalAnnotation;  // %eval -0.54
        public bool   HasEval;
        public List<List<PgnMove>> Variations = new List<List<PgnMove>>();
    }

    // ── Single-game parse ──────────────────────────────────────────────────────
    public static PgnGame Parse(string pgn)
    {
        var game = new PgnGame();
        if (string.IsNullOrWhiteSpace(pgn)) return game;

        int i = 0;
        SkipWhitespace(pgn, ref i);

        // 1. Parse headers  [Key "Value"]
        while (i < pgn.Length && pgn[i] == '[')
        {
            int end = pgn.IndexOf(']', i);
            if (end < 0) break;
            string header = pgn.Substring(i + 1, end - i - 1);
            var m = Regex.Match(header, @"(\w+)\s+""([^""]*)""");
            if (m.Success) game.Headers[m.Groups[1].Value] = m.Groups[2].Value;
            i = end + 1;
            SkipWhitespace(pgn, ref i);
        }

        // 2. Parse move text
        game.Moves = ParseMoveSection(pgn, ref i);
        game.Result = game.Headers.GetValueOrDefault("Result", "*");

        return game;
    }

    // ── Multi-game parse ──────────────────────────────────────────────────────
    public static List<PgnGame> ParseMulti(string pgn)
    {
        var games = new List<PgnGame>();
        // Split on boundaries: a '[' that follows a result token and whitespace
        var sections = Regex.Split(pgn.Trim(), @"(?=\[Event )");
        foreach (var section in sections)
        {
            var s = section.Trim();
            if (s.Length == 0) continue;
            try { games.Add(Parse(s)); }
            catch { /* skip malformed games */ }
        }
        return games;
    }

    // ── Move section parser ────────────────────────────────────────────────────
    private static List<PgnMove> ParseMoveSection(string pgn, ref int i)
    {
        var moves = new List<PgnMove>();
        PgnMove current = null;

        while (i < pgn.Length)
        {
            char c = pgn[i];

            // Skip whitespace
            if (char.IsWhiteSpace(c)) { i++; continue; }

            // Result tokens
            if (pgn.StartsWith("1-0", i) || pgn.StartsWith("0-1", i) ||
                pgn.StartsWith("1/2-1/2", i) || pgn[i] == '*')
            {
                i += pgn.StartsWith("1/2-1/2", i) ? 7 : pgn.StartsWith("1-0", i) || pgn.StartsWith("0-1", i) ? 3 : 1;
                break;
            }

            // Comments  { ... }
            if (c == '{')
            {
                int end = pgn.IndexOf('}', i);
                if (end < 0) break;
                string comment = pgn.Substring(i + 1, end - i - 1).Trim();
                if (current != null) ParseAnnotations(comment, current);
                i = end + 1;
                continue;
            }

            // Variations  ( ... )
            if (c == '(')
            {
                i++; // skip opening paren
                var variation = ParseMoveSection(pgn, ref i);
                if (current != null) current.Variations.Add(variation);
                continue;
            }

            if (c == ')') { i++; break; }

            // NAG  $12 etc — skip
            if (c == '$')
            {
                while (i < pgn.Length && !char.IsWhiteSpace(pgn[i])) i++;
                continue;
            }

            // Move number "12." or "12..." — skip
            if (char.IsDigit(c))
            {
                while (i < pgn.Length && (char.IsDigit(pgn[i]) || pgn[i] == '.')) i++;
                continue;
            }

            // Annotation glyphs ! ? !! ?? !? ?! — skip
            if (c == '!' || c == '?') { i++; continue; }

            // SAN token
            if (char.IsLetter(c) || c == 'O')
            {
                var sb = new StringBuilder();
                while (i < pgn.Length && !char.IsWhiteSpace(pgn[i]) && pgn[i] != '{' && pgn[i] != '(' && pgn[i] != ')')
                    sb.Append(pgn[i++]);
                string san = sb.ToString().TrimEnd(new[]{'!','?'});
                if (IsResultToken(san)) break;
                current = new PgnMove { San = san };
                moves.Add(current);
                continue;
            }

            i++;
        }

        return moves;
    }

    // ── Annotation extraction ─────────────────────────────────────────────────
    private static void ParseAnnotations(string comment, PgnMove move)
    {
        move.Comment = comment;

        // %clk 1:30:00
        var clkMatch = Regex.Match(comment, @"%clk\s+([\d:]+)");
        if (clkMatch.Success) move.ClkAnnotation = clkMatch.Groups[1].Value;

        // %eval -0.54 or %eval +3.21 or %eval #5
        var evalMatch = Regex.Match(comment, @"%eval\s+([+-]?[\d.]+|#[\d-]+)");
        if (evalMatch.Success)
        {
            move.HasEval = true;
            float.TryParse(evalMatch.Groups[1].Value, System.Globalization.NumberStyles.Any,
                System.Globalization.CultureInfo.InvariantCulture, out move.EvalAnnotation);
        }
    }

    // ── Clock string → seconds ────────────────────────────────────────────────
    /// <summary>Converts "%clk 1:30:05" → 5405 seconds.</summary>
    public static int ClkToSeconds(string clk)
    {
        if (string.IsNullOrEmpty(clk)) return 0;
        var parts = clk.Split(':');
        if (parts.Length == 3)
            return int.Parse(parts[0]) * 3600 + int.Parse(parts[1]) * 60 + int.Parse(parts[2]);
        if (parts.Length == 2)
            return int.Parse(parts[0]) * 60 + int.Parse(parts[1]);
        return int.Parse(parts[0]);
    }

    // ── PGN → UCI move list ───────────────────────────────────────────────────
    /// <summary>
    /// Replays PGN moves on a chess board and returns the UCI move list.
    /// Useful for feeding moves to Stockfish or the MoveTree.
    /// </summary>
    public static List<string> ToUciMoveList(PgnGame game)
    {
        var board = new ChessBoard();
        if (game.Headers.TryGetValue("FEN", out string fen))
            board.Load(fen);

        var uciList = new List<string>();
        foreach (var pm in game.Moves)
        {
            var move = board.SanToMove(pm.San);
            if (move == null) break;
            uciList.Add(move.Value.Uci);
            board.MakeMove(move.Value);
        }
        return uciList;
    }

    // ── Helpers ───────────────────────────────────────────────────────────────
    private static void SkipWhitespace(string s, ref int i)
    {
        while (i < s.Length && char.IsWhiteSpace(s[i])) i++;
    }

    private static bool IsResultToken(string s) =>
        s == "1-0" || s == "0-1" || s == "1/2-1/2" || s == "*";
}
