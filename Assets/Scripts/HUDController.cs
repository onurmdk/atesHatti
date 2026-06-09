using UnityEngine;
using TMPro;

/// <summary>
/// ╔══════════════════════════════════════════════════════════════════╗
///   ATEŞ HATTI — HUDController (Baş Üstü Gösterge Kontrolcüsü)
///   
///   Oyun içi üst HUD barındaki tüm bilgi metinlerini
///   (Gold, HP, Kills, Time) event-driven mimariyle güncelleyen sistem.
/// ╚══════════════════════════════════════════════════════════════════╝
/// 
/// Sorumluluk (SRP): SADECE UI text'lerini günceller.
///   Veri kaynağı değildir — GoldManager, PlayerHealth, CombatManager
///   ve GameManager'dan event/property dinleyerek UI'ı senkronize eder.
/// 
/// Güncelleme Stratejisi:
/// ─────────────────────
///   Gold  → Event-driven (GoldManager.OnGoldChanged)
///   HP    → Event-driven (PlayerHealth.OnHpChanged)
///   Kills → Event-driven (CombatManager.OnEnemyKilled)
///   Time  → Update (GameManager.ElapsedTime) — AMA sadece saniye
///           değiştiğinde string oluşturulur (frame başına değil)
/// 
/// GC Analizi:
/// ───────────
///   Gold/HP/Kills: Event tetiklendiğinde string oluşur (saniyede 0-5 kez)
///   Time: Saniyede 1 string oluşur (_lastDisplayedSecond cache'i ile)
///   Toplam: ~6 string/saniye — mobilde kabul edilebilir
///   Karşılaştırma: Her frame polling yapılsaydı → 240 string/saniye
/// </summary>
public class HUDController : MonoBehaviour
{
    // ════════════════════════════════════════════════════════════════
    //  INSPECTOR — UI TEXT REFERANSLARI
    // ════════════════════════════════════════════════════════════════

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

    // ════════════════════════════════════════════════════════════════
    //  STATE
    // ════════════════════════════════════════════════════════════════

    /// <summary>Toplam öldürülen düşman sayısı.</summary>
    private int _killCount;

    /// <summary>
    /// Son gösterilen saniye değeri.
    /// Time text'i sadece bu değer değiştiğinde güncellenir.
    /// 60 FPS'de saniyede 60 yerine 1 string allocation.
    /// </summary>
    private int _lastDisplayedSecond = -1;

    // ════════════════════════════════════════════════════════════════
    //  UNITY LIFECYCLE
    // ════════════════════════════════════════════════════════════════

    /// <summary>
    /// Start'ta subscribe — tüm Awake'ler tamamlandıktan sonra.
    /// Ardından UI'ı mevcut değerlerle başlat.
    /// </summary>
    private void Start()
    {
        ValidateReferences();
        SubscribeToEvents();
        InitializeUI();
    }

    /// <summary>
    /// Süre göstergesi Update'te güncellenir.
    /// 
    /// Neden event-driven değil?
    /// → GameManager'da elapsed time her frame artar ama
    ///   "saniye değişti" event'i yok (gereksiz complexity olurdu).
    ///   Bunun yerine Update'te sadece saniye değiştiğinde
    ///   string oluşturuyoruz — yılda 1 if kontrolü vs 1 string.
    /// 
    /// Optimizasyon: _lastDisplayedSecond cache'i
    /// → 60 FPS'de saniyede 60 frame çalışır ama sadece 1'inde
    ///   string oluşturulur. Diğer 59 frame'de tek bir int
    ///   karşılaştırma yapılır (== kontrolü) — maliyeti ~0.
    /// </summary>
    private void Update()
    {
        UpdateTimeDisplay();
    }

    /// <summary>
    /// Event'lerden temiz çıkış — memory leak önleme.
    /// </summary>
    private void OnDestroy()
    {
        UnsubscribeFromEvents();
    }

    // ════════════════════════════════════════════════════════════════
    //  EVENT SUBSCRIPTION
    // ════════════════════════════════════════════════════════════════

    /// <summary>
    /// İlgili event'lere abone olur.
    /// Null check ile güvenli — manager sahnede yoksa sessizce atlanır.
    /// </summary>
    private void SubscribeToEvents()
    {
        // ── GoldManager → Altın text (Bu run'daki kazanç) ──
        if (GoldManager.Instance != null)
        {
            GoldManager.Instance.OnRunGoldChanged += HandleGoldChanged;
        }
        else
        {
            LogWarning("GoldManager bulunamadı — altın göstergesi çalışmayacak.");
        }

        // ── PlayerHealth → HP text ──
        if (_playerHealth != null)
        {
            _playerHealth.OnHpChanged += HandleHpChanged;
        }
        else
        {
            LogWarning("PlayerHealth atanmamış — HP göstergesi çalışmayacak.");
        }

        // ── CombatManager → Kill counter ──
        if (CombatManager.Instance != null)
        {
            CombatManager.Instance.OnEnemyKilled += HandleEnemyKilled;
        }
        else
        {
            LogWarning("CombatManager bulunamadı — kill sayacı çalışmayacak.");
        }
    }

    /// <summary>
    /// Tüm event'lerden çıkış.
    /// -= operatörü abone olunmamış event'ten çıkış yapmaya çalışırsa
    /// hata vermez — güvenle çağrılabilir.
    /// </summary>
    private void UnsubscribeFromEvents()
    {
        if (GoldManager.Instance != null)
            GoldManager.Instance.OnRunGoldChanged -= HandleGoldChanged;

        if (_playerHealth != null)
            _playerHealth.OnHpChanged -= HandleHpChanged;

        if (CombatManager.Instance != null)
            CombatManager.Instance.OnEnemyKilled -= HandleEnemyKilled;
    }

    // ════════════════════════════════════════════════════════════════
    //  EVENT HANDLERS
    // ════════════════════════════════════════════════════════════════

    /// <summary>
    /// Altın değiştiğinde çağrılır.
    /// GoldManager.OnGoldChanged → bu handler.
    /// </summary>
    private void HandleGoldChanged(int newGoldAmount)
    {
        if (_goldText != null)
        {
            _goldText.SetText("GOLD: " + newGoldAmount.ToString());
        }
    }

    /// <summary>
    /// HP değiştiğinde çağrılır.
    /// PlayerHealth.OnHpChanged → bu handler.
    /// 
    /// Format: "HP 7/10"
    /// FloorToInt kullanılıyor — 7.3 HP → "HP 7/10" (küsurat gösterilmez)
    /// </summary>
    private void HandleHpChanged(float currentHp, float maxHp)
    {
        if (_hpText != null)
        {
            int current = Mathf.FloorToInt(currentHp);
            int max     = Mathf.FloorToInt(maxHp);
            _hpText.SetText("HP " + current.ToString() + "/" + max.ToString());
        }
    }

    /// <summary>
    /// Düşman öldürüldüğünde çağrılır.
    /// CombatManager.OnEnemyKilled → bu handler.
    /// 
    /// Parametreler:
    ///   goldValue → kullanılmıyor (GoldManager zaten altını topluyor)
    ///   position  → kullanılmıyor (floating text için ileride kullanılabilir)
    /// 
    /// Kill count bu script'te tutulur — merkezi bir "ScoreManager" yok henüz.
    /// İleride ScoreManager eklenirse bu sorumluluk oraya taşınır.
    /// </summary>
    private void HandleEnemyKilled(int goldValue, Vector3 position)
    {
        _killCount++;

        if (_killsText != null)
        {
            _killsText.SetText("KILLS: " + _killCount.ToString());
        }
    }

    // ════════════════════════════════════════════════════════════════
    //  SÜRE GÖSTERGESİ — Optimize Edilmiş Update
    // ════════════════════════════════════════════════════════════════

    /// <summary>
    /// Süre text'ini günceller — AMA sadece saniye değiştiğinde.
    /// 
    /// 60 FPS'de her frame çağrılır ama:
    ///   - 59 frame'de: tek bir int karşılaştırma (== kontrolü) → ~0 maliyet
    ///   - 1 frame'de: string oluşturma + SetText → kabul edilebilir
    /// 
    /// Sonuç: Saniyede 60 yerine 1 string allocation.
    /// </summary>
    private void UpdateTimeDisplay()
    {
        if (_timeText == null) return;

        // GameManager yoksa veya oyun oynamıyorsa güncelleme
        if (GameManager.Instance == null) return;

        float elapsed = GameManager.Instance.ElapsedTime;
        int totalSeconds = Mathf.FloorToInt(elapsed);

        // Saniye değişmediyse hiçbir şey yapma — string allocation yok
        if (totalSeconds == _lastDisplayedSecond)
            return;

        _lastDisplayedSecond = totalSeconds;

        int minutes = totalSeconds / 60;
        int seconds = totalSeconds % 60;

        // "TIME: 1:05" formatı
        _timeText.SetText("TIME: " + minutes.ToString() + ":" + seconds.ToString("D2"));
    }

    // ════════════════════════════════════════════════════════════════
    //  BAŞLANGIÇ DEĞERLERİ
    // ════════════════════════════════════════════════════════════════

    /// <summary>
    /// UI'ı mevcut değerlerle başlatır.
    /// 
    /// Neden gerekli?
    /// → Event'ler sadece DEĞİŞİKLİK olduğunda tetiklenir.
    /// → Oyun başladığında altın 0, HP full — ama event tetiklenmemiş.
    /// → Bu metod başlangıç değerlerini zorla set eder.
    /// </summary>
    private void InitializeUI()
    {
        // ── Altın (Bu run: her zaman 0'dan başlar) ──
        HandleGoldChanged(0);

        // ── HP ──
        if (_playerHealth != null)
        {
            HandleHpChanged(_playerHealth.CurrentHp, _playerHealth.MaxHp);
        }
        else
        {
            if (_hpText != null)
                _hpText.SetText("HP 0/0");
        }

        // ── Kills ──
        _killCount = 0;
        if (_killsText != null)
            _killsText.SetText("KILLS: 0");

        // ── Time ──
        _lastDisplayedSecond = -1; // Zorla ilk güncellemeyi tetikle
        if (_timeText != null)
            _timeText.SetText("TIME: 0:00");
    }

    // ════════════════════════════════════════════════════════════════
    //  PUBLIC API
    // ════════════════════════════════════════════════════════════════

    /// <summary>
    /// Tüm UI'ı dışarıdan zorla günceller.
    /// GameManager restart sonrası çağırabilir.
    /// </summary>
    public void RefreshAll()
    {
        InitializeUI();
    }

    /// <summary>Mevcut kill sayısı (istatistik için).</summary>
    public int KillCount => _killCount;

    // ════════════════════════════════════════════════════════════════
    //  DOĞRULAMA
    // ════════════════════════════════════════════════════════════════

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
