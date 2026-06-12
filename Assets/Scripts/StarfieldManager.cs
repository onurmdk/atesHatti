using UnityEngine;

[RequireComponent(typeof(ParticleSystem))]
public class StarfieldManager : MonoBehaviour
{
    private struct StarData
    {
        public float speed;
        public float size;
        public float alpha;
    }

    [Header("─── Ayarlar ───")]
    [SerializeField, Range(20, 200)]
    private int _starCount = 80;

    [SerializeField]
    private Color _starColor = new Color(0.78f, 0.84f, 0.90f);

    [SerializeField]
    private float _minSpeed = 0.8f;
    [SerializeField]
    private float _maxSpeed = 2.5f;

    [SerializeField]
    private float _minSize = 0.01f;
    [SerializeField]
    private float _maxSize = 0.04f;

    [SerializeField]
    private float _minAlpha = 0.3f;
    [SerializeField]
    private float _maxAlpha = 0.9f;

    private ParticleSystem _ps;
    private ParticleSystem.Particle[] _particles;
    private StarData[] _starData;

    private Camera _mainCamera;
    private float _screenMinX, _screenMaxX;
    private float _screenMinY, _screenMaxY;

    private void Awake()
    {
        _ps = GetComponent<ParticleSystem>();
        _mainCamera = Camera.main;

        RecalculateScreenBounds();
        InitializeStars();
    }

    private void Update()
    {
        float dt = Time.deltaTime;

        for (int i = 0; i < _starCount; i++)
        {
            _particles[i].position += Vector3.down * _starData[i].speed * dt;

            if (_particles[i].position.y < _screenMinY)
            {
                _particles[i].position = new Vector3(
                    Random.Range(_screenMinX, _screenMaxX),
                    _screenMaxY,
                    0f
                );
            }
        }

        _ps.SetParticles(_particles, _starCount);
    }

    private void InitializeStars()
    {
        _particles = new ParticleSystem.Particle[_starCount];
        _starData = new StarData[_starCount];

        var main = _ps.main;
        main.maxParticles = _starCount;
        main.simulationSpace = ParticleSystemSimulationSpace.World;

        for (int i = 0; i < _starCount; i++)
        {
            float depthFactor = Random.value;

            float speed = Mathf.Lerp(_minSpeed, _maxSpeed, depthFactor)
                        + Random.Range(-0.2f, 0.2f);
            speed = Mathf.Max(0.1f, speed);

            float size = Mathf.Lerp(_minSize, _maxSize, depthFactor)
                       + Random.Range(-0.005f, 0.005f);
            size = Mathf.Max(0.005f, size);

            float alpha = Mathf.Lerp(_minAlpha, _maxAlpha, depthFactor)
                        + Random.Range(-0.1f, 0.1f);
            alpha = Mathf.Clamp(alpha, _minAlpha, _maxAlpha);

            _starData[i] = new StarData
            {
                speed = speed,
                size  = size,
                alpha = alpha
            };

            _particles[i].position      = new Vector3(
                Random.Range(_screenMinX, _screenMaxX),
                Random.Range(_screenMinY, _screenMaxY),
                0f
            );
            _particles[i].startSize     = size;
            _particles[i].startColor    = new Color32(
                (byte)(_starColor.r * 255),
                (byte)(_starColor.g * 255),
                (byte)(_starColor.b * 255),
                (byte)(alpha * 255)
            );
        }

        _ps.SetParticles(_particles, _starCount);
    }

    private void RecalculateScreenBounds()
    {
        float zDist = Mathf.Abs(_mainCamera.transform.position.z);
        Vector3 bl = _mainCamera.ViewportToWorldPoint(new Vector3(0f, 0f, zDist));
        Vector3 tr = _mainCamera.ViewportToWorldPoint(new Vector3(1f, 1f, zDist));

        _screenMinX = bl.x;
        _screenMaxX = tr.x;
        _screenMinY = bl.y;
        _screenMaxY = tr.y;
    }
}