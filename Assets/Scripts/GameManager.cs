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

    [Header("─── Main Menu UI ───")]
    [Tooltip("Main Menu panel. Becomes active when the game opens.\n" +
             "Closes when the START button is pressed.\n" +
             "Hierarchy: Canvas → MainMenuPanel")]
    [SerializeField]
    private GameObject _mainMenuPanel;

    [Header("─── Shop / HUD Panel ───")]
    [Tooltip("ShopPanel object. Toggled open and closed in the Main Menu.\n" +
             "Visible as the HUD when the game is running but not clickable.\n" +
             "Hierarchy: Canvas → ShopPanel")]
    [SerializeField]
    private GameObject _shopPanel;

    [Tooltip("CanvasGroup component on the ShopPanel.\n" +
             "Used to control interactable and blocksRaycasts.\n" +
             "In Main Menu: clickable (shop). In-game: display only (HUD).")]
    [SerializeField]
    private CanvasGroup _shopCanvasGroup;

    [Header("─── Game Over UI ───")]
    [Tooltip("Game Over panel. Must be inactive at startup.\n" +
             "Hierarchy: Canvas → GameOverPanel")]
    [SerializeField]
    private GameObject _gameOverPanel;

    [Header("─── Pause UI ───")]
    [Tooltip("Pause menu panel. Opens when the Pause button is pressed during gameplay.\n" +
             "Hierarchy: Canvas → PausePanel")]
    [SerializeField]
    private GameObject _pausePanel;

    [Tooltip("Stop button in the top-right corner. Visible during gameplay.\n" +
             "Hidden on MainMenu and GameOver.\n" +
             "Hierarchy: Canvas → Btn_Pause")]
    [SerializeField]
    private GameObject _pauseButtonHUD;

    [Header("─── Stats Texts (Optional) ───")]
    [Tooltip("Text showing survival time.\n" +
             "Can be left null — skipped if missing.")]
    [SerializeField]
    private TMPro.TextMeshProUGUI _survivalTimeText;

    [Tooltip("Text showing total gold earned.\n" +
             "Can be left null — skipped if missing.")]
    [SerializeField]
    private TMPro.TextMeshProUGUI _totalGoldText;

    [Tooltip("Text showing number of enemies killed.\n" +
             "Can be left null — skipped if missing.")]
    [SerializeField]
    private TMPro.TextMeshProUGUI _killsText;

    [Header("─── Main Menu Display (UI) ───")]
    [Tooltip("Total gold display in the Main Menu.\n" +
             "Rich Text: '<color=#FFC107>GOLD: 150</color>'")]
    [SerializeField]
    private TextMeshProUGUI _mainMenuTotalGoldText;

    [Tooltip("High score display in the Main Menu.\n" +
             "Rich Text: '<color=#00C2FF>HIGH SCORE: 2:35</color>'")]
    [SerializeField]
    private TextMeshProUGUI _mainMenuHighScoreText;

    [Header("─── Player Reference ───")]
    [Tooltip("Player object with a PlayerHealth component.")]
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
            Debug.LogError("[GameManager] PlayerHealth reference is null — " +
                           "Game Over cannot be triggered!", this);
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
        Debug.Log("[GameManager] Game started — State: Playing");
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
        Debug.Log("[GameManager] Game paused.");
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
        Debug.Log("[GameManager] Game resumed.");
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
        Debug.Log($"[GameManager] GAME OVER — Time: {FormatTime(_elapsedTime)} | " +
                  $"Gold: {(GoldManager.Instance != null ? GoldManager.Instance.TotalGoldEarned : 0)}");
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
            Debug.LogWarning("[GameManager] GameOverPanel reference is null — UI cannot be shown!", this);
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
            Debug.LogError("[GameManager] PlayerHealth not assigned! " +
                           "Game Over cannot be triggered.", this);

        if (_mainMenuPanel == null)
            Debug.LogWarning("[GameManager] MainMenuPanel not assigned — " +
                             "Main Menu will not be shown.", this);

        if (_shopPanel == null)
            Debug.LogWarning("[GameManager] ShopPanel not assigned — " +
                             "Shop/HUD will not be shown.", this);

        if (_shopCanvasGroup == null && _shopPanel != null)
            Debug.LogWarning("[GameManager] No CanvasGroup assigned to ShopPanel — " +
                             "Interaction control will not work.", this);

        if (_gameOverPanel == null)
            Debug.LogWarning("[GameManager] GameOverPanel not assigned — " +
                             "Game Over UI will not be shown.", this);

        if (_pausePanel == null)
            Debug.LogWarning("[GameManager] PausePanel not assigned — " +
                             "Pause menu will not be shown.", this);

        if (_pauseButtonHUD == null)
            Debug.LogWarning("[GameManager] PauseButtonHUD not assigned — " +
                             "Pause button will not be shown.", this);

        if (_mainMenuTotalGoldText == null)
            Debug.LogWarning("[GameManager] MainMenuTotalGoldText not assigned — " +
                             "Gold will not be displayed in the Main Menu.", this);

        if (_mainMenuHighScoreText == null)
            Debug.LogWarning("[GameManager] MainMenuHighScoreText not assigned — " +
                             "High score will not be displayed in the Main Menu.", this);
    }
}