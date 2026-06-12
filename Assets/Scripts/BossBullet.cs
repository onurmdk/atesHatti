using UnityEngine;

[RequireComponent(typeof(Collider2D))]
public class BossBullet : MonoBehaviour
{
    private Vector3 _direction;
    private float   _speed;
    private float   _damage;
    private float   _lifetime;

    private Transform _cachedTransform;

    private const float MAX_LIFETIME = 6f;

    private void Awake()
    {
        _cachedTransform = transform;
    }

    private void Update()
    {
        _cachedTransform.position += _direction * _speed * Time.deltaTime;

        _lifetime -= Time.deltaTime;
        if (_lifetime <= 0f)
        {
            Destroy(gameObject);
        }
    }

    private void OnTriggerEnter2D(Collider2D other)
    {
        if (!other.CompareTag("Player")) return;

        PlayerHealth playerHealth = other.GetComponent<PlayerHealth>();
        if (playerHealth != null)
        {
            playerHealth.TakeDamage(_damage);
        }

        if (ParticleManager.Instance != null)
        {
            ParticleManager.Instance.PlaySpark(_cachedTransform.position, Color.red);
        }

        Destroy(gameObject);
    }

    public void Initialize(Vector3 direction, float speed, float damage)
    {
        _direction = direction.normalized;
        _speed     = speed;
        _damage    = damage;
        _lifetime  = MAX_LIFETIME;
    }
}