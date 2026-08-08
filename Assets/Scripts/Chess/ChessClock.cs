using System;
using System.Threading;

/// <summary>
/// Chess clock — port of src/engine/ClockManager.ts.
/// Runs on a background System.Threading.Timer (sub-second precision).
/// Fires OnTick every ~100ms, OnFlag when a player's time reaches zero.
/// </summary>
public class ChessClock : IDisposable
{
    // ── Events ────────────────────────────────────────────────────────────────
    /// <summary>Fired on main thread every ~100ms with (whiteMs, blackMs, activeSide).</summary>
    public event Action<long, long, char> OnTick;
    /// <summary>Fired when active player's clock reaches 0.</summary>
    public event Action<char>             OnFlag;

    // ── State ─────────────────────────────────────────────────────────────────
    private long  _whiteMsRemaining;
    private long  _blackMsRemaining;
    private char  _activeSide = ChessBoard.WHITE; // 'w' or 'b'
    private bool  _running;
    private long  _lastTickMs;
    private bool  _flagged;

    private Timer _timer;

    // ── Constructor ────────────────────────────────────────────────────────────
    /// <param name="whiteMs">White's starting time in milliseconds.</param>
    /// <param name="blackMs">Black's starting time in milliseconds.</param>
    public ChessClock(long whiteMs, long blackMs)
    {
        _whiteMsRemaining = whiteMs;
        _blackMsRemaining = blackMs;
    }

    // ── Control ───────────────────────────────────────────────────────────────

    public void Start(char sideToMove)
    {
        _activeSide = sideToMove;
        _running    = true;
        _flagged    = false;
        _lastTickMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        _timer      = new Timer(Tick, null, 100, 100);
    }

    public void Pause()
    {
        _running = false;
        _timer?.Change(Timeout.Infinite, Timeout.Infinite);
    }

    /// <summary>
    /// Called when a move is made: adds increment (if any), switches active side.
    /// Mirrors ClockManager.ts Phase C (Move Arrival).
    /// </summary>
    public void OnMove(char newActiveSide, long incrementMs = 0)
    {
        if (_activeSide == ChessBoard.WHITE) _whiteMsRemaining += incrementMs;
        else                                _blackMsRemaining  += incrementMs;

        _activeSide  = newActiveSide;
        _lastTickMs  = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        _running     = true;
        _timer?.Change(100, 100);
    }

    /// <summary>
    /// Hard-set a player's clock (e.g. on move-40 time bonus from PGN %clk).
    /// Mirrors ClockManager.ts Phase D (Out-of-Band Update).
    /// </summary>
    public void SetTime(char side, long ms)
    {
        if (side == ChessBoard.WHITE) _whiteMsRemaining = ms;
        else                         _blackMsRemaining  = ms;
    }

    public long WhiteMs => _whiteMsRemaining;
    public long BlackMs => _blackMsRemaining;
    public char ActiveSide => _activeSide;

    // ── Internal tick ─────────────────────────────────────────────────────────
    private void Tick(object _)
    {
        if (!_running || _flagged) return;

        long nowMs   = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        long elapsed = nowMs - _lastTickMs;
        _lastTickMs  = nowMs;

        if (_activeSide == ChessBoard.WHITE)
            _whiteMsRemaining = Math.Max(0, _whiteMsRemaining - elapsed);
        else
            _blackMsRemaining = Math.Max(0, _blackMsRemaining - elapsed);

        // Dispatch tick (Unity main-thread marshalling done in ClockDisplay)
        OnTick?.Invoke(_whiteMsRemaining, _blackMsRemaining, _activeSide);

        // Check flag
        bool flagged = _activeSide == ChessBoard.WHITE
            ? _whiteMsRemaining <= 0
            : _blackMsRemaining <= 0;

        if (flagged)
        {
            _flagged = true;
            _running = false;
            OnFlag?.Invoke(_activeSide);
        }
    }

    // ── Format ────────────────────────────────────────────────────────────────
    /// <summary>Formats milliseconds as M:SS or H:MM:SS.</summary>
    public static string FormatMs(long ms)
    {
        if (ms < 0) ms = 0;
        long totalSec = ms / 1000;
        long h = totalSec / 3600;
        long m = (totalSec % 3600) / 60;
        long s = totalSec % 60;
        return h > 0
            ? $"{h}:{m:D2}:{s:D2}"
            : $"{m}:{s:D2}";
    }

    // ── Dispose ───────────────────────────────────────────────────────────────
    public void Dispose()
    {
        _timer?.Dispose();
        _timer = null;
    }
}
