using UnityEngine;

/// <summary>
/// ╔══════════════════════════════════════════════════════════════════╗
///   ATEŞ HATTI — Boss (Progresif Boss Düşman)
///   
///   Her karşılaşmada güçlenen, oyuncuyu hedef alan spread ateş
///   yapan ve ping-pong hareket eden boss kontrolcüsü.
/// ╚══════════════════════════════════════════════════════════════════╝
/// 
/// Progresif Güçlenme (bossLevel bazlı):
/// ─────────────────────────────────────
///   HP:           35 + (level × 25)    → Lv0: 35, Lv1: 60, Lv2: 85
///   Gold Ödülü:   20 + (level × 10)    → Lv0: 20, Lv1: 30, Lv2: 40
///   Mermi Hasarı: 2 + (level / 2)      → Lv0: 2,  Lv2: 3,  Lv4: 4
///   Mermi Hızı:   5 + (level × 0.5)    → Lv0: 5,  Lv1: 5.5, Lv2: 6
/// 
/// Hareket Fazları:
/// ────────────────
///   Faz 1 (Giriş):  Ekranın üstünden stopY'ye kadar dikey iniş
///   Faz 2 (Savaş):  stopY'de sabit, sağa-sola ping-pong
///                    + 1.5 saniyede bir targeted spread ateş
/// 
/// Targeted Spread Saldırı:
/// ────────────────────────
///   1. Oyuncuya doğru yön vektörü hesapla
///   2. 3 mermi üret: Merkez (direkt), Sol (-20°), Sağ (+20°)
///   3. Oyuncu köşede bile olsa hedeflenir — kaçış yok
/// </summary>
[RequireComponent(typeof(SpriteRenderer))]
public class Boss : MonoBehaviour
{
    // ════════════════════════════════════════════════════════════════
    //  BASE STAT'LAR (Inspector'dan ince ayar yapılabilir)
    // ════════════════════════════════════════════════════════════════

    [Header("─── Temel İstatistikler ───")]
    [SerializeField] private float _baseHp          = 35f;
    [SerializeField] private int   _baseGold        = 20;
    [SerializeField] private int   _baseDamage      = 2;
    [SerializeField] private float _baseBulletSpeed  = 5f;

    [Header("─── Hareket ───")]
    [Tooltip("Giriş fazında aşağı iniş hızı.")]
    [SerializeField] private float _entrySpeed       = 2f;
    [Tooltip("Savaş fazında yatay ping-pong hızı.")]
    [SerializeField] private float _horizontalSpeed  = 2.5f;

    [Header("─── Saldırı ───")]
    [Tooltip("İki ateş arası süre (saniye).")]
    [SerializeField] private float _fireInterval     = 1.5f;
    [Tooltip("Spread açısı (derece). Yan mermiler bu kadar sapacak.")]
    [SerializeField] private float _spreadAngle      = 20f;

    [Header("─── Mermi Prefab ───")]
    [SerializeField] private BossBullet _bulletPrefab;

    // ════════════════════════════════════════════════════════════════
    //  RUNTIME STATE
    // ════════════════════════════════════════════════════════════════

    private float _maxHp;
    private float _currentHp;
    private int   _goldReward;
    private int   _bulletDamage;
    private float _bulletSpeed;

    // Hareket
    private float _stopY;
    private float _screenMinX, _screenMaxX;
    private int   _moveDirection = 1; // 1 = sağa, -1 = sola
    private bool  _hasEnteredArena;

    // Saldırı
    private float     _fireTimer;
    private Transform _targetPlayer;

    // Cache
    private Transform      _cachedTransform;
    private SpriteRenderer _cachedSpriteRenderer;
    private float          _spriteHalfWidth;

    // ════════════════════════════════════════════════════════════════
    //  UNITY LIFECYCLE
    // ════════════════════════════════════════════════════════════════

    private void Awake()
    {
        _cachedTransform      = transform;
        _cachedSpriteRenderer = GetComponent<SpriteRenderer>();

        // Sprite yarı genişliği (ping-pong sınırları için)
        if (_cachedSpriteRenderer != null && _cachedSpriteRenderer.sprite != null)
        {
            _spriteHalfWidth = _cachedSpriteRenderer.sprite.bounds.extents.x
                             * Mathf.Abs(_cachedTransform.localScale.x);
        }
        else
        {
            _spriteHalfWidth = 1f;
        }
    }

    private void Update()
    {
        if (!_hasEnteredArena)
        {
            HandleEntryPhase();
        }
        else
        {
            HandleCombatPhase();
        }
    }

    // ════════════════════════════════════════════════════════════════
    //  KONFİGÜRASYON — EnemySpawner Tarafından Çağrılır
    // ════════════════════════════════════════════════════════════════

    /// <summary>
    /// Boss'u seviyeye göre konfigüre eder ve başlangıç pozisyonunu set eder.
    /// </summary>
    /// <param name="bossLevel">Kaçıncı boss (0, 1, 2...)</param>
    /// <param name="screenMinX">Ekran sol sınırı (world)</param>
    /// <param name="screenMaxX">Ekran sağ sınırı (world)</param>
    /// <param name="stopY">Giriş fazında duracağı Y koordinatı</param>
    /// <param name="targetPlayer">Hedeflenecek oyuncu Transform'u</param>
    public void Configure(int bossLevel, float screenMinX, float screenMaxX,
                           float stopY, Transform targetPlayer)
    {
        // ── Progresif stat hesaplama ──
        _maxHp        = _baseHp + (bossLevel * 25f);
        _currentHp    = _maxHp;
        _goldReward   = _baseGold + (bossLevel * 10);
        _bulletDamage = _baseDamage + Mathf.FloorToInt(bossLevel / 2f);
        _bulletSpeed  = _baseBulletSpeed + (bossLevel * 0.5f);

        // ── Sınırlar ve hedef ──
        _screenMinX   = screenMinX;
        _screenMaxX   = screenMaxX;
        _stopY        = stopY;
        _targetPlayer = targetPlayer;

        // ── State reset ──
        _hasEnteredArena = false;
        _fireTimer       = _fireInterval;
        _moveDirection   = 1;

        // ── Başlangıç pozisyonu: Ekranın üstünde ──
        float startY = stopY + 8f; // Ekranın çok üstünden gelsin
        _cachedTransform.position = new Vector3(0f, startY, 0f);

        #if UNITY_EDITOR || DEVELOPMENT_BUILD
        Debug.Log($"[Boss] Configure → Level {bossLevel} | HP: {_maxHp} | " +
                  $"Gold: {_goldReward} | BulletDmg: {_bulletDamage} | " +
                  $"BulletSpd: {_bulletSpeed:F1}");
        #endif
    }

    // ════════════════════════════════════════════════════════════════
    //  FAZ 1: GİRİŞ — Dikey İniş
    // ════════════════════════════════════════════════════════════════

    /// <summary>
    /// Boss ekranın dışından stopY noktasına kadar yavaşça iner.
    /// stopY'ye ulaşınca savaş fazına geçer.
    /// </summary>
    private void HandleEntryPhase()
    {
        Vector3 pos = _cachedTransform.position;
        pos.y -= _entrySpeed * Time.deltaTime;

        if (pos.y <= _stopY)
        {
            pos.y = _stopY;
            _hasEnteredArena = true;
        }

        _cachedTransform.position = pos;
    }

    // ════════════════════════════════════════════════════════════════
    //  FAZ 2: SAVAŞ — Ping-Pong + Targeted Spread
    // ════════════════════════════════════════════════════════════════

    /// <summary>
    /// Yatay ping-pong hareket + periyodik targeted spread ateş.
    /// </summary>
    private void HandleCombatPhase()
    {
        float dt = Time.deltaTime;

        // ── Yatay hareket (ping-pong) ──
        Vector3 pos = _cachedTransform.position;
        pos.x += _horizontalSpeed * _moveDirection * dt;

        float leftBound  = _screenMinX + _spriteHalfWidth;
        float rightBound = _screenMaxX - _spriteHalfWidth;

        if (pos.x <= leftBound)
        {
            pos.x = leftBound;
            _moveDirection = 1;
        }
        else if (pos.x >= rightBound)
        {
            pos.x = rightBound;
            _moveDirection = -1;
        }

        _cachedTransform.position = pos;

        // ── Ateş timer'ı ──
        _fireTimer -= dt;
        if (_fireTimer <= 0f)
        {
            _fireTimer = _fireInterval;
            FireSpread();
        }
    }

    // ════════════════════════════════════════════════════════════════
    //  SALDIRI — Targeted Spread (3 Mermi)
    // ════════════════════════════════════════════════════════════════

    /// <summary>
    /// Oyuncuya doğru 3 mermi ateşler:
    ///   1. Merkez: Direkt oyuncuya
    ///   2. Sol:    -20° sapma
    ///   3. Sağ:    +20° sapma
    /// 
    /// Quaternion.Euler(0, 0, angle) ile 2D düzlemde yön döndürülür.
    /// Oyuncu köşede bile olsa en az 1 mermi onu hedefler.
    /// </summary>
    private void FireSpread()
    {
        if (_bulletPrefab == null) return;
        if (_targetPlayer == null) return;

        Vector3 bossPos   = _cachedTransform.position;
        Vector3 playerPos = _targetPlayer.position;

        // ── Oyuncuya doğru yön vektörü ──
        Vector3 dirToPlayer = (playerPos - bossPos).normalized;

        // ── Ateş noktası (Boss'un alt kenarı) ──
        Vector3 firePoint = bossPos;
        firePoint.y -= _spriteHalfWidth; // Alt kenardan ateş

        // ── 3 mermi: Merkez, Sol (-angle), Sağ (+angle) ──
        SpawnBullet(firePoint, dirToPlayer);
        SpawnBullet(firePoint, RotateDirection(dirToPlayer, -_spreadAngle));
        SpawnBullet(firePoint, RotateDirection(dirToPlayer,  _spreadAngle));
    }

    /// <summary>
    /// Tek bir BossBullet oluşturur ve başlatır.
    /// </summary>
    private void SpawnBullet(Vector3 position, Vector3 direction)
    {
        BossBullet bullet = Instantiate(_bulletPrefab, position, Quaternion.identity);
        bullet.Initialize(direction, _bulletSpeed, _bulletDamage);
    }

    /// <summary>
    /// 2D düzlemde yön vektörünü belirtilen derece kadar döndürür.
    /// Quaternion.Euler(0, 0, angle) Z ekseni etrafında döndürme yapar.
    /// </summary>
    private Vector3 RotateDirection(Vector3 direction, float angleDegrees)
    {
        return Quaternion.Euler(0f, 0f, angleDegrees) * direction;
    }

    // ════════════════════════════════════════════════════════════════
    //  HASAR ALMA
    // ════════════════════════════════════════════════════════════════

    /// <summary>
    /// Boss'a hasar verir.
    /// CombatManager veya doğrudan Bullet tarafından çağrılır.
    /// 
    /// Dönüş: true = boss öldü, false = hâlâ hayatta.
    /// </summary>
    public bool TakeDamage(float damage)
    {
        _currentHp -= damage;

        if (_currentHp <= 0f)
        {
            _currentHp = 0f;
            Die();
            return true;
        }

        return false;
    }

    /// <summary>
    /// Boss ölüm işlemleri.
    /// Patlama efekti, altın verme, CombatManager event, kendini yok etme.
    /// </summary>
    private void Die()
    {
        Vector3 deathPos = _cachedTransform.position;

        // ── Büyük patlama efekti ──
        if (ParticleManager.Instance != null)
        {
            Color bossColor = _cachedSpriteRenderer != null
                ? _cachedSpriteRenderer.color
                : Color.magenta;

            ParticleManager.Instance.PlayExplosion(deathPos, bossColor);
        }

        // ── Altın ver (CombatManager event'i üzerinden) ──
        if (CombatManager.Instance != null)
        {
            // CombatManager.OnEnemyKilled event'ini doğrudan tetikleyemeyiz
            // (private event invoke). Bunun yerine GoldManager'a doğrudan ekliyoruz.
            if (GoldManager.Instance != null)
            {
                GoldManager.Instance.AddGold(_goldReward);
            }
        }

        #if UNITY_EDITOR || DEVELOPMENT_BUILD
        Debug.Log($"[Boss] Öldü! Gold: +{_goldReward}");
        #endif

        // ── Kendini yok et ──
        Destroy(gameObject);
    }

    // ════════════════════════════════════════════════════════════════
    //  PUBLIC API
    // ════════════════════════════════════════════════════════════════

    public float CurrentHp  => _currentHp;
    public float MaxHp      => _maxHp;
    public int   GoldReward => _goldReward;
    public bool  IsAlive    => _currentHp > 0f;
}
