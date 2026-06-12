using UnityEngine;
using UnityEngine.Pool;

[RequireComponent(typeof(SpriteRenderer))]
public class Enemy : MonoBehaviour
{
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

    private float _currentHp;

    private float _currentDriftSpeed;

    private IObjectPool<Enemy> _ownerPool;

    private float _screenMinX;
    private float _screenMaxX;
    private float _screenBottomY;

    private Transform      _cachedTransform;
    private SpriteRenderer _cachedSpriteRenderer;

    private float _spriteHalfWidth;

    private const float OFF_SCREEN_MARGIN = 1.0f;

    private void Awake()
    {
        _cachedTransform      = transform;
        _cachedSpriteRenderer = GetComponent<SpriteRenderer>();

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

    private void Update()
    {
        float dt = Time.deltaTime;

        Vector3 pos = _cachedTransform.position;

        pos.y -= _speed * dt;

        pos.x += _currentDriftSpeed * dt;

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

        if (pos.y < _screenBottomY - OFF_SCREEN_MARGIN)
        {
            ReturnToPool();
        }
    }

    public void Configure(
        IObjectPool<Enemy> pool,
        Vector3             spawnPos,
        float               screenMinX,
        float               screenMaxX,
        float               screenBottomY)
    {
        _ownerPool     = pool;
        _screenMinX    = screenMinX;
        _screenMaxX    = screenMaxX;
        _screenBottomY = screenBottomY;

        _currentHp = _maxHp;

        float midX = (screenMinX + screenMaxX) * 0.5f;
        float signBias = spawnPos.x < midX - 0.5f ? 1f
                       : spawnPos.x > midX + 0.5f ? -1f
                       : (Random.value > 0.5f ? 1f : -1f);

        _currentDriftSpeed = _driftSpeed * signBias;

        _cachedTransform.position = spawnPos;
    }

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