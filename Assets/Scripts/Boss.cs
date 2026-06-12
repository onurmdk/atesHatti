using UnityEngine;

[RequireComponent(typeof(SpriteRenderer))]
public class Boss : MonoBehaviour
{
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

    private float _maxHp;
    private float _currentHp;
    private int   _goldReward;
    private int   _bulletDamage;
    private float _bulletSpeed;

    private float _stopY;
    private float _screenMinX, _screenMaxX;
    private int   _moveDirection = 1;
    private bool  _hasEnteredArena;

    private float     _fireTimer;
    private Transform _targetPlayer;

    private Transform      _cachedTransform;
    private SpriteRenderer _cachedSpriteRenderer;
    private float          _spriteHalfWidth;

    private void Awake()
    {
        _cachedTransform      = transform;
        _cachedSpriteRenderer = GetComponent<SpriteRenderer>();

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

    public void Configure(int bossLevel, float screenMinX, float screenMaxX,
                           float stopY, Transform targetPlayer)
    {
        _maxHp        = _baseHp + (bossLevel * 25f);
        _currentHp    = _maxHp;
        _goldReward   = _baseGold + (bossLevel * 10);
        _bulletDamage = _baseDamage + Mathf.FloorToInt(bossLevel / 2f);
        _bulletSpeed  = _baseBulletSpeed + (bossLevel * 0.5f);

        _screenMinX   = screenMinX;
        _screenMaxX   = screenMaxX;
        _stopY        = stopY;
        _targetPlayer = targetPlayer;

        _hasEnteredArena = false;
        _fireTimer       = _fireInterval;
        _moveDirection   = 1;

        float startY = stopY + 8f;
        _cachedTransform.position = new Vector3(0f, startY, 0f);

        #if UNITY_EDITOR || DEVELOPMENT_BUILD
        Debug.Log($"[Boss] Configure → Level {bossLevel} | HP: {_maxHp} | " +
                  $"Gold: {_goldReward} | BulletDmg: {_bulletDamage} | " +
                  $"BulletSpd: {_bulletSpeed:F1}");
        #endif
    }

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

    private void HandleCombatPhase()
    {
        float dt = Time.deltaTime;

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

        _fireTimer -= dt;
        if (_fireTimer <= 0f)
        {
            _fireTimer = _fireInterval;
            FireSpread();
        }
    }

    private void FireSpread()
    {
        if (_bulletPrefab == null) return;
        if (_targetPlayer == null) return;

        Vector3 bossPos   = _cachedTransform.position;
        Vector3 playerPos = _targetPlayer.position;

        Vector3 dirToPlayer = (playerPos - bossPos).normalized;

        Vector3 firePoint = bossPos;
        firePoint.y -= _spriteHalfWidth;

        SpawnBullet(firePoint, dirToPlayer);
        SpawnBullet(firePoint, RotateDirection(dirToPlayer, -_spreadAngle));
        SpawnBullet(firePoint, RotateDirection(dirToPlayer,  _spreadAngle));
    }

    private void SpawnBullet(Vector3 position, Vector3 direction)
    {
        BossBullet bullet = Instantiate(_bulletPrefab, position, Quaternion.identity);
        bullet.Initialize(direction, _bulletSpeed, _bulletDamage);
    }

    private Vector3 RotateDirection(Vector3 direction, float angleDegrees)
    {
        return Quaternion.Euler(0f, 0f, angleDegrees) * direction;
    }

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

    private void Die()
    {
        Vector3 deathPos = _cachedTransform.position;

        if (ParticleManager.Instance != null)
        {
            Color bossColor = _cachedSpriteRenderer != null
                ? _cachedSpriteRenderer.color
                : Color.magenta;

            ParticleManager.Instance.PlayExplosion(deathPos, bossColor);
        }

        if (CombatManager.Instance != null)
        {
            if (GoldManager.Instance != null)
            {
                GoldManager.Instance.AddGold(_goldReward);
            }
        }

        #if UNITY_EDITOR || DEVELOPMENT_BUILD
        Debug.Log($"[Boss] Öldü! Gold: +{_goldReward}");
        #endif

        Destroy(gameObject);
    }

    public float CurrentHp  => _currentHp;
    public float MaxHp      => _maxHp;
    public int   GoldReward => _goldReward;
    public bool  IsAlive    => _currentHp > 0f;
}