using UnityEngine;
using System;

public class CombatManager : MonoBehaviour
{
    public static CombatManager Instance { get; private set; }

    [Header("─── Oyuncu Referansı ───")]
    [Tooltip("Sahneye yerleştirilmiş Player objesi.\n" +
             "PlayerHealth bileşenine erişim için gerekli.")]
    [SerializeField]
    private PlayerHealth _playerHealth;

    [Header("─── Hasar Ayarları ───")]
    [Tooltip("Düşmanın oyuncuya body collision ile verdiği hasar.")]
    [SerializeField]
    private float _enemyBodyDamage = 1f;

    [Tooltip("Oyuncu mermisinin base hasarı.\n" +
             "İleride PlayerShooting'den okunacak (upgrade sistemi).")]
    [SerializeField]
    private float _bulletBaseDamage = 1f;

    public event Action<int, Vector3> OnEnemyKilled;

    public event Action<Vector3> OnEnemyHit;

    public event Action<Vector3> OnPlayerHit;

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;

        ValidateSetup();
    }

    private void OnDestroy()
    {
        if (Instance == this)
            Instance = null;
    }

    public void ProcessCollision(GameObject reporter, Collider2D other)
    {
        if (reporter.CompareTag("PlayerBullet") && other.CompareTag("Enemy"))
        {
            HandleBulletHitsEnemy(reporter, other);
            return;
        }

        if (reporter.CompareTag("Enemy") && other.CompareTag("Player"))
        {
            HandleEnemyHitsPlayer(reporter, other.gameObject);
            return;
        }

        if (reporter.CompareTag("Player") && other.CompareTag("Enemy"))
        {
            HandleEnemyHitsPlayer(other.gameObject, reporter);
            return;
        }
    }

    private void HandleBulletHitsEnemy(GameObject bulletObj, Collider2D enemyCollider)
    {
        Bullet bullet = bulletObj.GetComponent<Bullet>();
        Enemy  enemy  = enemyCollider.GetComponent<Enemy>();

        if (bullet == null || enemy == null) return;
        if (!enemy.IsAlive) return;

        bullet.OnHitTarget();

        Vector3 hitPos = bulletObj.transform.position;
        bool killed = enemy.TakeDamage(_bulletBaseDamage);

        if (killed)
        {
            Vector3 enemyPos = enemyCollider.transform.position;
            Color enemyColor = enemyCollider.GetComponent<SpriteRenderer>()?.color ?? Color.red;

            if (ParticleManager.Instance != null)
                ParticleManager.Instance.PlayExplosion(enemyPos, enemyColor);

            OnEnemyKilled?.Invoke(enemy.GoldValue, enemyPos);

            #if UNITY_EDITOR || DEVELOPMENT_BUILD
            Debug.Log($"[Combat] Düşman öldürüldü! +{enemy.GoldValue} Gold");
            #endif
        }
        else
        {
            if (ParticleManager.Instance != null)
                ParticleManager.Instance.PlaySpark(hitPos, Color.yellow);

            OnEnemyHit?.Invoke(hitPos);
        }
    }

    private void HandleEnemyHitsPlayer(GameObject enemyObj, GameObject playerObj)
    {
        if (_playerHealth == null || !_playerHealth.IsAlive) return;

        Enemy enemy = enemyObj.GetComponent<Enemy>();
        if (enemy == null || !enemy.IsAlive) return;

        bool damageApplied = _playerHealth.TakeDamage(_enemyBodyDamage);

        if (damageApplied)
        {
            Vector3 contactPos = enemyObj.transform.position;

            enemy.TakeDamage(float.MaxValue); 

            Color enemyColor = enemyObj.GetComponent<SpriteRenderer>()?.color ?? Color.red;
            if (ParticleManager.Instance != null)
                ParticleManager.Instance.PlayExplosion(contactPos, enemyColor);

            OnPlayerHit?.Invoke(contactPos);
        }
    }

    public void SetBulletDamage(float newDamage)
    {
        _bulletBaseDamage = Mathf.Max(0.1f, newDamage);
    }

    public float BulletDamage => _bulletBaseDamage;

    [System.Diagnostics.Conditional("UNITY_EDITOR"),
     System.Diagnostics.Conditional("DEVELOPMENT_BUILD")]
    private void ValidateSetup()
    {
        if (_playerHealth == null)
            Debug.LogError(
                "[CombatManager] PlayerHealth referansı atanmamış! " +
                "Inspector'da _playerHealth alanını doldurun.", this);
    }
}