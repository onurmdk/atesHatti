using UnityEngine;
using UnityEngine.Pool;

/// <summary>
/// ╔══════════════════════════════════════════════════════════════════╗
///   ATEŞ HATTI — Enemy (Düşman)
///   
///   Aşağı doğru hareket eden, ekran kenarlarından sekip yanal drift
///   yapan, ekran dışına çıkınca pool'a iade edilen düşman scripti.
/// ╚══════════════════════════════════════════════════════════════════╝
/// 
/// Sorumluluk (SRP): SADECE hareket + drift bounce + pool iade + stat tutma.
/// Çarpışma tespiti ve hasar alma ayrı bir Combat script'inde ele alınacak.
/// 
/// Tier Sistemi:
/// ─────────────
/// Bu script tek bir prefab için yazılmıştır. Tier bilgisi (Small/Medium/Large)
/// EnemySpawner tarafından pool'dan alınırken Configure() metodu ile set edilir.
/// Scale, color, HP, speed, gold — hepsi runtime'da atanır.
/// 
/// Performans:
/// ───────────
/// • Update'te SIFIR GC allocation
/// • Transform cache'li
/// • Ekran sınırları dışarıdan set edilir (her düşman ayrıca Camera'ya erişmez)
/// • Drift bounce: Basit float karşılaştırma, Physics2D kullanılmaz
/// </summary>
[RequireComponent(typeof(SpriteRenderer))]
public class Enemy : MonoBehaviour
{
    // ════════════════════════════════════════════════════════════════
    //  DÜŞMAN STATLARI
    // ════════════════════════════════════════════════════════════════

    /// <summary>
    /// Düşmanın mevcut stat değerleri.
    /// Configure() ile EnemySpawner tarafından set edilir.
    /// Public getter'lar üzerinden Combat sistemi tarafından okunur.
    /// 
    /// Neden ayrı struct değil de düz field'lar?
    /// → Struct kullanmak güzel bir abstraction olurdu ama
    ///   Combat sistemi her frame HP okuyacak. Struct field'ına
    ///   erişmek (enemy.Stats.currentHp) bir indirection daha ekler.
    ///   Düz field'lar daha doğrudan ve cache-friendly.
    /// </summary>
    private float _maxHp;
    private float _currentHp;
    private float _speed;          // Aşağı doğru hareket hızı (world units/s)
    private float _driftSpeed;     // Yanal kayma hızı (pozitif = sağa, negatif = sola)
    private int   _goldValue;      // Öldürüldüğünde düşecek altın

    // ════════════════════════════════════════════════════════════════
    //  POOL VE SINIR REFERANSLARI
    // ════════════════════════════════════════════════════════════════

    /// <summary>
    /// Bu düşmanın ait olduğu Object Pool referansı.
    /// Bullet.cs ile aynı pattern — düşman kendi kendini iade eder.
    /// </summary>
    private IObjectPool<Enemy> _ownerPool;

    /// <summary>
    /// Ekran sınırları (world coordinates).
    /// EnemySpawner tarafından Configure() içinde set edilir.
    /// 
    /// Neden her düşman kendi hesaplamıyor?
    /// → 30-40 düşman aynı anda aktif olabilir. Her biri Camera'ya
    ///   erişip sınır hesaplasaydı 40 × ViewportToWorldPoint = 40 method call.
    ///   Spawner bir kez hesaplayıp tüm düşmanlara dağıtması çok daha verimli.
    /// </summary>
    private float _screenMinX;
    private float _screenMaxX;
    private float _screenBottomY;  // Bu sınırın altına inince pool'a iade

    // ════════════════════════════════════════════════════════════════
    //  CACHE
    // ════════════════════════════════════════════════════════════════

    private Transform      _cachedTransform;
    private SpriteRenderer _cachedSpriteRenderer;

    /// <summary>
    /// Sprite'ın yarı genişliği (world units).
    /// Drift bounce hesabında ekran kenarından sprite'ın taşmaması için kullanılır.
    /// Configure() içinde scale değiştiğinde güncellenir.
    /// </summary>
    private float _spriteHalfWidth;

    /// <summary>
    /// Off-screen margin: Düşman ekranın ne kadar altına inince pool'a döner.
    /// Çok küçük = düşman ekran kenarında aniden kaybolur (görsel olarak kötü).
    /// 1.0 unit = düşman tamamen görünmez olduktan sonra iade edilir.
    /// </summary>
    private const float OFF_SCREEN_MARGIN = 1.0f;

    // ════════════════════════════════════════════════════════════════
    //  UNITY LIFECYCLE
    // ════════════════════════════════════════════════════════════════

    private void Awake()
    {
        _cachedTransform      = transform;
        _cachedSpriteRenderer = GetComponent<SpriteRenderer>();
    }

    /// <summary>
    /// Her frame: Aşağı hareket + yanal drift + sınır kontrolü.
    /// Toplam maliyet: 2 float toplama, 2 float karşılaştırma, 1 position set.
    /// GC allocation: SIFIR.
    /// </summary>
    private void Update()
    {
        float dt = Time.deltaTime;

        // ── Pozisyonu oku (bir kez — bridge call minimizasyonu) ──
        Vector3 pos = _cachedTransform.position;

        // ── Aşağı hareket ──
        pos.y -= _speed * dt;

        // ── Yanal drift ──
        pos.x += _driftSpeed * dt;

        // ── Drift bounce: Ekran kenarlarından sekme ──
        // Sprite yarı genişliğini hesaba katarak kenar tespiti yapılır.
        // Böylece düşmanın yarısı ekran dışına taşmaz.
        float leftBound  = _screenMinX + _spriteHalfWidth;
        float rightBound = _screenMaxX - _spriteHalfWidth;

        if (pos.x < leftBound)
        {
            pos.x = leftBound;
            _driftSpeed = Mathf.Abs(_driftSpeed);   // Sağa yönlendir
        }
        else if (pos.x > rightBound)
        {
            pos.x = rightBound;
            _driftSpeed = -Mathf.Abs(_driftSpeed);  // Sola yönlendir
        }

        // ── Pozisyonu uygula ──
        _cachedTransform.position = pos;

        // ── Ekran altı kontrolü ──
        // Düşman ekranın altından tamamen çıktıysa pool'a iade et
        if (pos.y < _screenBottomY - OFF_SCREEN_MARGIN)
        {
            ReturnToPool();
        }
    }

    // ════════════════════════════════════════════════════════════════
    //  KONFİGÜRASYON — EnemySpawner Tarafından Çağrılır
    // ════════════════════════════════════════════════════════════════

    /// <summary>
    /// Pool'dan her alınışta çağrılır. Düşmanı seçilen tier'a göre konfigüre eder.
    /// 
    /// Neden her şey tek metotta?
    /// → Pool'dan alınan bir düşman her seferinde farklı tier olabilir.
    ///   Tüm stat'ları, görünümü ve sınırları atomik olarak set etmek
    ///   yarı-konfigüre edilmiş obje riskini ortadan kaldırır.
    /// 
    /// Configure çağrılmadan düşman aktif edilmemelidir.
    /// Akış: pool.Get() → Configure() → SetActive(true) [pool callback]
    /// </summary>
    /// <param name="pool">Bu düşmanın ait olduğu pool</param>
    /// <param name="tier">Atanacak tier verileri</param>
    /// <param name="spawnPos">Spawn pozisyonu (world)</param>
    /// <param name="screenMinX">Ekran sol sınırı (world)</param>
    /// <param name="screenMaxX">Ekran sağ sınırı (world)</param>
    /// <param name="screenBottomY">Ekran alt sınırı (world)</param>
    public void Configure(
        IObjectPool<Enemy> pool,
        EnemyTierData      tier,
        Vector3             spawnPos,
        float               screenMinX,
        float               screenMaxX,
        float               screenBottomY)
    {
        // ── Pool ve sınır referansları ──
        _ownerPool     = pool;
        _screenMinX    = screenMinX;
        _screenMaxX    = screenMaxX;
        _screenBottomY = screenBottomY;

        // ── Stat'ları ata ──
        _maxHp     = tier.hp;
        _currentHp = tier.hp;
        _speed     = tier.speed;
        _goldValue = tier.goldValue;

        // ── Drift hızını rastgele ata ──
        // Mutlak değer tier'dan gelir, yön spawn pozisyonuna göre belirlenir
        // Sol yarıda spawn → sağa drift (pozitif)
        // Sağ yarıda spawn → sola drift (negatif)
        // Ortada spawn → rastgele yön
        float midX = (_screenMinX + _screenMaxX) * 0.5f;
        float signBias = spawnPos.x < midX - 0.5f ? 1f
                       : spawnPos.x > midX + 0.5f ? -1f
                       : (Random.value > 0.5f ? 1f : -1f);

        _driftSpeed = tier.driftSpeed * signBias;

        // ── Görünümü güncelle ──
        _cachedTransform.localScale = tier.scale;
        _cachedSpriteRenderer.color = tier.color;

        // ── Sprite yarı genişliğini yeniden hesapla ──
        // Scale değiştiği için extents de değişir
        if (_cachedSpriteRenderer.sprite != null)
        {
            float localExtentX = _cachedSpriteRenderer.sprite.bounds.extents.x;
            _spriteHalfWidth = localExtentX * Mathf.Abs(tier.scale.x);
        }
        else
        {
            _spriteHalfWidth = 0.5f * Mathf.Abs(tier.scale.x);
        }

        // ── Pozisyonu set et ──
        _cachedTransform.position = spawnPos;
    }

    // ════════════════════════════════════════════════════════════════
    //  POOL İADE
    // ════════════════════════════════════════════════════════════════

    /// <summary>
    /// Düşmanı pool'a iade eder.
    /// Bullet.cs ile aynı double-release koruması: activeSelf kontrolü.
    /// </summary>
    private void ReturnToPool()
    {
        if (!gameObject.activeSelf)
            return;

        if (_ownerPool != null)
        {
            _ownerPool.Release(this);
        }
        else
        {
            gameObject.SetActive(false);

            #if UNITY_EDITOR || DEVELOPMENT_BUILD
            Debug.LogWarning("[Enemy] Pool referansı null — doğrudan deaktif edildi.", this);
            #endif
        }
    }

    // ════════════════════════════════════════════════════════════════
    //  PUBLIC API — Combat Sistemi İçin
    // ════════════════════════════════════════════════════════════════

    /// <summary>
    /// Düşmana hasar verir. Combat/Collision sistemi tarafından çağrılacak.
    /// 
    /// Dönüş değeri: true = düşman öldü, false = hâlâ hayatta.
    /// Bu bilgi çağıran tarafta OnEnemyKilled event'ini tetiklemek,
    /// gold drop oluşturmak vb. için kullanılacak.
    /// </summary>
    public bool TakeDamage(float damage)
    {
        _currentHp -= damage;

        if (_currentHp <= 0f)
        {
            _currentHp = 0f;
            ReturnToPool();
            return true; // Öldü
        }

        return false; // Hayatta
    }

    /// <summary>Mevcut HP (UI veya HP bar için)</summary>
    public float CurrentHp => _currentHp;

    /// <summary>Maksimum HP (HP bar doluluk oranı için: current/max)</summary>
    public float MaxHp => _maxHp;

    /// <summary>Öldürüldüğünde düşecek altın miktarı</summary>
    public int GoldValue => _goldValue;

    /// <summary>Düşman hâlâ hayatta mı?</summary>
    public bool IsAlive => _currentHp > 0f;
}

// ════════════════════════════════════════════════════════════════════
//  TIER VERİ YAPISI
// ════════════════════════════════════════════════════════════════════

/// <summary>
/// Düşman tier konfigürasyonunu taşıyan yapı.
/// 
/// Neden struct ve class değil?
/// → Tier verileri değer tipidir — küçük, immutable, kısa ömürlü.
///   Struct olarak stack'te yaşar, GC'ye yük bindirmez.
///   EnemySpawner'da readonly array olarak tutulur.
/// 
/// Neden ScriptableObject değil (henüz)?
/// → Şu an 3 tier var ve değerler kod içinde tanımlı.
///   Prototip aşamasında bu yeterli. Production'da bu struct'ı
///   ScriptableObject'e taşımak tek bir refactor:
///     [CreateAssetMenu] public class EnemyTierSO : ScriptableObject { ... }
///   Struct field'ları 1:1 aynı kalır.
/// </summary>
[System.Serializable]
public struct EnemyTierData
{
    public string  name;       // Debug/log için ("Small", "Medium", "Large")
    public float   hp;
    public float   speed;      // Aşağı hareket hızı (world units/s)
    public float   driftSpeed; // Yanal kayma hızı (mutlak değer, yön runtime'da atanır)
    public int     goldValue;
    public Vector3 scale;
    public Color   color;

    public EnemyTierData(string name, float hp, float speed, float driftSpeed,
                         int goldValue, Vector3 scale, Color color)
    {
        this.name       = name;
        this.hp         = hp;
        this.speed      = speed;
        this.driftSpeed = driftSpeed;
        this.goldValue  = goldValue;
        this.scale      = scale;
        this.color      = color;
    }
}
