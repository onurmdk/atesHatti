using UnityEngine;
using UnityEngine.Pool;

/// <summary>
/// ╔══════════════════════════════════════════════════════════════════╗
///   ATEŞ HATTI — ParticleManager (Patlama Efekt Havuzu)
///   
///   Patlama ve isabet efektlerini Object Pool ile yöneten,
///   GC spike'sız partikül sistemi yöneticisi.
/// ╚══════════════════════════════════════════════════════════════════╝
/// 
/// Sorumluluk: SADECE partikül oluşturma + pool yönetimi.
/// Ne zaman ve nerede partikül oynatılacağına CombatManager karar verir.
/// 
/// Neden ParticleSystem Pool?
/// ─────────────────────────
/// Her patlama için Instantiate → Play → Destroy yaparsak:
///   - Her Instantiate: ~0.5-1ms + GC allocation
///   - Her Destroy: GC'ye iş yükü
///   - Aynı anda 5 düşman ölürse: 5ms frame spike (mobilde 1 frame = 16ms)
/// 
/// Pool ile:
///   - ParticleSystem objeleri bir kez oluşturulur
///   - Play → otomatik iade (OnParticleSystemStopped callback)
///   - Runtime'da SIFIR Instantiate/Destroy
/// 
/// Teknik Not: stopAction = Callback
/// ─────────────────────────────────
/// Unity'nin ParticleSystem.MainModule.stopAction = ParticleSystemStopAction.Callback
/// ayarlandığında, partikül oynatımı bitince OnParticleSystemStopped() çağrılır.
/// Bu callback'i kullanarak partikülü otomatik olarak pool'a iade ediyoruz.
/// Timer veya coroutine gerekmez — Unity'nin kendi lifecycle'ına bağlıyız.
/// </summary>
public class ParticleManager : MonoBehaviour
{
    // ════════════════════════════════════════════════════════════════
    //  SINGLETON
    // ════════════════════════════════════════════════════════════════

    /// <summary>
    /// Basit singleton pattern.
    /// 
    /// Neden singleton?
    /// → ParticleManager tüm sistemler tarafından erişilmeli:
    ///   CombatManager, BossController, Player death efekti vb.
    ///   Hepsine Inspector'dan referans sürüklemek fragile ve error-prone.
    ///   Singleton ile ParticleManager.Instance.PlayExplosion(...) çağrılır.
    /// 
    /// Neden DontDestroyOnLoad yok?
    /// → Sahne değişiminde partikül pool'u temizlenmeli.
    ///   Yeni sahne yeni ParticleManager oluşturur.
    /// </summary>
    public static ParticleManager Instance { get; private set; }

    // ════════════════════════════════════════════════════════════════
    //  INSPECTOR AYARLARI
    // ════════════════════════════════════════════════════════════════

    [Header("─── Patlama Efekti ───")]
    [Tooltip("Patlama partikül prefab'ı.\n" +
             "ParticleSystem bileşeni olmalı.\n" +
             "StopAction otomatik olarak Callback'e set edilir.")]
    [SerializeField]
    private ParticleSystem _explosionPrefab;

    [Header("─── İsabet Kıvılcımı ───")]
    [Tooltip("Mermi isabet kıvılcım prefab'ı (isteğe bağlı).\n" +
             "null ise küçük isabet efekti için de patlama prefab'ı kullanılır.")]
    [SerializeField]
    private ParticleSystem _sparkPrefab;

    [Header("─── Pool Ayarları ───")]
    [SerializeField, Range(5, 30)]
    private int _poolDefaultCapacity = 10;

    [SerializeField, Range(15, 50)]
    private int _poolMaxSize = 25;

    [SerializeField, Range(3, 15)]
    private int _sparkPoolDefaultCapacity = 8;

    [SerializeField, Range(10, 30)]
    private int _sparkPoolMaxSize = 20;

    // ════════════════════════════════════════════════════════════════
    //  POOL
    // ════════════════════════════════════════════════════════════════

    private ObjectPool<ParticleSystem> _explosionPool;
    private ObjectPool<ParticleSystem> _sparkPool;
    private Transform _container;

    // ════════════════════════════════════════════════════════════════
    //  UNITY LIFECYCLE
    // ════════════════════════════════════════════════════════════════

    private void Awake()
    {
        // Singleton kurulumu
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;

        ValidateSetup();
        CreateContainer();
        InitializePools();
    }

    private void OnDestroy()
    {
        if (Instance == this)
            Instance = null;

        _explosionPool?.Dispose();
        _sparkPool?.Dispose();
    }

    // ════════════════════════════════════════════════════════════════
    //  POOL BAŞLATMA
    // ════════════════════════════════════════════════════════════════

    private void InitializePools()
    {
        // ── Patlama Pool'u ──
        _explosionPool = new ObjectPool<ParticleSystem>(
            createFunc:      () => CreateParticle(_explosionPrefab),
            actionOnGet:     OnPoolGetParticle,
            actionOnRelease: OnPoolReleaseParticle,
            actionOnDestroy: OnPoolDestroyParticle,
            collectionCheck: false,
            defaultCapacity: _poolDefaultCapacity,
            maxSize:         _poolMaxSize
        );

        // ── Kıvılcım Pool'u ──
        // Spark prefab yoksa explosion prefab'ını fallback olarak kullan
        ParticleSystem sparkTemplate = _sparkPrefab != null ? _sparkPrefab : _explosionPrefab;

        _sparkPool = new ObjectPool<ParticleSystem>(
            createFunc:      () => CreateParticle(sparkTemplate),
            actionOnGet:     OnPoolGetParticle,
            actionOnRelease: OnPoolReleaseParticle,
            actionOnDestroy: OnPoolDestroyParticle,
            collectionCheck: false,
            defaultCapacity: _sparkPoolDefaultCapacity,
            maxSize:         _sparkPoolMaxSize
        );

        PreWarmPool(_explosionPool, _poolDefaultCapacity);
        PreWarmPool(_sparkPool, _sparkPoolDefaultCapacity);
    }

    /// <summary>
    /// Yeni partikül objesi oluşturur ve stopAction'ı Callback'e set eder.
    /// 
    /// stopAction = Callback:
    /// → Partikül oynatımı bitince Unity otomatik olarak 
    ///   OnParticleSystemStopped() çağırır.
    /// → Bu callback'te partikülü pool'a iade ediyoruz.
    /// → Timer, coroutine veya Update kontrolü gerekmez.
    /// → Unity'nin kendi lifecycle'ına bağlı, güvenilir ve verimli.
    /// 
    /// ParticleReturnHandler:
    /// → OnParticleSystemStopped callback'i ParticleSystem'ın kendi
    ///   GameObject'inde olmalı. Bu yüzden oluşturulan her partikül
    ///   objesine küçük bir handler bileşeni ekliyoruz.
    ///   Bu bileşen sadece pool referansını tutar ve callback'i yönlendirir.
    /// </summary>
    private ParticleSystem CreateParticle(ParticleSystem prefab)
    {
        ParticleSystem ps = Instantiate(prefab, _container);

        // Stop action'ı Callback'e set et
        var main = ps.main;
        main.stopAction = ParticleSystemStopAction.Callback;

        // Loop'u kapat — tek seferlik patlama
        main.loop = false;

        // Play on awake kapat — biz kontrol edeceğiz
        main.playOnAwake = false;

        // Handler bileşeni ekle (pool'a iade mekanizması)
        // GetComponent kontrolü: Pre-warm'da Get→Release döngüsünde
        // handler zaten eklenmiş olabilir (prefab'ta varsa)
        ParticleReturnHandler handler = ps.GetComponent<ParticleReturnHandler>();
        if (handler == null)
            handler = ps.gameObject.AddComponent<ParticleReturnHandler>();

        ps.gameObject.SetActive(false);
        return ps;
    }

    // ── Pool Callback'leri ──

    private void OnPoolGetParticle(ParticleSystem ps)
    {
        ps.gameObject.SetActive(true);
    }

    private void OnPoolReleaseParticle(ParticleSystem ps)
    {
        ps.gameObject.SetActive(false);
    }

    private void OnPoolDestroyParticle(ParticleSystem ps)
    {
        if (ps != null)
            Destroy(ps.gameObject);
    }

    private void PreWarmPool(ObjectPool<ParticleSystem> pool, int count)
    {
        ParticleSystem[] temp = new ParticleSystem[count];

        for (int i = 0; i < count; i++)
            temp[i] = pool.Get();

        for (int i = 0; i < count; i++)
            pool.Release(temp[i]);
    }

    // ════════════════════════════════════════════════════════════════
    //  PUBLIC API — Efekt Oynatma
    // ════════════════════════════════════════════════════════════════

    /// <summary>
    /// Büyük patlama efekti oynatır (düşman ölümü, oyuncu ölümü).
    /// 
    /// Akış:
    /// 1. Pool'dan ParticleSystem al
    /// 2. Pozisyonunu set et
    /// 3. Rengini değiştir (düşman rengine göre)
    /// 4. Play() çağır
    /// 5. Oynatım bitince OnParticleSystemStopped → otomatik pool'a iade
    /// 
    /// GC allocation: SIFIR (ParticleSystem.MainModule struct'tır).
    /// </summary>
    /// <param name="position">Patlamanın dünya pozisyonu</param>
    /// <param name="color">Partikül rengi (düşman tier rengine göre)</param>
    public void PlayExplosion(Vector3 position, Color color)
    {
        ParticleSystem ps = _explosionPool.Get();
        ConfigureAndPlay(ps, position, color, _explosionPool);
    }

    /// <summary>
    /// Küçük kıvılcım efekti oynatır (mermi isabet, hasar alma).
    /// Patlama ile aynı akış, farklı pool.
    /// </summary>
    public void PlaySpark(Vector3 position, Color color)
    {
        ParticleSystem ps = _sparkPool.Get();
        ConfigureAndPlay(ps, position, color, _sparkPool);
    }

    /// <summary>
    /// Partikülü konfigüre edip oynatır.
    /// 
    /// startColor değiştirme:
    /// → ParticleSystem.MainModule struct'tır — heap allocation yok.
    /// → main.startColor = new MinMaxGradient(color) da struct.
    /// → Tüm işlem stack'te gerçekleşir.
    /// </summary>
    private void ConfigureAndPlay(ParticleSystem ps, Vector3 position, Color color,
                                   ObjectPool<ParticleSystem> ownerPool)
    {
        // Pozisyon
        ps.transform.position = position;

        // Renk
        var main = ps.main;
        main.startColor = new ParticleSystem.MinMaxGradient(color);

        // Pool referansını handler'a ilet
        ParticleReturnHandler handler = ps.GetComponent<ParticleReturnHandler>();
        if (handler != null)
            handler.SetPool(ownerPool);

        // Önceki oynatımdan kalan partikülleri temizle
        ps.Clear(true);

        // Oynat
        ps.Play(true);
    }

    // ════════════════════════════════════════════════════════════════
    //  HIERARCHY
    // ════════════════════════════════════════════════════════════════

    private void CreateContainer()
    {
        GameObject container = new GameObject("── Particle Pool ──");
        _container = container.transform;
    }

    // ════════════════════════════════════════════════════════════════
    //  DOĞRULAMA
    // ════════════════════════════════════════════════════════════════

    [System.Diagnostics.Conditional("UNITY_EDITOR"),
     System.Diagnostics.Conditional("DEVELOPMENT_BUILD")]
    private void ValidateSetup()
    {
        if (_explosionPrefab == null)
            Debug.LogError("[ParticleManager] Explosion prefab atanmamış!", this);
    }
}

// ════════════════════════════════════════════════════════════════════════
//  YARDIMCI BİLEŞEN — Partikül Pool'a İade Mekanizması
// ════════════════════════════════════════════════════════════════════════

/// <summary>
/// Her pooled ParticleSystem objesine eklenen küçük handler.
/// 
/// Görevi: OnParticleSystemStopped callback'ini yakalayıp
/// partikülü doğru pool'a iade etmek.
/// 
/// Neden ayrı bileşen?
/// → OnParticleSystemStopped, ParticleSystem'ın attach olduğu
///   GameObject'teki MonoBehaviour'larda çağrılır.
///   ParticleManager ayrı bir objede olduğu için callback'i alamaz.
///   Bu handler her partikül objesinde olduğu için callback'i yakalar.
/// 
/// Bellek maliyeti: Obje başına ~40 byte (1 referans field).
/// 10 partikül × 40 byte = 400 byte — ihmal edilebilir.
/// </summary>
public class ParticleReturnHandler : MonoBehaviour
{
    private IObjectPool<ParticleSystem> _pool;
    private ParticleSystem _particleSystem;

    private void Awake()
    {
        _particleSystem = GetComponent<ParticleSystem>();
    }

    /// <summary>ParticleManager tarafından her Play öncesi çağrılır.</summary>
    public void SetPool(IObjectPool<ParticleSystem> pool)
    {
        _pool = pool;
    }

    /// <summary>
    /// Unity callback: Partikül oynatımı bittiğinde otomatik çağrılır.
    /// (MainModule.stopAction = ParticleSystemStopAction.Callback olmalı)
    /// 
    /// Double-release koruması: activeSelf kontrolü.
    /// </summary>
    private void OnParticleSystemStopped()
    {
        if (!gameObject.activeSelf)
            return;

        if (_pool != null)
        {
            _pool.Release(_particleSystem);
        }
        else
        {
            gameObject.SetActive(false);
        }
    }
}
