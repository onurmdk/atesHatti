using UnityEngine;
using UnityEngine.Pool;

public class PlayerShooting : MonoBehaviour
{
    [Header("─── Ateş Ayarları ───")]
    [Tooltip("İki mermi arasındaki süre (saniye).\n" +
             "0.28 = HTML prototipindeki referans hız.\n" +
             "Upgrade sistemi bu değeri runtime'da düşürecek.")]
    [SerializeField, Range(0.05f, 1f)]
    private float _fireInterval = 0.28f;

    [Tooltip("Merminin geminin ne kadar üstünde spawn olacağı (world units).\n" +
             "Çok düşük = mermi geminin içinden çıkıyor gibi görünür.\n" +
             "Çok yüksek = mermi havada beliriyor gibi görünür.\n" +
             "0.5 = geminin burnundan çıkış hissi.")]
    [SerializeField, Range(0.1f, 1.5f)]
    private float _muzzleOffsetY = 0.5f;

    [Header("─── Mermi Prefab ───")]
    [Tooltip("Pool'un oluşturacağı mermi prefab'ı.\n" +
             "Prefab'da Bullet.cs componenti olmalı.\n" +
             "SpriteRenderer veya TrailRenderer opsiyonel.")]
    [SerializeField]
    private Bullet _bulletPrefab;

    [Header("─── Pool Ayarları ───")]
    [Tooltip("Başlangıçta oluşturulacak mermi sayısı.\n" +
             "Çok düşük = ilk saniyelerde runtime Instantiate olur.\n" +
             "Çok yüksek = gereksiz başlangıç bellek kullanımı.\n" +
             "20 = ~5 saniye ateş kapasitesi (0.28 interval ile).")]
    [SerializeField, Range(5, 50)]
    private int _poolDefaultCapacity = 20;

    [Tooltip("Pool'da tutulacak maksimum inaktif mermi sayısı.\n" +
             "Bu sınır aşılırsa fazla mermiler Destroy edilir.\n" +
             "Memory leak koruması sağlar.")]
    [SerializeField, Range(20, 100)]
    private int _poolMaxSize = 50;

    private Transform _cachedTransform;
    private Camera    _mainCamera;

    private ObjectPool<Bullet> _bulletPool;

    private float _fireTimer;

    private float _screenTopY;

    private float _prevScreenW;
    private float _prevScreenH;
    private float _prevOrthoSize;

    private Transform _bulletContainer;

    private void Awake()
    {
        _cachedTransform = transform;
        _mainCamera      = Camera.main;

        ValidateSetup();
        CreateBulletContainer();
        InitializePool();
        RecalculateScreenTop();

        _fireTimer = _fireInterval;
    }

    private void Update()
    {
        RefreshScreenTopIfChanged();
        HandleAutoFire();
    }

    private void OnDestroy()
    {
        _bulletPool?.Dispose();
    }

    private void InitializePool()
    {
        _bulletPool = new ObjectPool<Bullet>(
            createFunc:      OnPoolCreateBullet,
            actionOnGet:     OnPoolGetBullet,
            actionOnRelease: OnPoolReleaseBullet,
            actionOnDestroy: OnPoolDestroyBullet,
            collectionCheck: false,
            defaultCapacity: _poolDefaultCapacity,
            maxSize:         _poolMaxSize
        );

        PreWarmPool();
    }

    private void PreWarmPool()
    {
        Bullet[] warmBullets = new Bullet[_poolDefaultCapacity];

        for (int i = 0; i < _poolDefaultCapacity; i++)
        {
            warmBullets[i] = _bulletPool.Get();
        }

        for (int i = 0; i < _poolDefaultCapacity; i++)
        {
            _bulletPool.Release(warmBullets[i]);
        }
    }

    private Bullet OnPoolCreateBullet()
    {
        Bullet bullet = Instantiate(_bulletPrefab, _bulletContainer);

        bullet.Initialize(_bulletPool, _screenTopY);

        return bullet;
    }

    private void OnPoolGetBullet(Bullet bullet)
    {
        bullet.gameObject.SetActive(true);
    }

    private void OnPoolReleaseBullet(Bullet bullet)
    {
        bullet.gameObject.SetActive(false);
    }

    private void OnPoolDestroyBullet(Bullet bullet)
    {
        if (bullet != null)
            Destroy(bullet.gameObject);
    }

    private void HandleAutoFire()
    {
        _fireTimer -= Time.deltaTime;

        if (_fireTimer <= 0f)
        {
            Fire();

            _fireTimer += _fireInterval;

            if (_fireTimer < 0f)
                _fireTimer = 0f;
        }
    }

    private void Fire()
    {
        Bullet bullet = _bulletPool.Get();

        Vector3 spawnPos = _cachedTransform.position;
        spawnPos.y += _muzzleOffsetY;

        bullet.transform.position = spawnPos;

        bullet.Initialize(_bulletPool, _screenTopY);
    }

    private void RefreshScreenTopIfChanged()
    {
        float sw = Screen.width;
        float sh = Screen.height;
        float os = _mainCamera.orthographicSize;

        if (sw != _prevScreenW || sh != _prevScreenH || !Mathf.Approximately(os, _prevOrthoSize))
        {
            RecalculateScreenTop();
        }
    }

    private void RecalculateScreenTop()
    {
        float zDist = Mathf.Abs(_mainCamera.transform.position.z);
        Vector3 topRight = _mainCamera.ViewportToWorldPoint(new Vector3(1f, 1f, zDist));
        _screenTopY = topRight.y;

        _prevScreenW   = Screen.width;
        _prevScreenH   = Screen.height;
        _prevOrthoSize = _mainCamera.orthographicSize;
    }

    private void CreateBulletContainer()
    {
        GameObject container = new GameObject("── Bullet Pool ──");
        _bulletContainer = container.transform;
    }

    public void SetFireInterval(float newInterval)
    {
        _fireInterval = Mathf.Max(0.05f, newInterval);
    }

    public float GetFireInterval()
    {
        return _fireInterval;
    }

    [System.Diagnostics.Conditional("UNITY_EDITOR"),
     System.Diagnostics.Conditional("DEVELOPMENT_BUILD")]
    private void ValidateSetup()
    {
        if (_bulletPrefab == null)
            Debug.LogError(
                "[PlayerShooting] Bullet Prefab atanmamış! " +
                "Inspector'da _bulletPrefab alanına mermi prefab'ını sürükleyin.", this);

        if (_mainCamera == null)
            Debug.LogError(
                "[PlayerShooting] MainCamera bulunamadı! " +
                "Kameranın tag'ini kontrol edin.", this);
    }

    #if UNITY_EDITOR
    private void OnDrawGizmosSelected()
    {
        Vector3 muzzle = transform.position;
        muzzle.y += _muzzleOffsetY;

        Gizmos.color = new Color(1f, 0.92f, 0.23f, 0.9f); 
        Gizmos.DrawWireSphere(muzzle, 0.08f);

        Gizmos.color = new Color(1f, 0.92f, 0.23f, 0.4f);
        Gizmos.DrawLine(muzzle, muzzle + Vector3.up * 1.5f);

        if (Application.isPlaying && _bulletPool != null)
        {
            UnityEditor.Handles.Label(
                muzzle + Vector3.right * 0.5f,
                $"Pool: {_bulletPool.CountActive} aktif / {_bulletPool.CountInactive} beklemede"
            );
        }
    }
    #endif

}