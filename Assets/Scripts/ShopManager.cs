using UnityEngine;
using System;

/// <summary>
/// ╔══════════════════════════════════════════════════════════════════╗
///   ATEŞ HATTI — ShopManager (Yükseltme Mağaza Yöneticisi)
///   
///   Upgrade satın alma mantığını yöneten, maliyetleri hesaplayan,
///   GoldManager ile altın harcayan ve stat'ları uygulayan sistem.
/// ╚══════════════════════════════════════════════════════════════════╝
/// 
/// Sorumluluk (SRP): SADECE satın alma mantığı ve maliyet hesabı.
///   - Altın kontrolü → GoldManager'a delege eder
///   - Veri kaydetme → SaveManager'a delege eder
///   - Stat uygulama → ilgili sisteme (PlayerShooting, CombatManager, PlayerHealth)
///   - UI gösterimi → ShopUI (ayrı script, ileride)
/// 
/// Upgrade Maliyet Formülü:
/// ────────────────────────
///   cost = CeilToInt(baseCost × Pow(costMultiplier, currentLevel))
/// 
///   Örnek (FireRate, baseCost=10, multiplier=1.6):
///     Lv 0→1:  10 × 1.6^0 = 10 Gold
///     Lv 1→2:  10 × 1.6^1 = 16 Gold
///     Lv 2→3:  10 × 1.6^2 = 26 Gold
///     Lv 3→4:  10 × 1.6^3 = 41 Gold
/// </summary>
public class ShopManager : MonoBehaviour
{
    // ════════════════════════════════════════════════════════════════
    //  SINGLETON
    // ════════════════════════════════════════════════════════════════

    public static ShopManager Instance { get; private set; }

    // ════════════════════════════════════════════════════════════════
    //  UPGRADE KONFİGÜRASYONLARI
    // ════════════════════════════════════════════════════════════════

    [System.Serializable]
    private struct UpgradeConfig
    {
        public UpgradeType type;
        public int         baseCost;
        public float       costMultiplier;
        public int         maxLevel;
        public string      displayName;

        public UpgradeConfig(UpgradeType type, int baseCost, float costMultiplier,
                             int maxLevel, string displayName)
        {
            this.type           = type;
            this.baseCost       = baseCost;
            this.costMultiplier = costMultiplier;
            this.maxLevel       = maxLevel;
            this.displayName    = displayName;
        }
    }

    private readonly UpgradeConfig[] _configs = new UpgradeConfig[]
    {
        new UpgradeConfig(UpgradeType.FireRate, 10,  1.6f, 8,  "ATEŞ HIZI"),
        new UpgradeConfig(UpgradeType.Damage,   20,  1.5f, 10, "HASAR"),
        new UpgradeConfig(UpgradeType.MaxHP,    15,  1.5f, 8,  "KALKAN"),
    };

    // ════════════════════════════════════════════════════════════════
    //  STAT BASE DEĞERLERİ
    // ════════════════════════════════════════════════════════════════

    private const float BASE_FIRE_INTERVAL = 0.65f;
    private const float MIN_FIRE_INTERVAL  = 0.08f;
    private const float FIRE_RATE_PER_LVL  = -0.025f;

    private const float BASE_DAMAGE        = 1f;
    private const float DAMAGE_PER_LVL     = 1f;

    private const float BASE_MAX_HP        = 1f;
    private const float HP_PER_LVL         = 1f;

    // ════════════════════════════════════════════════════════════════
    //  EVENTS
    // ════════════════════════════════════════════════════════════════

    /// <summary>
    /// Bir upgrade satın alındığında tetiklenir.
    /// Parametre: Satın alınan upgrade tipi.
    /// </summary>
    public event Action<UpgradeType> OnUpgradePurchased;

    // ════════════════════════════════════════════════════════════════
    //  INSPECTOR
    // ════════════════════════════════════════════════════════════════

    [Header("─── Oyuncu Sistemleri ───")]
    [Tooltip("Ateş hızı upgrade'ini uygulamak için.")]
    [SerializeField]
    private PlayerShooting _playerShooting;

    [Tooltip("Max HP upgrade'ini uygulamak için.")]
    [SerializeField]
    private PlayerHealth _playerHealth;

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

    private void Start()
    {
        ValidateSetup();
        ApplyAllUpgradesToSystems();
    }

    private void OnDestroy()
    {
        if (Instance == this)
            Instance = null;
    }

    // ════════════════════════════════════════════════════════════════
    //  MALİYET HESAPLAMA
    // ════════════════════════════════════════════════════════════════

    /// <summary>
    /// Bir sonraki seviyenin maliyetini hesaplar.
    /// maxLevel'a ulaşıldıysa -1 döner.
    /// </summary>
    public int GetUpgradeCost(UpgradeType type)
    {
        int configIndex = GetConfigIndex(type);
        if (configIndex < 0) return -1;

        UpgradeConfig config = _configs[configIndex];
        int currentLevel = SaveManager.Instance != null
            ? SaveManager.Instance.GetUpgradeLevel(type)
            : 0;

        if (currentLevel >= config.maxLevel)
            return -1;

        return Mathf.CeilToInt(config.baseCost * Mathf.Pow(config.costMultiplier, currentLevel));
    }

    public int GetCurrentLevel(UpgradeType type)
    {
        return SaveManager.Instance != null
            ? SaveManager.Instance.GetUpgradeLevel(type)
            : 0;
    }

    public bool IsMaxLevel(UpgradeType type)
    {
        int configIndex = GetConfigIndex(type);
        if (configIndex < 0) return true;

        return GetCurrentLevel(type) >= _configs[configIndex].maxLevel;
    }

    public string GetDisplayName(UpgradeType type)
    {
        int configIndex = GetConfigIndex(type);
        return configIndex >= 0 ? _configs[configIndex].displayName : "";
    }

    public int GetMaxLevel(UpgradeType type)
    {
        int configIndex = GetConfigIndex(type);
        return configIndex >= 0 ? _configs[configIndex].maxLevel : 0;
    }

    // ════════════════════════════════════════════════════════════════
    //  SATIN ALMA
    // ════════════════════════════════════════════════════════════════

    /// <summary>
    /// Upgrade satın almayı dener.
    /// 
    /// Akış:
    /// 1. Max seviye kontrolü
    /// 2. Maliyet hesapla
    /// 3. GoldManager.TrySpendGold(cost) → altın yeterli mi?
    /// 4. Yeterliyse: seviye artır + kaydet + stat uygula + event
    /// 5. Yetersizse: false dön
    /// 
    /// Dönüş: true = satın alındı, false = başarısız.
    /// </summary>
    public bool TryBuyUpgrade(UpgradeType type)
    {
        if (SaveManager.Instance == null || GoldManager.Instance == null)
        {
            LogWarning("SaveManager veya GoldManager bulunamadı — satın alma iptal.");
            return false;
        }

        if (IsMaxLevel(type))
        {
            #if UNITY_EDITOR || DEVELOPMENT_BUILD
            Debug.Log($"[ShopManager] {type} zaten maksimum seviyede.");
            #endif
            return false;
        }

        int cost = GetUpgradeCost(type);
        if (cost < 0) return false;

        if (!GoldManager.Instance.TrySpendGold(cost))
        {
            #if UNITY_EDITOR || DEVELOPMENT_BUILD
            Debug.Log($"[ShopManager] Yetersiz altın! Gereken: {cost}, Mevcut: {GoldManager.Instance.CurrentGold}");
            #endif
            return false;
        }

        // ── Seviyeyi artır ve kaydet ──
        SaveManager.Instance.IncrementUpgradeLevel(type);
        SaveManager.Instance.Save();

        // ── Stat uygula ──
        ApplyUpgradeToSystem(type);

        // ── Event tetikle ──
        OnUpgradePurchased?.Invoke(type);

        #if UNITY_EDITOR || DEVELOPMENT_BUILD
        int newLevel = SaveManager.Instance.GetUpgradeLevel(type);
        Debug.Log($"[ShopManager] {type} upgrade! Lv{newLevel - 1} → Lv{newLevel} | " +
                  $"Harcanan: {cost}G | Kalan: {GoldManager.Instance.CurrentGold}G");
        #endif

        return true;
    }

    // ── Button OnClick convenience method'ları ──
    // Unity Inspector'da enum parametre verilemediği için
    // her upgrade tipi için wrapper method gerekli.

    /// <summary>UI Button → OnClick'e bağlanır.</summary>
    public void BuyFireRate() => TryBuyUpgrade(UpgradeType.FireRate);

    /// <summary>UI Button → OnClick'e bağlanır.</summary>
    public void BuyDamage() => TryBuyUpgrade(UpgradeType.Damage);

    /// <summary>UI Button → OnClick'e bağlanır.</summary>
    public void BuyMaxHP() => TryBuyUpgrade(UpgradeType.MaxHP);

    // ════════════════════════════════════════════════════════════════
    //  STAT UYGULAMA
    // ════════════════════════════════════════════════════════════════

    /// <summary>Tüm kayıtlı upgrade'leri oyuncu sistemlerine uygular.</summary>
    private void ApplyAllUpgradesToSystems()
    {
        ApplyUpgradeToSystem(UpgradeType.FireRate);
        ApplyUpgradeToSystem(UpgradeType.Damage);
        ApplyUpgradeToSystem(UpgradeType.MaxHP);
    }

    /// <summary>Tek bir upgrade tipinin stat'ını ilgili sisteme uygular.</summary>
    private void ApplyUpgradeToSystem(UpgradeType type)
    {
        if (SaveManager.Instance == null) return;

        int level = SaveManager.Instance.GetUpgradeLevel(type);

        switch (type)
        {
            case UpgradeType.FireRate:
                if (_playerShooting != null)
                {
                    float newInterval = Mathf.Max(
                        MIN_FIRE_INTERVAL,
                        BASE_FIRE_INTERVAL + level * FIRE_RATE_PER_LVL
                    );
                    _playerShooting.SetFireInterval(newInterval);
                }
                break;

            case UpgradeType.Damage:
                if (CombatManager.Instance != null)
                {
                    float newDamage = BASE_DAMAGE + level * DAMAGE_PER_LVL;
                    CombatManager.Instance.SetBulletDamage(newDamage);
                }
                break;

            case UpgradeType.MaxHP:
                if (_playerHealth != null)
                {
                    float newMaxHp = BASE_MAX_HP + level * HP_PER_LVL;
                    _playerHealth.UpgradeMaxHp(newMaxHp);
                }
                break;
        }
    }

    // ════════════════════════════════════════════════════════════════
    //  STAT HESAPLAMA (UI PREVIEW İÇİN)
    // ════════════════════════════════════════════════════════════════

    /// <summary>Belirtilen seviyedeki stat değerini hesaplar.</summary>
    public float CalculateStatValue(UpgradeType type, int level)
    {
        return type switch
        {
            UpgradeType.FireRate => Mathf.Max(MIN_FIRE_INTERVAL,
                                              BASE_FIRE_INTERVAL + level * FIRE_RATE_PER_LVL),
            UpgradeType.Damage   => BASE_DAMAGE + level * DAMAGE_PER_LVL,
            UpgradeType.MaxHP    => BASE_MAX_HP + level * HP_PER_LVL,
            _                    => 0f
        };
    }

    // ════════════════════════════════════════════════════════════════
    //  YARDIMCI
    // ════════════════════════════════════════════════════════════════

    private int GetConfigIndex(UpgradeType type)
    {
        for (int i = 0; i < _configs.Length; i++)
        {
            if (_configs[i].type == type)
                return i;
        }
        return -1;
    }

    // ════════════════════════════════════════════════════════════════
    //  DOĞRULAMA
    // ════════════════════════════════════════════════════════════════

    [System.Diagnostics.Conditional("UNITY_EDITOR"),
     System.Diagnostics.Conditional("DEVELOPMENT_BUILD")]
    private void ValidateSetup()
    {
        if (SaveManager.Instance == null)
            Debug.LogError("[ShopManager] SaveManager bulunamadı!", this);

        if (_playerShooting == null)
            Debug.LogWarning("[ShopManager] PlayerShooting atanmamış — " +
                             "Fire Rate upgrade'i uygulanamayacak.", this);

        if (_playerHealth == null)
            Debug.LogWarning("[ShopManager] PlayerHealth atanmamış — " +
                             "Max HP upgrade'i uygulanamayacak.", this);
    }

    [System.Diagnostics.Conditional("UNITY_EDITOR"),
     System.Diagnostics.Conditional("DEVELOPMENT_BUILD")]
    private void LogWarning(string message)
    {
        Debug.LogWarning($"[ShopManager] {message}", this);
    }
}
