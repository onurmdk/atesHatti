using UnityEngine;
using System;

/// <summary>
/// ╔══════════════════════════════════════════════════════════════════╗
///   ATEŞ HATTI — GoldManager (Altın Ekonomi Yöneticisi)
///   
///   Oyuncu altınını takip eden, düşman ölümlerinden altın toplayan
///   ve değişiklikleri event ile duyuran merkezi ekonomi sistemi.
/// ╚══════════════════════════════════════════════════════════════════╝
/// 
/// Sorumluluk (SRP): SADECE altın miktarını yönetir.
///   - Nereden geldiği: CombatManager.OnEnemyKilled event'i
///   - Nereye gittiği: ShopManager (upgrade satın alma)
///   - Kim dinliyor: HUDController (UI güncelleme), ShopManager (buton aktifliği)
/// 
/// Event-Driven Akış:
/// ─────────────────
///   CombatManager.OnEnemyKilled(goldValue, position)
///           │
///           ▼
///   GoldManager.HandleEnemyKilled(goldValue, position)
///           │
///           ▼  _currentGold += goldValue
///           │
///           ▼
///   GoldManager.OnGoldChanged(newGoldAmount)
///           │
///           ├──▶ HUDController → "ALTIN: 150" text güncelle
///           └──▶ ShopManager   → buton aktifliğini kontrol et
/// 
/// Neden Update() yok?
/// → Tüm iletişim event-driven. Gold sadece düşman öldüğünde
///   veya upgrade satın alındığında değişir.
///   Her frame kontrol etmek gereksiz CPU harcaması.
/// </summary>
public class GoldManager : MonoBehaviour
{
    // ════════════════════════════════════════════════════════════════
    //  SINGLETON
    // ════════════════════════════════════════════════════════════════

    public static GoldManager Instance { get; private set; }

    // ════════════════════════════════════════════════════════════════
    //  EVENTS
    // ════════════════════════════════════════════════════════════════

    /// <summary>
    /// Altın miktarı değiştiğinde tetiklenir.
    /// Parametre: Yeni toplam altın miktarı.
    /// 
    /// Neden (int newTotal) ve (int delta) değil?
    /// → Dinleyiciler genellikle toplam miktarı göstermek ister:
    ///   "ALTIN: 150" — delta bilgisi burada gereksiz.
    /// </summary>
    public event Action<int> OnGoldChanged;

    /// <summary>
    /// Mevcut RUN'da kazanılan altın değiştiğinde tetiklenir.
    /// Parametre: Bu run'da kazanılan toplam altın.
    /// HUDController bu event'i dinler — oyun içi "GOLD: X" göstergesi.
    /// 
    /// Neden OnGoldChanged'den ayrı?
    /// → OnGoldChanged toplam (persistent) altını yansıtır.
    ///   OnRunGoldChanged sadece bu oturumdaki kazancı yansıtır.
    ///   HUD'da oyuncu "bu oyunda ne kazandım?" görmeli,
    ///   "toplam birikimim ne?" değil.
    /// </summary>
    public event Action<int> OnRunGoldChanged;

    // ════════════════════════════════════════════════════════════════
    //  STATE
    // ════════════════════════════════════════════════════════════════

    private int _currentGold;

    /// <summary>
    /// Bu oturumda (run) kazanılan altın.
    /// Her oyun başlangıcında sıfırlanır.
    /// HUD göstergesi ve Game Over istatistiği için.
    /// </summary>
    private int _currentRunGold;

    /// <summary>
    /// Toplam kazanılan altın (oyun sonu istatistik için).
    /// Harcanan altın bu değerden düşülmez.
    /// </summary>
    private int _totalGoldEarned;

    // ════════════════════════════════════════════════════════════════
    //  UNITY LIFECYCLE
    // ════════════════════════════════════════════════════════════════

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;
    }

    /// <summary>
    /// Start, tüm Awake'lerden sonra çağrılır.
    /// 
    /// Neden Awake'te değil de Start'ta subscribe ediyoruz?
    /// ──────────────────────────────────────────────────────
    /// Unity'de Awake çağrı sırası garanti DEĞİLDİR.
    /// CombatManager.Instance, GoldManager.Awake() sırasında
    /// henüz null olabilir (CombatManager.Awake henüz çalışmamışsa).
    /// 
    /// Start tüm Awake'lerden sonra çağrılır → tüm singleton'lar hazır.
    /// Script Execution Order'a bağımlı olmayan güvenli çözüm.
    /// </summary>
    private void Start()
    {
        // ── Kayıtlı altını yükle ──
        // SaveManager.Awake'te JSON'dan veri okundu.
        // Start tüm Awake'lerden sonra çağrılır → SaveManager.Instance hazır.
        // Bu sayede oyuncu önceki oturumdan kalan altınıyla başlar.
        if (SaveManager.Instance != null)
        {
            _currentGold = SaveManager.Instance.PersistentGold;

            #if UNITY_EDITOR || DEVELOPMENT_BUILD
            Debug.Log($"[GoldManager] Kayıtlı altın yüklendi: {_currentGold}");
            #endif

            // UI'ın başlangıç değerini alması için event tetikle
            OnGoldChanged?.Invoke(_currentGold);
        }

        SubscribeToCombatEvents();
    }

    private void OnDestroy()
    {
        UnsubscribeFromCombatEvents();

        if (Instance == this)
            Instance = null;
    }

    // ════════════════════════════════════════════════════════════════
    //  EVENT SUBSCRIPTION
    // ════════════════════════════════════════════════════════════════

    private void SubscribeToCombatEvents()
    {
        if (CombatManager.Instance != null)
        {
            CombatManager.Instance.OnEnemyKilled += HandleEnemyKilled;
        }
        else
        {
            #if UNITY_EDITOR || DEVELOPMENT_BUILD
            Debug.LogWarning("[GoldManager] CombatManager.Instance bulunamadı! " +
                             "Altın toplama çalışmayacak.", this);
            #endif
        }
    }

    /// <summary>
    /// Event'ten çıkış.
    /// 
    /// Neden OnDestroy'da unsubscribe şart?
    /// ──────────────────────────────────────
    /// C# event'leri subscriber'a strong reference tutar.
    /// Unsubscribe yapılmazsa:
    ///   1. GoldManager yok edilse bile CombatManager onu referans tutar
    ///   2. GC, GoldManager'ı toplayamaz → memory leak
    ///   3. Event tetiklenince yok edilmiş objeye çağrı → MissingReferenceException
    /// </summary>
    private void UnsubscribeFromCombatEvents()
    {
        if (CombatManager.Instance != null)
        {
            CombatManager.Instance.OnEnemyKilled -= HandleEnemyKilled;
        }
    }

    // ════════════════════════════════════════════════════════════════
    //  EVENT HANDLER
    // ════════════════════════════════════════════════════════════════

    /// <summary>
    /// Düşman öldürüldüğünde CombatManager tarafından tetiklenir.
    /// position parametresi ileride floating gold text için kullanılacak.
    /// </summary>
    private void HandleEnemyKilled(int goldValue, Vector3 position)
    {
        AddGold(goldValue);
    }

    // ════════════════════════════════════════════════════════════════
    //  ALTIN YÖNETİMİ
    // ════════════════════════════════════════════════════════════════

    /// <summary>
    /// Altın ekler ve OnGoldChanged event'ini tetikler.
    /// Düşman ölümü dışında da çağrılabilir (reklam ödülü, bonus vb.).
    /// </summary>
    public void AddGold(int amount)
    {
        if (amount <= 0) return;

        _currentGold     += amount;
        _totalGoldEarned += amount;
        _currentRunGold  += amount;

        // ── Kalıcı kayıt: Altını JSON'a yaz ──
        PersistCurrentGold();

        OnGoldChanged?.Invoke(_currentGold);
        OnRunGoldChanged?.Invoke(_currentRunGold);
    }

    /// <summary>
    /// Altın harcar. ShopManager tarafından çağrılır.
    /// 
    /// Dönüş: true = yeterli altın vardı ve harcandı.
    ///         false = yetersiz altın, işlem iptal.
    /// 
    /// TrySpend pattern'i atomik işlem garantisi sağlar:
    /// "kontrol et + harca" tek metotta → race condition riski yok.
    /// </summary>
    public bool TrySpendGold(int amount)
    {
        if (amount <= 0 || _currentGold < amount)
            return false;

        _currentGold -= amount;

        // ── Kalıcı kayıt: Altını JSON'a yaz ──
        PersistCurrentGold();

        OnGoldChanged?.Invoke(_currentGold);
        return true;
    }

    /// <summary>
    /// Mevcut altını SaveManager'a aktarıp disk'e yazar.
    /// AddGold ve TrySpendGold'dan çağrılır — DRY prensibi.
    /// 
    /// Neden her değişiklikte kaydediyoruz?
    /// → Mobilde uygulama her an öldürülebilir.
    ///   Kaydetmezsek oyuncu 50 altın kazanıp tab değiştirirse
    ///   o altınlar kaybolur. Her değişiklikte kaydetmek güvenli.
    ///   Maliyet: ~0.1ms — saniyede 3-4 düşman ölse bile kabul edilebilir.
    /// </summary>
    private void PersistCurrentGold()
    {
        if (SaveManager.Instance != null)
        {
            SaveManager.Instance.SetPersistentGold(_currentGold);
            SaveManager.Instance.Save();
        }
    }

    // ════════════════════════════════════════════════════════════════
    //  PUBLIC API
    // ════════════════════════════════════════════════════════════════

    /// <summary>Mevcut altın miktarı.</summary>
    public int CurrentGold => _currentGold;

    /// <summary>Bu run'da kazanılan altın (HUD ve Game Over için).</summary>
    public int CurrentRunGold => _currentRunGold;

    /// <summary>Toplam kazanılan altın (game over istatistiği için).</summary>
    public int TotalGoldEarned => _totalGoldEarned;

    /// <summary>Belirtilen miktarı karşılayacak altın var mı?</summary>
    public bool CanAfford(int amount) => _currentGold >= amount;

    /// <summary>
    /// Tüm değerleri sıfırlar. Yeni oyun başlangıcında çağrılır.
    /// </summary>
    public void ResetGold()
    {
        _currentGold     = 0;
        _totalGoldEarned = 0;
        _currentRunGold  = 0;

        OnGoldChanged?.Invoke(_currentGold);
        OnRunGoldChanged?.Invoke(0);
    }
}