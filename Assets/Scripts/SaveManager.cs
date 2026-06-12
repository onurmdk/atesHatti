using UnityEngine;
using System;

public class SaveManager : MonoBehaviour
{
    public static SaveManager Instance { get; private set; }

    [System.Serializable]
    public class PlayerSaveData
    {
        public int saveVersion = 1;

        public int fireRateLevel;
        public int damageLevel;
        public int maxHpLevel;

        public int persistentGold;

        public float highScore;

        public int totalGamesPlayed;
    }

    private const string SAVE_KEY = "ates_hatti_save_v1";

    private PlayerSaveData _data;

    public event Action OnDataSaved;

    public event Action<float> OnNewHighScore;

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;

        Load();
    }

    private void OnDestroy()
    {
        if (Instance == this)
            Instance = null;
    }

    private void OnApplicationPause(bool pauseStatus)
    {
        if (pauseStatus)
        {
            Save();
        }
    }

    private void OnApplicationQuit()
    {
        Save();
    }

    private void Load()
    {
        if (PlayerPrefs.HasKey(SAVE_KEY))
        {
            string json = PlayerPrefs.GetString(SAVE_KEY);

            try
            {
                _data = JsonUtility.FromJson<PlayerSaveData>(json);

                if (_data == null)
                {
                    _data = new PlayerSaveData();
                    LogWarning("Kaydedilmiş veri null döndü — varsayılan oluşturuldu.");
                }
            }
            catch (Exception e)
            {
                _data = new PlayerSaveData();
                LogWarning($"Save verisi okunamadı: {e.Message}. Varsayılan oluşturuldu.");
            }
        }
        else
        {
            _data = new PlayerSaveData();
        }

        #if UNITY_EDITOR || DEVELOPMENT_BUILD
        Debug.Log($"[SaveManager] Veri yüklendi → " +
                  $"FR Lv{_data.fireRateLevel} | DMG Lv{_data.damageLevel} | " +
                  $"HP Lv{_data.maxHpLevel} | Gold: {_data.persistentGold} | " +
                  $"High Score: {_data.highScore:F1}s | Games: {_data.totalGamesPlayed}");
        #endif
    }

    public void Save()
    {
        if (_data == null)
        {
            LogWarning("Data null — kaydetme atlandı.");
            return;
        }

        string json = JsonUtility.ToJson(_data);
        PlayerPrefs.SetString(SAVE_KEY, json);
        PlayerPrefs.Save();

        OnDataSaved?.Invoke();

        #if UNITY_EDITOR || DEVELOPMENT_BUILD
        Debug.Log("[SaveManager] Veri kaydedildi → " + json);
        #endif
    }

    public bool TryUpdateHighScore(float newScore)
    {
        if (newScore <= _data.highScore)
            return false;

        _data.highScore = newScore;
        Save();

        OnNewHighScore?.Invoke(newScore);

        #if UNITY_EDITOR || DEVELOPMENT_BUILD
        Debug.Log($"[SaveManager] YENİ YÜKSEK SKOR: {newScore:F1}s!");
        #endif

        return true;
    }

    public void IncrementGamesPlayed()
    {
        _data.totalGamesPlayed++;
    }

    public int   FireRateLevel    => _data.fireRateLevel;
    public int   DamageLevel      => _data.damageLevel;
    public int   MaxHpLevel       => _data.maxHpLevel;
    public int   PersistentGold   => _data.persistentGold;
    public float HighScore        => _data.highScore;
    public int   TotalGamesPlayed => _data.totalGamesPlayed;

    public void IncrementUpgradeLevel(UpgradeType type)
    {
        switch (type)
        {
            case UpgradeType.FireRate: _data.fireRateLevel++; break;
            case UpgradeType.Damage:   _data.damageLevel++;   break;
            case UpgradeType.MaxHP:    _data.maxHpLevel++;    break;
        }
    }

    public int GetUpgradeLevel(UpgradeType type)
    {
        return type switch
        {
            UpgradeType.FireRate => _data.fireRateLevel,
            UpgradeType.Damage   => _data.damageLevel,
            UpgradeType.MaxHP    => _data.maxHpLevel,
            _                    => 0
        };
    }

    public void SetPersistentGold(int gold)
    {
        _data.persistentGold = Mathf.Max(0, gold);
    }

    [ContextMenu("Tüm Save Verisini Sil")]
    public void DeleteAllData()
    {
        PlayerPrefs.DeleteKey(SAVE_KEY);
        PlayerPrefs.Save();
        _data = new PlayerSaveData();

        #if UNITY_EDITOR || DEVELOPMENT_BUILD
        Debug.Log("[SaveManager] Tüm save verisi silindi!");
        #endif
    }

    [System.Diagnostics.Conditional("UNITY_EDITOR"),
     System.Diagnostics.Conditional("DEVELOPMENT_BUILD")]
    private void LogWarning(string message)
    {
        Debug.LogWarning($"[SaveManager] {message}", this);
    }
}

public enum UpgradeType
{
    FireRate,
    Damage,
    MaxHP
}