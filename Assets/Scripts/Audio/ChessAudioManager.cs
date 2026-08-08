using UnityEngine;

/// <summary>
/// Chess audio manager — port of src/engine/audio.js.
///
/// Plays the 6 chess-specific SFX clips on move events.
/// All 190 music tracks are NOT included — only chess SFX.
///
/// Clips to place in Assets/Audio/SFX/:
///   move.mp3, capture.mp3, castle.mp3, check.mp3, promotion.mp3, notify.mp3
/// (Copied from public/audio/ in the web project)
/// </summary>
public class ChessAudioManager : MonoBehaviour
{
    // ── Singleton ─────────────────────────────────────────────────────────────
    public static ChessAudioManager Instance { get; private set; }

    // ── Audio Clips ───────────────────────────────────────────────────────────
    [Header("Chess SFX (assign in Inspector)")]
    [SerializeField] private AudioClip clipMove;
    [SerializeField] private AudioClip clipCapture;
    [SerializeField] private AudioClip clipCastle;
    [SerializeField] private AudioClip clipCheck;
    [SerializeField] private AudioClip clipPromotion;
    [SerializeField] private AudioClip clipNotify;

    [Header("Volume")]
    [Range(0f, 1f)]
    [SerializeField] private float sfxVolume = 0.85f;

    private AudioSource _source;

    // ── Lifecycle ──────────────────────────────────────────────────────────────
    private void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
        DontDestroyOnLoad(gameObject);
        _source = gameObject.AddComponent<AudioSource>();
        _source.playOnAwake = false;
    }

    // ── Public API ─────────────────────────────────────────────────────────────

    /// <summary>
    /// Plays the appropriate sound for a chess move.
    /// Mirrors playSoundForMove() + playCheckSound() in audio.js.
    /// </summary>
    public void PlayMoveSound(ChessMove move, bool isCheck)
    {
        if (move.IsCastle)
        {
            Play(clipCastle);
            return;
        }
        if (move.IsPromotion)
        {
            Play(clipPromotion);
            if (isCheck) Play(clipCheck); // promotion + check = both sounds
            return;
        }
        if (move.IsCapture || move.IsEnPassant)
        {
            Play(clipCapture);
        }
        else
        {
            Play(clipMove);
        }
        if (isCheck) Play(clipCheck);
    }

    public void PlayNotify()  => Play(clipNotify);
    public void PlayCapture() => Play(clipCapture);
    public void PlayMove()    => Play(clipMove);

    // ── Internal ───────────────────────────────────────────────────────────────
    private void Play(AudioClip clip)
    {
        if (clip == null || _source == null) return;
        _source.PlayOneShot(clip, sfxVolume);
    }

    // ── Volume control ────────────────────────────────────────────────────────
    public void SetVolume(float v)
    {
        sfxVolume = Mathf.Clamp01(v);
        PlayerPrefs.SetFloat("sfxVolume", sfxVolume);
    }

    public void LoadSettings()
    {
        sfxVolume = PlayerPrefs.GetFloat("sfxVolume", 0.85f);
    }
}
