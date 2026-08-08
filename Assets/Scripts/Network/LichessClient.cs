using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Networking;
using Newtonsoft.Json.Linq;

/// <summary>
/// Lichess API client — port of the fetch logic in Watch.tsx, WatchDetails.tsx,
/// and ClockManager.ts.
///
/// Handles:
///   - Broadcast list (ND-JSON stream from /api/broadcast)
///   - Round PGN stream (/api/stream/broadcast/round/{id}.pgn)
///   - Player profile lookups
/// </summary>
public class LichessClient : MonoBehaviour
{
    // ── Singleton ─────────────────────────────────────────────────────────────
    public static LichessClient Instance { get; private set; }

    private const string BASE = "https://lichess.org";

    private void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
        DontDestroyOnLoad(gameObject);
    }

    // ── Broadcast List ────────────────────────────────────────────────────────

    public class BroadcastRow
    {
        public string TourId;
        public string Name;
        public string Format;
        public string Rounds;
        public string Date;
        public string Status;   // "live" | "upcoming" | "past"
        public string ActiveRoundId;
        public long   StartsAt;
    }

    /// <summary>
    /// Fetches and parses the broadcast list — mirrors Watch.tsx fetch logic.
    /// Calls back on the Unity main thread with a sorted list.
    /// </summary>
    public void FetchBroadcasts(Action<List<BroadcastRow>> onSuccess, Action<string> onError)
    {
        StartCoroutine(DoFetchBroadcasts(onSuccess, onError));
    }

    private IEnumerator DoFetchBroadcasts(Action<List<BroadcastRow>> onSuccess,
                                           Action<string> onError)
    {
        string url = BASE + "/api/broadcast?nb=100";
        using var req = UnityWebRequest.Get(url);
        req.SetRequestHeader("Accept", "application/x-ndjson");
        yield return req.SendWebRequest();

        if (req.result != UnityWebRequest.Result.Success)
        {
            onError?.Invoke(req.error);
            yield break;
        }

        string text = req.downloadHandler.text;
        var rows = new List<BroadcastRow>();
        var seenGroups = new HashSet<string>();

        foreach (string line in text.Split('\n'))
        {
            string trimmed = line.Trim();
            if (string.IsNullOrEmpty(trimmed)) continue;

            JObject obj;
            try   { obj = JObject.Parse(trimmed); }
            catch { continue; }

            var tour = obj["tour"];
            if (tour == null) continue;

            string groupName = obj["group"]?.ToString();
            string tourId    = tour["id"]?.ToString();
            string tourName  = tour["name"]?.ToString();

            if (!string.IsNullOrEmpty(groupName))
            {
                if (seenGroups.Contains(groupName)) continue;
                seenGroups.Add(groupName);
                tourName = groupName;
            }

            var roundsArr = obj["rounds"] as JArray ?? new JArray();
            JObject activeRound = null;
            bool anyOngoing = false, allFinished = roundsArr.Count > 0;
            int completedRounds = 0;

            foreach (JObject r in roundsArr)
            {
                bool ongoing  = r["ongoing"]?.Value<bool>() ?? false;
                bool finished = r["finished"]?.Value<bool>() ?? false;
                if (ongoing)  { anyOngoing = true; activeRound ??= r; }
                if (finished) completedRounds++;
                else          allFinished = false;
                if (!ongoing && !finished) activeRound ??= r;
            }

            if (activeRound == null && roundsArr.Count > 0)
                activeRound = (JObject)roundsArr[roundsArr.Count - 1];

            string status = anyOngoing ? "live" : allFinished ? "past" : "upcoming";
            long startsAt = activeRound?["startsAt"]?.Value<long>() ?? 0;

            string dateStr = "—";
            if (startsAt > 0)
            {
                var dt = DateTimeOffset.FromUnixTimeMilliseconds(startsAt).LocalDateTime;
                dateStr = dt.ToString("d MMM yyyy");
            }

            rows.Add(new BroadcastRow
            {
                TourId        = tourId,
                Name          = tourName,
                Format        = tour["info"]?["format"]?.ToString() ?? tour["info"]?["tc"]?.ToString() ?? "—",
                Rounds        = roundsArr.Count > 0 ? $"{completedRounds}/{roundsArr.Count}" : "—",
                Date          = dateStr,
                Status        = status,
                ActiveRoundId = activeRound?["id"]?.ToString(),
                StartsAt      = startsAt
            });
        }

        // Sort: live → upcoming → past (mirrors Watch.tsx sort)
        rows.Sort((a, b) =>
        {
            int statusOrder(string s) => s == "live" ? 0 : s == "upcoming" ? 1 : 2;
            int cmp = statusOrder(a.Status).CompareTo(statusOrder(b.Status));
            if (cmp != 0) return cmp;
            return a.Status == "past"
                ? b.StartsAt.CompareTo(a.StartsAt)  // past: newest first
                : a.StartsAt.CompareTo(b.StartsAt);  // upcoming: soonest first
        });

        onSuccess?.Invoke(rows);
    }

    // ── Broadcast PGN Stream ──────────────────────────────────────────────────

    public class PgnChunk
    {
        public string RoundId;
        public string PgnText;
    }

    /// <summary>
    /// Streams ND-JSON PGN from a broadcast round.
    /// Calls onChunk for each PGN block received (could be many, one per board update).
    /// onDone called when stream ends.
    /// Mirrors WatchDetails.tsx fetch + ClockManager integration.
    /// </summary>
    public Coroutine StreamBroadcastPgn(string roundId,
                                         Action<PgnChunk> onChunk,
                                         Action           onDone,
                                         Action<string>   onError)
    {
        return StartCoroutine(DoStreamPgn(roundId, onChunk, onDone, onError));
    }

    private UnityWebRequest _activeStream;

    private IEnumerator DoStreamPgn(string roundId,
                                     Action<PgnChunk> onChunk,
                                     Action           onDone,
                                     Action<string>   onError)
    {
        string url = BASE + "/api/stream/broadcast/round/" + roundId + ".pgn";
        _activeStream = UnityWebRequest.Get(url);
        _activeStream.SetRequestHeader("Accept", "application/x-chess-pgn");

        // Streaming download handler
        var handler = new DownloadHandlerBuffer();
        _activeStream.downloadHandler = handler;
        _activeStream.SendWebRequest();

        int processedBytes = 0;
        string buffer = "";

        while (!_activeStream.isDone)
        {
            string current = _activeStream.downloadHandler.text;
            if (current.Length > processedBytes)
            {
                buffer += current.Substring(processedBytes);
                processedBytes = current.Length;

                // PGN blocks are separated by blank lines
                while (buffer.Contains("\n\n\n"))
                {
                    int idx = buffer.IndexOf("\n\n\n");
                    string block = buffer.Substring(0, idx).Trim();
                    buffer = buffer.Substring(idx + 3);
                    if (!string.IsNullOrEmpty(block))
                        onChunk?.Invoke(new PgnChunk { RoundId = roundId, PgnText = block });
                }
            }
            yield return new WaitForSeconds(0.5f); // poll every 500ms
        }

        if (_activeStream.result != UnityWebRequest.Result.Success)
            onError?.Invoke(_activeStream.error);
        else
            onDone?.Invoke();

        _activeStream.Dispose();
        _activeStream = null;
    }

    public void StopStream()
    {
        _activeStream?.Abort();
    }

    private void OnDestroy() => StopStream();
}
