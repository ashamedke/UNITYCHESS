using System;
using System.Collections.Generic;

/// <summary>
/// Branching move tree — port of src/engine/moveTree.js.
/// Supports: main line, variations, PGN annotation storage, navigation.
/// Each node stores the move that reached it plus annotations.
/// </summary>
public class MoveTree
{
    // ── Node ─────────────────────────────────────────────────────────────────
    public class Node
    {
        public string   Id;
        public string   San;       // SAN notation of the move
        public string   Uci;       // UCI notation
        public string   Fen;       // FEN after this move
        public string   Comment;
        public string   ClkAnnotation;
        public float    Eval;
        public bool     HasEval;
        public int      MoveNumber;
        public char     Color;     // 'w' or 'b'

        public Node              Parent;
        public List<Node>        Children = new List<Node>(); // first child = main line
    }

    // ── Root ──────────────────────────────────────────────────────────────────
    public readonly Node Root;

    private static int _nextId = 0;

    // ── Constructor ────────────────────────────────────────────────────────────
    public MoveTree(string startFen = ChessBoard.START_FEN)
    {
        Root = new Node
        {
            Id  = "root",
            Fen = startFen,
            San = "",
            Uci = ""
        };
    }

    // ── Add Move ──────────────────────────────────────────────────────────────
    /// <summary>
    /// Adds a move as a child of parentId.
    /// If the move (by UCI) already exists as a child, returns the existing node.
    /// Otherwise creates a new node (variation or next main-line move).
    /// Mirrors addMoveToTree() in moveTree.js.
    /// </summary>
    public Node AddMove(string parentId, string san, string uci, string fen,
                        int moveNumber, char color,
                        string comment = null, string clk = null,
                        float eval = 0f, bool hasEval = false)
    {
        var parent = FindById(parentId);
        if (parent == null) throw new ArgumentException("Parent node not found: " + parentId);

        // Check if this exact UCI move is already a child (transposition)
        foreach (var child in parent.Children)
        {
            if (child.Uci == uci) return child;
        }

        var node = new Node
        {
            Id             = "n" + (++_nextId),
            San            = san,
            Uci            = uci,
            Fen            = fen,
            Comment        = comment,
            ClkAnnotation  = clk,
            Eval           = eval,
            HasEval        = hasEval,
            MoveNumber     = moveNumber,
            Color          = color,
            Parent         = parent
        };
        parent.Children.Add(node);
        return node;
    }

    // ── Find ──────────────────────────────────────────────────────────────────
    public Node FindById(string id)
    {
        if (id == "root") return Root;
        return FindByIdRecursive(Root, id);
    }

    private Node FindByIdRecursive(Node node, string id)
    {
        if (node.Id == id) return node;
        foreach (var child in node.Children)
        {
            var found = FindByIdRecursive(child, id);
            if (found != null) return found;
        }
        return null;
    }

    // ── Path ──────────────────────────────────────────────────────────────────
    /// <summary>Returns the path from root to the given node (inclusive).</summary>
    public List<Node> GetPath(string nodeId)
    {
        var node = FindById(nodeId);
        var path = new List<Node>();
        while (node != null)
        {
            path.Insert(0, node);
            node = node.Parent;
        }
        return path;
    }

    /// <summary>Returns the UCI move list from root to node (for Stockfish position command).</summary>
    public List<string> GetUciPath(string nodeId)
    {
        var path = GetPath(nodeId);
        var ucis = new List<string>();
        foreach (var n in path)
        {
            if (!string.IsNullOrEmpty(n.Uci)) ucis.Add(n.Uci);
        }
        return ucis;
    }

    // ── Navigation helpers ────────────────────────────────────────────────────
    public Node GetNext(string nodeId)
    {
        var node = FindById(nodeId);
        return node?.Children.Count > 0 ? node.Children[0] : null;
    }

    public Node GetPrev(string nodeId)
    {
        var node = FindById(nodeId);
        return node?.Parent;
    }

    public Node GetFirst() => Root;

    public Node GetLast(string nodeId)
    {
        var node = FindById(nodeId);
        if (node == null) return null;
        while (node.Children.Count > 0) node = node.Children[0];
        return node;
    }

    // ── Build from PGN ────────────────────────────────────────────────────────
    /// <summary>
    /// Constructs a MoveTree from a parsed PgnGame, replaying moves on a ChessBoard.
    /// Mirrors treeFromHistory() in moveTree.js.
    /// </summary>
    public static MoveTree FromPgn(PgnParser.PgnGame game)
    {
        string startFen = game.Headers.TryGetValue("FEN", out string fen)
            ? fen : ChessBoard.START_FEN;

        var tree  = new MoveTree(startFen);
        var board = new ChessBoard(startFen);

        BuildBranch(game.Moves, tree, board, "root");
        return tree;
    }

    private static string BuildBranch(System.Collections.Generic.List<PgnParser.PgnMove> pgnMoves,
                                       MoveTree tree, ChessBoard board, string parentId)
    {
        string currentId = parentId;
        foreach (var pm in pgnMoves)
        {
            var move = board.SanToMove(pm.San);
            if (move == null) break;

            board.MakeMove(move.Value);
            string fen = board.Fen();

            var node = tree.AddMove(
                currentId,
                pm.San,
                move.Value.Uci,
                fen,
                board.MoveNumber,
                move.Value.Color,
                pm.Comment,
                pm.ClkAnnotation,
                pm.EvalAnnotation,
                pm.HasEval
            );
            currentId = node.Id;

            // Build variation branches (undo and replay)
            foreach (var variation in pm.Variations)
            {
                // Save state: undo the main move to replay from the parent position
                board.UndoMove();
                string savedFen = board.Fen();

                BuildBranch(variation, tree, board, node.Parent?.Id ?? "root");

                // Restore: replay the main move
                board.Load(fen); // shortcut — reload the post-main-move FEN
            }
        }
        return currentId;
    }
}
