using UnityEngine;

[RequireComponent(typeof(SpriteRenderer))]
public class PlayerController : MonoBehaviour
{
    [Header("─── Hareket ───")]
    [Tooltip("Takip hızı (exponential decay katsayısı).\n" +
             "Düşük (3-5) = kaygan/ağır his.\n" +
             "Yüksek (15-25) = keskin/ani tepki.\n" +
             "12 = HTML prototipindeki referans his.")]
    [SerializeField, Range(1f, 30f)]
    private float _followSpeed = 12f;

    [Header("─── Ekran Sınırları ───")]
    [Tooltip("Sprite kenarı ile ekran kenarı arasındaki minimum boşluk (world units).\n" +
             "0 = sprite tam ekran kenarına yapışır.\n" +
             "0.1 = küçük bir nefes payı bırakır.")]
    [SerializeField, Range(0f, 0.5f)]
    private float _edgePadding = 0.05f;

    private Transform _cachedTransform;

    private Camera _mainCamera;

    private float _spriteHalfW;
    private float _spriteHalfH;

    private float _boundsMinX;
    private float _boundsMaxX;
    private float _boundsMinY;
    private float _boundsMaxY;

    private float _prevScreenW;
    private float _prevScreenH;
    private float _prevOrthoSize;

    private Vector3 _targetWorldPos;

    private void Awake()
    {
        _cachedTransform = transform;
        _mainCamera      = Camera.main;

        ValidateReferences();

        CalculateSpriteExtents();

        RecalculateScreenBounds();

        _targetWorldPos = _cachedTransform.position;
    }

    private void Update()
    {
        RefreshBoundsIfScreenChanged();
        ReadInput();
        ApplyMovement();
    }

    private void ReadInput()
    {
        if (Input.touchCount > 0)
        {
            Touch touch = Input.GetTouch(0);

            if (touch.phase == TouchPhase.Ended || touch.phase == TouchPhase.Canceled)
                return;

            if (IsPointerOverUI(touch.fingerId))
                return;

            _targetWorldPos = ScreenPointToClampedWorld(touch.position);
            return;
        }

        #if UNITY_EDITOR || UNITY_STANDALONE
        if (Input.GetMouseButton(0))
        {
            if (IsPointerOverUI(-1))
                return;

            _targetWorldPos = ScreenPointToClampedWorld(Input.mousePosition);
        }
        #endif
    }

    private void ApplyMovement()
    {
        Vector3 currentPos = _cachedTransform.position;

        float smoothFactor = 1f - Mathf.Exp(-_followSpeed * Time.deltaTime);

        float newX = Mathf.Lerp(currentPos.x, _targetWorldPos.x, smoothFactor);
        float newY = Mathf.Lerp(currentPos.y, _targetWorldPos.y, smoothFactor);

        newX = Mathf.Clamp(newX, _boundsMinX, _boundsMaxX);
        newY = Mathf.Clamp(newY, _boundsMinY, _boundsMaxY);

        _cachedTransform.position = new Vector3(newX, newY, currentPos.z);
    }

    private void RefreshBoundsIfScreenChanged()
    {
        float currentW    = Screen.width;
        float currentH    = Screen.height;
        float currentOrtho = _mainCamera.orthographicSize;

        bool screenChanged = currentW    != _prevScreenW
                          || currentH    != _prevScreenH;
        bool cameraChanged = !Mathf.Approximately(currentOrtho, _prevOrthoSize);

        if (screenChanged || cameraChanged)
        {
            RecalculateScreenBounds();
        }
    }

    private void RecalculateScreenBounds()
    {
        float cameraDistance = Mathf.Abs(
            _mainCamera.transform.position.z - _cachedTransform.position.z
        );

        Vector3 bottomLeft = _mainCamera.ViewportToWorldPoint(
            new Vector3(0f, 0f, cameraDistance)
        );
        Vector3 topRight = _mainCamera.ViewportToWorldPoint(
            new Vector3(1f, 1f, cameraDistance)
        );

        float totalPadX = _spriteHalfW + _edgePadding;
        float totalPadY = _spriteHalfH + _edgePadding;

        _boundsMinX = bottomLeft.x + totalPadX;
        _boundsMaxX = topRight.x   - totalPadX;
        _boundsMinY = bottomLeft.y + totalPadY;
        _boundsMaxY = topRight.y   - totalPadY;

        if (_boundsMinX > _boundsMaxX)
        {
            float midX = (_boundsMinX + _boundsMaxX) * 0.5f;
            _boundsMinX = midX;
            _boundsMaxX = midX;
        }
        if (_boundsMinY > _boundsMaxY)
        {
            float midY = (_boundsMinY + _boundsMaxY) * 0.5f;
            _boundsMinY = midY;
            _boundsMaxY = midY;
        }

        _prevScreenW   = Screen.width;
        _prevScreenH   = Screen.height;
        _prevOrthoSize = _mainCamera.orthographicSize;
    }

    private Vector3 ScreenPointToClampedWorld(Vector2 screenPos)
    {
        float cameraDistance = Mathf.Abs(
            _mainCamera.transform.position.z - _cachedTransform.position.z
        );

        Vector3 worldPos = _mainCamera.ScreenToWorldPoint(
            new Vector3(screenPos.x, screenPos.y, cameraDistance)
        );

        worldPos.x = Mathf.Clamp(worldPos.x, _boundsMinX, _boundsMaxX);
        worldPos.y = Mathf.Clamp(worldPos.y, _boundsMinY, _boundsMaxY);

        worldPos.z = _cachedTransform.position.z;

        return worldPos;
    }

    private void CalculateSpriteExtents()
    {
        SpriteRenderer sr = GetComponent<SpriteRenderer>();

        if (sr != null && sr.sprite != null)
        {
            Vector2 localExtents = sr.sprite.bounds.extents;
            Vector3 scale = _cachedTransform.localScale;

            _spriteHalfW = localExtents.x * Mathf.Abs(scale.x);
            _spriteHalfH = localExtents.y * Mathf.Abs(scale.y);
        }
        else
        {
            _spriteHalfW = 0.5f;
            _spriteHalfH = 0.5f;

            LogWarning("SpriteRenderer veya Sprite bulunamadı. " +
                       "Varsayılan extents (0.5, 0.5) kullanılıyor.");
        }
    }

    private bool IsPointerOverUI(int fingerId)
    {
        UnityEngine.EventSystems.EventSystem currentES =
            UnityEngine.EventSystems.EventSystem.current;

        if (currentES == null)
            return false;

        return (fingerId < 0)
            ? currentES.IsPointerOverGameObject()
            : currentES.IsPointerOverGameObject(fingerId);
    }

    [System.Diagnostics.Conditional("UNITY_EDITOR"),
     System.Diagnostics.Conditional("DEVELOPMENT_BUILD")]
    private void ValidateReferences()
    {
        if (_mainCamera == null)
            Debug.LogError(
                "[PlayerController] Sahnede 'MainCamera' tag'li kamera bulunamadı! " +
                "Kameranın tag'ini kontrol edin.", this);

        if (GetComponent<SpriteRenderer>() == null)
            Debug.LogError(
                "[PlayerController] Bu GameObject'te SpriteRenderer yok! " +
                "RequireComponent olmasına rağmen çalışma zamanında kaldırılmış olabilir.", this);
    }

    [System.Diagnostics.Conditional("UNITY_EDITOR"),
     System.Diagnostics.Conditional("DEVELOPMENT_BUILD")]
    private void LogWarning(string message)
    {
        Debug.LogWarning($"[PlayerController] {message}", this);
    }

    public (float minX, float maxX, float minY, float maxY) GetMovementBounds()
    {
        return (_boundsMinX, _boundsMaxX, _boundsMinY, _boundsMaxY);
    }

    #if UNITY_EDITOR
    private void OnDrawGizmos()
    {
        float minX, maxX, minY, maxY;

        if (Application.isPlaying)
        {
            minX = _boundsMinX;
            maxX = _boundsMaxX;
            minY = _boundsMinY;
            maxY = _boundsMaxY;
        }
        else
        {
            Camera cam = Camera.main;
            if (cam == null) return;

            float halfW = 0.5f, halfH = 0.5f;
            SpriteRenderer sr = GetComponent<SpriteRenderer>();
            if (sr != null && sr.sprite != null)
            {
                Vector2 ext = sr.sprite.bounds.extents;
                Vector3 scl = transform.localScale;
                halfW = ext.x * Mathf.Abs(scl.x);
                halfH = ext.y * Mathf.Abs(scl.y);
            }

            float zDist = Mathf.Abs(cam.transform.position.z - transform.position.z);
            Vector3 bl = cam.ViewportToWorldPoint(new Vector3(0f, 0f, zDist));
            Vector3 tr = cam.ViewportToWorldPoint(new Vector3(1f, 1f, zDist));

            float padX = halfW + _edgePadding;
            float padY = halfH + _edgePadding;

            minX = bl.x + padX;
            maxX = tr.x - padX;
            minY = bl.y + padY;
            maxY = tr.y - padY;

            if (minX > maxX) { float mid = (minX + maxX) * 0.5f; minX = mid; maxX = mid; }
            if (minY > maxY) { float mid = (minY + maxY) * 0.5f; minY = mid; maxY = mid; }
        }

        Gizmos.color = new Color(0f, 1f, 1f, 0.8f); 

        float z = Application.isPlaying
            ? _cachedTransform.position.z
            : transform.position.z;

        Vector3 topLeft     = new Vector3(minX, maxY, z);
        Vector3 topRight    = new Vector3(maxX, maxY, z);
        Vector3 bottomRight = new Vector3(maxX, minY, z);
        Vector3 bottomLeft  = new Vector3(minX, minY, z);

        Gizmos.DrawLine(topLeft,     topRight);     
        Gizmos.DrawLine(topRight,    bottomRight);  
        Gizmos.DrawLine(bottomRight, bottomLeft);   
        Gizmos.DrawLine(bottomLeft,  topLeft);       

        float markerSize = 0.1f;
        Gizmos.DrawWireSphere(topLeft,     markerSize);
        Gizmos.DrawWireSphere(topRight,    markerSize);
        Gizmos.DrawWireSphere(bottomRight, markerSize);
        Gizmos.DrawWireSphere(bottomLeft,  markerSize);

        Gizmos.color = new Color(1f, 1f, 1f, 0.15f); 

        Camera gizmoCam = Application.isPlaying ? _mainCamera : Camera.main;
        if (gizmoCam != null)
        {
            float gizmoZ = Mathf.Abs(gizmoCam.transform.position.z - z);
            Vector3 screenBL = gizmoCam.ViewportToWorldPoint(new Vector3(0f, 0f, gizmoZ));
            Vector3 screenTR = gizmoCam.ViewportToWorldPoint(new Vector3(1f, 1f, gizmoZ));

            Vector3 sTL = new Vector3(screenBL.x, screenTR.y, z);
            Vector3 sTR = new Vector3(screenTR.x, screenTR.y, z);
            Vector3 sBR = new Vector3(screenTR.x, screenBL.y, z);
            Vector3 sBL = new Vector3(screenBL.x, screenBL.y, z);

            Gizmos.DrawLine(sTL, sTR);
            Gizmos.DrawLine(sTR, sBR);
            Gizmos.DrawLine(sBR, sBL);
            Gizmos.DrawLine(sBL, sTL);
        }
    }
    #endif
}