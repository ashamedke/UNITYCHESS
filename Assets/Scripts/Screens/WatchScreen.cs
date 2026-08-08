using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

/// <summary>
/// Watch screen — port of src/pages/Watch.tsx.
///
/// Fetches the Lichess broadcast list and displays it as a scrollable table.
/// Live | Upcoming | Finished rows with tap-to-open detail.
/// Landscape: full-width table with status column.
/// </summary>
public class WatchScreen : MonoBehaviour
{
    [Header("UI")]
    [SerializeField] private GameObject      loadingSpinner;
    [SerializeField] private GameObject      tableRoot;
    [SerializeField] private Transform       tableBody;
    [SerializeField] private GameObject      rowPrefab;
    [SerializeField] private TMP_Text        errorText;
    [SerializeField] private WatchDetailsScreen detailsScreen;

    // ── Lifecycle ──────────────────────────────────────────────────────────────
    private void OnEnable()
    {
        ClearTable();
        FetchBroadcasts();
    }

    private void OnDisable()
    {
        LichessClient.Instance?.StopStream();
    }

    // ── Fetch ──────────────────────────────────────────────────────────────────
    private void FetchBroadcasts()
    {
        if (loadingSpinner != null) loadingSpinner.SetActive(true);
        if (tableRoot != null) tableRoot.SetActive(false);
        if (errorText != null) errorText.text = "";

        LichessClient.Instance?.FetchBroadcasts(
            onSuccess: rows => PopulateTable(rows),
            onError:   err  =>
            {
                if (loadingSpinner != null) loadingSpinner.SetActive(false);
                if (errorText != null) errorText.text = "Error loading broadcasts: " + err;
            }
        );
    }

    // ── Table ──────────────────────────────────────────────────────────────────
    private void PopulateTable(List<LichessClient.BroadcastRow> rows)
    {
        if (loadingSpinner != null) loadingSpinner.SetActive(false);
        if (tableRoot != null) tableRoot.SetActive(true);

        ClearTable();

        for (int i = 0; i < rows.Count; i++)
        {
            var row = rows[i];
            var obj = Instantiate(rowPrefab, tableBody);
            var rowUI = obj.GetComponent<BroadcastRowUI>();
            if (rowUI != null)
            {
                rowUI.Set(i + 1, row, () => OpenDetails(row));
            }
        }
    }

    private void ClearTable()
    {
        foreach (Transform child in tableBody)
            Destroy(child.gameObject);
    }

    // ── Open Detail ────────────────────────────────────────────────────────────
    private void OpenDetails(LichessClient.BroadcastRow row)
    {
        if (row.ActiveRoundId == null) return;
        gameObject.SetActive(false);
        detailsScreen?.gameObject.SetActive(true);
        detailsScreen?.OpenRound(row.TourId, row.ActiveRoundId, row.Name,
                                  () =>
                                  {
                                      detailsScreen.gameObject.SetActive(false);
                                      gameObject.SetActive(true);
                                      FetchBroadcasts();
                                  });
    }
}

// ── Row UI ──────────────────────────────────────────────────────────────────
/// <summary>Individual broadcast row — attach to rowPrefab.</summary>
public class BroadcastRowUI : MonoBehaviour
{
    [SerializeField] private TMP_Text   indexText;
    [SerializeField] private TMP_Text   nameText;
    [SerializeField] private TMP_Text   formatText;
    [SerializeField] private TMP_Text   roundsText;
    [SerializeField] private TMP_Text   dateText;
    [SerializeField] private TMP_Text   statusText;
    [SerializeField] private Image      statusBadge;
    [SerializeField] private Button     rowButton;

    private static readonly Color LIVE_COLOR     = new Color(0.9f, 0.1f, 0.1f);
    private static readonly Color UPCOMING_COLOR = new Color(0.2f, 0.6f, 1.0f);
    private static readonly Color PAST_COLOR     = new Color(0.4f, 0.4f, 0.4f);

    public void Set(int index, LichessClient.BroadcastRow row, System.Action onClick)
    {
        if (indexText  != null) indexText.text  = index.ToString();
        if (nameText   != null) nameText.text   = row.Name ?? "—";
        if (formatText != null) formatText.text = row.Format ?? "—";
        if (roundsText != null) roundsText.text = row.Rounds ?? "—";
        if (dateText   != null) dateText.text   = row.Date ?? "—";

        Color statusColor = row.Status switch
        {
            "live"     => LIVE_COLOR,
            "upcoming" => UPCOMING_COLOR,
            _          => PAST_COLOR
        };
        string statusLabel = row.Status switch
        {
            "live"     => "LIVE",
            "upcoming" => "Upcoming",
            _          => "Finished"
        };
        if (statusText  != null) { statusText.text = statusLabel; statusText.color = statusColor; }
        if (statusBadge != null) statusBadge.color = statusColor;

        rowButton?.onClick.AddListener(() => onClick?.Invoke());

        // Highlight live rows
        if (row.Status == "live")
        {
            var bg = GetComponent<Image>();
            if (bg != null) bg.color = new Color(0.9f, 0.1f, 0.1f, 0.08f);
        }
    }
}
