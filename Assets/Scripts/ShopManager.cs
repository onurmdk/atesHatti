using UnityEngine;
using UnityEngine.UI;
using TMPro;
using System;

public class ShopManager : MonoBehaviour
{
    public static ShopManager Instance { get; private set; }

    [System.Serializable]
    private struct UpgradeConfig
    {
        public UpgradeType type;
        public int         baseCost;
        public float       costMultiplier;
        public int         maxLevel;
        public string      displayName;
        public string      colorHex;

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

    private const float BASE_FIRE_INTERVAL = 0.65f;
    private const float MIN_FIRE_INTERVAL  = 0.08f;
    private const float FIRE_RATE_PER_LVL  = -0.025f;

    private const float BASE_DAMAGE        = 1f;
    private const float DAMAGE_PER_LVL     = 1f;

    private const float BASE_MAX_HP        = 1f;
    private const float HP_PER_LVL         = 1f;

    public event Action<UpgradeType> OnUpgradePurchased;

    [Header("═══ Oyuncu Sistemleri ═══")]
    [SerializeField] private PlayerShooting _playerShooting;
    [SerializeField] private PlayerHealth   _playerHealth;

    [Header("═══ Detailed Shop UI (Main Menu) ═══")]
    [SerializeField] private TextMeshProUGUI _detailedFireRateInfo;
    [SerializeField] private Button _detailedFireRateBuyBtn;
    [SerializeField] private TextMeshProUGUI _detailedFireRateBuyText;

    [Space(5)]
    [SerializeField] private TextMeshProUGUI _detailedDamageInfo;
    [SerializeField] private Button _detailedDamageBuyBtn;
    [SerializeField] private TextMeshProUGUI _detailedDamageBuyText;

    [Space(5)]
    [SerializeField] private TextMeshProUGUI _detailedMaxHpInfo;
    [SerializeField] private Button _detailedMaxHpBuyBtn;
    [SerializeField] private TextMeshProUGUI _detailedMaxHpBuyText;

    [Header("═══ Info Shop UI (In-Game) ═══")]
    [SerializeField] private TextMeshProUGUI _infoFireRateLevel;
    [SerializeField] private TextMeshProUGUI _infoFireRateCost;

    [Space(5)]
    [SerializeField] private TextMeshProUGUI _infoDamageLevel;
    [SerializeField] private TextMeshProUGUI _infoDamageCost;

    [Space(5)]
    [SerializeField] private TextMeshProUGUI _infoMaxHpLevel;
    [SerializeField] private TextMeshProUGUI _infoMaxHpCost;

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

    private void HandleGoldChanged(int newGold)
    {
        RefreshAllDetailedButtons();
    }

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

        SaveManager.Instance.IncrementUpgradeLevel(type);
        SaveManager.Instance.Save();

        ApplyUpgradeToSystem(type);

        RefreshAllUI();

        OnUpgradePurchased?.Invoke(type);

        #if UNITY_EDITOR || DEVELOPMENT_BUILD
        int newLevel = SaveManager.Instance.GetUpgradeLevel(type);
        Debug.Log($"[ShopManager] {type} Lv{newLevel - 1} → Lv{newLevel} | " +
                  $"Harcanan: {cost}G | Kalan: {GoldManager.Instance.CurrentGold}G");
        #endif

        return true;
    }

    public void BuyFireRate() => TryBuyUpgrade(UpgradeType.FireRate);
    public void BuyDamage() => TryBuyUpgrade(UpgradeType.Damage);
    public void BuyMaxHP() => TryBuyUpgrade(UpgradeType.MaxHP);

    public void RefreshAllUI()
    {
        RefreshSingleUpgrade(UpgradeType.FireRate);
        RefreshSingleUpgrade(UpgradeType.Damage);
        RefreshSingleUpgrade(UpgradeType.MaxHP);
        RefreshAllDetailedButtons();
    }

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

        RefreshDetailedInfo(type, config, level, costStr);

        RefreshInfoUI(type, levelStr, costStr);
    }

    private void RefreshDetailedInfo(UpgradeType type, UpgradeConfig config,
                                      int level, string costStr)
    {
        TextMeshProUGUI infoText = GetDetailedInfoText(type);
        if (infoText == null) return;

        string richText =
            "<color=" + config.colorHex + "><b>" + config.displayName + "</b></color>\n" +
            "<color=#888888>Lv: " + level.ToString() + "</color>\n" +
            "<color=#FFC107>" + costStr + "</color>";

        infoText.SetText(richText);
    }

    private void RefreshAllDetailedButtons()
    {
        RefreshDetailedButton(UpgradeType.FireRate);
        RefreshDetailedButton(UpgradeType.Damage);
        RefreshDetailedButton(UpgradeType.MaxHP);
    }

    private void RefreshDetailedButton(UpgradeType type)
    {
        GetDetailedButtonRefs(type, out Button btn, out TextMeshProUGUI btnText);

        bool isMax = IsMaxLevel(type);
        int  cost  = GetUpgradeCost(type);
        int  gold  = GoldManager.Instance != null ? GoldManager.Instance.CurrentGold : 0;

        if (btn != null)
        {
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

    private void RefreshInfoUI(UpgradeType type, string levelStr, string costStr)
    {
        GetInfoRefs(type, out TextMeshProUGUI levelText, out TextMeshProUGUI costText);

        if (levelText != null)
            levelText.SetText(levelStr);

        if (costText != null)
            costText.SetText(costStr);
    }

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

    private int GetConfigIndex(UpgradeType type)
    {
        for (int i = 0; i < _configs.Length; i++)
        {
            if (_configs[i].type == type) return i;
        }
        return -1;
    }

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

        if (_detailedFireRateInfo == null || _detailedFireRateBuyBtn == null)
            Debug.LogWarning("[ShopManager] Detailed FireRate referansları eksik!", this);

        if (_detailedDamageInfo == null || _detailedDamageBuyBtn == null)
            Debug.LogWarning("[ShopManager] Detailed Damage referansları eksik!", this);

        if (_detailedMaxHpInfo == null || _detailedMaxHpBuyBtn == null)
            Debug.LogWarning("[ShopManager] Detailed MaxHp referansları eksik!", this);

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