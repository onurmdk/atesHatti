using UnityEngine;

/// <summary>
/// ╔══════════════════════════════════════════════════════════════════╗
///   ATEŞ HATTI — StarfieldManager (HTML Prototipi Sadık Yıldız Alanı)
///   
///   HTML prototipindeki StarField class'ının birebir Unity karşılığı.
///   80 yıldız, rastgele hız/boyut/parlaklık, tek renk (#c8d6e5).
///   Doğal paralaks: Yavaş+sönük = uzak, hızlı+parlak = yakın.
/// ╚══════════════════════════════════════════════════════════════════╝
/// 
/// HTML Prototipi Referans:
/// ────────────────────────
///   for (let i = 0; i &lt; 80; i++) {
///     stars.push({
///       x: rand(0, canvas.width),
///       y: rand(0, canvas.height),
///       size: rand(0.5, 2),        → Unity: 0.01 - 0.04 world units
///       speed: rand(30, 90),        → Unity: 0.8 - 2.5 world units/s
///       alpha: rand(0.3, 0.9),
///     });
///   }
/// 
/// Neden ParticleSystem.SetParticles() ile Manuel Kontrol?
/// ───────────────────────────────────────────────────────
/// Önceki denemede emission/velocity/shape modülleri kullanıldı.
/// Bu modüller karmaşık, debug edilmesi zor ve HTML davranışıyla
/// birebir eşleşmesi garanti değil.
/// 
/// SetParticles() ile:
///   - Her frame pozisyonları biz kontrol ediyoruz (HTML'deki gibi)
///   - Wrap-around mantığı birebir aynı
///   - Boyut ve alpha doğrudan set ediliyor
///   - Sıfır sürpriz, sıfır modül konfigürasyonu
///   - HTML'deki for döngüsünün 1:1 kopyası
/// 
/// Performans:
/// ───────────
/// • 80 partikül — mobilde ihmal edilebilir
/// • SetParticles: Tek bir native call ile tüm partiküller güncellenir
/// • GC allocation: SIFIR (ParticleSystem.Particle struct array reuse)
/// • SortingOrder -10: Tüm gameplay objelerinin arkasında
/// </summary>
public class StarfieldManager : MonoBehaviour
{
    // ════════════════════════════════════════════════════════════════
    //  INSPECTOR AYARLARI
    // ════════════════════════════════════════════════════════════════

    [Header("─── Yıldız Ayarları ───")]
    [Tooltip("Toplam yıldız sayısı. HTML prototipinde 80.")]
    [SerializeField, Range(30, 200)]
    private int _starCount = 80;

    [Tooltip("Yıldız rengi. HTML'de #c8d6e5 (açık gri-mavi).")]
    [SerializeField]
    private Color _starColor = new Color(0.784f, 0.839f, 0.898f, 1f); // #c8d6e5

    [Header("─── Hız (World Units/Saniye) ───")]
    [Tooltip("En yavaş yıldız hızı (uzak yıldızlar).")]
    [SerializeField]
    private float _minSpeed = 0.8f;

    [Tooltip("En hızlı yıldız hızı (yakın yıldızlar).")]
    [SerializeField]
    private float _maxSpeed = 2.5f;

    [Header("─── Boyut (World Units) ───")]
    [Tooltip("En küçük yıldız boyutu.")]
    [SerializeField]
    private float _minSize = 0.01f;

    [Tooltip("En büyük yıldız boyutu.")]
    [SerializeField]
    private float _maxSize = 0.04f;

    [Header("─── Parlaklık ───")]
    [Tooltip("En sönük yıldız alpha'sı (uzak).")]
    [SerializeField, Range(0.1f, 1f)]
    private float _minAlpha = 0.3f;

    [Tooltip("En parlak yıldız alpha'sı (yakın).")]
    [SerializeField, Range(0.1f, 1f)]
    private float _maxAlpha = 0.9f;

    [Header("─── Rendering ───")]
    [Tooltip("Sorting Order. Negatif = gameplay'in arkasında.\n" +
             "-10 önerilir. Player/Enemy genellikle 0'da.")]
    [SerializeField]
    private int _sortingOrder = -10;

    // ════════════════════════════════════════════════════════════════
    //  YILDIZ VERİ YAPISI
    // ════════════════════════════════════════════════════════════════

    /// <summary>
    /// Her yıldızın kalıcı verileri.
    /// HTML'deki { x, y, size, speed, alpha } objesinin karşılığı.
    /// Struct array — GC allocation yok, cache-friendly.
    /// </summary>
    private struct StarData
    {
        public float speed;   // Aşağı hareket hızı (world units/s)
        public float size;    // Partikül boyutu (world units)
        public float alpha;   // Parlaklık (0-1)
    }

    // ════════════════════════════════════════════════════════════════
    //  STATE
    // ════════════════════════════════════════════════════════════════

    private ParticleSystem _particleSystem;
    private ParticleSystem.Particle[] _particles;  // Reusable buffer — her frame yeniden kullanılır
    private StarData[] _starData;                   // Her yıldızın sabit özellikleri

    // Ekran sınırları
    private Camera _mainCamera;
    private float _screenMinX, _screenMaxX;
    private float _screenMinY, _screenMaxY;

    // ════════════════════════════════════════════════════════════════
    //  UNITY LIFECYCLE
    // ════════════════════════════════════════════════════════════════

    private void Awake()
    {
        _mainCamera = Camera.main;
        CalculateScreenBounds();
        CreateParticleSystem();
        InitializeStars();
    }

    /// <summary>
    /// Her frame: Yıldızları aşağı kaydır, wrap-around uygula, ParticleSystem'e gönder.
    /// 
    /// HTML karşılığı:
    ///   update(dt) {
    ///     for (const s of this.stars) {
    ///       s.y += s.speed * dt;
    ///       if (s.y > h + 2) { s.y = -2; s.x = rand(0, w); }
    ///     }
    ///   }
    /// 
    /// GC allocation: SIFIR (_particles array reuse edilir).
    /// </summary>
    private void Update()
    {
        float dt = Time.deltaTime;

        for (int i = 0; i < _starCount; i++)
        {
            // ── Aşağı hareket ──
            Vector3 pos = _particles[i].position;
            pos.y -= _starData[i].speed * dt;

            // ── Wrap-around: Ekran altından çıkınca üstten geri gel ──
            // HTML: if (s.y > h + 2) { s.y = -2; s.x = rand(0, w); }
            if (pos.y < _screenMinY - 0.5f)
            {
                pos.y = _screenMaxY + 0.5f;
                pos.x = Random.Range(_screenMinX, _screenMaxX);
            }

            _particles[i].position = pos;
        }

        // ── Tüm partikülleri tek seferde güncelle ──
        // SetParticles: Native C++ tarafına tek bir çağrı ile
        // 80 partikülün pozisyonunu aktarır. Her partikül için
        // ayrı SetPosition çağırmaktan çok daha verimli.
        _particleSystem.SetParticles(_particles, _starCount);
    }

    // ════════════════════════════════════════════════════════════════
    //  KURULUM
    // ════════════════════════════════════════════════════════════════

    /// <summary>
    /// Ekran sınırlarını world koordinatlarında hesaplar.
    /// Yıldızlar bu sınırlar içinde doğar ve hareket eder.
    /// </summary>
    private void CalculateScreenBounds()
    {
        float zDist = Mathf.Abs(_mainCamera.transform.position.z);

        Vector3 bottomLeft = _mainCamera.ViewportToWorldPoint(new Vector3(0f, 0f, zDist));
        Vector3 topRight   = _mainCamera.ViewportToWorldPoint(new Vector3(1f, 1f, zDist));

        _screenMinX = bottomLeft.x;
        _screenMaxX = topRight.x;
        _screenMinY = bottomLeft.y;
        _screenMaxY = topRight.y;
    }

    /// <summary>
    /// ParticleSystem'i manuel kontrol için konfigüre eder.
    /// 
    /// Anahtar ayarlar:
    ///   - maxParticles = _starCount (tam sayı, fazla yok)
    ///   - simulationSpace = Local (pozisyonları biz yönetiyoruz)
    ///   - playOnAwake = false (biz kontrol ediyoruz)
    ///   - emission kapalı (biz SetParticles ile veriyoruz)
    ///   - shape kapalı (pozisyonları biz belirliyoruz)
    ///   
    /// Renderer:
    ///   - sortingOrder = -10 (gameplay'in arkasında)
    ///   - Default-Particle materyali (additive blending — uzayda güzel görünür)
    /// </summary>
    private void CreateParticleSystem()
    {
        // ── Child GameObject oluştur ──
        GameObject go = new GameObject("Starfield");
        go.transform.SetParent(transform);
        go.transform.localPosition = Vector3.zero;

        _particleSystem = go.AddComponent<ParticleSystem>();

        // ── Main Module ──
        var main = _particleSystem.main;
        main.maxParticles    = _starCount;
        main.simulationSpace = ParticleSystemSimulationSpace.Local;
        main.playOnAwake     = false;
        main.loop            = false;

        // startLifetime çok yüksek — partiküller asla ölmesin
        // (biz wrap-around ile yönetiyoruz)
        main.startLifetime = Mathf.Infinity;
        main.startSpeed    = 0f;
        main.startSize     = 1f; // Her partikülün boyutunu ayrı set edeceğiz

        // ── Emission kapalı — biz SetParticles ile veriyoruz ──
        var emission = _particleSystem.emission;
        emission.enabled = false;

        // ── Shape kapalı — pozisyonları biz belirliyoruz ──
        var shape = _particleSystem.shape;
        shape.enabled = false;

        // ── Diğer modüller kapalı ──
        var vel = _particleSystem.velocityOverLifetime;
        vel.enabled = false;

        var col = _particleSystem.colorOverLifetime;
        col.enabled = false;

        var sizeOL = _particleSystem.sizeOverLifetime;
        sizeOL.enabled = false;

        var noise = _particleSystem.noise;
        noise.enabled = false;

        // ── Renderer ──
        ParticleSystemRenderer rend = _particleSystem.GetComponent<ParticleSystemRenderer>();
        rend.sortingOrder = _sortingOrder;
        rend.renderMode   = ParticleSystemRenderMode.Billboard;

        // Sprites-Default materyali: Düz alpha blending.
        // HTML'deki fillRect ile aynı davranış — renk birebir görünür.
        //
        // Neden Default-Particle DEĞİL?
        // → Default-Particle additive blending kullanır.
        //   Additive: Partikül rengi + arka plan rengi = toplam renk.
        //   Koyu arka plan (#080a12) üzerinde gri-mavi (#c8d6e5) toplandığında
        //   renk mora/pembeye kayar — tam da ekran görüntüsündeki sorun.
        //
        // Sprites-Default: Standart alpha blending.
        //   Partikül rengi olduğu gibi gösterilir, arka planla karışmaz.
        //   HTML canvas fillRect ile aynı davranış.
        rend.material = Resources.GetBuiltinResource<Material>("Sprites-Default.mat");

        // ── Partikülleri kabul etmeye hazır ──
        _particleSystem.Play();
    }

    /// <summary>
    /// 80 yıldızı rastgele pozisyon, hız, boyut ve alpha ile başlatır.
    /// 
    /// HTML karşılığı:
    ///   for (let i = 0; i &lt; STAR_COUNT; i++) {
    ///     stars.push({
    ///       x: rand(0, canvas.width),
    ///       y: rand(0, canvas.height),
    ///       size: rand(0.5, 2),
    ///       speed: rand(30, 90),
    ///       alpha: rand(0.3, 0.9),
    ///     });
    ///   }
    /// 
    /// Doğal paralaks efekti:
    /// → Hızlı yıldızlar büyük ve parlak (yakın hissi)
    /// → Yavaş yıldızlar küçük ve sönük (uzak hissi)
    /// Bu korelasyon, hız bazlı lerp ile sağlanır.
    /// </summary>
    private void InitializeStars()
    {
        _particles = new ParticleSystem.Particle[_starCount];
        _starData  = new StarData[_starCount];

        for (int i = 0; i < _starCount; i++)
        {
            // ── Hız (temel rastgelelik faktörü) ──
            // HTML: speed: rand(30, 90)
            float speed = Random.Range(_minSpeed, _maxSpeed);

            // ── Hız-bazlı derinlik faktörü (0 = en yavaş/uzak, 1 = en hızlı/yakın) ──
            float depthFactor = Mathf.InverseLerp(_minSpeed, _maxSpeed, speed);

            // ── Boyut: Hızlı = büyük, yavaş = küçük (doğal paralaks) ──
            // Küçük rastgelelik ekleniyor ki mekanik görünmesin
            float size = Mathf.Lerp(_minSize, _maxSize, depthFactor)
                       + Random.Range(-0.005f, 0.005f);
            size = Mathf.Max(0.005f, size);

            // ── Alpha: Hızlı = parlak, yavaş = sönük ──
            float alpha = Mathf.Lerp(_minAlpha, _maxAlpha, depthFactor)
                        + Random.Range(-0.1f, 0.1f);
            alpha = Mathf.Clamp(alpha, _minAlpha, _maxAlpha);

            // ── Kalıcı verileri kaydet ──
            _starData[i] = new StarData
            {
                speed = speed,
                size  = size,
                alpha = alpha
            };

            // ── Partikül başlangıç durumu ──
            _particles[i].position      = new Vector3(
                Random.Range(_screenMinX, _screenMaxX),
                Random.Range(_screenMinY, _screenMaxY),
                0f
            );
            _particles[i].startSize     = size;
            _particles[i].startColor    = new Color32(
                (byte)(_starColor.r * 255),
                (byte)(_starColor.g * 255),
                (byte)(_starColor.b * 255),
                (byte)(alpha * 255)
            );
            _particles[i].startLifetime = float.MaxValue;
            _particles[i].remainingLifetime = float.MaxValue;
        }

        // ── İlk frame'de tüm yıldızlar görünsün ──
        _particleSystem.SetParticles(_particles, _starCount);
    }
}
