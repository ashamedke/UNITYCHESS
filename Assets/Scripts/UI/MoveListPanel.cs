using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

/// <summary>
/// Move list panel — scrollable, clickable move list mirroring the web app's
/// move list in Analyze mode and Watch mode.
/// Shows moves in pairs (move number. white black), with variation indentation.
/// </summary>
public class MoveListPanel : MonoBehaviour
{
    [Header("UI")]
    [SerializeField] private Transform    listContainer;
    [SerializeField] private GameObject   movePairPrefab;
    [SerializeField] private ScrollRect   scrollRect;

    private string _highlightedId;
    private Action<string> _onMoveClicked;

    // ── Build ──────────────────────────────────────────────────────────────────
    public void BuildFrom(MoveTree tree, string currentNodeId, Action<string> onMoveClicked)
    {
        _highlightedId = currentNodeId;
        _onMoveClicked = onMoveClicked;

        foreach (Transform child in listContainer)
            Destroy(child.gameObject);

        // Walk main line and build pairs
        RenderBranch(tree.Root, 0);

        // Scroll to highlighted move
        StartCoroutine(ScrollToHighlighted());
    }

    private void RenderBranch(MoveTree.Node startNode, int depth)
    {
        var node = startNode;
        TMP_Text lastWhiteSlot = null;

        while (node != null && node.Children.Count > 0)
        {
            var child = node.Children[0]; // main line

            if (child.Color == ChessBoard.WHITE || (string.IsNullOrEmpty(lastWhiteSlot?.text)))
            {
                // Create a pair row
                var pair = Instantiate(movePairPrefab, listContainer);
                var pairUI = pair.GetComponent<MovePairUI>();

                // White move
                if (child.Color == ChessBoard.WHITE)
                {
                    pairUI?.SetWhite(child.MoveNumber + ".", child.San, child.Id,
                                     child.Id == _highlightedId, OnMoveClick);
                    lastWhiteSlot = null;

                    // Black move may follow
                    if (child.Children.Count > 0 && child.Children[0].Color == ChessBoard.BLACK)
                    {
                        var blackChild = child.Children[0];
                        pairUI?.SetBlack(blackChild.San, blackChild.Id,
                                         blackChild.Id == _highlightedId, OnMoveClick);
                        node = blackChild;
                    }
                    else
                    {
                        node = child;
                    }
                }
                else
                {
                    // Black move first (in variations starting mid-game)
                    pairUI?.SetWhitePlaceholder(child.MoveNumber + "...");
                    pairUI?.SetBlack(child.San, child.Id,
                                     child.Id == _highlightedId, OnMoveClick);
                    node = child;
                }

                // Render variations as indented sub-lists
                var parent = node.Parent ?? startNode;
                if (parent != null)
                {
                    for (int i = 1; i < parent.Children.Count; i++)
                    {
                        RenderVariationLabel(depth + 1);
                        RenderBranch(parent.Children[i - 1], depth + 1);
                    }
                }
            }
            else
            {
                node = node.Children[0];
            }
        }
    }

    private void RenderVariationLabel(int depth)
    {
        // Could add a visual indent or bracket label here
    }

    private void OnMoveClick(string nodeId)
        => _onMoveClicked?.Invoke(nodeId);

    private System.Collections.IEnumerator ScrollToHighlighted()
    {
        yield return null; // Wait one frame for layout
        // Scroll to highlighted move button — TODO: find by ID and scrollRect.ScrollTo
    }
}

/// <summary>One row of two moves (white + black).</summary>
public class MovePairUI : MonoBehaviour
{
    [SerializeField] private TMP_Text moveNumberText;
    [SerializeField] private Button   whiteBtn;
    [SerializeField] private TMP_Text whiteText;
    [SerializeField] private Button   blackBtn;
    [SerializeField] private TMP_Text blackText;

    private static readonly Color ACTIVE_COLOR = new Color(0.2f, 0.6f, 1f);
    private static readonly Color NORMAL_COLOR = Color.white;

    public void SetWhite(string num, string san, string nodeId, bool active, Action<string> onClick)
    {
        if (moveNumberText != null) moveNumberText.text = num;
        if (whiteText != null)
        {
            whiteText.text  = san;
            whiteText.color = active ? ACTIVE_COLOR : NORMAL_COLOR;
        }
        whiteBtn?.onClick.AddListener(() => onClick?.Invoke(nodeId));
    }

    public void SetWhitePlaceholder(string num)
    {
        if (moveNumberText != null) moveNumberText.text = num;
        if (whiteText != null) whiteText.text = "...";
    }

    public void SetBlack(string san, string nodeId, bool active, Action<string> onClick)
    {
        if (blackText != null)
        {
            blackText.text  = san;
            blackText.color = active ? ACTIVE_COLOR : NORMAL_COLOR;
        }
        blackBtn?.onClick.AddListener(() => onClick?.Invoke(nodeId));
    }
}
