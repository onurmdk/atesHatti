using UnityEngine;
using UnityEngine.Pool;

public class Bullet : MonoBehaviour
{
    [Header("─── Mermi Ayarları ───")]
    [Tooltip("Merminin yukarı doğru hareket hızı (world units/saniye).\n" +
             "Çok düşük = oyuncu merminin hedefe ulaşmasını bekler, tempo düşer.\n" +
             "Çok yüksek = mermi görünmeden kaybolur, tatmin hissi azalır.\n" +
             "15-20 arası 2D shooter'lar için ideal aralık.")]
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
            Debug.LogWarning("[Bullet] Pool referansı null — doğrudan deaktif edildi. " +
                             "Bu mermi Initialize() çağrılmadan mı kullanıldı?", this);
            #endif
        }
    }
}