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
/// Multi-Prefab Mimari:
/// ────────────────────
/// Her tier (Weak, Medium, Strong) ayrı bir prefab olarak tasarlanır.
/// Stat'lar (HP, speed, drift, gold) Inspector'dan set edilir.
/// Sprite, scale, color, animator — hepsi prefab'ın kendi asset'i.
/// Kod hiçbir görsel değişiklik yapmaz — tamamen veri odaklı.
/// 
/// EnemySpawner, CDF ile tier index seçer → ilgili pool'dan Get → Configure → SetActive.
/// 
/// Performans:
/// ───────────
/// • Update'te SIFIR GC allocation
/// • Transform cache'li
/// • Ekran sınırları dışarıdan set edilir
/// • Drift bounce: Basit float karşılaştırma
/// </summary>
[RequireComponent(typeof(SpriteRenderer))]
public class Enemy : MonoBehaviour
{
    // ════════════════════════════════════════════════════════════════
    //  DÜŞMAN STATLARI (Inspector'dan Set Edilir)
    // ════════════════════════════════════════════════════════════════

    [Header("─── Düşman Stat'ları ───")]
    [Tooltip("Düşmanın maksimum can puanı.\n" +
             "Her spawn'da bu değere resetlenir.")]
    [SerializeField]
    private float _maxHp = 1f;

    [Tooltip("Aşağı doğru hareket hızı (world units/saniye).\n" +
             "Weak: 4, Medium: 2.8, Strong: 1.8 önerilir.")]
    [SerializeField]
    private float _speed = 3f;

    [Tooltip("Yanal kayma hızı (mutlak değer, yön runtime'da atanır).\n" +
             "Weak: 1.5, Medium: 1.0, Strong: 0.6 önerilir.")]
    [SerializeField]
    private float _driftSpeed = 1f;

    [Tooltip("Öldürüldüğünde düşecek altın miktarı.")]
    [SerializeField]
    private int _goldValue = 1;

    // ════════════════════════════════════════════════════════════════
    //  RUNTIME STATE
    // ════════════════════════════════════════════════════════════════

    private float _currentHp;

    /// <summary>
    /// Runtime drift hızı (yönlü). Inspector'daki _driftSpeed mutlak değerdir.
    /// Configure()'da spawn pozisyonuna göre yön atanır (pozitif = sağa).
    /// </summary>
    private float _currentDriftSpeed;

    // ════════════════════════════════════════════════════════════════
    //  POOL VE SINIR REFERANSLARI
    // ════════════════════════════════════════════════════════════════

    private IObjectPool<Enemy> _ownerPool;

    private float _screenMinX;
    private float _screenMaxX;
    private float _screenBottomY;

    // ════════════════════════════════════════════════════════════════
    //  CACHE
    // ════════════════════════════════════════════════════════════════

    private Transform      _cachedTransform;
    private SpriteRenderer _cachedSpriteRenderer;

    /// <summary>
    /// Sprite'ın world-space yarı genişliği.
    /// Awake'te hesaplanır — prefab'ın kendi scale'i kullanılır,
    /// kodla scale değiştirilmediği için bir kez hesaplamak yeterli.
    /// </summary>
    private float _spriteHalfWidth;

    private const float OFF_SCREEN_MARGIN = 1.0f;

    // ════════════════════════════════════════════════════════════════
    //  UNITY LIFECYCLE
    // ════════════════════════════════════════════════════════════════

    private void Awake()
    {
        _cachedTransform      = transform;
        _cachedSpriteRenderer = GetComponent<SpriteRenderer>();

        // ── Sprite yarı genişliğini hesapla (bir kez, Awake'te) ──
        // Artık scale kodla değişmediği için Awake'te hesaplamak yeterli.
        // Prefab'ın kendi localScale'i dikkate alınır.
        if (_cachedSpriteRenderer != null && _cachedSpriteRenderer.sprite != null)
        {
            float localExtentX = _cachedSpriteRenderer.sprite.bounds.extents.x;
            _spriteHalfWidth = localExtentX * Mathf.Abs(_cachedTransform.localScale.x);
        }
        else
        {
            _spriteHalfWidth = 0.5f;
        }
    }

    /// <summary>
    /// Her frame: Aşağı hareket + yanal drift + sınır kontrolü.
    /// GC allocation: SIFIR.
    /// </summary>
    private void Update()
    {
        float dt = Time.deltaTime;

        Vector3 pos = _cachedTransform.position;

        // ── Aşağı hareket ──
        pos.y -= _speed * dt;

        // ── Yanal drift ──
        pos.x += _currentDriftSpeed * dt;

        // ── Drift bounce ──
        float leftBound  = _screenMinX + _spriteHalfWidth;
        float rightBound = _screenMaxX - _spriteHalfWidth;

        if (pos.x < leftBound)
        {
            pos.x = leftBound;
            _currentDriftSpeed = Mathf.Abs(_currentDriftSpeed);
        }
        else if (pos.x > rightBound)
        {
            pos.x = rightBound;
            _currentDriftSpeed = -Mathf.Abs(_currentDriftSpeed);
        }

        _cachedTransform.position = pos;

        // ── Ekran altı kontrolü ──
        if (pos.y < _screenBottomY - OFF_SCREEN_MARGIN)
        {
            ReturnToPool();
        }
    }

    // ════════════════════════════════════════════════════════════════
    //  KONFİGÜRASYON — EnemySpawner Tarafından Çağrılır
    // ════════════════════════════════════════════════════════════════

    /// <summary>
    /// Pool'dan her alınışta çağrılır. Düşmanı spawn pozisyonuna
    /// yerleştirir ve runtime state'ini başlatır.
    /// 
    /// Stat'lar (HP, speed, drift, gold) Inspector'dan gelir —
    /// burada set edilmez. Sadece runtime state resetlenir.
    /// 
    /// Eski tier parametresi kaldırıldı: Her prefab kendi stat'larını
    /// Inspector'da taşıyor, Configure'a stat geçmeye gerek yok.
    /// </summary>
    public void Configure(
        IObjectPool<Enemy> pool,
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

        // ── HP'yi yenile (her spawn'da tam can) ──
        _currentHp = _maxHp;

        // ── Drift yönünü spawn pozisyonuna göre ata ──
        // Sol yarıda → sağa drift (pozitif)
        // Sağ yarıda → sola drift (negatif)
        // Ortada → rastgele
        float midX = (screenMinX + screenMaxX) * 0.5f;
        float signBias = spawnPos.x < midX - 0.5f ? 1f
                       : spawnPos.x > midX + 0.5f ? -1f
                       : (Random.value > 0.5f ? 1f : -1f);

        _currentDriftSpeed = _driftSpeed * signBias;

        // ── Pozisyonu set et ──
        _cachedTransform.position = spawnPos;
    }

    // ════════════════════════════════════════════════════════════════
    //  POOL İADE
    // ════════════════════════════════════════════════════════════════

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

    public bool TakeDamage(float damage)
    {
        _currentHp -= damage;

        if (_currentHp <= 0f)
        {
            _currentHp = 0f;
            ReturnToPool();
            return true;
        }

        return false;
    }

    public float CurrentHp => _currentHp;
    public float MaxHp     => _maxHp;
    public int   GoldValue => _goldValue;
    public bool  IsAlive   => _currentHp > 0f;
}
