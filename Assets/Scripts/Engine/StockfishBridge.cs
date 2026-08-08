using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Threading;
using UnityEngine;

/// <summary>
/// Stockfish UCI bridge — port of src/engine/fish.js (web worker) and analysis.js.
///
/// On Android: copies the Stockfish binary from StreamingAssets to persistentDataPath
/// on first launch, sets execute permission, then runs it as a subprocess.
/// Communicates via UCI protocol over stdin/stdout.
///
/// Usage:
///   StockfishBridge.Instance.Evaluate(fen, depth, multiPV, callback);
///   StockfishBridge.Instance.GetBestMove(fen, movetime, callback);
/// </summary>
public class StockfishBridge : MonoBehaviour
{
    // ── Singleton ─────────────────────────────────────────────────────────────
    public static StockfishBridge Instance { get; private set; }

    // ── State ─────────────────────────────────────────────────────────────────
    private Process _process;
    private bool    _ready;
    private bool    _isAnalyzing;
    private Thread  _readThread;

    // Pending callbacks
    private Action<string>          _bestMoveCallback;
    private Action<List<PvInfo>>    _evalCallback;
    private List<PvInfo>            _currentPVs = new List<PvInfo>();
    private int                     _targetMultiPV = 1;

    // Pending UCI commands queue
    private readonly System.Collections.Concurrent.ConcurrentQueue<string>
        _commandQueue = new System.Collections.Concurrent.ConcurrentQueue<string>();

    // ── Types ─────────────────────────────────────────────────────────────────
    public class PvInfo
    {
        public int    MultiPVRank;
        public int    Depth;
        public int    CentiPawns; // or int.MinValue for mate
        public int    MateMoves;  // 0 if not mate
        public bool   IsMate;
        public string PvUci;     // space-separated UCI moves e.g. "e2e4 e7e5 ..."
        public string PvSan;     // SAN representation (computed after receiving PV)
    }

    // ── Lifecycle ──────────────────────────────────────────────────────────────
    private void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
        DontDestroyOnLoad(gameObject);
    }

    private void Start()
    {
        StartCoroutine(InitializeStockfish());
    }

    private System.Collections.IEnumerator InitializeStockfish()
    {
        string binaryPath = GetStockfishPath();

        // On first launch: copy binary from StreamingAssets to persistentDataPath
        if (!File.Exists(binaryPath))
        {
            yield return CopyBinaryFromStreamingAssets(binaryPath);
        }

        // Set execute permission on Android
#if UNITY_ANDROID && !UNITY_EDITOR
        SetExecutePermission(binaryPath);
#endif

        LaunchProcess(binaryPath);
    }

    private string GetStockfishPath()
    {
#if UNITY_ANDROID && !UNITY_EDITOR
        return Path.Combine(Application.persistentDataPath, "stockfish");
#elif UNITY_EDITOR
        return Path.Combine(Application.streamingAssetsPath, "../Plugins/Android/stockfish");
#else
        return Path.Combine(Application.streamingAssetsPath, "stockfish");
#endif
    }

    private System.Collections.IEnumerator CopyBinaryFromStreamingAssets(string destPath)
    {
#if UNITY_ANDROID && !UNITY_EDITOR
        // On Android, StreamingAssets are inside the APK — use UnityWebRequest to read
        string srcUrl = "jar:file://" + Application.dataPath + "!/assets/stockfish";
        var request = UnityEngine.Networking.UnityWebRequest.Get(srcUrl);
        yield return request.SendWebRequest();

        if (request.result == UnityEngine.Networking.UnityWebRequest.Result.Success)
        {
            File.WriteAllBytes(destPath, request.downloadHandler.data);
            UnityEngine.Debug.Log("[Stockfish] Binary copied to: " + destPath);
        }
        else
        {
            UnityEngine.Debug.LogError("[Stockfish] Failed to copy binary: " + request.error);
        }
#else
        yield return null; // No copy needed on non-Android
#endif
    }

    private void SetExecutePermission(string path)
    {
        try
        {
            using var chmod = new Process();
            chmod.StartInfo = new ProcessStartInfo
            {
                FileName  = "chmod",
                Arguments = "+x " + path,
                UseShellExecute = false,
                CreateNoWindow  = true
            };
            chmod.Start();
            chmod.WaitForExit();
        }
        catch (Exception ex)
        {
            UnityEngine.Debug.LogError("[Stockfish] chmod failed: " + ex.Message);
        }
    }

    private void LaunchProcess(string binaryPath)
    {
        try
        {
            _process = new Process();
            _process.StartInfo = new ProcessStartInfo
            {
                FileName               = binaryPath,
                UseShellExecute        = false,
                RedirectStandardInput  = true,
                RedirectStandardOutput = true,
                RedirectStandardError  = true,
                CreateNoWindow         = true
            };
            _process.Start();

            // Read thread
            _readThread = new Thread(ReadLoop) { IsBackground = true, Name = "StockfishRead" };
            _readThread.Start();

            // Init UCI
            SendCommand("uci");
            SendCommand("isready");
            SendCommand("setoption name Threads value 2");
            SendCommand("setoption name Hash value 64");

            UnityEngine.Debug.Log("[Stockfish] Process launched: " + binaryPath);
        }
        catch (Exception ex)
        {
            UnityEngine.Debug.LogError("[Stockfish] Failed to launch: " + ex.Message);
        }
    }

    // ── Public API ────────────────────────────────────────────────────────────

    /// <summary>
    /// Evaluate a position. Calls back on the Unity main thread.
    /// Mirrors evaluateFen() in analysis.js.
    /// </summary>
    public void Evaluate(string fen, int depth, int multiPV,
                         Action<List<PvInfo>> callback,
                         List<string> uciMovesBefore = null)
    {
        if (!_ready) { UnityEngine.Debug.LogWarning("[Stockfish] Not ready yet"); return; }

        _evalCallback  = callback;
        _targetMultiPV = multiPV;
        _currentPVs.Clear();

        SendCommand("stop");
        SendCommand("setoption name MultiPV value " + multiPV);

        string posCmd = "position fen " + fen;
        if (uciMovesBefore != null && uciMovesBefore.Count > 0)
            posCmd += " moves " + string.Join(" ", uciMovesBefore);

        SendCommand(posCmd);
        SendCommand("go depth " + depth);
        _isAnalyzing = true;
    }

    /// <summary>
    /// Get best move for given FEN + movetime. Calls back on Unity main thread.
    /// Used for Stockfish vs-computer play and puzzle verification.
    /// </summary>
    public void GetBestMove(string fen, int movetimeMs, Action<string> callback,
                            int skillLevel = 20, List<string> uciMovesBefore = null)
    {
        if (!_ready) return;

        _bestMoveCallback = callback;

        SendCommand("stop");
        SendCommand("setoption name Skill Level value " + skillLevel);
        SendCommand("setoption name MultiPV value 1");

        string posCmd = "position fen " + fen;
        if (uciMovesBefore != null && uciMovesBefore.Count > 0)
            posCmd += " moves " + string.Join(" ", uciMovesBefore);

        SendCommand(posCmd);
        SendCommand("go movetime " + movetimeMs);
        _isAnalyzing = true;
    }

    public void Stop() => SendCommand("stop");

    public bool IsReady => _ready;

    // ── Internal ──────────────────────────────────────────────────────────────

    private void SendCommand(string cmd)
    {
        if (_process == null || _process.HasExited) return;
        try
        {
            _process.StandardInput.WriteLine(cmd);
            _process.StandardInput.Flush();
        }
        catch (Exception ex)
        {
            UnityEngine.Debug.LogError("[Stockfish] SendCommand error: " + ex.Message);
        }
    }

    private void ReadLoop()
    {
        while (_process != null && !_process.HasExited)
        {
            string line;
            try { line = _process.StandardOutput.ReadLine(); }
            catch { break; }

            if (line == null) continue;
            ProcessLine(line);
        }
    }

    private void ProcessLine(string line)
    {
        if (line == "uciok")   return;
        if (line == "readyok") { _ready = true; return; }

        if (line.StartsWith("bestmove"))
        {
            _isAnalyzing = false;
            string bestMove = line.Split(' ')[1];
            if (bestMove != "(none)")
            {
                // Marshal to Unity main thread
                UnityMainThreadDispatcher.Enqueue(() =>
                {
                    _bestMoveCallback?.Invoke(bestMove);
                    _bestMoveCallback = null;

                    _evalCallback?.Invoke(new List<PvInfo>(_currentPVs));
                    _evalCallback = null;
                });
            }
            return;
        }

        if (line.StartsWith("info") && line.Contains("score"))
        {
            var pv = ParseInfoLine(line);
            if (pv != null)
            {
                // Update or replace entry with same multipv rank
                int idx = _currentPVs.FindIndex(p => p.MultiPVRank == pv.MultiPVRank);
                if (idx >= 0) _currentPVs[idx] = pv;
                else          _currentPVs.Add(pv);

                // Dispatch partial eval update every time
                var pvCopy = new List<PvInfo>(_currentPVs);
                UnityMainThreadDispatcher.Enqueue(() => _evalCallback?.Invoke(pvCopy));
            }
        }
    }

    private PvInfo ParseInfoLine(string line)
    {
        var parts = line.Split(' ');
        var pv = new PvInfo();
        bool pvStarted = false;
        var pvMoves = new System.Text.StringBuilder();

        for (int i = 0; i < parts.Length; i++)
        {
            switch (parts[i])
            {
                case "multipv": pv.MultiPVRank = int.Parse(parts[++i]); break;
                case "depth":   pv.Depth       = int.Parse(parts[++i]); break;
                case "cp":
                    pv.CentiPawns = int.Parse(parts[++i]);
                    pv.IsMate     = false;
                    break;
                case "mate":
                    pv.MateMoves = int.Parse(parts[++i]);
                    pv.IsMate    = true;
                    break;
                case "pv":
                    pvStarted = true;
                    break;
                default:
                    if (pvStarted)
                    {
                        if (pvMoves.Length > 0) pvMoves.Append(' ');
                        pvMoves.Append(parts[i]);
                    }
                    break;
            }
        }

        pv.PvUci = pvMoves.ToString();
        return pv.Depth > 0 ? pv : null;
    }

    // ── Cleanup ───────────────────────────────────────────────────────────────
    private void OnApplicationQuit()
    {
        SendCommand("quit");
        _process?.WaitForExit(500);
        _process?.Kill();
        _process?.Dispose();
    }
}
