using UnityEngine;

/// <summary>
/// ╔══════════════════════════════════════════════════════════════════╗
///   ATEŞ HATTI — BossBullet (Boss Mermisi)
///   
///   Boss tarafından oluşturulan, verilen yöne doğru hareket eden
///   ve oyuncuya çarpınca hasar veren basit mermi.
/// ╚══════════════════════════════════════════════════════════════════╝
/// 
/// Neden Object Pool değil de Destroy?
/// → Boss saniyede ~2 ateş eder × 3 mermi = 6 mermi/saniye.
///   Boss fight ~20-40 saniye sürer → toplam ~120-240 mermi.
///   Bu ölçekte Instantiate/Destroy kabul edilebilir.
///   Pool eklemek boss-specific complexity yaratır (ayrı pool yönetimi).
///   İleride profiling'de sorun görülürse pool'a geçilebilir.
/// </summary>
[RequireComponent(typeof(Collider2D))]
public class BossBullet : MonoBehaviour
{
    // ════════════════════════════════════════════════════════════════
    //  STATE
    // ════════════════════════════════════════════════════════════════

    private Vector3 _direction;
    private float   _speed;
    private float   _damage;
    private float   _lifetime;

    private Transform _cachedTransform;

    /// <summary>Ekran dışına çıkınca yok edilmeden önce maksimum yaşam süresi.</summary>
    private const float MAX_LIFETIME = 6f;

    // ════════════════════════════════════════════════════════════════
    //  UNITY LIFECYCLE
    // ════════════════════════════════════════════════════════════════

    private void Awake()
    {
        _cachedTransform = transform;
    }

    private void Update()
    {
        // ── Hareket ──
        _cachedTransform.position += _direction * _speed * Time.deltaTime;

        // ── Yaşam süresi kontrolü ──
        // Ekran dışına çıkan mermiler sonsuza kadar yaşamasın
        _lifetime -= Time.deltaTime;
        if (_lifetime <= 0f)
        {
            Destroy(gameObject);
        }
    }

    /// <summary>
    /// Oyuncu ile çarpışma. CombatRelay kullanılmaz —
    /// BossBullet kendi trigger'ını yönetir (basit, izole sistem).
    /// </summary>
    private void OnTriggerEnter2D(Collider2D other)
    {
        if (!other.CompareTag("Player")) return;

        // ── Oyuncuya hasar ver ──
        PlayerHealth playerHealth = other.GetComponent<PlayerHealth>();
        if (playerHealth != null)
        {
            playerHealth.TakeDamage(_damage);
        }

        // ── Patlama efekti (opsiyonel) ──
        if (ParticleManager.Instance != null)
        {
            ParticleManager.Instance.PlaySpark(_cachedTransform.position, Color.red);
        }

        // ── Kendini yok et ──
        Destroy(gameObject);
    }

    // ════════════════════════════════════════════════════════════════
    //  PUBLIC API — Boss Tarafından Çağrılır
    // ════════════════════════════════════════════════════════════════

    /// <summary>
    /// Mermiyi başlatır. Boss.FireSpread() tarafından çağrılır.
    /// </summary>
    /// <param name="direction">Normalize edilmiş hareket yönü</param>
    /// <param name="speed">Mermi hızı (world units/s)</param>
    /// <param name="damage">Oyuncuya verilecek hasar</param>
    public void Initialize(Vector3 direction, float speed, float damage)
    {
        _direction = direction.normalized;
        _speed     = speed;
        _damage    = damage;
        _lifetime  = MAX_LIFETIME;
    }
}
