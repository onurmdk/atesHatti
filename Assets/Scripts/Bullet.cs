using UnityEngine;
using UnityEngine.Pool;

public class Bullet : MonoBehaviour
{
    [Header("─── Bullet Settings ───")]
    [Tooltip("Bullet upward movement speed (world units/second).\n" +
             "Too low = the player waits for the bullet to reach its target, pacing suffers.\n" +
             "Too high = the bullet disappears before it is seen, satisfaction is reduced.\n" +
             "15-20 is the ideal range for 2D shooters.")]
    [SerializeField, Range(5f, 40f)]
    private float _speed = 18f;

    private IObjectPool<Bullet> _ownerPool;

    private float _screenTopY;

    private const float OFF_SCREEN_MARGIN = 0.5f;

    private Transform _cachedTransform;

    private void Awake()
    {
        _cachedTransform = transform;
    }

    private void Update()
    {
        Vector3 pos = _cachedTransform.position;
        pos.y += _speed * Time.deltaTime;
        _cachedTransform.position = pos;

        if (pos.y > _screenTopY + OFF_SCREEN_MARGIN)
        {
            ReturnToPool();
        }
    }

    public void Initialize(IObjectPool<Bullet> pool, float screenTopY)
    {
        _ownerPool  = pool;
        _screenTopY = screenTopY;
    }

    public void SetSpeed(float newSpeed)
    {
        _speed = newSpeed;
    }

    public void OnHitTarget()
    {
        ReturnToPool();
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
            Debug.LogWarning("[Bullet] Pool reference is null — deactivated directly. " +
                             "Was this bullet used without calling Initialize()?", this);
            #endif
        }
    }
}