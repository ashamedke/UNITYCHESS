using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

/// <summary>
/// Stats screen — port of src/pages/Stats.tsx.
///
/// Shows puzzle analytics: total/solved/failed, streak, solved today/week,
/// avg time, and a chart of recent activity.
/// Landscape: stat cards top row, recent puzzle log bottom.
/// </summary>
public class StatsScreen : MonoBehaviour
{
    [Header("Stat Cards")]
    [SerializeField] private TMP_Text totalText;
    [SerializeField] private TMP_Text solvedText;
    [SerializeField] private TMP_Text failedText;
    [SerializeField] private TMP_Text streakText;
    [SerializeField] private TMP_Text todayText;
    [SerializeField] private TMP_Text weekText;
    [SerializeField] private TMP_Text avgTimeText;
    [SerializeField] private TMP_Text accuracyText;

    [Header("Controls")]
    [SerializeField] private Button btnRefresh;
    [SerializeField] private Button btnPuzzles;

    private void OnEnable()
    {
        btnRefresh?.onClick.AddListener(Refresh);
        btnPuzzles?.onClick.AddListener(() => PracticeScreen.Instance?.ShowPuzzles());
        Refresh();
    }

    private void OnDisable()
    {
        btnRefresh?.onClick.RemoveAllListeners();
        btnPuzzles?.onClick.RemoveAllListeners();
    }

    private void Refresh()
    {
        var db = PuzzleDatabase.Instance;
        if (db == null) return;

        int total   = db.TotalPuzzles;
        int solved  = db.SolvedPuzzles;
        int failed  = db.FailedPuzzles;
        int unsolved = total - solved - failed;
        float accuracy = total > 0 ? (float)solved / Mathf.Max(1, solved + failed) * 100f : 0f;

        if (totalText  != null) totalText.text  = total.ToString();
        if (solvedText != null) solvedText.text = solved.ToString();
        if (failedText != null) failedText.text = failed.ToString();
        if (streakText != null) streakText.text = "🔥 " + db.CurrentStreak;
        if (todayText  != null) todayText.text  = db.GetSolvedToday().ToString();
        if (weekText   != null) weekText.text   = db.GetSolvedThisWeek().ToString();

        float avgTime = db.GetAvgTimeSolved();
        if (avgTimeText != null)
            avgTimeText.text = avgTime > 0 ? $"{avgTime:F0}s" : "—";

        if (accuracyText != null)
            accuracyText.text = total > 0 ? $"{accuracy:F1}%" : "—";
    }
}
