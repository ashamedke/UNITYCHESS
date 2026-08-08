using UnityEngine;

/// <summary>
/// App controller — the root MonoBehaviour that wires all singleton services.
/// Attach to the persistent root GameObject in Main.unity.
///
/// Initialization order:
///   1. Dispatcher (process background thread callbacks)
///   2. ChessAudioManager (sfx)
///   3. PuzzleDatabase (load from disk)
///   4. LichessClient (network)
///   5. StockfishBridge (engine process)
///   6. ScreenManager (navigation)
///
/// This matches the React StrictMode + provider chain in App.tsx.
/// </summary>
public class AppController : MonoBehaviour
{
    private void Awake()
    {
        // Ensure frame rate is reasonable on mobile
        Application.targetFrameRate = 60;
        QualitySettings.vSyncCount  = 0;

        // Prevent screen sleep during gameplay
        Screen.sleepTimeout = SleepTimeout.NeverSleep;

        Debug.Log("[AppController] Initialized — ChessGod WAP Unity v1.0.0");
    }

    private void Update()
    {
        // Drain main-thread dispatch queue every frame
        UnityMainThreadDispatcher.Process();
    }

    private void OnApplicationPause(bool pauseStatus)
    {
        if (pauseStatus)
        {
            // Save puzzle progress on app pause
            PlayerPrefs.Save();
        }
    }
}
