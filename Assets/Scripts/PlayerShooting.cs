using UnityEngine;
using UnityEngine.Pool;

public class PlayerShooting : MonoBehaviour
{
    [Header("─── Fire Settings ───")]
    [Tooltip("Time between bullets (seconds).\n" +
             "0.28 = reference speed from the HTML prototype.\n" +
             "The upgrade system will lower this value at runtime.")]
    [SerializeField, Range(0.05f, 1f)]
    private float _fireInterval = 0.28f;

    [Tooltip("How far above the ship the bullet spawns (world units).\n" +
             "Too low = the bullet appears to come from inside the ship.\n" +
             "Too high = the bullet appears to materialize in mid-air.\n" +
             "0.5 = feels like it exits from the ship's nose.")]
    [SerializeField, Range(0.1f, 1.5f)]
    private float _muzzleOffsetY = 0.5f;

    [Header("─── Bullet Prefab ───")]
    [Tooltip("Bullet prefab the pool will create.\n" +
             "The prefab must have a Bullet.cs component.\n" +
             "SpriteRenderer or TrailRenderer are optional.")]
    [SerializeField]
    private Bullet _bulletPrefab;

    [Header("─── Pool Settings ───")]
    [Tooltip("Number of bullets pre-created at startup.\n" +
             "Too low = runtime Instantiate calls in the first few seconds.\n" +
             "Too high = unnecessary startup memory usage.\n" +
             "20 = ~5 seconds of fire capacity (at 0.28 interval).")]
    [SerializeField, Range(5, 50)]
    private int _poolDefaultCapacity = 20;

    [Tooltip("Maximum number of inactive bullets kept in the pool.\n" +
             "Excess bullets beyond this limit are Destroyed.\n" +
             "Provides protection against memory leaks.")]
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
                "[PlayerShooting] Bullet Prefab not assigned! " +
                "Drag the bullet prefab onto the _bulletPrefab field in the Inspector.", this);

        if (_mainCamera == null)
            Debug.LogError(
                "[PlayerShooting] MainCamera not found! " +
                "Check the camera's tag.", this);
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
                $"Pool: {_bulletPool.CountActive} active / {_bulletPool.CountInactive} waiting"
            );
        }
    }
    #endif

}