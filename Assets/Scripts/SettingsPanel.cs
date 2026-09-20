using UnityEngine;
using TMPro;

/// <summary>
/// Main-menu settings panel. Kept separate from GameManager: it only
/// shows/hides itself and forwards a single destructive action.
/// </summary>
public class SettingsPanel : MonoBehaviour
{
    [Header("─── References ───")]
    [Tooltip("The panel GameObject to show and hide. Must start inactive.")]
    [SerializeField]
    private GameObject _panelRoot;

    [Tooltip("Label inside the delete button. Used for the confirm step.")]
    [SerializeField]
    private TextMeshProUGUI _deleteButtonLabel;

    private const string DELETE_LABEL  = "DELETE SAVE DATA";
    private const string CONFIRM_LABEL = "ARE YOU SURE?";

    private bool _awaitingConfirm;

    public void Open()
    {
        ResetConfirmState();

        if (_panelRoot != null)
            _panelRoot.SetActive(true);
    }

    public void Close()
    {
        ResetConfirmState();

        if (_panelRoot != null)
            _panelRoot.SetActive(false);
    }

    /// <summary>
    /// First press arms the action, second press performs it. Wiping
    /// progress is destructive and should never happen on a single tap.
    /// </summary>
    public void OnDeletePressed()
    {
        if (!_awaitingConfirm)
        {
            _awaitingConfirm = true;

            if (_deleteButtonLabel != null)
                _deleteButtonLabel.SetText(CONFIRM_LABEL);

            return;
        }

        if (SaveManager.Instance != null)
            SaveManager.Instance.DeleteAllData();

        // Reloading the scene is the simplest way to bring every system
        // back to a clean state after the save is wiped.
        if (GameManager.Instance != null)
            GameManager.Instance.ReturnToMainMenu();
    }

    private void ResetConfirmState()
    {
        _awaitingConfirm = false;

        if (_deleteButtonLabel != null)
            _deleteButtonLabel.SetText(DELETE_LABEL);
    }
}