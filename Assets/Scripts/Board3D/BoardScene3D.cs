using System.Collections;
using UnityEngine;

/// <summary>
/// 3D board scene manager — port of src/engine/scene.js and board.js.
///
/// Manages the square grid layout, coordinate mapping, and piece positioning.
/// The board is oriented in world space: a1 at local (-3.5, 0, -3.5), h8 at (3.5, 0, 3.5).
/// Each square = 1.0 world unit. Pieces sit on the board plane at y = table top.
///
/// Coordinate system mirrors board.js: squareToPosition(sq) → Vector3
/// </summary>
public class BoardScene3D : MonoBehaviour
{
    // ── Constants ─────────────────────────────────────────────────────────────
    public const float SQUARE_SIZE  = 1.0f;
    public const float BOARD_OFFSET = -3.5f; // Center the 8×8 grid
    public const float PIECE_Y      =  0.1f; // Slight elevation above board surface

    // ── State ─────────────────────────────────────────────────────────────────
    private bool _isFlipped; // true = black perspective

    // ── Public: Coordinate Mapping ────────────────────────────────────────────

    /// <summary>
    /// Converts a 0x88 square index to world-space position.
    /// Mirrors squareToPosition() in board.js.
    /// </summary>
    public Vector3 SquareToWorld(int sq88, bool flipped = false)
    {
        int file = sq88 & 7;
        int rank = sq88 >> 4;

        float x, z;
        if (flipped)
        {
            x = (7 - file) * SQUARE_SIZE + BOARD_OFFSET + SQUARE_SIZE * 0.5f;
            z = (7 - rank) * SQUARE_SIZE + BOARD_OFFSET + SQUARE_SIZE * 0.5f;
        }
        else
        {
            x = file * SQUARE_SIZE + BOARD_OFFSET + SQUARE_SIZE * 0.5f;
            z = rank * SQUARE_SIZE + BOARD_OFFSET + SQUARE_SIZE * 0.5f;
        }

        return transform.TransformPoint(new Vector3(x, PIECE_Y, z));
    }

    /// <summary>Overload accepting algebraic notation ("e4").</summary>
    public Vector3 SquareToWorld(string alg, bool flipped = false)
        => SquareToWorld(ChessBoard.AlgToIdx(alg), flipped);

    /// <summary>
    /// Converts a world-space hit point back to 0x88 square index.
    /// Returns -1 if outside the board.
    /// </summary>
    public int WorldToSquare(Vector3 worldPos)
    {
        Vector3 local = transform.InverseTransformPoint(worldPos);
        float fx = local.x - BOARD_OFFSET;
        float fz = local.z - BOARD_OFFSET;
        int file = Mathf.FloorToInt(fx / SQUARE_SIZE);
        int rank = Mathf.FloorToInt(fz / SQUARE_SIZE);

        if (_isFlipped)
        {
            file = 7 - file;
            rank = 7 - rank;
        }

        if (file < 0 || file > 7 || rank < 0 || rank > 7) return -1;
        return rank * 16 + file;
    }

    // ── Board Flip ────────────────────────────────────────────────────────────

    public void SetOrientation(bool whiteAtBottom)
    {
        _isFlipped = !whiteAtBottom;

        // Rotate the board 180° around Y to flip perspective
        float yRot = whiteAtBottom ? 0f : 180f;
        StartCoroutine(SmoothRotate(yRot));
    }

    public bool IsFlipped => _isFlipped;

    private IEnumerator SmoothRotate(float targetY)
    {
        Quaternion from = transform.localRotation;
        Quaternion to   = Quaternion.Euler(0f, targetY, 0f);
        float t = 0f;
        while (t < 1f)
        {
            t += Time.deltaTime * 3f;
            transform.localRotation = Quaternion.Lerp(from, to, Mathf.SmoothStep(0f, 1f, t));
            yield return null;
        }
        transform.localRotation = to;
    }

    // ── Square Center (world) ─────────────────────────────────────────────────
    /// <summary>Returns center of square in world space (for highlights, arrows).</summary>
    public Vector3 SquareCenter(int sq88)
    {
        Vector3 pos = SquareToWorld(sq88, _isFlipped);
        pos.y = 0.01f; // just above board surface for overlays
        return pos;
    }

    // ── Gizmos (editor visualization) ────────────────────────────────────────
    private void OnDrawGizmosSelected()
    {
        Gizmos.color = new Color(0.3f, 0.8f, 0.3f, 0.4f);
        for (int rank = 0; rank < 8; rank++)
        {
            for (int file = 0; file < 8; file++)
            {
                float x = file * SQUARE_SIZE + BOARD_OFFSET + SQUARE_SIZE * 0.5f;
                float z = rank * SQUARE_SIZE + BOARD_OFFSET + SQUARE_SIZE * 0.5f;
                Vector3 center = transform.TransformPoint(new Vector3(x, 0.01f, z));
                Gizmos.DrawWireCube(center, new Vector3(SQUARE_SIZE * 0.95f, 0.01f, SQUARE_SIZE * 0.95f));
            }
        }
    }
}
