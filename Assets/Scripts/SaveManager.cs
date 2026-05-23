using UnityEngine;
using System;

/// <summary>
/// ╔══════════════════════════════════════════════════════════════════╗
///   ATEŞ HATTI — SaveManager (Veri Kalıcılık Yöneticisi)
///   
///   Oyuncu ilerleme verilerini (upgrade seviyeleri, altın, yüksek skor)
///   PlayerPrefs + JSON ile kalıcı olarak saklayan sistem.
/// ╚══════════════════════════════════════════════════════════════════╝
/// 
/// Sorumluluk (SRP): SADECE veri okuma/yazma.
///   Upgrade mantığı ShopManager'da, altın yönetimi GoldManager'da.
///   SaveManager sadece "ne kaydedilecek, nasıl kaydedilecek" bilir.
/// 
/// Neden PlayerPrefs + JSON?
/// ─────────────────────────
/// Düz PlayerPrefs (key başına SetInt/GetInt):
///   - 6 ayrı key = 6 ayrı disk yazma
///   - Yeni alan eklemek → yeni key hatırlamak
///   - Versiyon uyumsuzluğu yönetimi zor
/// 
/// PlayerPrefs + JSON:
///   - Tek key ("save_data"), tek disk yazma → ATOMİK
///   - Yeni alan eklemek = struct'a field ekle → JsonUtility otomatik
///   - saveVersion field'ı ile geriye uyumluluk yönetilebilir
///   - Tüm veri tek string — debug/inspect etmesi kolay
/// 
/// Awake Timing:
/// ─────────────
/// SaveManager hiçbir singleton'a bağımlı DEĞİLDİR.
/// Kendi Awake'inde JSON'u okur ve hazır olur.
/// Diğer sistemler Start'ta SaveManager.Instance'a güvenle erişebilir.
/// </summary>
public class SaveManager : MonoBehaviour
{
    // ════════════════════════════════════════════════════════════════
    //  SINGLETON
    // ════════════════════════════════════════════════════════════════

    public static SaveManager Instance { get; private set; }

    // ════════════════════════════════════════════════════════════════
    //  SAVE DATA YAPISI
    // ════════════════════════════════════════════════════════════════

    /// <summary>
    /// Kalıcı olarak saklanan tüm oyuncu verileri.
    /// 
    /// [System.Serializable] → JsonUtility.ToJson/FromJson ile
    /// serialize/deserialize edilebilir.
    /// 
    /// Yeni alan eklemek:
    ///   1. Bu class'a field ekle
    ///   2. Varsayılan değer ata
    ///   3. Bitti — JsonUtility eksik field'ları default ile doldurur
    ///      (geriye uyumluluk otomatik)
    /// </summary>
    [System.Serializable]
    public class PlayerSaveData
    {
        /// <summary>
        /// Save format versiyonu.
        /// İleride save yapısı değişirse eski verileri migrate etmek için.
        /// </summary>
        public int saveVersion = 1;

        // ── Upgrade Seviyeleri ──
        public int fireRateLevel;
        public int damageLevel;
        public int maxHpLevel;

        // ── Ekonomi ──
        /// <summary>Oturumlar arası taşınan toplam altın.</summary>
        public int persistentGold;

        // ── Yüksek Skor ──
        /// <summary>En uzun hayatta kalma süresi (saniye).</summary>
        public float highScore;

        /// <summary>Toplam oynanan oyun sayısı.</summary>
        public int totalGamesPlayed;
    }

    // ════════════════════════════════════════════════════════════════
    //  CONSTANTS
    // ════════════════════════════════════════════════════════════════

    /// <summary>
    /// PlayerPrefs key'i. Tüm save verisi bu tek key altında saklanır.
    /// </summary>
    private const string SAVE_KEY = "ates_hatti_save_v1";

    // ════════════════════════════════════════════════════════════════
    //  STATE
    // ════════════════════════════════════════════════════════════════

    /// <summary>
    /// Bellekteki aktif save verisi.
    /// Tüm okuma/yazma bu instance üzerinden yapılır.
    /// Disk'e yazma sadece Save() çağrıldığında olur.
    /// </summary>
    private PlayerSaveData _data;

    // ════════════════════════════════════════════════════════════════
    //  EVENTS
    // ════════════════════════════════════════════════════════════════

    /// <summary>Veri kaydedildiğinde tetiklenir.</summary>
    public event Action OnDataSaved;

    /// <summary>
    /// Yüksek skor kırıldığında tetiklenir.
    /// Parametre: Yeni yüksek skor değeri.
    /// </summary>
    public event Action<float> OnNewHighScore;

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

        // Veriyi yükle — SaveManager hiçbir singleton'a bağımlı değil
        Load();
    }

    private void OnDestroy()
    {
        if (Instance == this)
            Instance = null;
    }

    /// <summary>
    /// Uygulama arka plana atıldığında otomatik kaydet.
    /// Android'de OS uygulamayı doğrudan öldürebilir —
    /// OnApplicationPause(true) en güvenilir kaydetme noktasıdır.
    /// </summary>
    private void OnApplicationPause(bool pauseStatus)
    {
        if (pauseStatus)
        {
            Save();
        }
    }

    /// <summary>
    /// Uygulama kapanırken son kaydetme şansı.
    /// Editor ve iOS'ta güvenilir, Android'de garanti yok.
    /// </summary>
    private void OnApplicationQuit()
    {
        Save();
    }

    // ════════════════════════════════════════════════════════════════
    //  VERİ OKUMA
    // ════════════════════════════════════════════════════════════════

    /// <summary>
    /// PlayerPrefs'ten JSON okuyup PlayerSaveData'ya deserialize eder.
    /// 
    /// Hata yönetimi:
    /// 1. Key yoksa (ilk oyun): Yeni data oluştur
    /// 2. JSON bozuksa (corrupt): Yeni data oluştur
    /// 3. JSON'da eksik field varsa (eski versiyon): 
    ///    JsonUtility eksik field'ları default ile doldurur → geriye uyumlu
    /// </summary>
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

    // ════════════════════════════════════════════════════════════════
    //  VERİ YAZMA
    // ════════════════════════════════════════════════════════════════

    /// <summary>
    /// Mevcut veriyi JSON'a çevirip PlayerPrefs'e yazar.
    /// 
    /// PlayerPrefs.Save() çağrısı disk yazmasını garanti eder.
    /// Maliyeti: ~0.1-1ms. Saniyede 1-2 kez kabul edilebilir.
    /// </summary>
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

    // ════════════════════════════════════════════════════════════════
    //  YÜKSEK SKOR
    // ════════════════════════════════════════════════════════════════

    /// <summary>
    /// Yeni skoru mevcut yüksek skorla karşılaştırır.
    /// Daha yüksekse günceller, kaydeder ve event tetikler.
    /// Dönüş: true = yeni yüksek skor.
    /// </summary>
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

    /// <summary>Oynanan oyun sayısını artırır.</summary>
    public void IncrementGamesPlayed()
    {
        _data.totalGamesPlayed++;
    }

    // ════════════════════════════════════════════════════════════════
    //  PUBLIC API — Veri Erişimi (Okuma)
    // ════════════════════════════════════════════════════════════════

    public int   FireRateLevel    => _data.fireRateLevel;
    public int   DamageLevel      => _data.damageLevel;
    public int   MaxHpLevel       => _data.maxHpLevel;
    public int   PersistentGold   => _data.persistentGold;
    public float HighScore        => _data.highScore;
    public int   TotalGamesPlayed => _data.totalGamesPlayed;

    // ════════════════════════════════════════════════════════════════
    //  PUBLIC API — Veri Yazma (ShopManager tarafından çağrılır)
    // ════════════════════════════════════════════════════════════════

    /// <summary>
    /// Belirtilen upgrade tipinin seviyesini 1 artırır.
    /// Kaydetme işlemi çağıran tarafta (ShopManager) yapılır.
    /// </summary>
    public void IncrementUpgradeLevel(UpgradeType type)
    {
        switch (type)
        {
            case UpgradeType.FireRate: _data.fireRateLevel++; break;
            case UpgradeType.Damage:   _data.damageLevel++;   break;
            case UpgradeType.MaxHP:    _data.maxHpLevel++;    break;
        }
    }

    /// <summary>Belirtilen upgrade tipinin mevcut seviyesini döner.</summary>
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

    /// <summary>Kalıcı altını günceller.</summary>
    public void SetPersistentGold(int gold)
    {
        _data.persistentGold = Mathf.Max(0, gold);
    }

    // ════════════════════════════════════════════════════════════════
    //  DEBUG
    // ════════════════════════════════════════════════════════════════

    /// <summary>TÜM kayıtlı veriyi siler.</summary>
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

    // ════════════════════════════════════════════════════════════════
    //  DOĞRULAMA
    // ════════════════════════════════════════════════════════════════

    [System.Diagnostics.Conditional("UNITY_EDITOR"),
     System.Diagnostics.Conditional("DEVELOPMENT_BUILD")]
    private void LogWarning(string message)
    {
        Debug.LogWarning($"[SaveManager] {message}", this);
    }
}

// ════════════════════════════════════════════════════════════════════════
//  UPGRADE TİP ENUM
// ════════════════════════════════════════════════════════════════════════

/// <summary>
/// Upgrade tipleri. ShopManager, SaveManager ve UI tarafından paylaşılır.
/// </summary>
public enum UpgradeType
{
    FireRate,
    Damage,
    MaxHP
}
