using UnityEngine;
using UnityEngine.SceneManagement;
using TMPro;
using System;

public class GameManager : MonoBehaviour
{
    public static GameManager Instance { get; private set; }

    public enum GameState
    {
        MainMenu,
        Playing,
        GameOver
    }

    public event Action<GameState> OnGameStateChanged;

    public event Action OnBeforeRestart;

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

    [Tooltip("Öldürülen düşman sayısını gösteren text.\n" +
             "null bırakılabilir — yoksa atlanır.")]
    [SerializeField]
    private TMPro.TextMeshProUGUI _killsText;

    [Header("─── Ana Menü Vitrin (UI) ───")]
    [Tooltip("Ana Menüdeki toplam altın göstergesi.\n" +
             "Rich Text: '<color=#FFC107>GOLD: 150</color>'")]
    [SerializeField]
    private TextMeshProUGUI _mainMenuTotalGoldText;

    [Tooltip("Ana Menüdeki yüksek skor göstergesi.\n" +
             "Rich Text: '<color=#00C2FF>HIGH SCORE: 2:35</color>'")]
    [SerializeField]
    private TextMeshProUGUI _mainMenuHighScoreText;

    [Header("─── Oyuncu Referansı ───")]
    [Tooltip("PlayerHealth bileşenine sahip Player objesi.")]
    [SerializeField]
    private PlayerHealth _playerHealth;

    private GameState _currentState;

    private float _elapsedTime;

    private int _runKills;

    private static bool _autoStartGame = false;

    public GameState CurrentState => _currentState;

    public bool IsPlaying => _currentState == GameState.Playing;

    public float ElapsedTime => _elapsedTime;

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;

        ValidateSetup();

        _currentState = GameState.MainMenu;
        _elapsedTime  = 0f;

        Time.timeScale = 0f;

        if (_mainMenuPanel != null)
            _mainMenuPanel.SetActive(true);

        if (_gameOverPanel != null)
            _gameOverPanel.SetActive(false);

        if (_shopPanel != null)
            _shopPanel.SetActive(false);

        if (_pausePanel != null)
            _pausePanel.SetActive(false);

        if (_pauseButtonHUD != null)
            _pauseButtonHUD.SetActive(false);
    }

    private void Start()
    {
        SubscribeToEvents();

        if (_currentState == GameState.MainMenu)
        {
            RefreshMainMenuUI();
        }

        if (_autoStartGame)
        {
            _autoStartGame = false;
            StartGame();
        }
    }

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

        if (CombatManager.Instance != null)
        {
            CombatManager.Instance.OnEnemyKilled += HandleEnemyKilledForStats;
        }

        if (GoldManager.Instance != null)
        {
            GoldManager.Instance.OnGoldChanged += HandleMenuGoldChanged;
        }
    }

    private void UnsubscribeFromEvents()
    {
        if (_playerHealth != null)
        {
            _playerHealth.OnPlayerDeath -= HandlePlayerDeath;
        }

        if (CombatManager.Instance != null)
        {
            CombatManager.Instance.OnEnemyKilled -= HandleEnemyKilledForStats;
        }

        if (GoldManager.Instance != null)
        {
            GoldManager.Instance.OnGoldChanged -= HandleMenuGoldChanged;
        }
    }

    private void HandleEnemyKilledForStats(int goldValue, Vector3 position)
    {
        _runKills++;
    }

    private void HandleMenuGoldChanged(int newGold)
    {
        if (_currentState == GameState.MainMenu)
        {
            RefreshMainMenuUI();
        }
    }

    public void StartGame()
    {
        if (_currentState != GameState.MainMenu)
            return;

        _currentState = GameState.Playing;

        _runKills = 0;

        Time.timeScale = 1f;

        if (_mainMenuPanel != null)
            _mainMenuPanel.SetActive(false);

        if (_shopPanel != null)
            _shopPanel.SetActive(true);

        if (_shopCanvasGroup != null)
        {
            _shopCanvasGroup.interactable = false;
            _shopCanvasGroup.blocksRaycasts = false;
        }

        if (_pauseButtonHUD != null)
            _pauseButtonHUD.SetActive(true);

        OnGameStateChanged?.Invoke(_currentState);

        #if UNITY_EDITOR || DEVELOPMENT_BUILD
        Debug.Log("[GameManager] Oyun başladı — State: Playing");
        #endif
    }

    public void ToggleShopMenu()
    {
        if (_currentState != GameState.MainMenu)
            return;

        if (_shopPanel == null)
            return;

        bool isCurrentlyActive = _shopPanel.activeSelf;

        if (isCurrentlyActive)
        {
            _shopPanel.SetActive(false);
        }
        else
        {
            _shopPanel.SetActive(true);

            if (_shopCanvasGroup != null)
            {
                _shopCanvasGroup.interactable = true;
                _shopCanvasGroup.blocksRaycasts = true;
            }
        }
    }

    public void PauseGame()
    {
        if (_currentState != GameState.Playing)
            return;

        Time.timeScale = 0f;

        if (_pausePanel != null)
            _pausePanel.SetActive(true);

        if (_pauseButtonHUD != null)
            _pauseButtonHUD.SetActive(false);

        #if UNITY_EDITOR || DEVELOPMENT_BUILD
        Debug.Log("[GameManager] Oyun duraklatıldı.");
        #endif
    }

    public void ResumeGame()
    {
        Time.timeScale = 1f;

        if (_pausePanel != null)
            _pausePanel.SetActive(false);

        if (_pauseButtonHUD != null)
            _pauseButtonHUD.SetActive(true);

        #if UNITY_EDITOR || DEVELOPMENT_BUILD
        Debug.Log("[GameManager] Oyuna devam edildi.");
        #endif
    }

    public void ReturnToMainMenu()
    {
        Time.timeScale = 1f;

        _autoStartGame = false;

        int currentSceneIndex = SceneManager.GetActiveScene().buildIndex;
        SceneManager.LoadScene(currentSceneIndex);
    }

    private void HandlePlayerDeath()
    {
        if (_currentState == GameState.GameOver)
            return;

        _currentState = GameState.GameOver;

        if (_pauseButtonHUD != null)
            _pauseButtonHUD.SetActive(false);

        OnGameStateChanged?.Invoke(_currentState);

        #if UNITY_EDITOR || DEVELOPMENT_BUILD
        Debug.Log($"[GameManager] GAME OVER — Süre: {FormatTime(_elapsedTime)} | " +
                  $"Altın: {(GoldManager.Instance != null ? GoldManager.Instance.TotalGoldEarned : 0)}");
        #endif

        if (SaveManager.Instance != null)
        {
            SaveManager.Instance.TryUpdateHighScore(_elapsedTime);
            SaveManager.Instance.IncrementGamesPlayed();
            SaveManager.Instance.Save();
        }

        Time.timeScale = 0f;

        ShowGameOverUI();
    }

    private void ShowGameOverUI()
    {
        if (_gameOverPanel == null)
        {
            #if UNITY_EDITOR || DEVELOPMENT_BUILD
            Debug.LogWarning("[GameManager] GameOverPanel referansı null — UI gösterilemiyor!", this);
            #endif
            return;
        }

        if (_survivalTimeText != null)
        {
            _survivalTimeText.SetText("Time: " + FormatTime(_elapsedTime));
        }

        if (_totalGoldText != null)
        {
            int runGold = GoldManager.Instance != null
                ? GoldManager.Instance.CurrentRunGold
                : 0;
            _totalGoldText.SetText("Gold Earned: " + runGold.ToString());
        }

        if (_killsText != null)
        {
            _killsText.SetText("Kills: " + _runKills.ToString());
        }

        _gameOverPanel.SetActive(true);
    }

    public void RestartGame()
    {
        OnBeforeRestart?.Invoke();

        Time.timeScale = 1f;

        _autoStartGame = true;

        int currentSceneIndex = SceneManager.GetActiveScene().buildIndex;
        SceneManager.LoadScene(currentSceneIndex);
    }

    private void RefreshMainMenuUI()
    {
        if (_mainMenuTotalGoldText != null)
        {
            int gold = SaveManager.Instance != null
                ? SaveManager.Instance.PersistentGold
                : 0;

            _mainMenuTotalGoldText.SetText(
                "<color=#FFC107>GOLD: " + gold.ToString() + "</color>");
        }

        if (_mainMenuHighScoreText != null)
        {
            float highScore = SaveManager.Instance != null
                ? SaveManager.Instance.HighScore
                : 0f;

            string formattedScore = FormatTime(highScore);

            _mainMenuHighScoreText.SetText(
                "<color=#00C2FF>HIGH SCORE: " + formattedScore + "</color>");
        }
    }

    private string FormatTime(float totalSeconds)
    {
        int minutes = Mathf.FloorToInt(totalSeconds / 60f);
        int seconds = Mathf.FloorToInt(totalSeconds % 60f);
        return minutes + ":" + seconds.ToString("D2");
    }

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

        if (_mainMenuTotalGoldText == null)
            Debug.LogWarning("[GameManager] MainMenuTotalGoldText atanmamış — " +
                             "Ana Menüde altın gösterilmeyecek.", this);

        if (_mainMenuHighScoreText == null)
            Debug.LogWarning("[GameManager] MainMenuHighScoreText atanmamış — " +
                             "Ana Menüde yüksek skor gösterilmeyecek.", this);
    }
}