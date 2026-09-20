using UnityEngine;
using System;

public class CombatManager : MonoBehaviour
{
    public static CombatManager Instance { get; private set; }

    [Header("─── Player Reference ───")]
    [Tooltip("The Player object placed in the scene.\n" +
             "Required to access the PlayerHealth component.")]
    [SerializeField]
    private PlayerHealth _playerHealth;

    [Header("─── Damage Settings ───")]
    [Tooltip("Damage the enemy deals to the player on body collision.")]
    [SerializeField]
    private float _enemyBodyDamage = 1f;

    [Tooltip("Base damage of a player bullet.\n" +
             "Will be read from PlayerShooting in the future (upgrade system).")]
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
    private void HandleBulletHitsEnemy(GameObject bulletObj, Collider2D targetCollider)
    {
        Bullet      bullet = bulletObj.GetComponent<Bullet>();
        IDamageable target = targetCollider.GetComponent<IDamageable>();

        if (bullet == null || target == null) return;
        if (!target.IsAlive) return;

        // Read everything we need BEFORE applying damage: a dying target is
        // either released back to its pool or destroyed inside TakeDamage().
        Vector3 hitPos    = bulletObj.transform.position;
        Vector3 targetPos = targetCollider.transform.position;
        int     goldValue = target.GoldValue;

        SpriteRenderer targetRenderer = targetCollider.GetComponent<SpriteRenderer>();
        Color targetTint = targetRenderer != null ? targetRenderer.color : Color.red;

        bullet.OnHitTarget();

        bool killed = target.TakeDamage(_bulletBaseDamage);

        if (killed)
        {
            if (ParticleManager.Instance != null)
                ParticleManager.Instance.PlayExplosion(targetPos, targetTint);

            OnEnemyKilled?.Invoke(goldValue, targetPos);

            #if UNITY_EDITOR || DEVELOPMENT_BUILD
            Debug.Log($"[Combat] Target destroyed! +{goldValue} Gold");
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
                "[CombatManager] PlayerHealth reference not assigned! " +
                "Fill the _playerHealth field in the Inspector.", this);
    }
}