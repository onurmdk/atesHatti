using UnityEngine;
using UnityEngine.UI;
using TMPro;

/// <summary>
/// ╔══════════════════════════════════════════════════════════════════╗
///   ATEŞ HATTI — HUDController (Baş Üstü Gösterge Kontrolcüsü)
///   
///   Oyun içi UI elementlerini (altın, HP bar, bilgi metinleri)
///   event-driven mimariyle güncelleyen HUD yöneticisi.
/// ╚══════════════════════════════════════════════════════════════════╝
/// 
/// Sorumluluk (SRP): SADECE UI elementlerini günceller.
///   Veri kaynağı değildir — GoldManager ve PlayerHealth'ten
///   event dinleyerek UI'ı senkronize eder.
/// 
/// Event-Driven Mimari — Neden Update() Yok?
/// ──────────────────────────────────────────
/// Geleneksel yaklaşım:
///   void Update() {
///       goldText.text = "ALTIN: " + GoldManager.Instance.CurrentGold;
///       hpSlider.value = playerHealth.CurrentHp / playerHealth.MaxHp;
///   }
/// Bu her frame:
///   - 2 string concatenation → 2 GC allocation
///   - 2 singleton lookup → 2 method call
///   - 60fps × 2 allocation = 120 allocation/saniye
///   - Mobilde GC spike riski
/// 
/// Event-driven yaklaşım:
///   - Altın değiştiğinde → OnGoldChanged → text güncelle (saniyede 0-5 kez)
///   - HP değiştiğinde → OnHpChanged → slider güncelle (saniyede 0-3 kez)
///   - Toplam: ~8 güncelleme/saniye vs 120
///   - %93 daha az iş, SIFIR gereksiz allocation
/// 
/// ╔═══════════════════════════════════════════════════════════════╗
///   Unity Editor Kurulumu aşağıda ayrıntılı açıklanmıştır.
/// ╚═══════════════════════════════════════════════════════════════╝
/// </summary>
public class HUDController : MonoBehaviour
{
    // ════════════════════════════════════════════════════════════════
    //  INSPECTOR — UI REFERANSLARI
    // ════════════════════════════════════════════════════════════════

    [Header("─── Altın Göstergesi ───")]
    [Tooltip("Altın miktarını gösteren TextMeshPro UI elementi.\n" +
             "Hierarchy: Canvas → HUD_Panel → GoldText")]
    [SerializeField]
    private TextMeshProUGUI _goldText;

    [Header("─── HP Bar ───")]
    [Tooltip("Oyuncu sağlığını gösteren Slider.\n" +
             "Min: 0, Max: 1 (normalize edilmiş).")]
    [SerializeField]
    private Slider _hpSlider;

    [Tooltip("HP bar'ın doluluk rengini değiştiren Image.\n" +
             "Slider → Fill Area → Fill objesi.\n" +
             "HP düştükçe yeşilden kırmızıya geçiş yapar.")]
    [SerializeField]
    private Image _hpFillImage;

    [Header("─── HP Renk Geçişi ───")]
    [Tooltip("Tam HP rengi.")]
    [SerializeField]
    private Color _hpColorFull = new Color(0.18f, 0.84f, 0.45f, 1f);  // Yeşil

    [Tooltip("Sıfır HP rengi.")]
    [SerializeField]
    private Color _hpColorEmpty = new Color(1f, 0.27f, 0.34f, 1f);    // Kırmızı

    [Header("─── Oyuncu Referansı ───")]
    [Tooltip("PlayerHealth bileşenine sahip Player objesi.\n" +
             "Inspector'dan sürükle-bırak ile ata.")]
    [SerializeField]
    private PlayerHealth _playerHealth;

    // ════════════════════════════════════════════════════════════════
    //  STRING CACHE — GC Minimizasyonu
    // ════════════════════════════════════════════════════════════════

    /// <summary>
    /// Altın text prefix'i. Her güncelleme de yeniden oluşturulmaz.
    /// 
    /// String allocation analizi:
    /// ──────────────────────────
    /// $"ALTIN: {gold}" → her çağrıda yeni string (GC allocation).
    /// Ama bu event-driven — saniyede 0-5 kez çağrılır.
    /// 60fps polling'deki 120 allocation/s'ye kıyasla ihmal edilebilir.
    /// 
    /// Daha agresif optimizasyon (StringBuilder veya char[] buffer)
    /// bu ölçekte premature optimization olur — complexity'ye değmez.
    /// Ancak ileride 60+ düşman/saniye ölürse reconsider edilebilir.
    /// </summary>
    private const string GOLD_PREFIX = "ALTIN: ";

    // ════════════════════════════════════════════════════════════════
    //  UNITY LIFECYCLE
    // ════════════════════════════════════════════════════════════════

    /// <summary>
    /// Start'ta subscribe ediyoruz — Awake değil.
    /// Sebep: GoldManager ve CombatManager singleton'ları
    /// kendi Awake'lerinde Instance'larını set eder.
    /// Start tüm Awake'lerden sonra çağrılır → Instance'lar hazır.
    /// </summary>
    private void Start()
    {
        ValidateReferences();
        SubscribeToEvents();
        InitializeUI();
    }

    /// <summary>
    /// Event'lerden çıkış — memory leak önleme.
    /// 
    /// Senaryo: HUD objesi yok edilir ama GoldManager hayatta kalır.
    /// Unsubscribe yapılmazsa GoldManager hâlâ HUD'a referans tutar →
    /// event tetiklenince MissingReferenceException fırlar.
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
    /// Her subscribe'da null check yapılır — manager sahnede yoksa
    /// sessizce atlanır, NullReferenceException fırlamaz.
    /// </summary>
    private void SubscribeToEvents()
    {
        // ── GoldManager → Altın text güncellemesi ──
        if (GoldManager.Instance != null)
        {
            GoldManager.Instance.OnGoldChanged += HandleGoldChanged;
        }
        else
        {
            LogWarning("GoldManager.Instance bulunamadı — altın göstergesi çalışmayacak.");
        }

        // ── PlayerHealth → HP bar güncellemesi ──
        if (_playerHealth != null)
        {
            _playerHealth.OnHpChanged += HandleHpChanged;
        }
        else
        {
            LogWarning("PlayerHealth referansı atanmamış — HP bar çalışmayacak.");
        }
    }

    /// <summary>
    /// Tüm event'lerden temiz çıkış.
    /// 
    /// Savunmacı programlama: -= operatörü, abone olunmamış bir event'ten
    /// çıkış yapmaya çalışırsa hata vermez — güvenle çağrılabilir.
    /// Bu yüzden "abone miydim?" kontrolü gerekmez.
    /// </summary>
    private void UnsubscribeFromEvents()
    {
        // GoldManager hâlâ hayattaysa unsubscribe et
        if (GoldManager.Instance != null)
        {
            GoldManager.Instance.OnGoldChanged -= HandleGoldChanged;
        }

        // PlayerHealth referansı varsa unsubscribe et
        if (_playerHealth != null)
        {
            _playerHealth.OnHpChanged -= HandleHpChanged;
        }
    }

    // ════════════════════════════════════════════════════════════════
    //  EVENT HANDLERS
    // ════════════════════════════════════════════════════════════════

    /// <summary>
    /// Altın miktarı değiştiğinde çağrılır.
    /// GoldManager.OnGoldChanged → bu handler.
    /// 
    /// String allocation: Evet, her çağrıda 1 string oluşur.
    /// Ama event-driven olduğu için saniyede 0-5 kez — kabul edilebilir.
    /// </summary>
    private void HandleGoldChanged(int newGoldAmount)
    {
        if (_goldText != null)
        {
            // TextMeshPro SetText metodu internal buffer kullanır
            // ve string concatenation'dan daha verimlidir.
            // Ama int parametre desteği için format string gerekir.
            // Bu ölçekte fark ihmal edilebilir.
            _goldText.SetText(GOLD_PREFIX + newGoldAmount.ToString());
        }
    }

    /// <summary>
    /// HP değiştiğinde çağrılır.
    /// PlayerHealth.OnHpChanged → bu handler.
    /// 
    /// İki güncelleme yapılır:
    /// 1. Slider value → 0 ile 1 arası normalize edilmiş HP oranı
    /// 2. Fill rengi → HP oranına göre yeşil-kırmızı gradient
    /// 
    /// GC allocation: SIFIR (float assignment + Color.Lerp struct işlemi).
    /// </summary>
    private void HandleHpChanged(float currentHp, float maxHp)
    {
        if (_hpSlider == null) return;

        // ── Slider değerini güncelle ──
        // Guard: maxHp 0 olursa division by zero
        float ratio = maxHp > 0f ? currentHp / maxHp : 0f;
        _hpSlider.value = ratio;

        // ── Renk geçişi ──
        // Color.Lerp struct döner — heap allocation YOK.
        // ratio 1.0 → full HP color (yeşil)
        // ratio 0.0 → empty HP color (kırmızı)
        if (_hpFillImage != null)
        {
            _hpFillImage.color = Color.Lerp(_hpColorEmpty, _hpColorFull, ratio);
        }
    }

    // ════════════════════════════════════════════════════════════════
    //  BAŞLANGIÇ DEĞERLERİ
    // ════════════════════════════════════════════════════════════════

    /// <summary>
    /// UI'ı mevcut değerlerle başlatır.
    /// 
    /// Neden gerekli?
    /// → Event'ler sadece DEĞİŞİKLİK olduğunda tetiklenir.
    /// → Oyun başladığında altın 0 ve HP full — ama henüz event tetiklenmemiş.
    /// → UI boş kalır. Bu metod başlangıç değerlerini zorla set eder.
    /// </summary>
    private void InitializeUI()
    {
        // ── Altın ──
        int initialGold = GoldManager.Instance != null ? GoldManager.Instance.CurrentGold : 0;
        HandleGoldChanged(initialGold);

        // ── HP ──
        if (_playerHealth != null)
        {
            HandleHpChanged(_playerHealth.CurrentHp, _playerHealth.MaxHp);
        }
        else
        {
            // Fallback: Full HP göster
            if (_hpSlider != null)
                _hpSlider.value = 1f;

            if (_hpFillImage != null)
                _hpFillImage.color = _hpColorFull;
        }
    }

    // ════════════════════════════════════════════════════════════════
    //  PUBLIC API
    // ════════════════════════════════════════════════════════════════

    /// <summary>
    /// HUD'ı mevcut değerlerle yeniden senkronize eder.
    /// Yeni oyun başlatıldığında GameManager tarafından çağrılır.
    /// (Player reset edilir → HP değişir → ama HUD henüz subscribe
    /// olmamış olabilir. Bu metod zorla senkronize eder.)
    /// </summary>
    public void RefreshAll()
    {
        InitializeUI();
    }

    // ════════════════════════════════════════════════════════════════
    //  DOĞRULAMA
    // ════════════════════════════════════════════════════════════════

    [System.Diagnostics.Conditional("UNITY_EDITOR"),
     System.Diagnostics.Conditional("DEVELOPMENT_BUILD")]
    private void ValidateReferences()
    {
        if (_goldText == null)
            Debug.LogError("[HUDController] GoldText (TextMeshProUGUI) atanmamış! " +
                           "Inspector'da _goldText alanını doldurun.", this);

        if (_hpSlider == null)
            Debug.LogError("[HUDController] HP Slider atanmamış! " +
                           "Inspector'da _hpSlider alanını doldurun.", this);

        if (_hpFillImage == null)
            Debug.LogWarning("[HUDController] HP Fill Image atanmamış — " +
                             "renk geçişi çalışmayacak.", this);

        if (_playerHealth == null)
            Debug.LogError("[HUDController] PlayerHealth referansı atanmamış! " +
                           "Inspector'da _playerHealth alanını doldurun.", this);
    }

    [System.Diagnostics.Conditional("UNITY_EDITOR"),
     System.Diagnostics.Conditional("DEVELOPMENT_BUILD")]
    private void LogWarning(string message)
    {
        Debug.LogWarning($"[HUDController] {message}", this);
    }
}