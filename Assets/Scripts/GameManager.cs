using UnityEngine;
using UnityEngine.SceneManagement;
using System;

/// <summary>
/// ╔══════════════════════════════════════════════════════════════════╗
///   ATEŞ HATTI — GameManager (Merkezi Oyun Durum Yöneticisi)
///   
///   Oyunun yaşam döngüsünü yöneten, state geçişlerini kontrol eden
///   ve tüm sistemleri koordine eden en üst seviye yönetici.
/// ╚══════════════════════════════════════════════════════════════════╝
/// 
/// Sorumluluk:
/// ───────────
/// • Oyun durumunu (Playing, GameOver) yönetir
/// • PlayerDeath event'ini dinleyip GameOver akışını tetikler
/// • Time.timeScale kontrolü (pause/resume)
/// • Sahne yeniden yükleme (restart)
/// • GameOver UI panelini açıp kapatır
/// • Oyun süresi takibi (istatistik için)
/// 
/// State Akış Diyagramı:
/// ────────────────────
///   ┌──────────┐   PlayerDeath    ┌───────────┐
///   │ PLAYING  │ ───────────────▶ │ GAME_OVER │
///   │          │                  │           │
///   └──────────┘                  └─────┬─────┘
///        ▲                              │
///        │      RestartGame()           │
///        │      (Scene Reload)          │
///        └──────────────────────────────┘
/// 
/// Neden Scene Reload ve Manual Reset Değil?
/// ─────────────────────────────────────────
/// Scene reload tüm state'i garanti sıfırlar — state sızıntısı riski SIFIR.
/// Manual reset'te her yeni eklenen sistem için ResetX() yazmak gerekir;
/// bir tane unutulursa önceki oyundan veri kalıntısı kalır.
/// 300ms yükleme süresi, loading screen arkasında gizlenir.
/// Profiling sonrası darboğaz görülürse manual reset'e geçilebilir
/// (ResetX metotları zaten tüm manager'larda mevcut).
/// </summary>
public class GameManager : MonoBehaviour
{
    // ════════════════════════════════════════════════════════════════
    //  SINGLETON
    // ════════════════════════════════════════════════════════════════

    public static GameManager Instance { get; private set; }

    // ════════════════════════════════════════════════════════════════
    //  GAME STATE ENUM
    // ════════════════════════════════════════════════════════════════

    /// <summary>
    /// Oyunun mevcut durumu.
    /// 
    /// Neden sadece 2 state?
    /// → Şu an oyun ya oynanıyor ya da bitmiş.
    ///   İleride eklenmesi muhtemel state'ler:
    ///     - Paused      (pause menü)
    ///     - BossFight   (normal spawn durur, boss aktif)
    ///     - Countdown   (3-2-1 geri sayım)
    ///   Bunlar gerektiğinde enum'a eklenir — şu an YAGNI.
    /// </summary>
    public enum GameState
    {
        Playing,
        GameOver
    }

    // ════════════════════════════════════════════════════════════════
    //  EVENTS
    // ════════════════════════════════════════════════════════════════

    /// <summary>
    /// Oyun durumu değiştiğinde tetiklenir.
    /// Dinleyiciler: Tüm sistemler bu event'i dinleyerek
    /// kendi davranışlarını ayarlayabilir.
    /// 
    /// Örnek kullanım:
    ///   EnemySpawner → GameOver'da spawn'ı durdur
    ///   AudioManager → GameOver müziğine geç
    ///   AdManager    → GameOver'da interstitial reklam göster
    /// </summary>
    public event Action<GameState> OnGameStateChanged;

    /// <summary>
    /// Restart öncesi tetiklenir.
    /// Dinleyiciler: SaveManager (upgrade verilerini kaydet),
    /// Analytics (session verisi gönder) vb.
    /// Scene reload tüm objeleri yok edeceği için bu event
    /// "son dakika işlemleri" için fırsat verir.
    /// </summary>
    public event Action OnBeforeRestart;

    // ════════════════════════════════════════════════════════════════
    //  INSPECTOR — UI REFERANSLARI
    // ════════════════════════════════════════════════════════════════

    [Header("─── Game Over UI ───")]
    [Tooltip("Game Over paneli. Başlangıçta deaktif olmalı.\n" +
             "Hierarchy: Canvas → GameOverPanel")]
    [SerializeField]
    private GameObject _gameOverPanel;

    [Header("─── İstatistik Metinleri (Opsiyonel) ───")]
    [Tooltip("Hayatta kalma süresini gösteren text.\n" +
             "null bırakılabilir — yoksa atlanır.")]
    [SerializeField]
    private TMPro.TextMeshProUGUI _survivalTimeText;

    [Tooltip("Toplam kazanılan altını gösteren text.\n" +
             "null bırakılabilir — yoksa atlanır.")]
    [SerializeField]
    private TMPro.TextMeshProUGUI _totalGoldText;

    [Header("─── Oyuncu Referansı ───")]
    [Tooltip("PlayerHealth bileşenine sahip Player objesi.")]
    [SerializeField]
    private PlayerHealth _playerHealth;

    // ════════════════════════════════════════════════════════════════
    //  STATE
    // ════════════════════════════════════════════════════════════════

    private GameState _currentState;

    /// <summary>
    /// Oyun süresi (saniye).
    /// Time.timeScale = 0 olduğunda artmayı durdurur (unscaled değil).
    /// Bu sayede pause süresince süre ilerlermez.
    /// </summary>
    private float _elapsedTime;

    // ════════════════════════════════════════════════════════════════
    //  PUBLIC PROPERTIES
    // ════════════════════════════════════════════════════════════════

    /// <summary>Mevcut oyun durumu.</summary>
    public GameState CurrentState => _currentState;

    /// <summary>Oyun aktif olarak oynanıyor mu?</summary>
    public bool IsPlaying => _currentState == GameState.Playing;

    /// <summary>Toplam geçen süre (saniye).</summary>
    public float ElapsedTime => _elapsedTime;

    // ════════════════════════════════════════════════════════════════
    //  UNITY LIFECYCLE
    // ════════════════════════════════════════════════════════════════

    private void Awake()
    {
        // ── Singleton ──
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;

        ValidateSetup();

        // ── Başlangıç state ──
        _currentState = GameState.Playing;
        _elapsedTime  = 0f;

        // ── timeScale'i garanti et ──
        // Önceki oturumdan timeScale = 0 kalmış olabilir
        // (restart sırasında scene reload timeScale'i resetlemez!)
        Time.timeScale = 1f;

        // ── Game Over panelini kapat ──
        if (_gameOverPanel != null)
            _gameOverPanel.SetActive(false);
    }

    /// <summary>
    /// Start'ta subscribe — tüm Awake'ler tamamlandıktan sonra.
    /// </summary>
    private void Start()
    {
        SubscribeToEvents();
    }

    /// <summary>
    /// Oyun süresini takip eder.
    /// Time.deltaTime kullanıldığı için timeScale = 0'da otomatik durur.
    /// </summary>
    private void Update()
    {
        if (_currentState == GameState.Playing)
        {
            _elapsedTime += Time.deltaTime;
        }
    }

    private void OnDestroy()
    {
        UnsubscribeFromEvents();

        if (Instance == this)
            Instance = null;
    }

    // ════════════════════════════════════════════════════════════════
    //  EVENT SUBSCRIPTION
    // ════════════════════════════════════════════════════════════════

    private void SubscribeToEvents()
    {
        if (_playerHealth != null)
        {
            _playerHealth.OnPlayerDeath += HandlePlayerDeath;
        }
        else
        {
            #if UNITY_EDITOR || DEVELOPMENT_BUILD
            Debug.LogError("[GameManager] PlayerHealth referansı null — " +
                           "Game Over tetiklenemeyecek!", this);
            #endif
        }
    }

    private void UnsubscribeFromEvents()
    {
        if (_playerHealth != null)
        {
            _playerHealth.OnPlayerDeath -= HandlePlayerDeath;
        }
    }

    // ════════════════════════════════════════════════════════════════
    //  GAME OVER AKIŞI
    // ════════════════════════════════════════════════════════════════

    /// <summary>
    /// PlayerHealth.OnPlayerDeath event'i tarafından tetiklenir.
    /// 
    /// Akış:
    /// 1. State'i GameOver'a geçir
    /// 2. OnGameStateChanged event'ini tetikle (diğer sistemler dursun)
    /// 3. Time.timeScale = 0 (tüm gameplay duraklar)
    /// 4. Game Over UI panelini aç
    /// 5. İstatistikleri göster
    /// 
    /// Neden timeScale = 0?
    /// ────────────────────
    /// Tüm Time.deltaTime bağımlı sistemler otomatik durur:
    ///   - Enemy hareket → durur
    ///   - Bullet hareket → durur
    ///   - EnemySpawner timer → durur
    ///   - PlayerShooting timer → durur
    ///   - Particle sistemleri → durur (güzel freeze efekti)
    /// 
    /// Tek bir satırla tüm gameplay pause'lanır — her sisteme
    /// ayrıca "dur" komutu göndermeye gerek yok.
    /// 
    /// Dikkat: Time.unscaledDeltaTime hâlâ çalışır.
    /// UI animasyonları (buton hover, panel fade-in) bunla yapılmalı.
    /// </summary>
    private void HandlePlayerDeath()
    {
        // Zaten GameOver'daysa tekrar tetikleme (güvenlik)
        if (_currentState == GameState.GameOver)
            return;

        // ── State geçişi ──
        _currentState = GameState.GameOver;

        // ── Diğer sistemleri bilgilendir ──
        OnGameStateChanged?.Invoke(_currentState);

        #if UNITY_EDITOR || DEVELOPMENT_BUILD
        Debug.Log($"[GameManager] GAME OVER — Süre: {FormatTime(_elapsedTime)} | " +
                  $"Altın: {(GoldManager.Instance != null ? GoldManager.Instance.TotalGoldEarned : 0)}");
        #endif

        // ── Zamanı durdur ──
        Time.timeScale = 0f;

        // ── UI'ı göster ──
        ShowGameOverUI();
    }

    /// <summary>
    /// Game Over panelini açar ve istatistikleri doldurur.
    /// </summary>
    private void ShowGameOverUI()
    {
        if (_gameOverPanel == null)
        {
            #if UNITY_EDITOR || DEVELOPMENT_BUILD
            Debug.LogWarning("[GameManager] GameOverPanel referansı null — UI gösterilemiyor!", this);
            #endif
            return;
        }

        // ── İstatistik metinlerini doldur ──
        if (_survivalTimeText != null)
        {
            _survivalTimeText.SetText(FormatTime(_elapsedTime));
        }

        if (_totalGoldText != null)
        {
            int totalGold = GoldManager.Instance != null
                ? GoldManager.Instance.TotalGoldEarned
                : 0;
            _totalGoldText.SetText(totalGold.ToString());
        }

        // ── Paneli aç ──
        _gameOverPanel.SetActive(true);
    }

    // ════════════════════════════════════════════════════════════════
    //  RESTART
    // ════════════════════════════════════════════════════════════════

    /// <summary>
    /// Oyunu yeniden başlatır. UI Button'ın OnClick event'ine bağlanır.
    /// 
    /// Akış:
    /// 1. OnBeforeRestart event'i tetikle (kaydetme fırsatı)
    /// 2. Time.timeScale = 1 (KRİTİK — yoksa yeni sahne de donuk başlar)
    /// 3. Aktif sahneyi yeniden yükle
    /// 
    /// ╔═══════════════════════════════════════════════════════════╗
    /// ║  KRİTİK: Time.timeScale MUTLAKA LoadScene'den ÖNCE      ║
    /// ║  1'e set edilmelidir!                                     ║
    /// ║                                                           ║
    /// ║  Scene reload timeScale'i SIFIRLAMAz.                    ║
    /// ║  timeScale = 0 ile yüklenen sahne donuk başlar —         ║
    /// ║  hiçbir şey hareket etmez, timer'lar çalışmaz.           ║
    /// ║  Bu, debug edilmesi çok zor bir bug'dır.                  ║
    /// ╚═══════════════════════════════════════════════════════════╝
    /// 
    /// Neden Scene Reload?
    /// → Tüm GameObject'ler yok edilip yeniden oluşturulur.
    /// → Tüm pool'lar, timer'lar, state'ler garanti sıfırlanır.
    /// → State sızıntısı riski SIFIR.
    /// → Dezavantaj: ~200-500ms yükleme süresi (mobilde kabul edilebilir).
    /// </summary>
    public void RestartGame()
    {
        // ── Son dakika işlemleri (save, analytics vb.) ──
        OnBeforeRestart?.Invoke();

        // ── timeScale'i ÖNCE resetle ──
        Time.timeScale = 1f;

        // ── Sahneyi yeniden yükle ──
        // GetActiveScene().buildIndex: Mevcut sahnenin build index'ini alır.
        // Sahne adı hardcoded değil — sahne yeniden adlandırılsa bile çalışır.
        int currentSceneIndex = SceneManager.GetActiveScene().buildIndex;
        SceneManager.LoadScene(currentSceneIndex);
    }

    // ════════════════════════════════════════════════════════════════
    //  YARDIMCI METODLAR
    // ════════════════════════════════════════════════════════════════

    /// <summary>
    /// Saniyeyi "M:SS" formatına çevirir.
    /// Örnek: 125.7 saniye → "2:05"
    /// 
    /// GC notu: ToString("D2") her çağrıda string oluşturur.
    /// Ama bu sadece Game Over'da 1 kez çağrılır — kabul edilebilir.
    /// </summary>
    private string FormatTime(float totalSeconds)
    {
        int minutes = Mathf.FloorToInt(totalSeconds / 60f);
        int seconds = Mathf.FloorToInt(totalSeconds % 60f);
        return minutes + ":" + seconds.ToString("D2");
    }

    // ════════════════════════════════════════════════════════════════
    //  DOĞRULAMA
    // ════════════════════════════════════════════════════════════════

    [System.Diagnostics.Conditional("UNITY_EDITOR"),
     System.Diagnostics.Conditional("DEVELOPMENT_BUILD")]
    private void ValidateSetup()
    {
        if (_playerHealth == null)
            Debug.LogError("[GameManager] PlayerHealth atanmamış! " +
                           "Game Over tetiklenemeyecek.", this);

        if (_gameOverPanel == null)
            Debug.LogWarning("[GameManager] GameOverPanel atanmamış — " +
                             "Game Over UI gösterilmeyecek.", this);
    }
}
