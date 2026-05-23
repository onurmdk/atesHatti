using UnityEngine;
using UnityEngine.Pool;

/// <summary>
/// ╔══════════════════════════════════════════════════════════════════╗
///   ATEŞ HATTI — Bullet (Mermi)
///   
///   Yukarı doğru sabit hızla hareket eden ve ekran dışına çıkınca
///   kendini Object Pool'a iade eden mermi scripti.
/// ╚══════════════════════════════════════════════════════════════════╝
/// 
/// Sorumluluk (SRP): SADECE hareket + ekran dışı tespiti + pool'a iade.
/// Hasar verme mantığı bu script'te DEĞİL — çarpışma tespitinde
/// (PlayerShooting veya ayrı bir DamageDealer) ele alınacak.
/// 
/// Object Pool Mantığı:
/// ─────────────────────
/// Geleneksel yaklaşımda her mermi için Instantiate() çağrılır,
/// ekran dışına çıkınca Destroy() ile yok edilir. Bu iki işlem:
///   1. Instantiate: Memory allocation + component initialization
///   2. Destroy:     GC'ye iş yükü + frame spike riski
/// Her saniye ~4 mermi × 60 saniye = 240 allocation/dakika.
/// Mobilde bu GC spike'lara ve frame drop'lara neden olur.
/// 
/// Object Pool ile:
///   - Mermiler oyun başında (veya ilk ihtiyaçta) bir kez oluşturulur.
///   - Kullanılınca pool'dan alınır (Get), SetActive(true).
///   - İşi bitince pool'a iade edilir (Release), SetActive(false).
///   - Hiçbir zaman Destroy edilmez, tekrar tekrar kullanılır.
///   - GC allocation: SIFIR (oyun döngüsü boyunca).
/// 
/// Performans:
/// ───────────
/// • Update'te tek bir float karşılaştırma (y > _screenTopY)
/// • transform.position doğrudan set edilir (Translate yerine — bir method call daha az)
/// • Camera reference bu script'te tutulmaz — PlayerShooting sınırı set eder
/// </summary>
public class Bullet : MonoBehaviour
{
    // ════════════════════════════════════════════════════════════════
    //  INSPECTOR AYARLARI
    // ════════════════════════════════════════════════════════════════

    [Header("─── Mermi Ayarları ───")]
    [Tooltip("Merminin yukarı doğru hareket hızı (world units/saniye).\n" +
             "Çok düşük = oyuncu merminin hedefe ulaşmasını bekler, tempo düşer.\n" +
             "Çok yüksek = mermi görünmeden kaybolur, tatmin hissi azalır.\n" +
             "15-20 arası 2D shooter'lar için ideal aralık.")]
    [SerializeField, Range(5f, 40f)]
    private float _speed = 18f;

    // ════════════════════════════════════════════════════════════════
    //  POOL REFERANSI VE SINIR DEĞERİ
    // ════════════════════════════════════════════════════════════════

    /// <summary>
    /// Bu merminin ait olduğu Object Pool referansı.
    /// PlayerShooting tarafından mermi oluşturulurken set edilir.
    /// 
    /// Neden mermi kendi pool'unu biliyor?
    /// ───────────────────────────────────
    /// Alternatif: Merkezi bir BulletManager her frame tüm mermileri kontrol eder.
    /// Bu O(n) iteration gerektirir ve manager'ı bottleneck yapar.
    /// 
    /// Mevcut yaklaşım: Her mermi kendi sınır kontrolünü yapar ve
    /// kendini pool'a iade eder. Sorumluluk dağıtılmış, tek point of failure yok.
    /// Manager sadece spawn ile ilgilenir (PlayerShooting).
    /// </summary>
    private IObjectPool<Bullet> _ownerPool;

    /// <summary>
    /// Ekranın üst sınırı (world Y koordinatı).
    /// Mermi bu Y'nin üstüne çıkınca pool'a iade edilir.
    /// 
    /// Neden her frame Camera ile hesaplamıyoruz?
    /// → PlayerShooting zaten bu değeri hesaplıyor ve set ediyor.
    ///   Her mermi ayrıca Camera.main'e erişmesin — gereksiz overhead.
    ///   Tek bir yerden hesaplanıp dağıtılması SRP'ye daha uygun.
    /// </summary>
    private float _screenTopY;

    /// <summary>
    /// Ekran üstünden ne kadar ötede deactivate olacağını belirler.
    /// Küçük bir margin bırakıyoruz ki mermi ekranın tam kenarında
    /// ani bir şekilde kaybolmasın — görsel olarak daha temiz.
    /// </summary>
    private const float OFF_SCREEN_MARGIN = 0.5f;

    // ════════════════════════════════════════════════════════════════
    //  CACHE
    // ════════════════════════════════════════════════════════════════

    private Transform _cachedTransform;

    // ════════════════════════════════════════════════════════════════
    //  UNITY LIFECYCLE
    // ════════════════════════════════════════════════════════════════

    private void Awake()
    {
        _cachedTransform = transform;
    }

    /// <summary>
    /// Her frame çağrılır (mermi aktifken).
    /// 
    /// İçerik: Yukarı hareket + sınır kontrolü.
    /// GC Allocation: SIFIR.
    /// Branch sayısı: 1 (if kontrolü).
    /// </summary>
    private void Update()
    {
        // ── Yukarı hareket ──
        // Vector3 struct'tır — new Vector3() heap allocation yapmaz.
        Vector3 pos = _cachedTransform.position;
        pos.y += _speed * Time.deltaTime;
        _cachedTransform.position = pos;

        // ── Ekran dışı kontrolü ──
        // Mermi ekranın üstünden margin kadar ötesine geçtiyse pool'a iade et.
        // Bu kontrol her frame tek bir float karşılaştırma — maliyeti neredeyse sıfır.
        if (pos.y > _screenTopY + OFF_SCREEN_MARGIN)
        {
            ReturnToPool();
        }
    }

    // ════════════════════════════════════════════════════════════════
    //  PUBLIC API — PlayerShooting Tarafından Çağrılır
    // ════════════════════════════════════════════════════════════════

    /// <summary>
    /// Mermi pool'dan alındığında PlayerShooting tarafından çağrılır.
    /// Pool referansını ve ekran sınırını set eder.
    /// 
    /// Neden ayrı bir Initialize metodu?
    /// → Awake/Start sadece ilk oluşturmada çalışır.
    ///   Pool'dan her alınışta yeniden konfigüre edilmesi gereken
    ///   değerleri burada set ediyoruz.
    /// → Constructor kullanamıyoruz — MonoBehaviour'da constructor yasak.
    /// </summary>
    /// <param name="pool">Bu merminin ait olduğu pool</param>
    /// <param name="screenTopY">Ekranın üst sınırı (world Y)</param>
    public void Initialize(IObjectPool<Bullet> pool, float screenTopY)
    {
        _ownerPool  = pool;
        _screenTopY = screenTopY;
    }

    /// <summary>
    /// Merminin hızını runtime'da değiştirmek için.
    /// Upgrade sistemi PlayerShooting üzerinden bu metodu çağırabilir.
    /// </summary>
    public void SetSpeed(float newSpeed)
    {
        _speed = newSpeed;
    }

    /// <summary>
    /// Mermi bir düşmana isabet ettiğinde dışarıdan çağrılır.
    /// (Collision/Trigger handler veya raycast sistemi tarafından)
    /// Merminin kendisini pool'a iade etmesini tetikler.
    /// </summary>
    public void OnHitTarget()
    {
        ReturnToPool();
    }

    // ════════════════════════════════════════════════════════════════
    //  POOL İADE
    // ════════════════════════════════════════════════════════════════

    /// <summary>
    /// Mermiyi pool'a iade eder.
    /// 
    /// Güvenlik kontrolleri:
    /// 1. Pool null kontrolü: Pool referansı set edilmemişse fallback olarak deactivate et.
    /// 2. GameObject aktiflik kontrolü: Zaten pool'a iade edilmiş (deaktif) bir mermi
    ///    tekrar iade edilmeye çalışılabilir (çarpışma + ekran dışı aynı frame'de).
    ///    Bu durumda double-release hatası oluşur. activeSelf kontrolü bunu önler.
    /// </summary>
    private void ReturnToPool()
    {
        // Zaten deaktifse (pool'a iade edilmişse) tekrar iade etme
        // Bu, aynı frame'de hem OnHitTarget hem de ekran-dışı tetiklenmesi durumunu önler
        if (!gameObject.activeSelf)
            return;

        if (_ownerPool != null)
        {
            // Pool'un Release callback'i gameObject.SetActive(false) çağıracak
            _ownerPool.Release(this);
        }
        else
        {
            // Pool referansı yoksa güvenli fallback — objeyi sadece deaktif et
            // Bu durum normal akışta olmamalı, ama defensif programlama
            gameObject.SetActive(false);

            #if UNITY_EDITOR || DEVELOPMENT_BUILD
            Debug.LogWarning("[Bullet] Pool referansı null — doğrudan deaktif edildi. " +
                             "Bu mermi Initialize() çağrılmadan mı kullanıldı?", this);
            #endif
        }
    }
}
