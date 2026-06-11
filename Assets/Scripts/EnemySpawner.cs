using UnityEngine;
using UnityEngine.Pool;

/// <summary>
/// ╔══════════════════════════════════════════════════════════════════╗
///   ATEŞ HATTI — EnemySpawner (Düşman Dalga Yöneticisi)
///   
///   Ekranın üstünden düşman spawn eden, zorluk skalalaması yapan,
///   CDF ile tier seçen ve Multi-Pool ile bellek yöneten merkezi spawner.
/// ╚══════════════════════════════════════════════════════════════════╝
/// 
/// Multi-Prefab — Multi-Pool Mimari:
/// ──────────────────────────────────
///   Inspector'dan 3 farklı prefab atanır: [Weak, Medium, Strong]
///   Her prefab için ayrı ObjectPool oluşturulur.
///   CDF ile tier index seçilir → ilgili pool'dan Get → Configure → SetActive.
/// 
///   _enemyPrefabs[0] → _enemyPools[0]  (Weak)
///   _enemyPrefabs[1] → _enemyPools[1]  (Medium)
///   _enemyPrefabs[2] → _enemyPools[2]  (Strong)
/// 
/// Zorluk Skalalaması (KORUNDU):
/// ─────────────────────────────
///   Her _difficultyInterval saniyede:
///     1. Spawn aralığı *= _difficultyMultiplier (min: _minSpawnInterval)
///     2. Büyük düşman CDF ağırlığı artar
///   Çift katmanlı artış = exponential zorluk eğrisi
/// 
/// Performans:
/// ───────────
/// • Update'te SIFIR GC allocation
/// • Ekran sınırları dirty-check ile sadece değişince hesaplanır
/// • CDF: 2 branch, O(1), allocation yok
/// • Pre-warm: Her pool ayrı ayrı ısıtılır
/// </summary>
public class EnemySpawner : MonoBehaviour
{
    // ════════════════════════════════════════════════════════════════
    //  INSPECTOR — Prefab ve Pool Ayarları
    // ════════════════════════════════════════════════════════════════

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

    // ════════════════════════════════════════════════════════════════
    //  CDF — Tier Ağırlık Dağılımı (KORUNDU)
    // ════════════════════════════════════════════════════════════════

    /// <summary>
    /// Başlangıç ağırlıkları: Weak %60, Medium %25, Strong %15
    /// Zorluk arttıkça güçlü düşmanların oranı yükselir.
    /// </summary>
    private readonly float[] _baseTierWeights = { 0.60f, 0.25f, 0.15f };
    private readonly float[] _currentTierCDF  = new float[3];

    // ════════════════════════════════════════════════════════════════
    //  MULTI-POOL SİSTEMİ
    // ════════════════════════════════════════════════════════════════

    /// <summary>
    /// Her prefab için ayrı ObjectPool.
    /// _enemyPools[0] → Weak pool
    /// _enemyPools[1] → Medium pool
    /// _enemyPools[2] → Strong pool
    /// 
    /// Neden tek pool değil?
    /// → Tek pool'da tüm prefab'lar karışır. Get() ile Weak istiyorsun
    ///   ama pool'dan Strong çıkabilir — kontrol yok.
    ///   Ayrı pool'larla hangi tier'ı istiyorsak o pool'dan çekeriz.
    /// </summary>
    private ObjectPool<Enemy>[] _enemyPools;
    private Transform           _enemyContainer;

    // ── Timer'lar ──
    private float _spawnTimer;
    private float _currentSpawnInterval;
    private float _difficultyTimer;
    private int   _difficultyLevel;

    // ── Ekran sınırları ──
    private Camera _mainCamera;
    private float  _screenMinX;
    private float  _screenMaxX;
    private float  _screenTopY;
    private float  _screenBottomY;

    // Dirty check
    private float _prevScreenW;
    private float _prevScreenH;
    private float _prevOrthoSize;

    // ── Spawn kontrolü ──
    private bool _isSpawningEnabled = true;

    // ── Boss Fight State ──
    private float _bossTimer;
    private int   _currentBossLevel;
    private Boss  _activeBoss;
    private bool  _isBossFightActive;

    // ════════════════════════════════════════════════════════════════
    //  UNITY LIFECYCLE
    // ════════════════════════════════════════════════════════════════

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

        // Boss state
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

        // ── Boss Fight aktifken: Normal spawn ve zorluk DURUR ──
        // Sadece boss'un ölüp ölmediğini kontrol et
        if (_isBossFightActive)
        {
            CheckBossStatus();
            return;
        }

        // ── Normal akış: Zorluk + Spawn + Boss Timer ──
        UpdateDifficultyScaling(dt);
        UpdateSpawnTimer(dt);
        UpdateBossTimer(dt);
    }

    private void OnDestroy()
    {
        // Tüm pool'ları temizle
        if (_enemyPools != null)
        {
            for (int i = 0; i < _enemyPools.Length; i++)
            {
                _enemyPools[i]?.Dispose();
            }
        }
    }

    // ════════════════════════════════════════════════════════════════
    //  MULTI-POOL BAŞLATMA
    // ════════════════════════════════════════════════════════════════

    /// <summary>
    /// Her prefab için ayrı ObjectPool oluşturur ve pre-warm yapar.
    /// 
    /// Closure pattern: Her pool kendi prefab index'ini closure ile yakalar.
    /// Lambda içindeki `prefabIndex` değişkeni döngü bitince de doğru
    /// değeri tutar — C# closure semantiği bunu garanti eder.
    /// </summary>
    private void InitializePools()
    {
        int prefabCount = _enemyPrefabs.Length;
        _enemyPools = new ObjectPool<Enemy>[prefabCount];

        for (int i = 0; i < prefabCount; i++)
        {
            // Closure için local copy — döngü değişkeni doğrudan capture edilmez
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

            // Pre-warm: Bu pool'u başlangıçta doldur
            PreWarmSinglePool(_enemyPools[i]);
        }
    }

    /// <summary>
    /// Tek bir pool'u pre-warm eder.
    /// Get → Release döngüsü ile _poolDefaultCapacity kadar obje oluşturulur.
    /// </summary>
    private void PreWarmSinglePool(ObjectPool<Enemy> pool)
    {
        Enemy[] temp = new Enemy[_poolDefaultCapacity];

        for (int i = 0; i < _poolDefaultCapacity; i++)
            temp[i] = pool.Get();

        for (int i = 0; i < _poolDefaultCapacity; i++)
            pool.Release(temp[i]);
    }

    // ── Pool Callback'leri ──

    /// <summary>
    /// Belirtilen prefab index'inden yeni düşman oluşturur.
    /// Her pool kendi prefab'ını closure ile bilir.
    /// </summary>
    private Enemy OnPoolCreate(int prefabIndex)
    {
        Enemy enemy = Instantiate(_enemyPrefabs[prefabIndex], _enemyContainer);
        return enemy;
    }

    private void OnPoolGet(Enemy enemy)
    {
        // SetActive burada yapılmıyor — SpawnEnemy'de Configure'dan sonra
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

    // ════════════════════════════════════════════════════════════════
    //  SPAWN ZAMANLAMA (KORUNDU)
    // ════════════════════════════════════════════════════════════════

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

    // ════════════════════════════════════════════════════════════════
    //  ZORLUK SKALAMASI (KORUNDU — BİREBİR AYNI MATEMATİK)
    // ════════════════════════════════════════════════════════════════

    /// <summary>
    /// İki katmanlı zorluk artışı:
    /// 1. Spawn aralığı azalır → daha fazla düşman
    /// 2. Güçlü düşman CDF ağırlığı artar → daha fazla toplam HP
    /// 
    /// Spawn aralığı _minSpawnInterval altına düşemez (Mathf.Max garantisi).
    /// </summary>
    private void UpdateDifficultyScaling(float dt)
    {
        _difficultyTimer += dt;

        if (_difficultyTimer >= _difficultyInterval)
        {
            _difficultyTimer -= _difficultyInterval;
            _difficultyLevel++;

            // ── Spawn aralığını azalt (minimum sınırla) ──
            _currentSpawnInterval *= _difficultyMultiplier;
            _currentSpawnInterval = Mathf.Max(_currentSpawnInterval, _minSpawnInterval);

            // ── Tier ağırlıklarını güncelle ──
            RecalculateTierWeights();

            #if UNITY_EDITOR || DEVELOPMENT_BUILD
            Debug.Log($"[EnemySpawner] Zorluk Lv{_difficultyLevel} | " +
                      $"Spawn: {_currentSpawnInterval:F3}s | " +
                      $"CDF: [{_currentTierCDF[0]:F2}, {_currentTierCDF[1]:F2}, {_currentTierCDF[2]:F2}]");
            #endif
        }
    }

    /// <summary>
    /// CDF ağırlık hesabı (KORUNDU — birebir aynı formül).
    /// 
    /// Her zorluk seviyesinde:
    ///   - Weak ağırlığı %3 düşer (min %25)
    ///   - Medium %1 artar
    ///   - Strong %2 artar (max %35)
    ///   - Normalize edilir (toplam = 1.0)
    /// </summary>
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

    // ════════════════════════════════════════════════════════════════
    //  DÜŞMAN SPAWN (Multi-Pool Versiyonu)
    // ════════════════════════════════════════════════════════════════

    /// <summary>
    /// CDF ile tier seçer, ilgili pool'dan düşman alır, konfigüre eder.
    /// 
    /// Akış:
    /// 1. SelectTierIndex() → CDF ile 0/1/2 index belirle
    /// 2. _enemyPools[index].Get() → ilgili pool'dan düşman al
    /// 3. Spawn pozisyonu hesapla (prefab'ın kendi sprite genişliğiyle)
    /// 4. Configure() → pool ref + pozisyon + sınırlar
    /// 5. SetActive(true)
    /// </summary>
    private void SpawnEnemy()
    {
        // ── Tier seç (CDF — korunmuş matematik) ──
        int tierIndex = SelectTierIndex();

        // ── Güvenlik: Index prefab array sınırları içinde mi? ──
        if (tierIndex < 0 || tierIndex >= _enemyPools.Length)
            return;

        // ── İlgili pool'dan düşman al ──
        Enemy enemy = _enemyPools[tierIndex].Get();

        // ── Spawn pozisyonu ──
        // Prefab'ın kendi SpriteRenderer'ından genişlik oku
        SpriteRenderer sr = enemy.GetComponent<SpriteRenderer>();
        float spriteHalfW = 0.5f;
        if (sr != null && sr.sprite != null)
        {
            spriteHalfW = sr.sprite.bounds.extents.x * Mathf.Abs(enemy.transform.localScale.x);
        }

        float spawnX = Random.Range(_screenMinX + spriteHalfW, _screenMaxX - spriteHalfW);
        float spawnY = _screenTopY + _spawnOffsetAboveScreen;

        Vector3 spawnPos = new Vector3(spawnX, spawnY, 0f);

        // ── Konfigüre et (yeni imza — tier parametresi yok) ──
        enemy.Configure(
            pool:          _enemyPools[tierIndex],
            spawnPos:      spawnPos,
            screenMinX:    _screenMinX,
            screenMaxX:    _screenMaxX,
            screenBottomY: _screenBottomY
        );

        // ── Aktif et ──
        enemy.gameObject.SetActive(true);
    }

    /// <summary>
    /// CDF ile tier index seçer (KORUNDU — birebir aynı mantık).
    /// 
    /// CDF = [0.60, 0.85, 1.00]:
    ///   roll < 0.60 → 0 (Weak)
    ///   roll < 0.85 → 1 (Medium)
    ///   else        → 2 (Strong)
    /// </summary>
    private int SelectTierIndex()
    {
        float roll = Random.value;

        if (roll < _currentTierCDF[0]) return 0;  // Weak
        if (roll < _currentTierCDF[1]) return 1;  // Medium
        return 2;                                   // Strong
    }

    // ════════════════════════════════════════════════════════════════
    //  BOSS SİSTEMİ
    // ════════════════════════════════════════════════════════════════

    /// <summary>
    /// Boss spawn timer'ını günceller.
    /// _bossInterval saniyeye ulaşınca boss'u spawn eder.
    /// </summary>
    private void UpdateBossTimer(float dt)
    {
        _bossTimer += dt;

        if (_bossTimer >= _bossInterval)
        {
            _bossTimer = 0f;
            SpawnBoss();
        }
    }

    /// <summary>
    /// Boss'u oluşturur ve konfigüre eder.
    /// 
    /// Akış:
    /// 1. isBossFightActive = true → normal spawn + zorluk DURUR
    /// 2. "Player" tag ile oyuncuyu bul
    /// 3. Boss'u Instantiate et
    /// 4. Configure(level, bounds, stopY, player) ile başlat
    /// </summary>
    private void SpawnBoss()
    {
        if (_bossPrefab == null)
        {
            #if UNITY_EDITOR || DEVELOPMENT_BUILD
            Debug.LogError("[EnemySpawner] Boss prefab atanmamış!", this);
            #endif
            return;
        }

        // ── Normal akışı durdur ──
        _isBossFightActive = true;

        // ── Oyuncuyu bul ──
        Transform playerTransform = null;
        GameObject playerObj = GameObject.FindWithTag("Player");
        if (playerObj != null)
        {
            playerTransform = playerObj.transform;
        }

        // ── Boss'u oluştur ──
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

    /// <summary>
    /// Boss fight sırasında boss'un durumunu kontrol eder.
    /// Boss öldüyse (Destroy olmuş → null) veya IsAlive false ise:
    ///   - Boss fight'ı bitir
    ///   - Boss level'ını artır
    ///   - Normal akışa devam et (zorluk kaldığı yerden)
    ///   - Oyuncuya kısa nefes molası ver
    /// </summary>
    private void CheckBossStatus()
    {
        // Unity'de Destroy edilen obje null döner (== operator override)
        if (_activeBoss == null)
        {
            // ── Boss öldü → normal akışa dön ──
            _isBossFightActive = false;
            _currentBossLevel++;
            _bossTimer = 0f;

            // Oyuncuya kısa nefes molası: Spawn timer'ı resetle
            // Böylece boss ölür ölmez düşman yağmuru başlamaz
            _spawnTimer = _currentSpawnInterval;

            #if UNITY_EDITOR || DEVELOPMENT_BUILD
            Debug.Log($"[EnemySpawner] Boss defeated! Next boss level: {_currentBossLevel}");
            #endif
        }
    }

    // ════════════════════════════════════════════════════════════════
    //  EKRAN SINIRI HESAPLAMASI (KORUNDU)
    // ════════════════════════════════════════════════════════════════

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

    // ════════════════════════════════════════════════════════════════
    //  HIERARCHY YÖNETİMİ
    // ════════════════════════════════════════════════════════════════

    private void CreateEnemyContainer()
    {
        GameObject container = new GameObject("── Enemy Pool ──");
        _enemyContainer = container.transform;
    }

    // ════════════════════════════════════════════════════════════════
    //  PUBLIC API (KORUNDU)
    // ════════════════════════════════════════════════════════════════

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

        // Boss state reset
        _bossTimer         = 0f;
        _currentBossLevel  = 0;
        _isBossFightActive = false;
        _activeBoss        = null;

        RecalculateTierWeights();
    }

    // ════════════════════════════════════════════════════════════════
    //  DOĞRULAMA
    // ════════════════════════════════════════════════════════════════

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

    // ════════════════════════════════════════════════════════════════
    //  EDITOR GIZMOS (KORUNDU)
    // ════════════════════════════════════════════════════════════════

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
