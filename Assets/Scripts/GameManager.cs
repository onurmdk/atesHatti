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
/// • Oyun durumunu (MainMenu, Playing, GameOver) yönetir
/// • Ana Menüde zamanı dondurur, BAŞLAT ile oyunu başlatır
/// • PlayerDeath event'ini dinleyip GameOver akışını tetikler
/// • Time.timeScale kontrolü (menu freeze / gameplay / pause)
/// • Sahne yeniden yükleme (restart)
/// • UI panellerini (MainMenu, GameOver) açıp kapatır
/// • Oyun süresi takibi (istatistik için)
/// 
/// State Akış Diyagramı:
/// ────────────────────
///   ┌───────────┐  StartGame()   ┌──────────┐  PlayerDeath   ┌───────────┐
///   │ MAIN_MENU │ ─────────────▶ │ PLAYING  │ ─────────────▶ │ GAME_OVER │
///   │ (donuk)   │                │ (aktif)  │                │ (donuk)   │
///   └───────────┘                └──────────┘                └─────┬─────┘
///                                      ▲                          │
///                                      │     RestartGame()        │
///                                      │     (Scene Reload)       │
///                                      └──────────────────────────┘
///                                      ↑ Reload → Awake → MainMenu
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
    /// State Akış Diyagramı:
    /// ────────────────────
    ///   ┌───────────┐  StartGame()   ┌──────────┐  PlayerDeath   ┌───────────┐
    ///   │ MAIN_MENU │ ─────────────▶ │ PLAYING  │ ─────────────▶ │ GAME_OVER │
    ///   │ (donuk)   │                │ (aktif)  │                │ (donuk)   │
    ///   └───────────┘                └──────────┘                └─────┬─────┘
    ///                                      ▲                          │
    ///                                      │     RestartGame()        │
    ///                                      │     (Scene Reload)       │
    ///                                      └──────────────────────────┘
    ///                                      ↑ Reload → Awake → MainMenu
    /// </summary>
    public enum GameState
    {
        MainMenu,
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

    [Header("─── Ana Menü UI ───")]
    [Tooltip("Ana Menü paneli. Oyun açıldığında aktif olur.\n" +
             "BAŞLAT butonuna basılınca kapanır.\n" +
             "Hierarchy: Canvas → MainMenuPanel")]
    [SerializeField]
    private GameObject _mainMenuPanel;

    [Header("─── Mağaza / HUD Paneli ───")]
    [Tooltip("ShopPanel objesi. Ana Menüde toggle ile açılıp kapanır.\n" +
             "Oyun başladığında HUD olarak görünür ama tıklanamaz.\n" +
             "Hierarchy: Canvas → ShopPanel")]
    [SerializeField]
    private GameObject _shopPanel;

    [Tooltip("ShopPanel üzerindeki CanvasGroup bileşeni.\n" +
             "interactable ve blocksRaycasts kontrolü için kullanılır.\n" +
             "Ana Menüde: tıklanabilir (mağaza). Oyun içinde: sadece görüntü (HUD).")]
    [SerializeField]
    private CanvasGroup _shopCanvasGroup;

    [Header("─── Game Over UI ───")]
    [Tooltip("Game Over paneli. Başlangıçta deaktif olmalı.\n" +
             "Hierarchy: Canvas → GameOverPanel")]
    [SerializeField]
    private GameObject _gameOverPanel;

    [Header("─── Duraklatma (Pause) UI ───")]
    [Tooltip("Duraklatma menü paneli. Oyun içinde Pause butonuna basılınca açılır.\n" +
             "Hierarchy: Canvas → PausePanel")]
    [SerializeField]
    private GameObject _pausePanel;

    [Tooltip("Sağ üst köşedeki durdurma butonu. Oyun sırasında görünür.\n" +
             "MainMenu ve GameOver'da gizlenir.\n" +
             "Hierarchy: Canvas → Btn_Pause")]
    [SerializeField]
    private GameObject _pauseButtonHUD;

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

        // ── Başlangıç state: Ana Menü ──
        // Oyun MainMenu state'inde başlar — timeScale = 0.
        // Tüm gameplay sistemleri (spawner, shooting, hareket) donuk kalır.
        // BAŞLAT butonuna basılınca StartGame() çağrılır → Playing'e geçilir.
        _currentState = GameState.MainMenu;
        _elapsedTime  = 0f;

        // ── Zamanı dondur (Ana Menü ekranı) ──
        Time.timeScale = 0f;

        // ── Panelleri ayarla ──
        if (_mainMenuPanel != null)
            _mainMenuPanel.SetActive(true);

        if (_gameOverPanel != null)
            _gameOverPanel.SetActive(false);

        // ── ShopPanel: Ana Menüde başlangıçta gizli ──
        // Oyuncu "MAĞAZAYI GÖRÜNTÜLE" butonuyla açacak.
        if (_shopPanel != null)
            _shopPanel.SetActive(false);

        // ── Pause UI: Ana Menüde kapalı ──
        // Sadece Playing state'inde Btn_Pause görünür olacak.
        if (_pausePanel != null)
            _pausePanel.SetActive(false);

        if (_pauseButtonHUD != null)
            _pauseButtonHUD.SetActive(false);
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
    //  ANA MENÜ → OYUN BAŞLATMA
    // ════════════════════════════════════════════════════════════════

    /// <summary>
    /// Ana Menüdeki BAŞLAT butonuna basıldığında çağrılır.
    /// 
    /// Akış:
    /// 1. State'i Playing'e geçir
    /// 2. Time.timeScale = 1 → tüm gameplay sistemleri canlanır
    /// 3. Ana Menü panelini kapat
    /// 4. OnGameStateChanged event'i tetikle
    /// 
    /// Neden timeScale ile kontrol?
    /// ────────────────────────────
    /// Ana Menü ekranındayken timeScale = 0 olduğu için:
    ///   - EnemySpawner timer'ı ilerlemez → düşman spawn olmaz
    ///   - PlayerShooting timer'ı ilerlemez → mermi atılmaz
    ///   - Bullet/Enemy hareketi durur → sahne donuk
    ///   - _elapsedTime artmaz → süre sayılmaz
    /// 
    /// BAŞLAT'a basılınca timeScale = 1 → her şey aynı anda canlanır.
    /// Hiçbir sisteme ayrıca "başla" komutu göndermeye gerek yok.
    /// 
    /// Inspector Bağlantısı:
    ///   MainMenuPanel → BaşlatButton → OnClick → GameManager.StartGame()
    /// </summary>
    public void StartGame()
    {
        // Güvenlik: Sadece MainMenu state'inden çağrılabilir
        if (_currentState != GameState.MainMenu)
            return;

        // ── State geçişi ──
        _currentState = GameState.Playing;

        // ── Zamanı başlat ──
        Time.timeScale = 1f;

        // ── Ana Menü panelini kapat ──
        if (_mainMenuPanel != null)
            _mainMenuPanel.SetActive(false);

        // ── ShopPanel'i HUD moduna geçir ──
        // Görünür ama tıklanamaz → oyun içinde sadece bilgi gösterir.
        // interactable = false: Butonlar tepki vermez.
        // blocksRaycasts = false: Dokunma olayları ShopPanel'i delip
        //   alttaki gameplay'e (PlayerController touch input) ulaşır.
        //   Bu olmadan oyuncu shop panelinin üzerinde gemiyi hareket ettiremez.
        if (_shopPanel != null)
            _shopPanel.SetActive(true);

        if (_shopCanvasGroup != null)
        {
            _shopCanvasGroup.interactable = false;
            _shopCanvasGroup.blocksRaycasts = false;
        }

        // ── Pause butonunu göster ──
        // Oyun sırasında sağ üst köşede görünür olacak.
        if (_pauseButtonHUD != null)
            _pauseButtonHUD.SetActive(true);

        // ── Diğer sistemleri bilgilendir ──
        OnGameStateChanged?.Invoke(_currentState);

        #if UNITY_EDITOR || DEVELOPMENT_BUILD
        Debug.Log("[GameManager] Oyun başladı — State: Playing");
        #endif
    }

    // ════════════════════════════════════════════════════════════════
    //  ANA MENÜ — MAĞAZA TOGGLE
    // ════════════════════════════════════════════════════════════════

    /// <summary>
    /// Ana Menüdeki "MAĞAZAYI GÖRÜNTÜLE" butonuna basıldığında çağrılır.
    /// ShopPanel'in aktiflik durumunu tersine çevirir (toggle).
    /// 
    /// Açık → Kapalı: SetActive(false)
    /// Kapalı → Açık: SetActive(true) + tıklanabilir
    /// 
    /// Neden CanvasGroup.interactable ve blocksRaycasts?
    /// ─────────────────────────────────────────────────
    /// Mağaza açıkken butonlara tıklanabilmeli (upgrade satın alma).
    /// Bu yüzden interactable = true ve blocksRaycasts = true.
    /// 
    /// Oyun başladığında (StartGame) ise panel HUD'a dönüşür:
    /// interactable = false → butonlar tepki vermez
    /// blocksRaycasts = false → dokunma alttaki gameplay'e geçer
    /// 
    /// Güvenlik: Sadece MainMenu state'inde çalışır.
    /// Oyun sırasında veya GameOver'da mağaza toggle edilemez.
    /// 
    /// Inspector Bağlantısı:
    ///   MainMenuPanel → MağazaButton → OnClick → GameManager.ToggleShopMenu()
    /// </summary>
    public void ToggleShopMenu()
    {
        // Güvenlik: Sadece Ana Menüde çalışsın
        if (_currentState != GameState.MainMenu)
            return;

        if (_shopPanel == null)
            return;

        // ── Toggle: Açıksa kapat, kapalıysa aç ──
        bool isCurrentlyActive = _shopPanel.activeSelf;

        if (isCurrentlyActive)
        {
            // ── Mağazayı kapat ──
            _shopPanel.SetActive(false);
        }
        else
        {
            // ── Mağazayı aç (tam etkileşimli) ──
            _shopPanel.SetActive(true);

            if (_shopCanvasGroup != null)
            {
                _shopCanvasGroup.interactable = true;
                _shopCanvasGroup.blocksRaycasts = true;
            }
        }
    }

    // ════════════════════════════════════════════════════════════════
    //  DURAKLATMA (PAUSE) SİSTEMİ
    // ════════════════════════════════════════════════════════════════

    /// <summary>
    /// Oyunu duraklatır. Btn_Pause'un OnClick'ine bağlanır.
    /// 
    /// Güvenlik: SADECE Playing state'inde çalışır.
    /// GameOver'da Pause butonuna basılması engellenmiş olur —
    /// çünkü GameOver'da zaten timeScale = 0 ve pause butonu gizli.
    /// Ama yine de state guard koyuyoruz (defensive programming).
    /// 
    /// Akış:
    /// 1. timeScale = 0 → tüm gameplay donar
    /// 2. PausePanel açılır (Devam Et / Ana Menü butonları)
    /// 3. Btn_Pause gizlenir (pause menüsü zaten açık)
    /// 
    /// State DEĞİŞMİYOR — hâlâ Playing.
    /// Pause geçici bir UI durumu, state machine'de ayrı state değil.
    /// Neden? → Pause'dan çıkınca Playing'e "geri dönmek" yerine
    /// "hiç ayrılmamış" olmak daha temiz. HandlePlayerDeath
    /// sadece Playing state'inde tetikleniyor, pause sırasında
    /// timeScale = 0 olduğu için zaten ölüm gerçekleşemez.
    /// 
    /// Inspector Bağlantısı:
    ///   Btn_Pause → OnClick → GameManager.PauseGame()
    /// </summary>
    public void PauseGame()
    {
        // Güvenlik: Sadece oyun oynanırken pause yapılabilir
        if (_currentState != GameState.Playing)
            return;

        // ── Zamanı durdur ──
        Time.timeScale = 0f;

        // ── Pause panelini aç ──
        if (_pausePanel != null)
            _pausePanel.SetActive(true);

        // ── Pause butonunu gizle (menü zaten açık) ──
        if (_pauseButtonHUD != null)
            _pauseButtonHUD.SetActive(false);

        #if UNITY_EDITOR || DEVELOPMENT_BUILD
        Debug.Log("[GameManager] Oyun duraklatıldı.");
        #endif
    }

    /// <summary>
    /// Oyuna devam eder. PausePanel'deki "DEVAM ET" butonuna bağlanır.
    /// 
    /// Akış:
    /// 1. timeScale = 1 → gameplay devam eder
    /// 2. PausePanel kapanır
    /// 3. Btn_Pause tekrar görünür olur
    /// 
    /// Inspector Bağlantısı:
    ///   PausePanel → Btn_Resume → OnClick → GameManager.ResumeGame()
    /// </summary>
    public void ResumeGame()
    {
        // ── Zamanı başlat ──
        Time.timeScale = 1f;

        // ── Pause panelini kapat ──
        if (_pausePanel != null)
            _pausePanel.SetActive(false);

        // ── Pause butonunu tekrar göster ──
        if (_pauseButtonHUD != null)
            _pauseButtonHUD.SetActive(true);

        #if UNITY_EDITOR || DEVELOPMENT_BUILD
        Debug.Log("[GameManager] Oyuna devam edildi.");
        #endif
    }

    /// <summary>
    /// Duraklatma menüsünden Ana Menüye döner.
    /// PausePanel'deki "ANA MENÜYE DÖN" butonuna bağlanır.
    /// 
    /// Akış:
    /// 1. timeScale = 1 (KRİTİK — Scene reload timeScale'i sıfırlamaz)
    /// 2. Sahneyi yeniden yükle → Awake → MainMenu state → timeScale = 0
    /// 
    /// Neden ayrı bir metod ve RestartGame() kullanmıyoruz?
    /// → RestartGame OnBeforeRestart event'i tetikler (save, analytics).
    ///   Ana Menüye dönüşte bunlar tetiklenmemeli — oyun henüz "bitmedi",
    ///   oyuncu sadece menüye dönmek istiyor.
    ///   İleride bu ayrımı kullanabiliriz (pause'dan dönüşte reklam gösterme,
    ///   ama game over'da göster gibi).
    /// 
    /// Inspector Bağlantısı:
    ///   PausePanel → Btn_MainMenu → OnClick → GameManager.ReturnToMainMenu()
    /// </summary>
    public void ReturnToMainMenu()
    {
        // ── timeScale'i ÖNCE resetle ──
        Time.timeScale = 1f;

        // ── Sahneyi yeniden yükle ──
        // Awake çalışır → _currentState = MainMenu → timeScale = 0
        // → MainMenuPanel açılır → tüm state temiz başlar
        int currentSceneIndex = SceneManager.GetActiveScene().buildIndex;
        SceneManager.LoadScene(currentSceneIndex);
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

        // ── Pause butonunu gizle (Game Over'da pause anlamsız) ──
        if (_pauseButtonHUD != null)
            _pauseButtonHUD.SetActive(false);

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

        if (_mainMenuPanel == null)
            Debug.LogWarning("[GameManager] MainMenuPanel atanmamış — " +
                             "Ana Menü gösterilmeyecek.", this);

        if (_shopPanel == null)
            Debug.LogWarning("[GameManager] ShopPanel atanmamış — " +
                             "Mağaza/HUD gösterilmeyecek.", this);

        if (_shopCanvasGroup == null && _shopPanel != null)
            Debug.LogWarning("[GameManager] ShopPanel'e CanvasGroup atanmamış — " +
                             "Etkileşim kontrolü çalışmayacak.", this);

        if (_gameOverPanel == null)
            Debug.LogWarning("[GameManager] GameOverPanel atanmamış — " +
                             "Game Over UI gösterilmeyecek.", this);

        if (_pausePanel == null)
            Debug.LogWarning("[GameManager] PausePanel atanmamış — " +
                             "Duraklatma menüsü gösterilmeyecek.", this);

        if (_pauseButtonHUD == null)
            Debug.LogWarning("[GameManager] PauseButtonHUD atanmamış — " +
                             "Duraklatma butonu gösterilmeyecek.", this);
    }
}
