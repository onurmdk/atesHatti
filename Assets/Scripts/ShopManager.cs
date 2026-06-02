using UnityEngine;
using UnityEngine.UI;
using TMPro;
using System;

/// <summary>
/// ╔══════════════════════════════════════════════════════════════════╗
///   ATEŞ HATTI — ShopManager (Çift Ekranlı Mağaza Yöneticisi)
///   
///   İki farklı UI panelini tek merkezden yöneten upgrade sistemi.
///   "Single Source of Truth" — tüm veri ve hesaplama burada,
///   iki UI katmanı sadece gösterim.
/// ╚══════════════════════════════════════════════════════════════════╝
/// 
/// Çift Ekran Mimarisi:
/// ────────────────────
/// 
/// ┌─────────────────────────────────────────────────────────────┐
/// │  DetailedShopPanel (Ana Menü Pop-up)                        │
/// │  ┌──────────────┐ ┌──────────────┐ ┌──────────────┐        │
/// │  │ Block_FireRate│ │ Block_Damage │ │ Block_MaxHp  │        │
/// │  │  Text_Info   │ │  Text_Info   │ │  Text_Info   │        │
/// │  │  Btn_Buy     │ │  Btn_Buy     │ │  Btn_Buy     │        │
/// │  └──────────────┘ └──────────────┘ └──────────────┘        │
/// │  Etkileşimli — Satın alma buradan yapılır                   │
/// │  Rich Text formatı (renkli, bold)                           │
/// └─────────────────────────────────────────────────────────────┘
/// 
/// ┌─────────────────────────────────────────────────────────────┐
/// │  ShopPanel / Info UI (Oyun İçi Alt Bar)                     │
/// │  ┌──────────────┐ ┌──────────────┐ ┌──────────────┐        │
/// │  │Info_FireRate  │ │Info_Damage   │ │Info_MaxHp    │        │
/// │  │ Text_Level   │ │ Text_Level   │ │ Text_Level   │        │
/// │  │ Text_Cost    │ │ Text_Cost    │ │ Text_Cost    │        │
/// │  └──────────────┘ └──────────────┘ └──────────────┘        │
/// │  Salt okunur — Sadece bilgi gösterir, tıklanamaz            │
/// └─────────────────────────────────────────────────────────────┘
/// 
/// Satın alma → TryBuyUpgrade() → GoldManager.TrySpendGold()
///   → SaveManager.IncrementUpgradeLevel() + Save()
///   → ApplyUpgradeToSystem() (stat uygula)
///   → RefreshAllUI() (HER İKİ ekranı da güncelle)
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
        public string      colorHex;       // Rich Text başlık rengi

        public UpgradeConfig(UpgradeType type, int baseCost, float costMultiplier,
                             int maxLevel, string displayName, string colorHex)
        {
            this.type           = type;
            this.baseCost       = baseCost;
            this.costMultiplier = costMultiplier;
            this.maxLevel       = maxLevel;
            this.displayName    = displayName;
            this.colorHex       = colorHex;
        }
    }

    private readonly UpgradeConfig[] _configs = new UpgradeConfig[]
    {
        new UpgradeConfig(UpgradeType.FireRate, 10,  1.6f, 8,  "FIRE RATE", "#00C2FF"),
        new UpgradeConfig(UpgradeType.Damage,   20,  1.5f, 10, "DAMAGE",    "#FF6B6B"),
        new UpgradeConfig(UpgradeType.MaxHP,    15,  1.5f, 8,  "MAX HP",    "#2ED573"),
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

    /// <summary>Upgrade satın alındığında tetiklenir.</summary>
    public event Action<UpgradeType> OnUpgradePurchased;

    // ════════════════════════════════════════════════════════════════
    //  INSPECTOR — Oyuncu Sistem Referansları
    // ════════════════════════════════════════════════════════════════

    [Header("═══ Oyuncu Sistemleri ═══")]
    [SerializeField] private PlayerShooting _playerShooting;
    [SerializeField] private PlayerHealth   _playerHealth;

    // ════════════════════════════════════════════════════════════════
    //  INSPECTOR — DETAILED SHOP UI (Ana Menü Pop-up)
    // ════════════════════════════════════════════════════════════════

    [Header("═══ Detailed Shop UI (Main Menu) ═══")]
    [Tooltip("Block_FireRate → Text_Info (Rich Text bilgi yazısı)")]
    [SerializeField] private TextMeshProUGUI _detailedFireRateInfo;
    [Tooltip("Block_FireRate → Btn_Buy (Satın al butonu)")]
    [SerializeField] private Button _detailedFireRateBuyBtn;
    [Tooltip("Block_FireRate → Btn_Buy → Text (Buton üzerindeki yazı)")]
    [SerializeField] private TextMeshProUGUI _detailedFireRateBuyText;

    [Space(5)]
    [Tooltip("Block_Damage → Text_Info")]
    [SerializeField] private TextMeshProUGUI _detailedDamageInfo;
    [Tooltip("Block_Damage → Btn_Buy")]
    [SerializeField] private Button _detailedDamageBuyBtn;
    [Tooltip("Block_Damage → Btn_Buy → Text")]
    [SerializeField] private TextMeshProUGUI _detailedDamageBuyText;

    [Space(5)]
    [Tooltip("Block_MaxHp → Text_Info")]
    [SerializeField] private TextMeshProUGUI _detailedMaxHpInfo;
    [Tooltip("Block_MaxHp → Btn_Buy")]
    [SerializeField] private Button _detailedMaxHpBuyBtn;
    [Tooltip("Block_MaxHp → Btn_Buy → Text")]
    [SerializeField] private TextMeshProUGUI _detailedMaxHpBuyText;

    // ════════════════════════════════════════════════════════════════
    //  INSPECTOR — INFO SHOP UI (Oyun İçi Alt Bar — Salt Okunur)
    // ════════════════════════════════════════════════════════════════

    [Header("═══ Info Shop UI (In-Game) ═══")]
    [Tooltip("Info_FireRate → Text_Level (Örn: 'Lv 3')")]
    [SerializeField] private TextMeshProUGUI _infoFireRateLevel;
    [Tooltip("Info_FireRate → Text_Cost (Örn: '26 G')")]
    [SerializeField] private TextMeshProUGUI _infoFireRateCost;

    [Space(5)]
    [Tooltip("Info_Damage → Text_Level")]
    [SerializeField] private TextMeshProUGUI _infoDamageLevel;
    [Tooltip("Info_Damage → Text_Cost")]
    [SerializeField] private TextMeshProUGUI _infoDamageCost;

    [Space(5)]
    [Tooltip("Info_MaxHp → Text_Level")]
    [SerializeField] private TextMeshProUGUI _infoMaxHpLevel;
    [Tooltip("Info_MaxHp → Text_Cost")]
    [SerializeField] private TextMeshProUGUI _infoMaxHpCost;

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
        SubscribeToGoldChanges();
        RefreshAllUI();
    }

    private void OnEnable()
    {
        RefreshAllUI();
    }

    private void OnDestroy()
    {
        UnsubscribeFromGoldChanges();

        if (Instance == this)
            Instance = null;
    }

    // ════════════════════════════════════════════════════════════════
    //  GOLD EVENT — Afford Durumu Güncellemesi
    // ════════════════════════════════════════════════════════════════

    private void SubscribeToGoldChanges()
    {
        if (GoldManager.Instance != null)
            GoldManager.Instance.OnGoldChanged += HandleGoldChanged;
    }

    private void UnsubscribeFromGoldChanges()
    {
        if (GoldManager.Instance != null)
            GoldManager.Instance.OnGoldChanged -= HandleGoldChanged;
    }

    /// <summary>
    /// Altın değiştiğinde buton aktiflik durumlarını güncelle.
    /// Yeterli altın yoksa Btn_Buy deaktif olur (interactable = false).
    /// </summary>
    private void HandleGoldChanged(int newGold)
    {
        RefreshAllDetailedButtons();
    }

    // ════════════════════════════════════════════════════════════════
    //  MALİYET HESAPLAMA
    // ════════════════════════════════════════════════════════════════

    public int GetUpgradeCost(UpgradeType type)
    {
        int configIndex = GetConfigIndex(type);
        if (configIndex < 0) return -1;

        UpgradeConfig config = _configs[configIndex];
        int currentLevel = SaveManager.Instance != null
            ? SaveManager.Instance.GetUpgradeLevel(type) : 0;

        if (currentLevel >= config.maxLevel)
            return -1;

        return Mathf.CeilToInt(config.baseCost * Mathf.Pow(config.costMultiplier, currentLevel));
    }

    public int GetCurrentLevel(UpgradeType type)
    {
        return SaveManager.Instance != null
            ? SaveManager.Instance.GetUpgradeLevel(type) : 0;
    }

    public bool IsMaxLevel(UpgradeType type)
    {
        int configIndex = GetConfigIndex(type);
        if (configIndex < 0) return true;
        return GetCurrentLevel(type) >= _configs[configIndex].maxLevel;
    }

    public int GetMaxLevel(UpgradeType type)
    {
        int configIndex = GetConfigIndex(type);
        return configIndex >= 0 ? _configs[configIndex].maxLevel : 0;
    }

    // ════════════════════════════════════════════════════════════════
    //  SATIN ALMA
    // ════════════════════════════════════════════════════════════════

    public bool TryBuyUpgrade(UpgradeType type)
    {
        if (SaveManager.Instance == null || GoldManager.Instance == null)
        {
            LogWarning("SaveManager veya GoldManager bulunamadı.");
            return false;
        }

        if (IsMaxLevel(type))
            return false;

        int cost = GetUpgradeCost(type);
        if (cost < 0) return false;

        if (!GoldManager.Instance.TrySpendGold(cost))
            return false;

        // ── Seviyeyi artır ve kaydet ──
        SaveManager.Instance.IncrementUpgradeLevel(type);
        SaveManager.Instance.Save();

        // ── Stat uygula ──
        ApplyUpgradeToSystem(type);

        // ── HER İKİ UI'ı da güncelle (Dual Sync) ──
        RefreshAllUI();

        // ── Event tetikle ──
        OnUpgradePurchased?.Invoke(type);

        #if UNITY_EDITOR || DEVELOPMENT_BUILD
        int newLevel = SaveManager.Instance.GetUpgradeLevel(type);
        Debug.Log($"[ShopManager] {type} Lv{newLevel - 1} → Lv{newLevel} | " +
                  $"Harcanan: {cost}G | Kalan: {GoldManager.Instance.CurrentGold}G");
        #endif

        return true;
    }

    // ── Button OnClick Wrapper'ları ──
    // DetailedShopPanel'deki Btn_Buy butonlarına bağlanır.

    /// <summary>Block_FireRate → Btn_Buy → OnClick</summary>
    public void BuyFireRate() => TryBuyUpgrade(UpgradeType.FireRate);

    /// <summary>Block_Damage → Btn_Buy → OnClick</summary>
    public void BuyDamage() => TryBuyUpgrade(UpgradeType.Damage);

    /// <summary>Block_MaxHp → Btn_Buy → OnClick</summary>
    public void BuyMaxHP() => TryBuyUpgrade(UpgradeType.MaxHP);

    // ════════════════════════════════════════════════════════════════
    //  UI GÜNCELLEME — Dual Sync (Her İki Ekranı Aynı Anda)
    // ════════════════════════════════════════════════════════════════

    /// <summary>
    /// Tüm UI elementlerini güncel verilerle senkronize eder.
    /// Tek çağrı ile hem DetailedShop hem InfoShop güncellenir.
    /// </summary>
    public void RefreshAllUI()
    {
        RefreshSingleUpgrade(UpgradeType.FireRate);
        RefreshSingleUpgrade(UpgradeType.Damage);
        RefreshSingleUpgrade(UpgradeType.MaxHP);
        RefreshAllDetailedButtons();
    }

    /// <summary>
    /// Tek bir upgrade tipinin her iki ekrandaki metinlerini günceller.
    /// </summary>
    private void RefreshSingleUpgrade(UpgradeType type)
    {
        int configIndex = GetConfigIndex(type);
        if (configIndex < 0) return;

        UpgradeConfig config = _configs[configIndex];
        int  level = GetCurrentLevel(type);
        bool isMax = level >= config.maxLevel;
        int  cost  = isMax ? -1 : GetUpgradeCost(type);

        string costStr  = isMax ? "MAKS" : (cost.ToString() + " G");
        string levelStr = "Lv " + level.ToString();

        // ── 1) Detailed Shop UI (Rich Text) ──
        RefreshDetailedInfo(type, config, level, costStr);

        // ── 2) Info Shop UI (Oyun İçi — Düz Text) ──
        RefreshInfoUI(type, levelStr, costStr);
    }

    // ════════════════════════════════════════════════════════════════
    //  DETAILED SHOP — Rich Text Güncelleme
    // ════════════════════════════════════════════════════════════════

    /// <summary>
    /// DetailedShopPanel'deki Text_Info'yu Rich Text formatıyla günceller.
    /// 
    /// Format:
    /// ───────
    ///   &lt;color=#00C2FF&gt;&lt;b&gt;FIRE RATE&lt;/b&gt;&lt;/color&gt;
    ///   &lt;color=#888888&gt;Lv: 3&lt;/color&gt;
    ///   &lt;color=#FFC107&gt;26 G&lt;/color&gt;
    /// 
    /// Maks seviyede:
    ///   &lt;color=#00C2FF&gt;&lt;b&gt;FIRE RATE&lt;/b&gt;&lt;/color&gt;
    ///   &lt;color=#888888&gt;Lv: 8&lt;/color&gt;
    ///   &lt;color=#FFC107&gt;MAKS&lt;/color&gt;
    /// 
    /// TMP Rich Text özelliği kullanılır — TextMeshPro Inspector'da
    /// "Rich Text" checkbox'ı aktif olmalı (varsayılan olarak aktif).
    /// </summary>
    private void RefreshDetailedInfo(UpgradeType type, UpgradeConfig config,
                                      int level, string costStr)
    {
        TextMeshProUGUI infoText = GetDetailedInfoText(type);
        if (infoText == null) return;

        // Rich Text formatında birleşik bilgi yazısı
        string richText =
            "<color=" + config.colorHex + "><b>" + config.displayName + "</b></color>\n" +
            "<color=#888888>Lv: " + level.ToString() + "</color>\n" +
            "<color=#FFC107>" + costStr + "</color>";

        infoText.SetText(richText);
    }

    /// <summary>
    /// DetailedShopPanel'deki tüm Btn_Buy butonlarının aktiflik durumunu günceller.
    /// Yeterli altın yoksa veya maks seviyeyse buton deaktif olur.
    /// </summary>
    private void RefreshAllDetailedButtons()
    {
        RefreshDetailedButton(UpgradeType.FireRate);
        RefreshDetailedButton(UpgradeType.Damage);
        RefreshDetailedButton(UpgradeType.MaxHP);
    }

    /// <summary>
    /// Tek bir Btn_Buy butonunun aktiflik ve metin durumunu günceller.
    /// </summary>
    private void RefreshDetailedButton(UpgradeType type)
    {
        GetDetailedButtonRefs(type, out Button btn, out TextMeshProUGUI btnText);

        bool isMax = IsMaxLevel(type);
        int  cost  = GetUpgradeCost(type);
        int  gold  = GoldManager.Instance != null ? GoldManager.Instance.CurrentGold : 0;

        if (btn != null)
        {
            // Maks seviye veya yetersiz altın → buton deaktif
            btn.interactable = !isMax && gold >= cost;
        }

        if (btnText != null)
        {
            if (isMax)
                btnText.SetText("MAKS SEVİYE");
            else
                btnText.SetText(cost.ToString() + " G");
        }
    }

    // ════════════════════════════════════════════════════════════════
    //  INFO SHOP — Oyun İçi Alt Bar Güncelleme (Salt Okunur)
    // ════════════════════════════════════════════════════════════════

    /// <summary>
    /// ShopPanel (oyun içi) Info bloklarının seviye ve fiyat metinlerini günceller.
    /// Buton yok — sadece düz text.
    /// </summary>
    private void RefreshInfoUI(UpgradeType type, string levelStr, string costStr)
    {
        GetInfoRefs(type, out TextMeshProUGUI levelText, out TextMeshProUGUI costText);

        if (levelText != null)
            levelText.SetText(levelStr);

        if (costText != null)
            costText.SetText(costStr);
    }

    // ════════════════════════════════════════════════════════════════
    //  UI REFERANS ROUTER'LARI
    // ════════════════════════════════════════════════════════════════

    /// <summary>DetailedShopPanel'deki Text_Info referansını döner.</summary>
    private TextMeshProUGUI GetDetailedInfoText(UpgradeType type)
    {
        return type switch
        {
            UpgradeType.FireRate => _detailedFireRateInfo,
            UpgradeType.Damage   => _detailedDamageInfo,
            UpgradeType.MaxHP    => _detailedMaxHpInfo,
            _                    => null
        };
    }

    /// <summary>DetailedShopPanel'deki Btn_Buy ve üzerindeki text referanslarını döner.</summary>
    private void GetDetailedButtonRefs(UpgradeType type, out Button btn, out TextMeshProUGUI btnText)
    {
        switch (type)
        {
            case UpgradeType.FireRate:
                btn     = _detailedFireRateBuyBtn;
                btnText = _detailedFireRateBuyText;
                return;
            case UpgradeType.Damage:
                btn     = _detailedDamageBuyBtn;
                btnText = _detailedDamageBuyText;
                return;
            case UpgradeType.MaxHP:
                btn     = _detailedMaxHpBuyBtn;
                btnText = _detailedMaxHpBuyText;
                return;
            default:
                btn     = null;
                btnText = null;
                return;
        }
    }

    /// <summary>Info ShopPanel'deki (oyun içi) Level ve Cost text referanslarını döner.</summary>
    private void GetInfoRefs(UpgradeType type,
                              out TextMeshProUGUI levelText,
                              out TextMeshProUGUI costText)
    {
        switch (type)
        {
            case UpgradeType.FireRate:
                levelText = _infoFireRateLevel;
                costText  = _infoFireRateCost;
                return;
            case UpgradeType.Damage:
                levelText = _infoDamageLevel;
                costText  = _infoDamageCost;
                return;
            case UpgradeType.MaxHP:
                levelText = _infoMaxHpLevel;
                costText  = _infoMaxHpCost;
                return;
            default:
                levelText = null;
                costText  = null;
                return;
        }
    }

    // ════════════════════════════════════════════════════════════════
    //  STAT UYGULAMA
    // ════════════════════════════════════════════════════════════════

    private void ApplyAllUpgradesToSystems()
    {
        ApplyUpgradeToSystem(UpgradeType.FireRate);
        ApplyUpgradeToSystem(UpgradeType.Damage);
        ApplyUpgradeToSystem(UpgradeType.MaxHP);
    }

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
                        BASE_FIRE_INTERVAL + level * FIRE_RATE_PER_LVL);
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

    /// <summary>Stat hesaplama (UI preview için).</summary>
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
            if (_configs[i].type == type) return i;
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
            Debug.LogWarning("[ShopManager] PlayerShooting atanmamış.", this);

        if (_playerHealth == null)
            Debug.LogWarning("[ShopManager] PlayerHealth atanmamış.", this);

        // Detailed Shop kontrolleri
        if (_detailedFireRateInfo == null || _detailedFireRateBuyBtn == null)
            Debug.LogWarning("[ShopManager] Detailed FireRate referansları eksik!", this);

        if (_detailedDamageInfo == null || _detailedDamageBuyBtn == null)
            Debug.LogWarning("[ShopManager] Detailed Damage referansları eksik!", this);

        if (_detailedMaxHpInfo == null || _detailedMaxHpBuyBtn == null)
            Debug.LogWarning("[ShopManager] Detailed MaxHp referansları eksik!", this);

        // Info Shop kontrolleri
        if (_infoFireRateLevel == null || _infoFireRateCost == null)
            Debug.LogWarning("[ShopManager] Info FireRate referansları eksik!", this);

        if (_infoDamageLevel == null || _infoDamageCost == null)
            Debug.LogWarning("[ShopManager] Info Damage referansları eksik!", this);

        if (_infoMaxHpLevel == null || _infoMaxHpCost == null)
            Debug.LogWarning("[ShopManager] Info MaxHp referansları eksik!", this);
    }

    [System.Diagnostics.Conditional("UNITY_EDITOR"),
     System.Diagnostics.Conditional("DEVELOPMENT_BUILD")]
    private void LogWarning(string message)
    {
        Debug.LogWarning($"[ShopManager] {message}", this);
    }
}
