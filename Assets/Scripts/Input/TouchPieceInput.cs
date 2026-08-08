using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// Touch input handler for 3D piece drag-and-drop.
/// Ports the chessground touch interaction from the web app.
///
/// Landscape layout: board occupies left ~60% of screen.
/// Input flow:
///   1. Finger touches a square with a friendly piece → piece selected
///   2. Ghost piece follows finger while dragging
///   3. Finger lifts on a legal destination → move committed
///   4. Finger lifts on illegal square → selection cancelled
///
/// Also supports tap-select-then-tap-destination (two taps) for precision.
/// </summary>
public class TouchPieceInput : MonoBehaviour
{
    // ── Dependencies ──────────────────────────────────────────────────────────
    [Header("References")]
    [SerializeField] private Camera         boardCamera;
    [SerializeField] private BoardScene3D   boardScene;
    [SerializeField] private PieceManager   pieceManager;
    [SerializeField] private GhostPiece     ghostPiece;
    [SerializeField] private SquareHighlight squareHighlight;

    [Header("Settings")]
    [SerializeField] private LayerMask boardLayerMask;
    [SerializeField] private float     dragThreshold = 10f; // pixels before we start dragging

    // ── State ─────────────────────────────────────────────────────────────────
    private ChessBoard _board;                // live board state from Analyze/Practice screen
    private bool       _interactionEnabled;

    private int    _selectedSquare  = -1;     // 0x88 index, -1 = none
    private bool   _isDragging;
    private Vector2 _touchStart;
    private int    _activeFingerId  = -1;

    // Callback fired when user completes a move (from, to, promotion)
    public event System.Action<string, string, char> OnMove;

    // ── Public API ────────────────────────────────────────────────────────────

    public void SetBoard(ChessBoard board) => _board = board;
    public void SetEnabled(bool enabled)   => _interactionEnabled = enabled;

    // ── Update ────────────────────────────────────────────────────────────────
    private void Update()
    {
        if (!_interactionEnabled || _board == null) return;

        var touches = Touchscreen.current?.touches;
        if (touches == null) return;

        foreach (var touch in touches)
        {
            var phase = touch.phase.ReadValue();

            if (phase == UnityEngine.InputSystem.TouchPhase.Began)
                HandleTouchBegan(touch);
            else if (phase == UnityEngine.InputSystem.TouchPhase.Moved)
                HandleTouchMoved(touch);
            else if (phase == UnityEngine.InputSystem.TouchPhase.Ended ||
                     phase == UnityEngine.InputSystem.TouchPhase.Canceled)
                HandleTouchEnded(touch);
        }

#if UNITY_EDITOR
        // Mouse support in editor for testing
        HandleEditorMouse();
#endif
    }

    // ── Touch Began ───────────────────────────────────────────────────────────
    private void HandleTouchBegan(UnityEngine.InputSystem.Controls.TouchControl touch)
    {
        if (_activeFingerId >= 0) return; // already tracking a finger
        Vector2 pos = touch.position.ReadValue();
        _activeFingerId = touch.touchId.ReadValue();
        _touchStart     = pos;
        _isDragging     = false;

        int sq = ScreenToSquare(pos);
        if (sq < 0) return;

        // Only select a square with a friendly piece
        if (_board.PieceColorAt(ChessBoard.IdxToAlg(sq)) != _board.Turn)
        {
            // Could be a tap-to-move if a piece is already selected
            if (_selectedSquare >= 0) TryCommitMove(_selectedSquare, sq);
            return;
        }

        _selectedSquare = sq;
        ShowLegalHighlights(sq);
        ghostPiece.Show(pieceManager.GetPieceAt(sq), pos);
    }

    // ── Touch Moved ───────────────────────────────────────────────────────────
    private void HandleTouchMoved(UnityEngine.InputSystem.Controls.TouchControl touch)
    {
        if (touch.touchId.ReadValue() != _activeFingerId) return;
        Vector2 pos = touch.position.ReadValue();

        if (!_isDragging && Vector2.Distance(pos, _touchStart) > dragThreshold)
            _isDragging = true;

        if (_isDragging && ghostPiece.IsVisible)
            ghostPiece.SetScreenPosition(pos);
    }

    // ── Touch Ended ───────────────────────────────────────────────────────────
    private void HandleTouchEnded(UnityEngine.InputSystem.Controls.TouchControl touch)
    {
        if (touch.touchId.ReadValue() != _activeFingerId) return;
        _activeFingerId = -1;

        Vector2 pos = touch.position.ReadValue();
        int targetSq = ScreenToSquare(pos);

        ghostPiece.Hide();

        if (_isDragging)
        {
            // Drag-drop: commit or cancel
            if (targetSq >= 0 && targetSq != _selectedSquare)
                TryCommitMove(_selectedSquare, targetSq);
            else
                CancelSelection();
        }
        // else: tap — leave selection active for second tap
    }

    // ── Move Commit ───────────────────────────────────────────────────────────
    private void TryCommitMove(int fromSq, int toSq)
    {
        if (fromSq < 0 || toSq < 0) { CancelSelection(); return; }

        string from = ChessBoard.IdxToAlg(fromSq);
        string to   = ChessBoard.IdxToAlg(toSq);

        // Check if legal
        var legal = _board.GenerateMoves();
        bool isLegal  = false;
        bool isPromo  = false;
        foreach (var m in legal)
        {
            if (m.FromAlg == from && m.ToAlg == to)
            {
                isLegal = true;
                if (m.IsPromotion) isPromo = true;
                break;
            }
        }

        if (!isLegal) { CancelSelection(); return; }

        CancelSelection();

        if (isPromo)
            PromotionDialog.Instance?.Show(from, to, _board.Turn, OnPromotionChosen);
        else
            OnMove?.Invoke(from, to, '\0');
    }

    private void OnPromotionChosen(string from, string to, char piece)
        => OnMove?.Invoke(from, to, piece);

    private void CancelSelection()
    {
        _selectedSquare = -1;
        _isDragging     = false;
        squareHighlight?.ClearHighlights();
    }

    // ── Highlighting ──────────────────────────────────────────────────────────
    private void ShowLegalHighlights(int fromSq)
    {
        squareHighlight?.ClearHighlights();
        squareHighlight?.HighlightSquare(fromSq, SquareHighlight.Type.Selected);

        string fromAlg = ChessBoard.IdxToAlg(fromSq);
        var legal = _board.GenerateMoves();
        foreach (var m in legal)
        {
            if (m.FromAlg != fromAlg) continue;
            bool isCapture = m.IsCapture || m.IsEnPassant;
            squareHighlight?.HighlightSquare(m.To,
                isCapture ? SquareHighlight.Type.LegalCapture : SquareHighlight.Type.LegalMove);
        }
    }

    // ── Raycasting ────────────────────────────────────────────────────────────
    /// <summary>Returns 0x88 square index from screen position, or -1 if off-board.</summary>
    private int ScreenToSquare(Vector2 screenPos)
    {
        if (boardCamera == null) return -1;
        Ray ray = boardCamera.ScreenPointToRay(screenPos);
        if (!Physics.Raycast(ray, out RaycastHit hit, 100f, boardLayerMask)) return -1;
        return boardScene?.WorldToSquare(hit.point) ?? -1;
    }

#if UNITY_EDITOR
    private bool _mouseDown;
    private void HandleEditorMouse()
    {
        if (Mouse.current == null) return;
        Vector2 mousePos = Mouse.current.position.ReadValue();

        if (Mouse.current.leftButton.wasPressedThisFrame)
        {
            _mouseDown = true;
            _touchStart = mousePos;
            HandleTouchBeganAt(mousePos);
        }
        else if (Mouse.current.leftButton.isPressed && _mouseDown)
        {
            if (!_isDragging && Vector2.Distance(mousePos, _touchStart) > dragThreshold)
                _isDragging = true;
            if (_isDragging) ghostPiece.SetScreenPosition(mousePos);
        }
        else if (Mouse.current.leftButton.wasReleasedThisFrame && _mouseDown)
        {
            _mouseDown = false;
            int targetSq = ScreenToSquare(mousePos);
            ghostPiece.Hide();
            if (_isDragging && targetSq >= 0 && targetSq != _selectedSquare)
                TryCommitMove(_selectedSquare, targetSq);
            _isDragging = false;
        }
    }

    private void HandleTouchBeganAt(Vector2 pos)
    {
        _isDragging = false;
        int sq = ScreenToSquare(pos);
        if (sq < 0) return;
        if (_board.PieceColorAt(ChessBoard.IdxToAlg(sq)) != _board.Turn)
        {
            if (_selectedSquare >= 0) TryCommitMove(_selectedSquare, sq);
            return;
        }
        _selectedSquare = sq;
        ShowLegalHighlights(sq);
        ghostPiece.Show(pieceManager.GetPieceAt(sq), pos);
    }
#endif
}
