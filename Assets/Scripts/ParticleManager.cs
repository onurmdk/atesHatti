using UnityEngine;
using UnityEngine.Pool;

public class ParticleManager : MonoBehaviour
{
    public static ParticleManager Instance { get; private set; }

    [Header("─── Patlama Efekti ───")]
    [Tooltip("Patlama partikül prefab'ı.\n" +
             "ParticleSystem bileşeni olmalı.\n" +
             "StopAction otomatik olarak Callback'e set edilir.")]
    [SerializeField]
    private ParticleSystem _explosionPrefab;

    [Header("─── İsabet Kıvılcımı ───")]
    [Tooltip("Mermi isabet kıvılcım prefab'ı (isteğe bağlı).\n" +
             "null ise küçük isabet efekti için de patlama prefab'ı kullanılır.")]
    [SerializeField]
    private ParticleSystem _sparkPrefab;

    [Header("─── Pool Ayarları ───")]
    [SerializeField, Range(5, 30)]
    private int _poolDefaultCapacity = 10;

    [SerializeField, Range(15, 50)]
    private int _poolMaxSize = 25;

    [SerializeField, Range(3, 15)]
    private int _sparkPoolDefaultCapacity = 8;

    [SerializeField, Range(10, 30)]
    private int _sparkPoolMaxSize = 20;

    private ObjectPool<ParticleSystem> _explosionPool;
    private ObjectPool<ParticleSystem> _sparkPool;
    private Transform _container;

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;

        ValidateSetup();
        CreateContainer();
        InitializePools();
    }

    private void OnDestroy()
    {
        if (Instance == this)
            Instance = null;

        _explosionPool?.Dispose();
        _sparkPool?.Dispose();
    }

    private void InitializePools()
    {
        _explosionPool = new ObjectPool<ParticleSystem>(
            createFunc:      () => CreateParticle(_explosionPrefab),
            actionOnGet:     OnPoolGetParticle,
            actionOnRelease: OnPoolReleaseParticle,
            actionOnDestroy: OnPoolDestroyParticle,
            collectionCheck: false,
            defaultCapacity: _poolDefaultCapacity,
            maxSize:         _poolMaxSize
        );

        ParticleSystem sparkTemplate = _sparkPrefab != null ? _sparkPrefab : _explosionPrefab;

        _sparkPool = new ObjectPool<ParticleSystem>(
            createFunc:      () => CreateParticle(sparkTemplate),
            actionOnGet:     OnPoolGetParticle,
            actionOnRelease: OnPoolReleaseParticle,
            actionOnDestroy: OnPoolDestroyParticle,
            collectionCheck: false,
            defaultCapacity: _sparkPoolDefaultCapacity,
            maxSize:         _sparkPoolMaxSize
        );

        PreWarmPool(_explosionPool, _poolDefaultCapacity);
        PreWarmPool(_sparkPool, _sparkPoolDefaultCapacity);
    }

    private ParticleSystem CreateParticle(ParticleSystem prefab)
    {
        ParticleSystem ps = Instantiate(prefab, _container);

        var main = ps.main;
        main.stopAction = ParticleSystemStopAction.Callback;

        main.loop = false;

        main.playOnAwake = false;

        ParticleReturnHandler handler = ps.GetComponent<ParticleReturnHandler>();
        if (handler == null)
            handler = ps.gameObject.AddComponent<ParticleReturnHandler>();

        ps.gameObject.SetActive(false);
        return ps;
    }

    private void OnPoolGetParticle(ParticleSystem ps)
    {
        ps.gameObject.SetActive(true);
    }

    private void OnPoolReleaseParticle(ParticleSystem ps)
    {
        ps.gameObject.SetActive(false);
    }

    private void OnPoolDestroyParticle(ParticleSystem ps)
    {
        if (ps != null)
            Destroy(ps.gameObject);
    }

    private void PreWarmPool(ObjectPool<ParticleSystem> pool, int count)
    {
        ParticleSystem[] temp = new ParticleSystem[count];

        for (int i = 0; i < count; i++)
            temp[i] = pool.Get();

        for (int i = 0; i < count; i++)
            pool.Release(temp[i]);
    }

    public void PlayExplosion(Vector3 position, Color color)
    {
        ParticleSystem ps = _explosionPool.Get();
        ConfigureAndPlay(ps, position, color, _explosionPool);
    }

    public void PlaySpark(Vector3 position, Color color)
    {
        ParticleSystem ps = _sparkPool.Get();
        ConfigureAndPlay(ps, position, color, _sparkPool);
    }

    private void ConfigureAndPlay(ParticleSystem ps, Vector3 position, Color color,
                                   ObjectPool<ParticleSystem> ownerPool)
    {
        ps.transform.position = position;

        var main = ps.main;
        main.startColor = new ParticleSystem.MinMaxGradient(color);

        ParticleReturnHandler handler = ps.GetComponent<ParticleReturnHandler>();
        if (handler != null)
            handler.SetPool(ownerPool);

        ps.Clear(true);

        ps.Play(true);
    }

    private void CreateContainer()
    {
        GameObject container = new GameObject("── Particle Pool ──");
        _container = container.transform;
    }

    [System.Diagnostics.Conditional("UNITY_EDITOR"),
     System.Diagnostics.Conditional("DEVELOPMENT_BUILD")]
    private void ValidateSetup()
    {
        if (_explosionPrefab == null)
            Debug.LogError("[ParticleManager] Explosion prefab atanmamış!", this);
    }
}

public class ParticleReturnHandler : MonoBehaviour
{
    private IObjectPool<ParticleSystem> _pool;
    private ParticleSystem _particleSystem;

    private void Awake()
    {
        _particleSystem = GetComponent<ParticleSystem>();
    }

    public void SetPool(IObjectPool<ParticleSystem> pool)
    {
        _pool = pool;
    }

    private void OnParticleSystemStopped()
    {
        if (!gameObject.activeSelf)
            return;

        if (_pool != null)
        {
            _pool.Release(_particleSystem);
        }
        else
        {
            gameObject.SetActive(false);
        }
    }
}