using UnityEngine;
using UnityEngine.Pool;

/// <summary>
/// ╔══════════════════════════════════════════════════════════════════╗
///   ATEŞ HATTI — PlayerShooting (Otomatik Ateş Sistemi)
///   
///   Oyuncunun gemisinden belirli aralıklarla otomatik mermi atan,
///   mermileri Object Pool ile yöneten ateş sistemi.
/// ╚══════════════════════════════════════════════════════════════════╝
/// 
/// Sorumluluk (SRP): SADECE ateş etme + mermi pool yönetimi.
/// Hareket PlayerController'da, hasar sistemi ayrı bir script'te olacak.
/// 
/// Object Pool Akışı:
/// ──────────────────
///   ┌─────────────┐     Get()      ┌──────────────┐
///   │  POOL        │ ──────────────▶│  AKTİF MERMİ │
///   │  (Inactive)  │               │  (Sahne'de)   │
///   │              │◀──────────────│              │
///   └─────────────┘    Release()   └──────────────┘
///                    (ekran dışı                    
///                     veya isabet)                  
/// 
/// Pool Konfigürasyonu:
///   defaultCapacity: 20 → Oyun başında 20 mermi pre-allocate edilir.
///   maxSize: 50 → Pool'da en fazla 50 inaktif mermi tutulur.
///                  Fazlası Destroy edilir (memory leak koruması).
///   collectionCheck: false → Release'de "zaten pool'da mı" kontrolü yapılmaz.
///                           Bullet.cs'deki activeSelf kontrolü bu görevi üstlenir.
///                           Release build'de gereksiz overhead'den kaçınılır.
/// 
/// Neden Unity'nin ObjectPool'u?
/// ─────────────────────────────
/// Custom Queue-based pool ile karşılaştırıldığında:
///   + Resmi, test edilmiş, battle-proven implementasyon
///   + maxSize ile otomatik memory leak koruması
///   + IObjectPool interface'i sayesinde Bullet, pool tipinden bağımsız
///   + CountActive/CountInactive ile runtime debug kolaylığı
///   - Delegate callback overhead'i var (~2-3ns/çağrı) ama 30-40 mermi
///     ölçeğinde ölçülemeyecek kadar küçük
/// </summary>
public class PlayerShooting : MonoBehaviour
{
    // ════════════════════════════════════════════════════════════════
    //  INSPECTOR AYARLARI
    // ════════════════════════════════════════════════════════════════

    [Header("─── Ateş Ayarları ───")]
    [Tooltip("İki mermi arasındaki süre (saniye).\n" +
             "0.28 = HTML prototipindeki referans hız.\n" +
             "Upgrade sistemi bu değeri runtime'da düşürecek.")]
    [SerializeField, Range(0.05f, 1f)]
    private float _fireInterval = 0.28f;

    [Tooltip("Merminin geminin ne kadar üstünde spawn olacağı (world units).\n" +
             "Çok düşük = mermi geminin içinden çıkıyor gibi görünür.\n" +
             "Çok yüksek = mermi havada beliriyor gibi görünür.\n" +
             "0.5 = geminin burnundan çıkış hissi.")]
    [SerializeField, Range(0.1f, 1.5f)]
    private float _muzzleOffsetY = 0.5f;

    [Header("─── Mermi Prefab ───")]
    [Tooltip("Pool'un oluşturacağı mermi prefab'ı.\n" +
             "Prefab'da Bullet.cs componenti olmalı.\n" +
             "SpriteRenderer veya TrailRenderer opsiyonel.")]
    [SerializeField]
    private Bullet _bulletPrefab;

    [Header("─── Pool Ayarları ───")]
    [Tooltip("Başlangıçta oluşturulacak mermi sayısı.\n" +
             "Çok düşük = ilk saniyelerde runtime Instantiate olur.\n" +
             "Çok yüksek = gereksiz başlangıç bellek kullanımı.\n" +
             "20 = ~5 saniye ateş kapasitesi (0.28 interval ile).")]
    [SerializeField, Range(5, 50)]
    private int _poolDefaultCapacity = 20;

    [Tooltip("Pool'da tutulacak maksimum inaktif mermi sayısı.\n" +
             "Bu sınır aşılırsa fazla mermiler Destroy edilir.\n" +
             "Memory leak koruması sağlar.")]
    [SerializeField, Range(20, 100)]
    private int _poolMaxSize = 50;

    // ════════════════════════════════════════════════════════════════
    //  CACHE
    // ════════════════════════════════════════════════════════════════

    private Transform _cachedTransform;
    private Camera    _mainCamera;

    // ════════════════════════════════════════════════════════════════
    //  POOL
    // ════════════════════════════════════════════════════════════════

    /// <summary>
    /// Unity'nin built-in Object Pool implementasyonu.
    /// Stack-based (LIFO): En son iade edilen mermi ilk alınır.
    /// Thread-safe DEĞİL ama Unity main thread'de çalıştığı için sorun yok.
    /// </summary>
    private ObjectPool<Bullet> _bulletPool;

    // ════════════════════════════════════════════════════════════════
    //  ATEŞ STATE
    // ════════════════════════════════════════════════════════════════

    /// <summary>
    /// Bir sonraki ateşe kadar kalan süre.
    /// Her frame Time.deltaTime düşülür, 0'ın altına inince ateş edilir.
    /// </summary>
    private float _fireTimer;

    /// <summary>
    /// Ekranın üst sınırı (world Y koordinatı).
    /// Mermiler bu sınırı aşınca pool'a iade edilir.
    /// Her frame hesaplanmaz — sadece ekran değişince güncellenir.
    /// </summary>
    private float _screenTopY;

    // Ekran değişiklik tespiti için
    private float _prevScreenW;
    private float _prevScreenH;
    private float _prevOrthoSize;

    /// <summary>
    /// Pool'dan alınan mermilerin parent'ı olacak boş GameObject.
    /// Hierarchy'yi temiz tutar — 40 mermi root'ta dağılmaz.
    /// Bir kez oluşturulur, pooled mermiler bunun altına yerleşir.
    /// </summary>
    private Transform _bulletContainer;

    // ════════════════════════════════════════════════════════════════
    //  UNITY LIFECYCLE
    // ════════════════════════════════════════════════════════════════

    private void Awake()
    {
        _cachedTransform = transform;
        _mainCamera      = Camera.main;

        ValidateSetup();
        CreateBulletContainer();
        InitializePool();
        RecalculateScreenTop();

        // İlk mermiyi hemen atmasın — kısa bir bekleme ver
        _fireTimer = _fireInterval;
    }

    private void Update()
    {
        RefreshScreenTopIfChanged();
        HandleAutoFire();
    }

    /// <summary>
    /// Script deaktif olduğunda veya sahne değiştiğinde pool'u temizle.
    /// Bu olmadan pool'daki inaktif mermiler orphan kalır.
    /// 
    /// Neden OnDestroy değil OnDisable?
    /// → OnDisable, obje deaktif olduğunda da çağrılır (oyuncu ölümü gibi).
    ///   Ama pool'u sadece sahne değişiminde veya obje yok edildiğinde temizlemek istiyoruz.
    ///   
    /// Aslında ikisi de çağrılır, ama pool.Dispose() idempotent'tır —
    /// ikinci çağrı güvenli bir şekilde hiçbir şey yapmaz.
    /// Bu yüzden OnDestroy tercih ediyoruz.
    /// </summary>
    private void OnDestroy()
    {
        _bulletPool?.Dispose();
    }

    // ════════════════════════════════════════════════════════════════
    //  POOL BAŞLATMA
    // ════════════════════════════════════════════════════════════════

    /// <summary>
    /// Object Pool'u oluşturur ve callback'leri tanımlar.
    /// 
    /// Callback akışı:
    /// ───────────────
    /// createFunc:        Pool boşken yeni mermi gerektiğinde çağrılır → Instantiate
    /// actionOnGet:       Pool'dan mermi alınırken çağrılır → SetActive(true)
    /// actionOnRelease:   Mermi pool'a iade edilirken çağrılır → SetActive(false)
    /// actionOnDestroy:   Pool maxSize'ı aşıldığında fazla mermi yok edilir → Destroy
    /// 
    /// collectionCheck: false
    /// → "Bu mermi zaten pool'da mı?" kontrolünü devre dışı bırakır.
    /// → Bullet.cs'deki activeSelf kontrolü aynı işi daha ucuza yapar.
    /// → Release build'de gereksiz string formatting + exception overhead'inden kaçınılır.
    /// </summary>
    private void InitializePool()
    {
        _bulletPool = new ObjectPool<Bullet>(
            createFunc:      OnPoolCreateBullet,
            actionOnGet:     OnPoolGetBullet,
            actionOnRelease: OnPoolReleaseBullet,
            actionOnDestroy: OnPoolDestroyBullet,
            collectionCheck: false,
            defaultCapacity: _poolDefaultCapacity,
            maxSize:         _poolMaxSize
        );

        // ── Pre-warm: Başlangıçta defaultCapacity kadar mermi oluştur ──
        // İlk saniyelerde runtime Instantiate spike'ını önler.
        // Get → hemen Release = oluştur ve pool'a koy.
        PreWarmPool();
    }

    /// <summary>
    /// Pool'u başlangıçta doldurur.
    /// 
    /// Neden pre-warm?
    /// → Pre-warm olmadan ilk 5 saniyede her ateşte Instantiate çağrılır.
    ///   Mobilde bu, oyun başlangıcındaki ilk saniyelerde frame spike'a neden olur.
    ///   Loading screen sırasında oluşturmak çok daha güvenli.
    /// 
    /// Nasıl çalışır?
    /// → Get() çağrısı pool boşsa createFunc'ı tetikler → Instantiate.
    /// → Hemen Release() ile pool'a iade ederiz → SetActive(false).
    /// → Sonuç: Pool defaultCapacity kadar hazır mermi ile başlar.
    /// </summary>
    private void PreWarmPool()
    {
        Bullet[] warmBullets = new Bullet[_poolDefaultCapacity];

        for (int i = 0; i < _poolDefaultCapacity; i++)
        {
            warmBullets[i] = _bulletPool.Get();
        }

        for (int i = 0; i < _poolDefaultCapacity; i++)
        {
            _bulletPool.Release(warmBullets[i]);
        }
    }

    // ── Pool Callback'leri ──

    /// <summary>
    /// Pool boşken çağrılır. Yeni bir mermi objesi oluşturur.
    /// Bu, pool'un HAYATI BOYUNCA mümkün olan en az kez çağrılmalı.
    /// Pre-warm ve doğru maxSize ayarıyla runtime'da neredeyse hiç çağrılmaz.
    /// </summary>
    private Bullet OnPoolCreateBullet()
    {
        // _bulletContainer altında oluştur — Hierarchy temiz kalır
        Bullet bullet = Instantiate(_bulletPrefab, _bulletContainer);

        // Pool referansını ve ekran sınırını set et
        bullet.Initialize(_bulletPool, _screenTopY);

        return bullet;
    }

    /// <summary>
    /// Pool'dan mermi alınırken çağrılır.
    /// Objeyi aktif eder — görünür ve Update çalışmaya başlar.
    /// </summary>
    private void OnPoolGetBullet(Bullet bullet)
    {
        bullet.gameObject.SetActive(true);
    }

    /// <summary>
    /// Mermi pool'a iade edilirken çağrılır.
    /// Objeyi deaktif eder — görünmez ve Update durur.
    /// 
    /// Neden transform.position sıfırlamıyoruz?
    /// → Gereksiz. Mermi tekrar Get() ile alınırken pozisyon
    ///   Fire() metodunda zaten yeniden set ediliyor.
    /// </summary>
    private void OnPoolReleaseBullet(Bullet bullet)
    {
        bullet.gameObject.SetActive(false);
    }

    /// <summary>
    /// Pool maxSize'ı aşıldığında fazla mermiler için çağrılır.
    /// Gerçek Destroy işlemi sadece burada olur — normal akışta asla çağrılmaz.
    /// Bu, aşırı spawn durumlarında memory leak'i önleyen güvenlik ağıdır.
    /// </summary>
    private void OnPoolDestroyBullet(Bullet bullet)
    {
        if (bullet != null)
            Destroy(bullet.gameObject);
    }

    // ════════════════════════════════════════════════════════════════
    //  OTOMATİK ATEŞ
    // ════════════════════════════════════════════════════════════════

    /// <summary>
    /// Fire interval'e göre otomatik ateş kontrolü.
    /// 
    /// Timer mantığı:
    /// → Her frame _fireTimer'dan Time.deltaTime düşülür.
    /// → 0'ın altına inince ateş edilir ve timer resetlenir.
    /// → -= yerine doğrudan = kullanılmıyor çünkü:
    ///   Eğer frame çok uzun sürerse (lag spike) ve timer -0.15 olursa,
    ///   += _fireInterval ile bir sonraki ateş 0.13s sonra olur (0.28 - 0.15).
    ///   Bu, kayıp zamanı telafi eder ve ateş ritmini korur.
    /// </summary>
    private void HandleAutoFire()
    {
        _fireTimer -= Time.deltaTime;

        if (_fireTimer <= 0f)
        {
            Fire();

            // Timer'ı resetle — negatif kalan süreyi koru (ritim telafisi)
            _fireTimer += _fireInterval;

            // Güvenlik: Çok büyük bir lag spike'ta timer hâlâ negatifse
            // sonsuz ateş döngüsünü önle
            if (_fireTimer < 0f)
                _fireTimer = 0f;
        }
    }

    /// <summary>
    /// Tek bir mermi ateşler.
    /// 
    /// Akış:
    /// 1. Pool'dan mermi al (Get) → OnPoolGetBullet çağrılır → SetActive(true)
    /// 2. Pozisyonu geminin burnuna set et
    /// 3. Ekran sınırını güncelle (orientation değişmiş olabilir)
    /// 4. Mermi kendi Update'inde yukarı hareket etmeye başlar
    /// </summary>
    private void Fire()
    {
        Bullet bullet = _bulletPool.Get();

        // Spawn pozisyonu: Geminin merkez X'i, üst kenarından muzzleOffset kadar yukarısı
        Vector3 spawnPos = _cachedTransform.position;
        spawnPos.y += _muzzleOffsetY;

        bullet.transform.position = spawnPos;

        // Ekran sınırını güncelle (her mermi için değil, zaten cache'li ama
        // orientation değişmişse son değeri ilet)
        bullet.Initialize(_bulletPool, _screenTopY);
    }

    // ════════════════════════════════════════════════════════════════
    //  EKRAN ÜST SINIRI
    // ════════════════════════════════════════════════════════════════

    /// <summary>
    /// Ekran boyutu veya kamera değiştiyse üst sınırı yeniden hesaplar.
    /// PlayerController'daki aynı pattern — dirty check ile gereksiz
    /// hesaplamadan kaçınılır.
    /// </summary>
    private void RefreshScreenTopIfChanged()
    {
        float sw = Screen.width;
        float sh = Screen.height;
        float os = _mainCamera.orthographicSize;

        if (sw != _prevScreenW || sh != _prevScreenH || !Mathf.Approximately(os, _prevOrthoSize))
        {
            RecalculateScreenTop();
        }
    }

    /// <summary>
    /// Viewport (1,1) noktasından ekranın üst kenarının world Y'sini hesaplar.
    /// </summary>
    private void RecalculateScreenTop()
    {
        float zDist = Mathf.Abs(_mainCamera.transform.position.z);
        Vector3 topRight = _mainCamera.ViewportToWorldPoint(new Vector3(1f, 1f, zDist));
        _screenTopY = topRight.y;

        _prevScreenW   = Screen.width;
        _prevScreenH   = Screen.height;
        _prevOrthoSize = _mainCamera.orthographicSize;
    }

    // ════════════════════════════════════════════════════════════════
    //  HIERARCHY YÖNETİMİ
    // ════════════════════════════════════════════════════════════════

    /// <summary>
    /// Mermiler için boş bir parent GameObject oluşturur.
    /// 
    /// Neden?
    /// → 40 mermi root Hierarchy'de dağılırsa:
    ///   1. Editor'da Hierarchy paneli okunamaz hale gelir
    ///   2. Root obje sayısı arttıkça Unity'nin internal scene traversal'ı yavaşlar
    /// 
    /// Container altında toplamak:
    ///   + Hierarchy temiz
    ///   + Tüm mermileri tek tıkla collapse/expand edebilirsin
    ///   + Debug sırasında kaç mermi aktif anında görülür
    /// 
    /// DontDestroyOnLoad KULLANILMIYOR — sahne değişiminde pool temizlenmeli.
    /// </summary>
    private void CreateBulletContainer()
    {
        GameObject container = new GameObject("── Bullet Pool ──");
        _bulletContainer = container.transform;

        // Bullet container'ı root'ta tut — player'ın child'ı yapma!
        // Aksi halde player hareket ettikçe tüm mermiler de hareket eder.
    }

    // ════════════════════════════════════════════════════════════════
    //  PUBLIC API — Upgrade Sistemi İçin
    // ════════════════════════════════════════════════════════════════

    /// <summary>
    /// Ateş hızını değiştirir. ShopManager veya Upgrade sistemi tarafından çağrılır.
    /// 
    /// Parametre: Yeni fire interval (saniye). Düşük = daha hızlı.
    /// Minimum 0.05s ile sınırlandırılmış — daha düşük değerler
    /// ekranı mermiyle doldurur ve gameplay okunmaz hale gelir.
    /// </summary>
    public void SetFireInterval(float newInterval)
    {
        _fireInterval = Mathf.Max(0.05f, newInterval);
    }

    /// <summary>
    /// Mevcut fire interval'i döner.
    /// Upgrade sistemi mevcut değeri okuyup azaltabilir.
    /// </summary>
    public float GetFireInterval()
    {
        return _fireInterval;
    }

    // ════════════════════════════════════════════════════════════════
    //  DOĞRULAMA (Sadece Development Build)
    // ════════════════════════════════════════════════════════════════

    [System.Diagnostics.Conditional("UNITY_EDITOR"),
     System.Diagnostics.Conditional("DEVELOPMENT_BUILD")]
    private void ValidateSetup()
    {
        if (_bulletPrefab == null)
            Debug.LogError(
                "[PlayerShooting] Bullet Prefab atanmamış! " +
                "Inspector'da _bulletPrefab alanına mermi prefab'ını sürükleyin.", this);

        if (_mainCamera == null)
            Debug.LogError(
                "[PlayerShooting] MainCamera bulunamadı! " +
                "Kameranın tag'ini kontrol edin.", this);
    }

    // ════════════════════════════════════════════════════════════════
    //  EDITOR — Pool Durumu Gizmos
    // ════════════════════════════════════════════════════════════════

    #if UNITY_EDITOR
    /// <summary>
    /// Scene view'da muzzle pozisyonunu ve ateş yönünü gösterir.
    /// Pool durumu Inspector'da CountActive/CountInactive ile izlenebilir.
    /// </summary>
    private void OnDrawGizmosSelected()
    {
        // Muzzle noktası (merminin spawn olacağı yer)
        Vector3 muzzle = transform.position;
        muzzle.y += _muzzleOffsetY;

        Gizmos.color = new Color(1f, 0.92f, 0.23f, 0.9f); // Sarı (mermi rengi)
        Gizmos.DrawWireSphere(muzzle, 0.08f);

        // Ateş yönü oku
        Gizmos.color = new Color(1f, 0.92f, 0.23f, 0.4f);
        Gizmos.DrawLine(muzzle, muzzle + Vector3.up * 1.5f);

        // Pool bilgisi (Play mode'da)
        if (Application.isPlaying && _bulletPool != null)
        {
            UnityEditor.Handles.Label(
                muzzle + Vector3.right * 0.5f,
                $"Pool: {_bulletPool.CountActive} aktif / {_bulletPool.CountInactive} beklemede"
            );
        }
    }
    #endif

}
