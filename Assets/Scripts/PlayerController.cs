using UnityEngine;

/// <summary>
/// ╔══════════════════════════════════════════════════════════════════╗
///   ATEŞ HATTI — PlayerController
///   
///   Oyuncu gemisinin dokunma/mouse ile hareketini ve ekran sınırları
///   içinde kalmasını yöneten ana hareket kontrolcüsü.
///   
///   TEK SORUMLULUK: Hareket + Ekran Clamp.
///   Ateş, hasar, sağlık vb. ayrı scriptlerde ele alınır (SRP).
/// ╚══════════════════════════════════════════════════════════════════╝
/// 
/// Performans Garantileri:
/// ───────────────────────
/// • Update döngüsünde SIFIR GC Allocation (heap'e hiçbir şey yazılmaz).
/// • Camera.main ve Transform Awake'te cache'lenir.
/// • Ekran sınırları sadece çözünürlük/orientation değişince recalculate edilir.
/// • Tüm debug logları DEVELOPMENT_BUILD veya UNITY_EDITOR dışında strip edilir.
/// 
/// Kullanım:
/// ─────────
/// 1. Player GameObject'e SpriteRenderer ve bu script'i ekle.
/// 2. Kamera Orthographic olmalı (2D top-down).
/// 3. UI butonları varsa sahnede EventSystem olmalı (input filtresi için).
/// 4. Inspector'dan _followSpeed ve _edgePadding ayarlanabilir.
/// </summary>
[RequireComponent(typeof(SpriteRenderer))]
public class PlayerController : MonoBehaviour
{
    // ════════════════════════════════════════════════════════════════
    //  INSPECTOR — Tasarımcı Tarafından Ayarlanabilir Değerler
    // ════════════════════════════════════════════════════════════════

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

    // ════════════════════════════════════════════════════════════════
    //  CACHE — Awake'te bir kez set edilir, Update'te tekrar aranmaz
    // ════════════════════════════════════════════════════════════════

    /// <summary>
    /// Unity'de transform property'si her erişimde C# → C++ bridge call yapar.
    /// 60 FPS × frame başına 3-4 erişim = 240 bridge call/saniye.
    /// Local değişkende cache'lemek bu maliyeti sıfırlar.
    /// </summary>
    private Transform _cachedTransform;

    /// <summary>
    /// Camera.main her çağrıda FindGameObjectWithTag("MainCamera") yapar.
    /// Mobilde her frame bunu çağırmak ciddi performans kaybıdır.
    /// Awake'te bir kez cache'leyip tekrar kullanıyoruz.
    /// </summary>
    private Camera _mainCamera;

    // ════════════════════════════════════════════════════════════════
    //  SPRITE BOYUTLARI — Clamp hesabında kullanılır
    // ════════════════════════════════════════════════════════════════

    /// <summary>
    /// Sprite'ın world-space yarı genişliği ve yarı yüksekliği.
    /// sprite.bounds.extents (local-space) × localScale ile hesaplanır.
    /// SpriteRenderer.bounds yerine bu yöntem tercih edildi çünkü:
    ///   - sprite.bounds obje pozisyonundan bağımsızdır (daha güvenli)
    ///   - Runtime'da scale değişirse Awake'te alınan değer hâlâ doğrudur
    ///     (çünkü scale'i ayrıca çarpıyoruz, gerekirse recalculate edilebilir)
    /// </summary>
    private float _spriteHalfW;
    private float _spriteHalfH;

    // ════════════════════════════════════════════════════════════════
    //  EKRAN SINIRLARI — Dinamik, sadece değişiklikte güncellenir
    // ════════════════════════════════════════════════════════════════

    /// <summary>
    /// Geminin hareket edebileceği world-space sınırları.
    /// Sprite extents ve padding zaten düşülmüş halde tutulur.
    /// Clamp işlemi: Mathf.Clamp(x, _boundsMinX, _boundsMaxX)
    /// </summary>
    private float _boundsMinX;
    private float _boundsMaxX;
    private float _boundsMinY;
    private float _boundsMaxY;

    /// <summary>
    /// Dirty check için önceki frame'in ekran değerleri.
    /// Sadece bunlar değiştiğinde RecalculateScreenBounds çağrılır.
    /// float karşılaştırma → GC yok, CPU maliyeti ihmal edilebilir.
    /// </summary>
    private float _prevScreenW;
    private float _prevScreenH;
    private float _prevOrthoSize;

    // ════════════════════════════════════════════════════════════════
    //  HAREKET STATE
    // ════════════════════════════════════════════════════════════════

    /// <summary>
    /// Geminin dokunma/mouse pozisyonuna göre hedeflediği world pozisyonu.
    /// Her frame bu hedefe doğru exponential interpolation yapılır.
    /// Input yokken son hedef korunur → gemi yerinde durur, drift etmez.
    /// </summary>
    private Vector3 _targetWorldPos;

    // ════════════════════════════════════════════════════════════════
    //  UNITY LIFECYCLE
    // ════════════════════════════════════════════════════════════════

    private void Awake()
    {
        // ── Referansları cache'le ──
        _cachedTransform = transform;
        _mainCamera      = Camera.main;

        ValidateReferences();

        // ── Sprite yarı boyutlarını hesapla ──
        CalculateSpriteExtents();

        // ── İlk sınır hesaplaması ──
        RecalculateScreenBounds();

        // ── Başlangıç hedefi: geminin mevcut pozisyonu ──
        // Böylece oyun başladığında gemi aniden (0,0)'a fırlamaz
        _targetWorldPos = _cachedTransform.position;
    }

    /// <summary>
    /// Ana güncelleme döngüsü. Her frame çağrılır.
    /// 
    /// Sıralama önemli:
    /// 1. Sınır kontrolü (orientation değişmiş olabilir)
    /// 2. Input oku (hedef pozisyonu güncelle)
    /// 3. Hareketi uygula (interpole et + clamp et)
    /// </summary>
    private void Update()
    {
        RefreshBoundsIfScreenChanged();
        ReadInput();
        ApplyMovement();
    }

    // ════════════════════════════════════════════════════════════════
    //  INPUT — Touch (mobil) + Mouse (editör fallback)
    // ════════════════════════════════════════════════════════════════

    /// <summary>
    /// Aktif input'u okur ve _targetWorldPos'u günceller.
    /// 
    /// Öncelik sırası:
    /// 1. Touch (mobilde her zaman önce kontrol edilir)
    /// 2. Mouse (sadece UNITY_EDITOR veya UNITY_STANDALONE'de derlenir)
    /// 
    /// Input yoksa _targetWorldPos değişmez → gemi son pozisyonda kalır.
    /// 
    /// UI Filtresi:
    /// EventSystem.IsPointerOverGameObject ile UI butonlarına basılınca
    /// gemi hareket etmez. Bu olmadan "Upgrade" butonuna her basışta
    /// gemi butonun altına fırlardı.
    /// </summary>
    private void ReadInput()
    {
        // ── TOUCH (Android / iOS) ──
        if (Input.touchCount > 0)
        {
            // Sadece ilk parmağı oku — multi-touch'ta diğer parmaklar UI vb. için olabilir
            Touch touch = Input.GetTouch(0);

            // Biten veya iptal edilen dokunuşları yoksay
            if (touch.phase == TouchPhase.Ended || touch.phase == TouchPhase.Canceled)
                return;

            // UI üzerindeyse gameplay input'unu yoksay
            if (IsPointerOverUI(touch.fingerId))
                return;

            // Ekran pikselini → world pozisyonuna çevir ve sınırla
            _targetWorldPos = ScreenPointToClampedWorld(touch.position);
            return;
        }

        // ── MOUSE (Editor / PC test) ──
        // Bu blok mobil build'de derlenmez → zero overhead
        #if UNITY_EDITOR || UNITY_STANDALONE
        if (Input.GetMouseButton(0))
        {
            if (IsPointerOverUI(-1))
                return;

            _targetWorldPos = ScreenPointToClampedWorld(Input.mousePosition);
        }
        #endif
    }

    // ════════════════════════════════════════════════════════════════
    //  HAREKET — Frame-Rate Independent Exponential Interpolation
    // ════════════════════════════════════════════════════════════════

    /// <summary>
    /// Gemiyi _targetWorldPos'a doğru yumuşak bir şekilde kaydırır.
    /// 
    /// ┌─────────────────────────────────────────────────────────────┐
    /// │ NEDEN KLASIK LERP DEĞİL?                                   │
    /// │                                                             │
    /// │ Lerp(a, b, speed * dt) frame-rate BAĞIMLIDIR:               │
    /// │   60 FPS → t = 12 × 0.0167 = 0.200 → mesafenin %20'si     │
    /// │   30 FPS → t = 12 × 0.0333 = 0.400 → mesafenin %40'ı      │
    /// │   Aynı sürede 30 FPS'de gemi daha hızlı gider!             │
    /// │                                                             │
    /// │ DOĞRU FORMÜL: t = 1 - exp(-speed × dt)                     │
    /// │   60 FPS → 2 frame sonrası: 1-(1-0.181)² = 0.329           │
    /// │   30 FPS → 1 frame sonrası: 1-exp(-12×0.033) = 0.327       │
    /// │   ≈ Aynı! Frame-rate'den bağımsız, fiziksel olarak doğru.  │
    /// └─────────────────────────────────────────────────────────────┘
    /// </summary>
    private void ApplyMovement()
    {
        Vector3 currentPos = _cachedTransform.position;

        // Frame-rate independent smoothing faktörü
        float smoothFactor = 1f - Mathf.Exp(-_followSpeed * Time.deltaTime);

        // Her ekseni ayrı interpole et (Vector3.Lerp yerine — Z'yi korumak için)
        float newX = Mathf.Lerp(currentPos.x, _targetWorldPos.x, smoothFactor);
        float newY = Mathf.Lerp(currentPos.y, _targetWorldPos.y, smoothFactor);

        // Ekran sınırlarına clamp et
        newX = Mathf.Clamp(newX, _boundsMinX, _boundsMaxX);
        newY = Mathf.Clamp(newY, _boundsMinY, _boundsMaxY);

        // Pozisyonu güncelle (Z ekseni korunur — sorting layer bozulmasın)
        _cachedTransform.position = new Vector3(newX, newY, currentPos.z);
    }

    // ════════════════════════════════════════════════════════════════
    //  EKRAN SINIRI HESAPLAMASI
    // ════════════════════════════════════════════════════════════════

    /// <summary>
    /// Her frame çağrılır ama sadece ekran parametreleri değiştiyse 
    /// gerçek hesaplamayı tetikler.
    /// 
    /// Ne zaman değişir?
    /// - Mobilde orientation (dikey ↔ yatay) değiştiğinde
    /// - Editörde Game view yeniden boyutlandırıldığında
    /// - Camera.orthographicSize runtime'da değiştirildiğinde (zoom efekti vb.)
    /// 
    /// Maliyet: 3 float karşılaştırma ≈ ~0 CPU, GC yok.
    /// </summary>
    private void RefreshBoundsIfScreenChanged()
    {
        float currentW    = Screen.width;
        float currentH    = Screen.height;
        float currentOrtho = _mainCamera.orthographicSize;

        // Herhangi biri değiştiyse yeniden hesapla
        bool screenChanged = currentW    != _prevScreenW
                          || currentH    != _prevScreenH;
        bool cameraChanged = !Mathf.Approximately(currentOrtho, _prevOrthoSize);

        if (screenChanged || cameraChanged)
        {
            RecalculateScreenBounds();
        }
    }

    /// <summary>
    /// Kameranın viewport köşelerini world koordinatlarına çevirir,
    /// sprite yarı boyutunu ve padding'i düşerek hareket sınırlarını belirler.
    /// 
    /// Viewport koordinatları:
    ///   (0, 0) = ekranın sol-alt köşesi
    ///   (1, 1) = ekranın sağ-üst köşesi
    /// 
    /// z parametresi neden gerekli?
    ///   ViewportToWorldPoint 3D bir nokta döner. z, kameradan ne kadar
    ///   uzaktaki düzlemde hesaplama yapılacağını belirtir.
    ///   2D oyunlarda kamera z=-10, objeler z=0 → mesafe = 10.
    ///   Yanlış z verilirse sınırlar hatalı hesaplanır.
    /// </summary>
    private void RecalculateScreenBounds()
    {
        // Kamera ile oyuncu arasındaki z mesafesi
        float cameraDistance = Mathf.Abs(
            _mainCamera.transform.position.z - _cachedTransform.position.z
        );

        // Viewport köşelerini world-space'e çevir
        Vector3 bottomLeft = _mainCamera.ViewportToWorldPoint(
            new Vector3(0f, 0f, cameraDistance)
        );
        Vector3 topRight = _mainCamera.ViewportToWorldPoint(
            new Vector3(1f, 1f, cameraDistance)
        );

        // Sprite yarı boyutu ve padding'i düş
        // Böylece gemi ekranın tam kenarında durur, yarısı dışarı taşmaz
        float totalPadX = _spriteHalfW + _edgePadding;
        float totalPadY = _spriteHalfH + _edgePadding;

        _boundsMinX = bottomLeft.x + totalPadX;
        _boundsMaxX = topRight.x   - totalPadX;
        _boundsMinY = bottomLeft.y + totalPadY;
        _boundsMaxY = topRight.y   - totalPadY;

        // ── Güvenlik: Ekran çok küçük veya sprite çok büyükse ──
        // min > max olabilir → Clamp hatalı davranır, gemi titrer.
        // Çözüm: Ortaya kilitle, gemi sadece o noktada durabilir.
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

        // Dirty check değerlerini güncelle
        _prevScreenW   = Screen.width;
        _prevScreenH   = Screen.height;
        _prevOrthoSize = _mainCamera.orthographicSize;
    }

    // ════════════════════════════════════════════════════════════════
    //  YARDIMCI METODLAR
    // ════════════════════════════════════════════════════════════════

    /// <summary>
    /// Ekran piksel koordinatını world pozisyonuna çevirir 
    /// ve hemen sınırlara clamp eder.
    /// 
    /// Neden hedefi de clamp ediyoruz?
    /// Kullanıcı parmağını ekran dışına sürüklerse (edge swipe),
    /// hedef sınır dışında kalır → gemi sınıra yapışıp titreyebilir.
    /// Hedefi de clamp ederek bunu önlüyoruz.
    /// </summary>
    private Vector3 ScreenPointToClampedWorld(Vector2 screenPos)
    {
        float cameraDistance = Mathf.Abs(
            _mainCamera.transform.position.z - _cachedTransform.position.z
        );

        Vector3 worldPos = _mainCamera.ScreenToWorldPoint(
            new Vector3(screenPos.x, screenPos.y, cameraDistance)
        );

        // Sınırla
        worldPos.x = Mathf.Clamp(worldPos.x, _boundsMinX, _boundsMaxX);
        worldPos.y = Mathf.Clamp(worldPos.y, _boundsMinY, _boundsMaxY);

        // Z'yi geminin katmanında tut
        worldPos.z = _cachedTransform.position.z;

        return worldPos;
    }

    /// <summary>
    /// SpriteRenderer üzerindeki sprite'ın world-space yarı boyutlarını hesaplar.
    /// 
    /// Neden sr.bounds.extents değil de sprite.bounds.extents × localScale?
    /// ─────────────────────────────────────────────────────────────────────
    /// sr.bounds (SpriteRenderer.bounds):
    ///   World-space'dir, objenin O ANKİ pozisyonuna bağlıdır.
    ///   Doğru extents verir ama Awake sırasında obje pozisyonu
    ///   henüz kesinleşmemişse sorun çıkabilir.
    /// 
    /// sprite.bounds (Sprite.bounds):
    ///   Local-space'dir, pozisyondan bağımsızdır.
    ///   localScale ile çarparak world-space yarı boyutu elde ederiz.
    ///   Bu yöntem daha güvenli ve öngörülebilirdir.
    /// </summary>
    private void CalculateSpriteExtents()
    {
        SpriteRenderer sr = GetComponent<SpriteRenderer>();

        if (sr != null && sr.sprite != null)
        {
            // sprite.bounds.extents: Sprite'ın LOCAL yarı boyutu (pivot'tan kenara)
            // localScale ile çarparak WORLD yarı boyutunu elde ediyoruz
            Vector2 localExtents = sr.sprite.bounds.extents;
            Vector3 scale = _cachedTransform.localScale;

            _spriteHalfW = localExtents.x * Mathf.Abs(scale.x);
            _spriteHalfH = localExtents.y * Mathf.Abs(scale.y);
        }
        else
        {
            // Sprite atanmamışsa güvenli varsayılan
            _spriteHalfW = 0.5f;
            _spriteHalfH = 0.5f;

            LogWarning("SpriteRenderer veya Sprite bulunamadı. " +
                       "Varsayılan extents (0.5, 0.5) kullanılıyor.");
        }
    }

    /// <summary>
    /// Verilen pointer/finger ID'nin bir UI elementi üzerinde olup olmadığını kontrol eder.
    /// 
    /// fingerId parametresi:
    ///   Touch için → touch.fingerId (0, 1, 2...)
    ///   Mouse için → -1 (parametresiz overload tetiklenir)
    /// 
    /// EventSystem sahnede yoksa false döner → gameplay her zaman çalışır.
    /// EventSystem.current getter'ı static cache kullanır, GC üretmez.
    /// </summary>
    private bool IsPointerOverUI(int fingerId)
    {
        UnityEngine.EventSystems.EventSystem currentES =
            UnityEngine.EventSystems.EventSystem.current;

        if (currentES == null)
            return false;

        // Mouse (-1) için parametresiz overload, touch için fingerId'li overload
        return (fingerId < 0)
            ? currentES.IsPointerOverGameObject()
            : currentES.IsPointerOverGameObject(fingerId);
    }

    // ════════════════════════════════════════════════════════════════
    //  DOĞRULAMA
    // ════════════════════════════════════════════════════════════════

    /// <summary>
    /// Awake'te kritik referansların varlığını kontrol eder.
    /// Sadece Editor ve Development Build'de çalışır — Release'de strip edilir.
    /// </summary>
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

    /// <summary>
    /// Conditional attribute ile sadece development build'lerde derlenen log metodu.
    /// Release APK'da bu method çağrıları tamamen strip edilir — string allocation bile olmaz.
    /// </summary>
    [System.Diagnostics.Conditional("UNITY_EDITOR"),
     System.Diagnostics.Conditional("DEVELOPMENT_BUILD")]
    private void LogWarning(string message)
    {
        Debug.LogWarning($"[PlayerController] {message}", this);
    }

    // ════════════════════════════════════════════════════════════════
    //  PUBLIC API — Diğer Sistemler İçin
    // ════════════════════════════════════════════════════════════════

    /// <summary>
    /// Geminin hareket edebileceği world-space sınırlarını döner.
    /// 
    /// Kullanım alanları:
    ///   - PlayerShooting: Mermi spawn pozisyonunu sınır içinde tutmak
    ///   - EnemySpawner:   Düşmanları ekran genişliğinde spawn etmek
    ///   - UI:             Sınır göstergeleri
    /// 
    /// ValueTuple döner → struct, GC allocation SIFIR.
    /// </summary>
    public (float minX, float maxX, float minY, float maxY) GetMovementBounds()
    {
        return (_boundsMinX, _boundsMaxX, _boundsMinY, _boundsMaxY);
    }

    // ════════════════════════════════════════════════════════════════
    //  EDITOR GIZMOS — Sadece Unity Editor'da çalışır
    // ════════════════════════════════════════════════════════════════

    #if UNITY_EDITOR
    /// <summary>
    /// Scene ve Game view'da hareket sınırlarını cyan wireframe kutu olarak çizer.
    /// 
    /// Neden faydalı?
    ///   - Sprite extents ve padding'in doğru hesaplandığını görsel olarak doğrularsın.
    ///   - Farklı aspect ratio'larda (16:9, 18:9, 20:9) sınırların nasıl değiştiğini
    ///     Game view'ı resize ederek anlık görebilirsin.
    ///   - Ekip arkadaşların kamerayı veya sprite'ı değiştirdiğinde sınırların
    ///     hâlâ doğru olup olmadığını hemen fark edersin.
    /// 
    /// Runtime'da (Play mode) hesaplanan _bounds değerlerini kullanır.
    /// Edit mode'da (Play değilken) anlık hesaplama yapar.
    /// Build'de bu method tamamen strip edilir — #if UNITY_EDITOR bloğu sayesinde.
    /// </summary>
    private void OnDrawGizmos()
    {
        float minX, maxX, minY, maxY;

        if (Application.isPlaying)
        {
            // Play mode: Zaten hesaplanmış sınırları kullan
            minX = _boundsMinX;
            maxX = _boundsMaxX;
            minY = _boundsMinY;
            maxY = _boundsMaxY;
        }
        else
        {
            // Edit mode: Anlık hesapla (cache'lenmiş değerler yoktur)
            Camera cam = Camera.main;
            if (cam == null) return;

            // Sprite extents'i hesapla
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

            // Güvenlik kontrolü
            if (minX > maxX) { float mid = (minX + maxX) * 0.5f; minX = mid; maxX = mid; }
            if (minY > maxY) { float mid = (minY + maxY) * 0.5f; minY = mid; maxY = mid; }
        }

        // ── Wireframe kutuyu çiz ──
        Gizmos.color = new Color(0f, 1f, 1f, 0.8f); // Cyan, yarı saydam

        // Kutunun 4 köşesi (Z = objenin Z'si)
        float z = Application.isPlaying
            ? _cachedTransform.position.z
            : transform.position.z;

        Vector3 topLeft     = new Vector3(minX, maxY, z);
        Vector3 topRight    = new Vector3(maxX, maxY, z);
        Vector3 bottomRight = new Vector3(maxX, minY, z);
        Vector3 bottomLeft  = new Vector3(minX, minY, z);

        // 4 kenarı çiz
        Gizmos.DrawLine(topLeft,     topRight);     // Üst kenar
        Gizmos.DrawLine(topRight,    bottomRight);  // Sağ kenar
        Gizmos.DrawLine(bottomRight, bottomLeft);   // Alt kenar
        Gizmos.DrawLine(bottomLeft,  topLeft);       // Sol kenar

        // Köşelere küçük işaretler (noktasal referans)
        float markerSize = 0.1f;
        Gizmos.DrawWireSphere(topLeft,     markerSize);
        Gizmos.DrawWireSphere(topRight,    markerSize);
        Gizmos.DrawWireSphere(bottomRight, markerSize);
        Gizmos.DrawWireSphere(bottomLeft,  markerSize);

        // ── Ekranın gerçek kenarlarını da çiz (karşılaştırma için) ──
        // Böylece padding'in ne kadar alan kapladığı görsel olarak anlaşılır
        Gizmos.color = new Color(1f, 1f, 1f, 0.15f); // Beyaz, çok soluk

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
