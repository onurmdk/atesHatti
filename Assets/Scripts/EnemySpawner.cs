using UnityEngine;
using UnityEngine.Pool;

/// <summary>
/// ╔══════════════════════════════════════════════════════════════════╗
///   ATEŞ HATTI — EnemySpawner (Düşman Dalga Yöneticisi)
///   
///   Ekranın üstünden düşman spawn eden, zorluk skalalaması yapan,
///   tier atayan ve Object Pool ile bellek yöneten merkezi spawner.
/// ╚══════════════════════════════════════════════════════════════════╝
/// 
/// Sorumluluk (SRP): SADECE spawn zamanlaması + tier seçimi + pool yönetimi.
/// Düşman hareketi Enemy.cs'te, combat ayrı bir script'te ele alınacak.
/// 
/// Tek Prefab — Çoklu Tier Stratejisi:
/// ────────────────────────────────────
/// Bellekte sadece 1 Enemy prefab'ı var. Pool'dan alınan her düşman,
/// ağırlıklı rastgele (weighted random) ile bir tier'a atanır.
/// Tier bilgisi (scale, color, HP, speed, gold) Configure() ile set edilir.
/// 
/// Bu yaklaşımın avantajları:
///   + Tek pool, basit yönetim
///   + Bellek verimli (1 prefab × N instance)
///   + Yeni tier eklemek = _tiers array'ine bir struct eklemek
///   + Tier dengesini kod değiştirmeden Inspector'dan ayarlayabilirsin
/// 
/// Dezavantajı:
///   - Tier'lara özel sprite/animator eklenemez (aynı prefab)
///   - Production'da ScriptableObject'e geçiş önerilir
/// 
/// Zorluk Skalalaması:
/// ───────────────────
/// Her DIFFICULTY_INTERVAL saniyede spawn aralığı DIFFICULTY_MULTIPLIER ile çarpılır.
/// Örnek: 1.2s → 1.06s → 0.93s → ... → minimum 0.25s
/// Ayrıca büyük düşmanların spawn olasılığı da kademeli olarak artar.
/// 
/// Performans:
/// ───────────
/// • Update'te SIFIR GC allocation
/// • Ekran sınırları dirty-check ile sadece değişince hesaplanır
/// • Weighted random: Tek bir Random.value + if chain — array allocation yok
/// • Pre-warm ile başlangıçta Instantiate spike'ı önlenir
/// </summary>
public class EnemySpawner : MonoBehaviour
{
    // ════════════════════════════════════════════════════════════════
    //  INSPECTOR — Prefab ve Pool Ayarları
    // ════════════════════════════════════════════════════════════════

    [Header("─── Prefab ───")]
    [Tooltip("Tek düşman prefab'ı. Tüm tier'lar aynı prefab'dan oluşturulur.\n" +
             "Prefab'da Enemy.cs ve SpriteRenderer olmalı.")]
    [SerializeField]
    private Enemy _enemyPrefab;

    [Header("─── Pool Ayarları ───")]
    [Tooltip("Başlangıçta pre-warm edilecek düşman sayısı.")]
    [SerializeField, Range(10, 60)]
    private int _poolDefaultCapacity = 30;

    [Tooltip("Pool'da tutulacak maksimum inaktif düşman. Fazlası Destroy edilir.")]
    [SerializeField, Range(30, 100)]
    private int _poolMaxSize = 60;

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
    [Tooltip("Düşmanın ekranın üstünden ne kadar yukarıda spawn olacağı (world units).\n" +
             "Ekranın tam kenarında spawn edersen düşman aniden belirir — kötü UX.\n" +
             "1.0-1.5 arası değerle ekranın üstünden kayarak gelir.")]
    [SerializeField, Range(0.5f, 3f)]
    private float _spawnOffsetAboveScreen = 1.2f;

    // ════════════════════════════════════════════════════════════════
    //  TIER VERİLERİ
    // ════════════════════════════════════════════════════════════════

    /// <summary>
    /// 3 düşman tier'ının stat tanımları.
    /// HTML prototipindeki değerlerle eşleştirildi.
    /// 
    /// Readonly array — runtime'da değiştirilmez.
    /// Struct array olduğu için GC'ye yük bindirmez.
    /// 
    /// İleride bu array'i Inspector'dan düzenlenebilir yapmak:
    ///   [SerializeField] private EnemyTierData[] _tiers;
    /// Veya ScriptableObject'e taşımak:
    ///   [SerializeField] private EnemyTierSO[] _tierConfigs;
    /// Her iki geçiş de bu yapıyı bozmaz.
    /// </summary>
    private readonly EnemyTierData[] _tiers = new EnemyTierData[]
    {
        // ── SMALL: Hızlı, kırılgan, düşük ödül ──
        new EnemyTierData(
            name:       "Small",
            hp:         3f,
            speed:      4.0f,        // Hızlı düşüş
            driftSpeed: 1.5f,        // Hızlı yanal kayma
            goldValue:  1,
            scale:      new Vector3(0.6f, 0.6f, 1f),
            color:      new Color(0.93f, 0.35f, 0.14f, 1f)   // Turuncu (#ee5a24)
        ),

        // ── MEDIUM: Dengeli, orta ödül ──
        new EnemyTierData(
            name:       "Medium",
            hp:         7f,
            speed:      2.8f,        // Orta hız
            driftSpeed: 1.0f,        // Orta kayma
            goldValue:  2,
            scale:      new Vector3(0.9f, 0.9f, 1f),
            color:      new Color(0.92f, 0.30f, 0.29f, 1f)   // Kırmızı (#eb4d4b)
        ),

        // ── LARGE: Yavaş, dayanıklı, yüksek ödül ──
        new EnemyTierData(
            name:       "Large",
            hp:         13f,
            speed:      1.8f,        // Yavaş düşüş
            driftSpeed: 0.6f,        // Yavaş kayma
            goldValue:  3,
            scale:      new Vector3(1.3f, 1.3f, 1f),
            color:      new Color(0.72f, 0.08f, 0.25f, 1f)   // Koyu kırmızı (#b71540)
        ),
    };

    /// <summary>
    /// Tier seçimi için ağırlık tablosu.
    /// İndeks 0 = Small, 1 = Medium, 2 = Large
    /// 
    /// Başlangıç ağırlıkları: Small %60, Medium %25, Large %15
    /// Zorluk arttıkça büyük düşmanların oranı kademeli olarak yükselir.
    /// Bu değerler _baseTierWeights'ten hesaplanır, runtime'da güncellenir.
    /// 
    /// Neden float array?
    /// → Cumulative distribution function (CDF) kullanıyoruz.
    ///   Random.value < w[0] → Small
    ///   Random.value < w[0]+w[1] → Medium
    ///   else → Large
    ///   Bu, weighted random'ın en verimli implementasyonlarından biri.
    ///   Allocation yok, branch sayısı sabit (2).
    /// </summary>
    private readonly float[] _baseTierWeights = { 0.60f, 0.25f, 0.15f };
    private readonly float[] _currentTierCDF  = new float[3]; // Cumulative Distribution

    // ════════════════════════════════════════════════════════════════
    //  POOL VE STATE
    // ════════════════════════════════════════════════════════════════

    private ObjectPool<Enemy> _enemyPool;
    private Transform         _enemyContainer;

    // ── Timer'lar ──
    private float _spawnTimer;           // Sonraki spawn'a kalan süre
    private float _currentSpawnInterval; // Mevcut spawn aralığı (zorluk ile azalır)
    private float _difficultyTimer;      // Sonraki zorluk artışına kalan süre
    private int   _difficultyLevel;      // Kaç kez zorluk arttı (tier ağırlık hesabı için)

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

    // ── Spawn aktif/pasif kontrolü (Boss fight sırasında kapatmak için) ──
    private bool _isSpawningEnabled = true;

    // ════════════════════════════════════════════════════════════════
    //  UNITY LIFECYCLE
    // ════════════════════════════════════════════════════════════════

    private void Awake()
    {
        _mainCamera = Camera.main;

        ValidateSetup();
        CreateEnemyContainer();
        RecalculateScreenBounds();
        InitializePool();
        RecalculateTierWeights();

        // State başlat
        _currentSpawnInterval = _baseSpawnInterval;
        _spawnTimer           = _currentSpawnInterval;
        _difficultyTimer      = 0f;
        _difficultyLevel      = 0;
    }

    private void Update()
    {
        if (!_isSpawningEnabled) return;

        float dt = Time.deltaTime;

        RefreshBoundsIfChanged();
        UpdateDifficultyScaling(dt);
        UpdateSpawnTimer(dt);
    }

    private void OnDestroy()
    {
        _enemyPool?.Dispose();
    }

    // ════════════════════════════════════════════════════════════════
    //  POOL BAŞLATMA
    // ════════════════════════════════════════════════════════════════

    private void InitializePool()
    {
        _enemyPool = new ObjectPool<Enemy>(
            createFunc:      OnPoolCreate,
            actionOnGet:     OnPoolGet,
            actionOnRelease: OnPoolRelease,
            actionOnDestroy: OnPoolDestroy,
            collectionCheck: false,
            defaultCapacity: _poolDefaultCapacity,
            maxSize:         _poolMaxSize
        );

        PreWarmPool();
    }

    /// <summary>
    /// Pool'u başlangıçta doldurur.
    /// Bullet pool ile aynı pattern — Get → Release döngüsü.
    /// </summary>
    private void PreWarmPool()
    {
        Enemy[] warmEnemies = new Enemy[_poolDefaultCapacity];

        for (int i = 0; i < _poolDefaultCapacity; i++)
        {
            warmEnemies[i] = _enemyPool.Get();
        }

        for (int i = 0; i < _poolDefaultCapacity; i++)
        {
            _enemyPool.Release(warmEnemies[i]);
        }
    }

    // ── Pool Callback'leri ──

    private Enemy OnPoolCreate()
    {
        Enemy enemy = Instantiate(_enemyPrefab, _enemyContainer);
        return enemy;
    }

    private void OnPoolGet(Enemy enemy)
    {
        // SetActive burada yapılmıyor!
        // Configure() çağrısından SONRA aktif edilmeli.
        // Aksi halde yarı-konfigüre edilmiş düşman 1 frame görünür.
        // → SpawnEnemy() metodunda açıkça SetActive(true) çağrılıyor.
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
    //  SPAWN ZAMANLAMA
    // ════════════════════════════════════════════════════════════════

    /// <summary>
    /// Spawn timer'ını günceller ve süre dolduğunda düşman spawn eder.
    /// 
    /// Timer mantığı: PlayerShooting ile aynı "ritim telafisi" pattern'i.
    /// _spawnTimer += _currentSpawnInterval ile lag spike sonrası
    /// kayıp spawn'lar telafi edilir, ama sonsuz döngü güvenliği var.
    /// </summary>
    private void UpdateSpawnTimer(float dt)
    {
        _spawnTimer -= dt;

        if (_spawnTimer <= 0f)
        {
            SpawnEnemy();

            _spawnTimer += _currentSpawnInterval;

            // Güvenlik: Çok büyük lag spike'ta sonsuz spawn önle
            if (_spawnTimer < 0f)
                _spawnTimer = 0f;
        }
    }

    // ════════════════════════════════════════════════════════════════
    //  ZORLUK SKALAMASI
    // ════════════════════════════════════════════════════════════════

    /// <summary>
    /// Belirli aralıklarla spawn hızını artırır ve tier ağırlıklarını günceller.
    /// 
    /// İki katmanlı zorluk artışı:
    /// 1. Spawn aralığı azalır → Birim zamanda daha fazla düşman
    /// 2. Büyük düşman oranı artar → Birim zamanda daha fazla toplam HP
    /// 
    /// Bu çift artış, oyuncunun upgrade'lemezse giderek zorlanmasını sağlar.
    /// Exponential curve kullanılıyor — ilk artışlar yavaş, sonrakiler hızlı.
    /// </summary>
    private void UpdateDifficultyScaling(float dt)
    {
        _difficultyTimer += dt;

        if (_difficultyTimer >= _difficultyInterval)
        {
            _difficultyTimer -= _difficultyInterval;
            _difficultyLevel++;

            // ── Spawn aralığını azalt ──
            _currentSpawnInterval *= _difficultyMultiplier;
            _currentSpawnInterval = Mathf.Max(_currentSpawnInterval, _minSpawnInterval);

            // ── Tier ağırlıklarını güncelle ──
            RecalculateTierWeights();

            #if UNITY_EDITOR || DEVELOPMENT_BUILD
            Debug.Log($"[EnemySpawner] Zorluk seviyesi {_difficultyLevel} | " +
                      $"Spawn aralığı: {_currentSpawnInterval:F3}s | " +
                      $"Tier CDF: [{_currentTierCDF[0]:F2}, {_currentTierCDF[1]:F2}, {_currentTierCDF[2]:F2}]");
            #endif
        }
    }

    /// <summary>
    /// Zorluk seviyesine göre tier ağırlıklarını yeniden hesaplar.
    /// 
    /// Mantık:
    /// - Her zorluk seviyesinde Small'ın ağırlığı %3 düşer
    /// - Medium %1, Large %2 artar
    /// - Small minimum %25'e, Large maksimum %35'e sınırlandırılır
    /// - Ağırlıklar normalize edilir (toplamları 1.0 olur)
    /// - CDF (Cumulative Distribution Function) hesaplanır
    /// 
    /// Sonuç: Oyun ilerledikçe büyük ve dayanıklı düşmanlar daha sık gelir,
    /// ama Small düşmanlar asla tamamen kaybolmaz (hız çeşitliliği korunur).
    /// </summary>
    private void RecalculateTierWeights()
    {
        float smallW  = Mathf.Max(0.25f, _baseTierWeights[0] - _difficultyLevel * 0.03f);
        float mediumW = _baseTierWeights[1] + _difficultyLevel * 0.01f;
        float largeW  = Mathf.Min(0.35f, _baseTierWeights[2] + _difficultyLevel * 0.02f);

        // Normalize (toplamı 1.0 yap)
        float total = smallW + mediumW + largeW;
        smallW  /= total;
        mediumW /= total;
        // largeW'u hesaplamaya gerek yok — CDF'de son eleman her zaman 1.0 olur

        // CDF: [Small sınırı, Medium sınırı, 1.0]
        _currentTierCDF[0] = smallW;
        _currentTierCDF[1] = smallW + mediumW;
        _currentTierCDF[2] = 1.0f;
    }

    // ════════════════════════════════════════════════════════════════
    //  DÜŞMAN SPAWN
    // ════════════════════════════════════════════════════════════════

    /// <summary>
    /// Pool'dan düşman alır, tier atar, konfigüre eder ve aktif eder.
    /// 
    /// Akış:
    /// 1. Pool'dan Get → OnPoolGet (ama henüz SetActive yapılmıyor!)
    /// 2. Tier seç (weighted random)
    /// 3. Spawn pozisyonu hesapla
    /// 4. Configure() ile tüm stat'ları set et
    /// 5. SetActive(true) — tam konfigüre edilmiş düşman sahneye girer
    /// 
    /// Neden OnPoolGet'te SetActive yapmıyoruz?
    /// → OnPoolGet, Configure'dan ÖNCE çağrılır. Eğer orada SetActive(true)
    ///   yaparsak, 1 frame boyunca önceki tier'ın scale/color'ıyla görünür.
    ///   Flicker efekti oluşur — oyuncu fark eder, profesyonel görünmez.
    /// </summary>
    private void SpawnEnemy()
    {
        Enemy enemy = _enemyPool.Get();

        // ── Tier seç (weighted random + CDF) ──
        int tierIndex = SelectTierIndex();
        EnemyTierData tier = _tiers[tierIndex];

        // ── Spawn pozisyonu ──
        // X: Ekran genişliğinde rastgele, sprite yarı genişliği kadar içeride
        // Y: Ekranın üstünden _spawnOffsetAboveScreen kadar yukarıda
        float spriteHalfW = (tier.scale.x *
            (_enemyPrefab.GetComponent<SpriteRenderer>()?.sprite?.bounds.extents.x ?? 0.5f));

        float spawnX = Random.Range(_screenMinX + spriteHalfW, _screenMaxX - spriteHalfW);
        float spawnY = _screenTopY + _spawnOffsetAboveScreen;

        Vector3 spawnPos = new Vector3(spawnX, spawnY, 0f);

        // ── Konfigüre et ──
        enemy.Configure(
            pool:          _enemyPool,
            tier:          tier,
            spawnPos:      spawnPos,
            screenMinX:    _screenMinX,
            screenMaxX:    _screenMaxX,
            screenBottomY: _screenBottomY
        );

        // ── ŞİMDİ aktif et — tam konfigüre edilmiş, flicker riski yok ──
        enemy.gameObject.SetActive(true);
    }

    /// <summary>
    /// CDF (Cumulative Distribution Function) kullanarak tier seçer.
    /// 
    /// Nasıl çalışır:
    /// ─────────────
    /// CDF = [0.60, 0.85, 1.00] durumunda:
    ///   Random.value = 0.42 → 0.42 < 0.60  → Small (indeks 0)
    ///   Random.value = 0.73 → 0.73 < 0.85  → Medium (indeks 1)
    ///   Random.value = 0.91 → 0.91 ≥ 0.85  → Large (indeks 2)
    /// 
    /// Performans: Sabit 2 branch (if-else), allocation yok, O(1).
    /// 3 tier için loop yazmak yerine explicit if chain kullanıyoruz çünkü:
    /// - 3 eleman için loop overhead > explicit branch overhead
    /// - Compiler branch prediction'ı daha iyi optimize eder
    /// - Okunabilirlik daha yüksek
    /// </summary>
    private int SelectTierIndex()
    {
        float roll = Random.value;

        if (roll < _currentTierCDF[0]) return 0;  // Small
        if (roll < _currentTierCDF[1]) return 1;  // Medium
        return 2;                                   // Large
    }

    // ════════════════════════════════════════════════════════════════
    //  EKRAN SINIRI HESAPLAMASI
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

    /// <summary>
    /// Viewport köşelerinden 4 sınırı hesaplar.
    /// Spawner hem üst (spawn noktası) hem alt (düşman iade noktası)
    /// hem de sol-sağ (spawn X aralığı ve drift sınırları) bilgisine ihtiyaç duyar.
    /// </summary>
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
    //  PUBLIC API — Boss/Wave Sistemi İçin
    // ════════════════════════════════════════════════════════════════

    /// <summary>
    /// Spawn'ı açar/kapatır. Boss fight başladığında kapatılır,
    /// boss öldükten sonra tekrar açılır.
    /// </summary>
    public void SetSpawningEnabled(bool enabled)
    {
        _isSpawningEnabled = enabled;

        // Spawn yeniden açıldığında timer'ı resetle
        // Böylece boss fight sonrası ilk düşman hemen spawn olmaz,
        // oyuncuya kısa bir nefes molası verilir
        if (enabled)
        {
            _spawnTimer = _currentSpawnInterval;
        }
    }

    /// <summary>Spawn aktif mi?</summary>
    public bool IsSpawningEnabled => _isSpawningEnabled;

    /// <summary>Mevcut zorluk seviyesi (debug/UI için)</summary>
    public int DifficultyLevel => _difficultyLevel;

    /// <summary>Mevcut spawn aralığı (debug/UI için)</summary>
    public float CurrentSpawnInterval => _currentSpawnInterval;

    /// <summary>
    /// Zorluk parametrelerini sıfırlar. Yeni oyun başlangıcında çağrılır.
    /// </summary>
    public void ResetDifficulty()
    {
        _difficultyLevel      = 0;
        _difficultyTimer      = 0f;
        _currentSpawnInterval = _baseSpawnInterval;
        _spawnTimer           = _baseSpawnInterval;
        _isSpawningEnabled    = true;

        RecalculateTierWeights();
    }

    // ════════════════════════════════════════════════════════════════
    //  DOĞRULAMA
    // ════════════════════════════════════════════════════════════════

    [System.Diagnostics.Conditional("UNITY_EDITOR"),
     System.Diagnostics.Conditional("DEVELOPMENT_BUILD")]
    private void ValidateSetup()
    {
        if (_enemyPrefab == null)
            Debug.LogError(
                "[EnemySpawner] Enemy Prefab atanmamış! " +
                "Inspector'da _enemyPrefab alanını doldurun.", this);

        if (_mainCamera == null)
            Debug.LogError(
                "[EnemySpawner] MainCamera bulunamadı!", this);

        if (_enemyPrefab != null && _enemyPrefab.GetComponent<SpriteRenderer>() == null)
            Debug.LogError(
                "[EnemySpawner] Enemy Prefab'da SpriteRenderer yok! " +
                "Tier renklendirmesi çalışmaz.", this);
    }

    // ════════════════════════════════════════════════════════════════
    //  EDITOR GIZMOS
    // ════════════════════════════════════════════════════════════════

    #if UNITY_EDITOR
    /// <summary>
    /// Scene view'da spawn bölgesini ve ekran sınırlarını gösterir.
    /// Spawn hattı yeşil, ekran sınırları beyaz.
    /// </summary>
    private void OnDrawGizmos()
    {
        Camera cam = Application.isPlaying ? _mainCamera : Camera.main;
        if (cam == null) return;

        float zDist = Mathf.Abs(cam.transform.position.z);
        Vector3 bl = cam.ViewportToWorldPoint(new Vector3(0f, 0f, zDist));
        Vector3 tr = cam.ViewportToWorldPoint(new Vector3(1f, 1f, zDist));

        float spawnY = tr.y + _spawnOffsetAboveScreen;

        // Spawn hattı (yeşil)
        Gizmos.color = new Color(0.18f, 0.84f, 0.45f, 0.7f);
        Gizmos.DrawLine(
            new Vector3(bl.x, spawnY, 0f),
            new Vector3(tr.x, spawnY, 0f)
        );

        // Spawn hattı etiketi
        UnityEditor.Handles.color = new Color(0.18f, 0.84f, 0.45f, 0.9f);
        UnityEditor.Handles.Label(
            new Vector3(bl.x + 0.1f, spawnY + 0.15f, 0f),
            Application.isPlaying
                ? $"SPAWN | Interval: {_currentSpawnInterval:F2}s | Difficulty: Lv{_difficultyLevel}"
                : "SPAWN LINE"
        );

        // Despawn hattı (kırmızı, ekranın altı)
        Gizmos.color = new Color(1f, 0.27f, 0.27f, 0.5f);
        float despawnY = bl.y - OFF_SCREEN_MARGIN_GIZMO;
        Gizmos.DrawLine(
            new Vector3(bl.x, despawnY, 0f),
            new Vector3(tr.x, despawnY, 0f)
        );
    }

    private const float OFF_SCREEN_MARGIN_GIZMO = 1.0f;
    #endif
}
