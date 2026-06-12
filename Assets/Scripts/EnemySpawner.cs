using UnityEngine;
using UnityEngine.Pool;

public class EnemySpawner : MonoBehaviour
{
    [Header("─── Düşman Prefab'ları ───")]
    [Tooltip("3 elemanlı dizi: [0]=Weak, [1]=Medium, [2]=Strong.\n" +
             "Her prefab kendi stat'larını (HP, speed, drift, gold)\n" +
             "Inspector'da taşır. Sıralama KRİTİK — CDF indeksleri buna bağlı.")]
    [SerializeField]
    private Enemy[] _enemyPrefabs;

    [Header("─── Pool Ayarları (Her Prefab İçin) ───")]
    [Tooltip("Her pool'un başlangıç kapasitesi.")]
    [SerializeField, Range(5, 30)]
    private int _poolDefaultCapacity = 10;

    [Tooltip("Her pool'un maksimum inaktif obje sayısı.")]
    [SerializeField, Range(15, 60)]
    private int _poolMaxSize = 25;

    [Header("─── Spawn Zamanlaması ───")]
    [Tooltip("İlk spawn aralığı (saniye). Her difficulty bump'ta azalır.")]
    [SerializeField, Range(0.3f, 3f)]
    private float _baseSpawnInterval = 1.2f;

    [Tooltip("Spawn aralığının düşebileceği minimum değer (saniye).\n" +
             "Bu sınır olmazsa spawn hızı sonsuza gider ve ekran düşmanla dolar.")]
    [SerializeField, Range(0.15f, 0.8f)]
    private float _minSpawnInterval = 0.25f;

    [Header("─── Zorluk Skalalaması ───")]
    [Tooltip("Kaç saniyede bir zorluk artar.")]
    [SerializeField, Range(5f, 30f)]
    private float _difficultyInterval = 15f;

    [Tooltip("Her zorluk artışında spawn aralığı bu katsayıyla çarpılır.\n" +
             "0.88 = her 15 saniyede %12 hızlanma.")]
    [SerializeField, Range(0.7f, 0.98f)]
    private float _difficultyMultiplier = 0.88f;

    [Header("─── Spawn Pozisyonu ───")]
    [Tooltip("Düşmanın ekranın üstünden ne kadar yukarıda spawn olacağı.")]
    [SerializeField, Range(0.5f, 3f)]
    private float _spawnOffsetAboveScreen = 1.2f;

    [Header("─── Boss Sistemi ───")]
    [Tooltip("Boss prefab'ı. Inspector'dan sürükle.")]
    [SerializeField]
    private Boss _bossPrefab;

    [Tooltip("İlk boss'un gelme süresi (saniye).")]
    [SerializeField, Range(20f, 120f)]
    private float _bossInterval = 45f;

    [Tooltip("Boss'un ekranın üstünden inip duracağı Y noktası.")]
    [SerializeField]
    private float _bossStopY = 3.5f;

    private readonly float[] _baseTierWeights = { 0.60f, 0.25f, 0.15f };
    private readonly float[] _currentTierCDF  = new float[3];

    private ObjectPool<Enemy>[] _enemyPools;
    private Transform           _enemyContainer;

    private float _spawnTimer;
    private float _currentSpawnInterval;
    private float _difficultyTimer;
    private int   _difficultyLevel;

    private Camera _mainCamera;
    private float  _screenMinX;
    private float  _screenMaxX;
    private float  _screenTopY;
    private float  _screenBottomY;

    private float _prevScreenW;
    private float _prevScreenH;
    private float _prevOrthoSize;

    private bool _isSpawningEnabled = true;

    private float _bossTimer;
    private int   _currentBossLevel;
    private Boss  _activeBoss;
    private bool  _isBossFightActive;

    private void Awake()
    {
        _mainCamera = Camera.main;

        ValidateSetup();
        CreateEnemyContainer();
        RecalculateScreenBounds();
        InitializePools();
        RecalculateTierWeights();

        _currentSpawnInterval = _baseSpawnInterval;
        _spawnTimer           = _currentSpawnInterval;
        _difficultyTimer      = 0f;
        _difficultyLevel      = 0;

        _bossTimer         = 0f;
        _currentBossLevel  = 0;
        _isBossFightActive = false;
        _activeBoss        = null;
    }

    private void Update()
    {
        if (!_isSpawningEnabled) return;

        float dt = Time.deltaTime;
        RefreshBoundsIfChanged();

        if (_isBossFightActive)
        {
            CheckBossStatus();
            return;
        }

        UpdateDifficultyScaling(dt);
        UpdateSpawnTimer(dt);
        UpdateBossTimer(dt);
    }

    private void OnDestroy()
    {
        if (_enemyPools != null)
        {
            for (int i = 0; i < _enemyPools.Length; i++)
            {
                _enemyPools[i]?.Dispose();
            }
        }
    }

    private void InitializePools()
    {
        int prefabCount = _enemyPrefabs.Length;
        _enemyPools = new ObjectPool<Enemy>[prefabCount];

        for (int i = 0; i < prefabCount; i++)
        {
            int prefabIndex = i;

            _enemyPools[i] = new ObjectPool<Enemy>(
                createFunc:      () => OnPoolCreate(prefabIndex),
                actionOnGet:     OnPoolGet,
                actionOnRelease: OnPoolRelease,
                actionOnDestroy: OnPoolDestroy,
                collectionCheck: false,
                defaultCapacity: _poolDefaultCapacity,
                maxSize:         _poolMaxSize
            );

            PreWarmSinglePool(_enemyPools[i]);
        }
    }

    private void PreWarmSinglePool(ObjectPool<Enemy> pool)
    {
        Enemy[] temp = new Enemy[_poolDefaultCapacity];

        for (int i = 0; i < _poolDefaultCapacity; i++)
            temp[i] = pool.Get();

        for (int i = 0; i < _poolDefaultCapacity; i++)
            pool.Release(temp[i]);
    }

    private Enemy OnPoolCreate(int prefabIndex)
    {
        Enemy enemy = Instantiate(_enemyPrefabs[prefabIndex], _enemyContainer);
        return enemy;
    }

    private void OnPoolGet(Enemy enemy)
    {
    }

    private void OnPoolRelease(Enemy enemy)
    {
        enemy.gameObject.SetActive(false);
    }

    private void OnPoolDestroy(Enemy enemy)
    {
        if (enemy != null)
            Destroy(enemy.gameObject);
    }

    private void UpdateSpawnTimer(float dt)
    {
        _spawnTimer -= dt;

        if (_spawnTimer <= 0f)
        {
            SpawnEnemy();

            _spawnTimer += _currentSpawnInterval;

            if (_spawnTimer < 0f)
                _spawnTimer = 0f;
        }
    }

    private void UpdateDifficultyScaling(float dt)
    {
        _difficultyTimer += dt;

        if (_difficultyTimer >= _difficultyInterval)
        {
            _difficultyTimer -= _difficultyInterval;
            _difficultyLevel++;

            _currentSpawnInterval *= _difficultyMultiplier;
            _currentSpawnInterval = Mathf.Max(_currentSpawnInterval, _minSpawnInterval);

            RecalculateTierWeights();

            #if UNITY_EDITOR || DEVELOPMENT_BUILD
            Debug.Log($"[EnemySpawner] Zorluk Lv{_difficultyLevel} | " +
                      $"Spawn: {_currentSpawnInterval:F3}s | " +
                      $"CDF: [{_currentTierCDF[0]:F2}, {_currentTierCDF[1]:F2}, {_currentTierCDF[2]:F2}]");
            #endif
        }
    }

    private void RecalculateTierWeights()
    {
        float smallW  = Mathf.Max(0.25f, _baseTierWeights[0] - _difficultyLevel * 0.03f);
        float mediumW = _baseTierWeights[1] + _difficultyLevel * 0.01f;
        float largeW  = Mathf.Min(0.35f, _baseTierWeights[2] + _difficultyLevel * 0.02f);

        float total = smallW + mediumW + largeW;
        smallW  /= total;
        mediumW /= total;

        _currentTierCDF[0] = smallW;
        _currentTierCDF[1] = smallW + mediumW;
        _currentTierCDF[2] = 1.0f;
    }

    private void SpawnEnemy()
    {
        int tierIndex = SelectTierIndex();

        if (tierIndex < 0 || tierIndex >= _enemyPools.Length)
            return;

        Enemy enemy = _enemyPools[tierIndex].Get();

        SpriteRenderer sr = enemy.GetComponent<SpriteRenderer>();
        float spriteHalfW = 0.5f;
        if (sr != null && sr.sprite != null)
        {
            spriteHalfW = sr.sprite.bounds.extents.x * Mathf.Abs(enemy.transform.localScale.x);
        }

        float spawnX = Random.Range(_screenMinX + spriteHalfW, _screenMaxX - spriteHalfW);
        float spawnY = _screenTopY + _spawnOffsetAboveScreen;

        Vector3 spawnPos = new Vector3(spawnX, spawnY, 0f);

        enemy.Configure(
            pool:          _enemyPools[tierIndex],
            spawnPos:      spawnPos,
            screenMinX:    _screenMinX,
            screenMaxX:    _screenMaxX,
            screenBottomY: _screenBottomY
        );

        enemy.gameObject.SetActive(true);
    }

    private int SelectTierIndex()
    {
        float roll = Random.value;

        if (roll < _currentTierCDF[0]) return 0;
        if (roll < _currentTierCDF[1]) return 1;
        return 2;
    }

    private void UpdateBossTimer(float dt)
    {
        _bossTimer += dt;

        if (_bossTimer >= _bossInterval)
        {
            _bossTimer = 0f;
            SpawnBoss();
        }
    }

    private void SpawnBoss()
    {
        if (_bossPrefab == null)
        {
            #if UNITY_EDITOR || DEVELOPMENT_BUILD
            Debug.LogError("[EnemySpawner] Boss prefab atanmamış!", this);
            #endif
            return;
        }

        _isBossFightActive = true;

        Transform playerTransform = null;
        GameObject playerObj = GameObject.FindWithTag("Player");
        if (playerObj != null)
        {
            playerTransform = playerObj.transform;
        }

        _activeBoss = Instantiate(_bossPrefab);
        _activeBoss.Configure(
            _currentBossLevel,
            _screenMinX,
            _screenMaxX,
            _bossStopY,
            playerTransform
        );

        #if UNITY_EDITOR || DEVELOPMENT_BUILD
        Debug.Log($"[EnemySpawner] BOSS SPAWNED! Level: {_currentBossLevel}");
        #endif
    }

    private void CheckBossStatus()
    {
        if (_activeBoss == null)
        {
            _isBossFightActive = false;
            _currentBossLevel++;
            _bossTimer = 0f;

            _spawnTimer = _currentSpawnInterval;

            #if UNITY_EDITOR || DEVELOPMENT_BUILD
            Debug.Log($"[EnemySpawner] Boss defeated! Next boss level: {_currentBossLevel}");
            #endif
        }
    }

    private void RefreshBoundsIfChanged()
    {
        float sw = Screen.width;
        float sh = Screen.height;
        float os = _mainCamera.orthographicSize;

        if (sw != _prevScreenW || sh != _prevScreenH || !Mathf.Approximately(os, _prevOrthoSize))
        {
            RecalculateScreenBounds();
        }
    }

    private void RecalculateScreenBounds()
    {
        float zDist = Mathf.Abs(_mainCamera.transform.position.z);

        Vector3 bottomLeft = _mainCamera.ViewportToWorldPoint(new Vector3(0f, 0f, zDist));
        Vector3 topRight   = _mainCamera.ViewportToWorldPoint(new Vector3(1f, 1f, zDist));

        _screenMinX    = bottomLeft.x;
        _screenMaxX    = topRight.x;
        _screenTopY    = topRight.y;
        _screenBottomY = bottomLeft.y;

        _prevScreenW   = Screen.width;
        _prevScreenH   = Screen.height;
        _prevOrthoSize = _mainCamera.orthographicSize;
    }

    private void CreateEnemyContainer()
    {
        GameObject container = new GameObject("── Enemy Pool ──");
        _enemyContainer = container.transform;
    }

    public void SetSpawningEnabled(bool enabled)
    {
        _isSpawningEnabled = enabled;

        if (enabled)
        {
            _spawnTimer = _currentSpawnInterval;
        }
    }

    public bool  IsSpawningEnabled    => _isSpawningEnabled;
    public int   DifficultyLevel      => _difficultyLevel;
    public float CurrentSpawnInterval => _currentSpawnInterval;
    public bool  IsBossFightActive    => _isBossFightActive;
    public int   CurrentBossLevel     => _currentBossLevel;
    public Boss  ActiveBoss           => _activeBoss;

    public void ResetDifficulty()
    {
        _difficultyLevel      = 0;
        _difficultyTimer      = 0f;
        _currentSpawnInterval = _baseSpawnInterval;
        _spawnTimer           = _baseSpawnInterval;
        _isSpawningEnabled    = true;

        _bossTimer         = 0f;
        _currentBossLevel  = 0;
        _isBossFightActive = false;
        _activeBoss        = null;

        RecalculateTierWeights();
    }

    [System.Diagnostics.Conditional("UNITY_EDITOR"),
     System.Diagnostics.Conditional("DEVELOPMENT_BUILD")]
    private void ValidateSetup()
    {
        if (_enemyPrefabs == null || _enemyPrefabs.Length == 0)
            Debug.LogError("[EnemySpawner] Enemy Prefab dizisi boş! " +
                           "Inspector'dan 3 prefab atayın.", this);

        if (_enemyPrefabs != null && _enemyPrefabs.Length != 3)
            Debug.LogWarning($"[EnemySpawner] {_enemyPrefabs.Length} prefab atanmış, " +
                             "3 bekleniyor (Weak, Medium, Strong).", this);

        if (_mainCamera == null)
            Debug.LogError("[EnemySpawner] MainCamera bulunamadı!", this);

        if (_bossPrefab == null)
            Debug.LogWarning("[EnemySpawner] Boss prefab atanmamış — " +
                             "Boss fight çalışmayacak.", this);

        if (_enemyPrefabs != null)
        {
            for (int i = 0; i < _enemyPrefabs.Length; i++)
            {
                if (_enemyPrefabs[i] == null)
                    Debug.LogError($"[EnemySpawner] Prefab[{i}] null!", this);
            }
        }
    }

    #if UNITY_EDITOR
    private void OnDrawGizmos()
    {
        Camera cam = Application.isPlaying ? _mainCamera : Camera.main;
        if (cam == null) return;

        float zDist = Mathf.Abs(cam.transform.position.z);
        Vector3 bl = cam.ViewportToWorldPoint(new Vector3(0f, 0f, zDist));
        Vector3 tr = cam.ViewportToWorldPoint(new Vector3(1f, 1f, zDist));

        float spawnY = tr.y + _spawnOffsetAboveScreen;

        Gizmos.color = new Color(0.18f, 0.84f, 0.45f, 0.7f);
        Gizmos.DrawLine(
            new Vector3(bl.x, spawnY, 0f),
            new Vector3(tr.x, spawnY, 0f)
        );

        UnityEditor.Handles.color = new Color(0.18f, 0.84f, 0.45f, 0.9f);
        UnityEditor.Handles.Label(
            new Vector3(bl.x + 0.1f, spawnY + 0.15f, 0f),
            Application.isPlaying
                ? $"SPAWN | Interval: {_currentSpawnInterval:F2}s | Difficulty: Lv{_difficultyLevel}"
                : "SPAWN LINE"
        );

        Gizmos.color = new Color(1f, 0.27f, 0.27f, 0.5f);
        float despawnY = bl.y - 1.0f;
        Gizmos.DrawLine(
            new Vector3(bl.x, despawnY, 0f),
            new Vector3(tr.x, despawnY, 0f)
        );
    }
    #endif
}