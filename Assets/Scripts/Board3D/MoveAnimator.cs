using System;
using System.Collections;
using UnityEngine;

/// <summary>
/// Move animator — port of src/engine/animator.js (GSAP).
/// Uses Unity coroutines with smooth step easing instead of GSAP.
///
/// Handles:
///   - Piece slide A → B  (MoveSlide)
///   - Capture: piece arcs off-board to CapturedRack  (CaptureArc)
///   - Castling: simultaneous king + rook slides
///   - Promotion: swap piece model at destination
/// </summary>
public class MoveAnimator : MonoBehaviour
{
    [SerializeField] private float slideDuration   = 0.18f; // seconds for a piece slide
    [SerializeField] private float captureArcHeight = 1.2f;  // height of capture arc
    [SerializeField] private float captureDuration  = 0.25f;

    // ── Public API ─────────────────────────────────────────────────────────────

    /// <summary>
    /// Animate a full move: slide, optional simultaneous capture arc, optional castle rook.
    /// Calls onComplete when all animations are done.
    /// </summary>
    public void AnimateMove(
        GameObject movingPiece, Vector3 toPos,
        GameObject capturedPiece, CapturedRack capturedRack,
        GameObject castleRook, Vector3 rookToPos,
        char promotionPiece, GameObject promotionPrefab,
        Action onComplete)
    {
        StartCoroutine(DoAnimateMove(movingPiece, toPos,
            capturedPiece, capturedRack,
            castleRook, rookToPos,
            promotionPiece, promotionPrefab,
            onComplete));
    }

    // ── Coroutine ──────────────────────────────────────────────────────────────
    private IEnumerator DoAnimateMove(
        GameObject movingPiece, Vector3 toPos,
        GameObject capturedPiece, CapturedRack capturedRack,
        GameObject castleRook, Vector3 rookToPos,
        char promotionPiece, GameObject promotionPrefab,
        Action onComplete)
    {
        // Capture arc starts immediately, in parallel with the slide
        Coroutine captureCoroutine = null;
        if (capturedPiece != null)
        {
            var rack = capturedRack;
            captureCoroutine = StartCoroutine(CaptureArc(capturedPiece, rack));
        }

        // Castle rook slides in parallel
        Coroutine rookCoroutine = null;
        if (castleRook != null)
            rookCoroutine = StartCoroutine(SlidePiece(castleRook, rookToPos, slideDuration));

        // Main piece slide
        yield return SlidePiece(movingPiece, toPos, slideDuration);

        // Wait for capture + rook to finish
        if (captureCoroutine != null) yield return captureCoroutine;
        if (rookCoroutine    != null) yield return rookCoroutine;

        // Promotion: replace piece model
        if (promotionPiece != '\0' && promotionPrefab != null)
        {
            yield return DoPromotion(movingPiece, promotionPrefab, toPos);
        }

        onComplete?.Invoke();
    }

    // ── Slide ──────────────────────────────────────────────────────────────────
    private IEnumerator SlidePiece(GameObject piece, Vector3 target, float duration)
    {
        if (piece == null) yield break;

        Vector3 start = piece.transform.position;
        float   t     = 0f;

        while (t < 1f)
        {
            if (piece == null) yield break;
            t += Time.deltaTime / duration;
            float eased = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(t));
            piece.transform.position = Vector3.Lerp(start, target, eased);
            yield return null;
        }

        if (piece != null) piece.transform.position = target;
    }

    // ── Capture Arc ────────────────────────────────────────────────────────────
    private IEnumerator CaptureArc(GameObject piece, CapturedRack rack)
    {
        if (piece == null) yield break;

        Vector3 start  = piece.transform.position;
        Vector3 end    = rack != null ? rack.NextSlotPosition() : start + Vector3.up * 3f;
        float   t      = 0f;

        while (t < 1f)
        {
            if (piece == null) yield break;
            t += Time.deltaTime / captureDuration;
            float eased = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(t));
            Vector3 lerped = Vector3.Lerp(start, end, eased);
            lerped.y += Mathf.Sin(eased * Mathf.PI) * captureArcHeight;
            piece.transform.position = lerped;
            yield return null;
        }

        if (piece != null)
        {
            rack?.AddCapturedPiece(piece);
            if (piece != null && rack == null) Destroy(piece);
        }
    }

    // ── Promotion Swap ─────────────────────────────────────────────────────────
    private IEnumerator DoPromotion(GameObject pawn, GameObject prefab, Vector3 pos)
    {
        // Brief scale-down of pawn
        float t = 0f;
        Vector3 origScale = pawn.transform.localScale;
        while (t < 1f)
        {
            t += Time.deltaTime / 0.12f;
            pawn.transform.localScale = Vector3.Lerp(origScale, Vector3.zero, Mathf.Clamp01(t));
            yield return null;
        }
        Destroy(pawn);

        // Spawn promoted piece with scale-up
        GameObject promoted = Instantiate(prefab, pos, Quaternion.identity,
                                          transform.parent);
        promoted.transform.localScale = Vector3.zero;
        t = 0f;
        while (t < 1f)
        {
            t += Time.deltaTime / 0.15f;
            promoted.transform.localScale = Vector3.Lerp(Vector3.zero, origScale,
                                             Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(t)));
            yield return null;
        }
        promoted.transform.localScale = origScale;
    }
}
