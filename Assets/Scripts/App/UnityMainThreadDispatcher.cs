using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Simple main-thread dispatcher.
/// Background threads (Stockfish read loop, clock timer) enqueue Unity API calls here.
/// Process() is called from a MonoBehaviour's Update().
/// </summary>
public static class UnityMainThreadDispatcher
{
    private static readonly Queue<Action> _queue = new Queue<Action>();
    private static readonly object        _lock  = new object();

    public static void Enqueue(Action action)
    {
        lock (_lock) _queue.Enqueue(action);
    }

    /// <summary>Call this from a MonoBehaviour.Update() once per frame.</summary>
    public static void Process()
    {
        while (true)
        {
            Action action;
            lock (_lock)
            {
                if (_queue.Count == 0) break;
                action = _queue.Dequeue();
            }
            try { action(); }
            catch (Exception ex) { Debug.LogError("[Dispatcher] " + ex); }
        }
    }
}

/// <summary>
/// MonoBehaviour that drives UnityMainThreadDispatcher each frame.
/// Attach once to a persistent GameObject (AppController).
/// </summary>
public class MainThreadDispatcherRunner : MonoBehaviour
{
    private void Update() => UnityMainThreadDispatcher.Process();
}
