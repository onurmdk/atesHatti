using UnityEngine;
using TMPro;

public class HUDController : MonoBehaviour
{
    [Header("─── Üst HUD Metinleri ───")]
    [Tooltip("Altın miktarını gösteren text.\n" +
             "Format: 'GOLD: 150'")]
    [SerializeField]
    private TextMeshProUGUI _goldText;

    [Tooltip("Oyuncu canını gösteren text.\n" +
             "Format: 'HP 7/10'")]
    [SerializeField]
    private TextMeshProUGUI _hpText;

    [Tooltip("Öldürülen düşman sayısını gösteren text.\n" +
             "Format: 'KILLS: 42'")]
    [SerializeField]
    private TextMeshProUGUI _killsText;

    [Tooltip("Oyun süresini gösteren text.\n" +
             "Format: 'TIME: 1:05'")]
    [SerializeField]
    private TextMeshProUGUI _timeText;

    [Header("─── Oyuncu Referansı ───")]
    [Tooltip("PlayerHealth bileşenine sahip Player objesi.")]
    [SerializeField]
    private PlayerHealth _playerHealth;

    private int _killCount;

    private int _lastDisplayedSecond = -1;

    private void Start()
    {
        ValidateReferences();
        SubscribeToEvents();
        InitializeUI();
    }

    private void Update()
    {
        UpdateTimeDisplay();
    }

    private void OnDestroy()
    {
        UnsubscribeFromEvents();
    }

    private void SubscribeToEvents()
    {
        if (GoldManager.Instance != null)
        {
            GoldManager.Instance.OnRunGoldChanged += HandleGoldChanged;
        }
        else
        {
            LogWarning("GoldManager bulunamadı — altın göstergesi çalışmayacak.");
        }

        if (_playerHealth != null)
        {
            _playerHealth.OnHpChanged += HandleHpChanged;
        }
        else
        {
            LogWarning("PlayerHealth atanmamış — HP göstergesi çalışmayacak.");
        }

        if (CombatManager.Instance != null)
        {
            CombatManager.Instance.OnEnemyKilled += HandleEnemyKilled;
        }
        else
        {
            LogWarning("CombatManager bulunamadı — kill sayacı çalışmayacak.");
        }
    }

    private void UnsubscribeFromEvents()
    {
        if (GoldManager.Instance != null)
            GoldManager.Instance.OnRunGoldChanged -= HandleGoldChanged;

        if (_playerHealth != null)
            _playerHealth.OnHpChanged -= HandleHpChanged;

        if (CombatManager.Instance != null)
            CombatManager.Instance.OnEnemyKilled -= HandleEnemyKilled;
    }

    private void HandleGoldChanged(int newGoldAmount)
    {
        if (_goldText != null)
        {
            _goldText.SetText("GOLD: " + newGoldAmount.ToString());
        }
    }

    private void HandleHpChanged(float currentHp, float maxHp)
    {
        if (_hpText != null)
        {
            int current = Mathf.FloorToInt(currentHp);
            int max     = Mathf.FloorToInt(maxHp);
            _hpText.SetText("HP " + current.ToString() + "/" + max.ToString());
        }
    }

    private void HandleEnemyKilled(int goldValue, Vector3 position)
    {
        _killCount++;

        if (_killsText != null)
        {
            _killsText.SetText("KILLS: " + _killCount.ToString());
        }
    }

    private void UpdateTimeDisplay()
    {
        if (_timeText == null) return;

        if (GameManager.Instance == null) return;

        float elapsed = GameManager.Instance.ElapsedTime;
        int totalSeconds = Mathf.FloorToInt(elapsed);

        if (totalSeconds == _lastDisplayedSecond)
            return;

        _lastDisplayedSecond = totalSeconds;

        int minutes = totalSeconds / 60;
        int seconds = totalSeconds % 60;

        _timeText.SetText("TIME: " + minutes.ToString() + ":" + seconds.ToString("D2"));
    }

    private void InitializeUI()
    {
        HandleGoldChanged(0);

        if (_playerHealth != null)
        {
            HandleHpChanged(_playerHealth.CurrentHp, _playerHealth.MaxHp);
        }
        else
        {
            if (_hpText != null)
                _hpText.SetText("HP 0/0");
        }

        _killCount = 0;
        if (_killsText != null)
            _killsText.SetText("KILLS: 0");

        _lastDisplayedSecond = -1; 
        if (_timeText != null)
            _timeText.SetText("TIME: 0:00");
    }

    public void RefreshAll()
    {
        InitializeUI();
    }

    public int KillCount => _killCount;

    [System.Diagnostics.Conditional("UNITY_EDITOR"),
     System.Diagnostics.Conditional("DEVELOPMENT_BUILD")]
    private void ValidateReferences()
    {
        if (_goldText == null)
            Debug.LogError("[HUDController] GoldText atanmamış!", this);

        if (_hpText == null)
            Debug.LogError("[HUDController] HpText atanmamış!", this);

        if (_killsText == null)
            Debug.LogWarning("[HUDController] KillsText atanmamış.", this);

        if (_timeText == null)
            Debug.LogWarning("[HUDController] TimeText atanmamış.", this);

        if (_playerHealth == null)
            Debug.LogError("[HUDController] PlayerHealth atanmamış!", this);
    }

    [System.Diagnostics.Conditional("UNITY_EDITOR"),
     System.Diagnostics.Conditional("DEVELOPMENT_BUILD")]
    private void LogWarning(string message)
    {
        Debug.LogWarning($"[HUDController] {message}", this);
    }
}