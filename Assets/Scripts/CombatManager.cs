using UnityEngine;
using System;

/// <summary>
/// ╔══════════════════════════════════════════════════════════════════╗
///   ATEŞ HATTI — CombatManager (Merkezi Çarpışma Yöneticisi)
///   
///   Tüm combat mantığını tek bir yerde toplayan merkezi yönetici.
///   Prefab'lardaki CombatRelay bileşenleri, çarpışma event'lerini
///   bu manager'a yönlendirir.
/// ╚══════════════════════════════════════════════════════════════════╝
/// 
/// Mimari: Mediator Pattern
/// ────────────────────────
/// Geleneksel yaklaşımda her prefab kendi OnTriggerEnter2D'sini yönetir:
///   Bullet.OnTriggerEnter2D → düşmana hasar ver
///   Enemy.OnTriggerEnter2D  → oyuncuya hasar ver
/// Bu combat mantığını 3-4 dosyaya dağıtır, değişiklik riskli olur.
/// 
/// Mediator yaklaşımında:
///   CombatRelay.OnTriggerEnter2D → CombatManager.ProcessCollision()
///   Tüm "kim kime ne yaptı" mantığı tek yerde.
///   Yeni düşman tipi, yeni silah tipi eklemek → sadece bu dosya değişir.
/// 
/// CombatRelay nedir?
/// → Prefab'lara eklenen 1 bileşenlik hafif bir script.
///   Sadece OnTriggerEnter2D'yi yakalar ve CombatManager'a iletir.
///   Kendi başına hiçbir logic içermez.
/// 
/// Tag Sistemi:
/// ───────────
/// Unity Tag'leri ile obje tipini belirleriz:
///   "PlayerBullet" → Oyuncu mermisi
///   "Enemy"        → Düşman
///   "Player"       → Oyuncu gemisi
/// CompareTag() kullanılır (== operatörü string allocation yapar, CompareTag yapmaz).
/// </summary>
public class CombatManager : MonoBehaviour
{
    // ════════════════════════════════════════════════════════════════
    //  SINGLETON
    // ════════════════════════════════════════════════════════════════

    public static CombatManager Instance { get; private set; }

    // ════════════════════════════════════════════════════════════════
    //  REFERANSLAR
    // ════════════════════════════════════════════════════════════════

    [Header("─── Oyuncu Referansı ───")]
    [Tooltip("Sahneye yerleştirilmiş Player objesi.\n" +
             "PlayerHealth bileşenine erişim için gerekli.")]
    [SerializeField]
    private PlayerHealth _playerHealth;

    [Header("─── Hasar Ayarları ───")]
    [Tooltip("Düşmanın oyuncuya body collision ile verdiği hasar.")]
    [SerializeField]
    private float _enemyBodyDamage = 1f;

    [Tooltip("Oyuncu mermisinin base hasarı.\n" +
             "İleride PlayerShooting'den okunacak (upgrade sistemi).")]
    [SerializeField]
    private float _bulletBaseDamage = 1f;

    // ════════════════════════════════════════════════════════════════
    //  EVENTS — Decoupled İletişim
    // ════════════════════════════════════════════════════════════════

    /// <summary>
    /// Düşman öldürüldüğünde tetiklenir.
    /// Parametreler: (goldValue, worldPosition)
    /// Dinleyiciler: 
    ///   - GoldManager (altın ekleme)
    ///   - ScoreManager (skor güncelleme)  
    ///   - UI (kill counter)
    ///   - Ses sistemi (patlama sesi)
    /// </summary>
    public event Action<int, Vector3> OnEnemyKilled;

    /// <summary>
    /// Düşmana hasar verildiğinde (ölmeden) tetiklenir.
    /// Parametre: (worldPosition)
    /// Dinleyiciler: Ses sistemi (hit sound), combo sistemi
    /// </summary>
    public event Action<Vector3> OnEnemyHit;

    /// <summary>
    /// Oyuncu hasar aldığında tetiklenir.
    /// Parametre: (worldPosition)
    /// Dinleyiciler: Kamera shake, UI flash, ses sistemi
    /// </summary>
    public event Action<Vector3> OnPlayerHit;

    // ════════════════════════════════════════════════════════════════
    //  UNITY LIFECYCLE
    // ════════════════════════════════════════════════════════════════

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;

        ValidateSetup();
    }

    private void OnDestroy()
    {
        if (Instance == this)
            Instance = null;
    }

    // ════════════════════════════════════════════════════════════════
    //  ÇARPIŞMA İŞLEME — CombatRelay Tarafından Çağrılır
    // ════════════════════════════════════════════════════════════════

    /// <summary>
    /// CombatRelay.OnTriggerEnter2D'den yönlendirilen merkezi çarpışma işleyici.
    /// 
    /// Tag-based routing ile kim kime çarptığını belirler:
    /// 
    /// ┌──────────────┬──────────────┬────────────────────────────────┐
    /// │ Reporter Tag  │ Other Tag    │ Sonuç                         │
    /// ├──────────────┼──────────────┼────────────────────────────────┤
    /// │ PlayerBullet  │ Enemy        │ Düşmana hasar ver             │
    /// │ Enemy         │ Player       │ Oyuncuya hasar ver            │
    /// │ Player        │ Enemy        │ Oyuncuya hasar ver (tersi)    │
    /// │ PlayerBullet  │ Player       │ Yoksay (kendi mermimiz)       │
    /// └──────────────┴──────────────┴────────────────────────────────┘
    /// 
    /// Neden CompareTag?
    /// → gameObject.tag == "Enemy" → string comparison + potential GC allocation
    /// → gameObject.CompareTag("Enemy") → internal optimized comparison, SIFIR GC
    /// 
    /// Performans: Tag karşılaştırma chain'i en olası çarpışmadan başlar.
    /// Mermi-düşman çarpışması en sık olan — ilk kontrol edilir.
    /// </summary>
    /// <param name="reporter">Çarpışmayı bildiren obje (CombatRelay'in sahibi)</param>
    /// <param name="other">Çarpışılan obje</param>
    public void ProcessCollision(GameObject reporter, Collider2D other)
    {
        // ════════════════════════════════════════
        //  DURUM 1: Mermi → Düşman
        // ════════════════════════════════════════
        if (reporter.CompareTag("PlayerBullet") && other.CompareTag("Enemy"))
        {
            HandleBulletHitsEnemy(reporter, other);
            return;
        }

        // ════════════════════════════════════════
        //  DURUM 2: Düşman → Oyuncu (veya Oyuncu → Düşman)
        // ════════════════════════════════════════
        // İki yönlü kontrol: Unity hangi objenin OnTriggerEnter2D'sini
        // çağıracağını Rigidbody konfigürasyonuna göre belirler.
        // Her iki yönü de yakalamamız gerekir.
        if (reporter.CompareTag("Enemy") && other.CompareTag("Player"))
        {
            HandleEnemyHitsPlayer(reporter, other.gameObject);
            return;
        }

        if (reporter.CompareTag("Player") && other.CompareTag("Enemy"))
        {
            HandleEnemyHitsPlayer(other.gameObject, reporter);
            return;
        }
    }

    // ════════════════════════════════════════════════════════════════
    //  ÇARPIŞMA SENARYOLARI
    // ════════════════════════════════════════════════════════════════

    /// <summary>
    /// Oyuncu mermisi düşmana isabet etti.
    /// 
    /// Akış:
    /// 1. Mermiyi pool'a iade et
    /// 2. Düşmana hasar ver
    /// 3. Düşman öldüyse → büyük patlama + OnEnemyKilled event
    /// 4. Düşman hayattaysa → küçük kıvılcım + OnEnemyHit event
    /// </summary>
    private void HandleBulletHitsEnemy(GameObject bulletObj, Collider2D enemyCollider)
    {
        // ── Bileşenleri al ──
        // GetComponent her çarpışmada çağrılır — bu kaçınılmaz.
        // Ama OnTriggerEnter2D zaten physics frame'de çağrılır (FixedUpdate hızında),
        // 60fps'de bile saniyede ~50 çağrı. GetComponent burada kabul edilebilir.
        Bullet bullet = bulletObj.GetComponent<Bullet>();
        Enemy  enemy  = enemyCollider.GetComponent<Enemy>();

        if (bullet == null || enemy == null) return;
        if (!enemy.IsAlive) return;

        // ── Mermiyi iade et ──
        bullet.OnHitTarget();

        // ── Düşmana hasar ver ──
        Vector3 hitPos = bulletObj.transform.position;
        bool killed = enemy.TakeDamage(_bulletBaseDamage);

        if (killed)
        {
            // ── Düşman öldü: Büyük patlama ──
            Vector3 enemyPos = enemyCollider.transform.position;
            Color enemyColor = enemyCollider.GetComponent<SpriteRenderer>()?.color ?? Color.red;

            // Patlama efekti
            if (ParticleManager.Instance != null)
                ParticleManager.Instance.PlayExplosion(enemyPos, enemyColor);

            // Event: Düşman öldürüldü (gold, skor, UI güncellemesi için)
            OnEnemyKilled?.Invoke(enemy.GoldValue, enemyPos);

            #if UNITY_EDITOR || DEVELOPMENT_BUILD
            Debug.Log($"[Combat] Düşman öldürüldü! +{enemy.GoldValue} Gold");
            #endif
        }
        else
        {
            // ── Düşman hayatta: Küçük kıvılcım ──
            if (ParticleManager.Instance != null)
                ParticleManager.Instance.PlaySpark(hitPos, Color.yellow);

            // Event: İsabet (ses, combo vb.)
            OnEnemyHit?.Invoke(hitPos);
        }
    }

    /// <summary>
    /// Düşman oyuncuya body collision yaptı.
    /// 
    /// Akış:
    /// 1. Oyuncuya hasar ver (iFrame kontrolü PlayerHealth'te)
    /// 2. Hasar verildiyse → düşmanı pool'a iade et + patlama
    /// 3. Hasar verilmediyse (iFrame aktif) → hiçbir şey yapma
    /// 
    /// Neden düşman sadece hasar verildiğinde iade ediliyor?
    /// → iFrame sırasında düşman oyuncunun üzerinden geçmeli.
    ///   Her temasda düşmanı yok etsek, iFrame anlamsız kalır
    ///   çünkü düşman zaten yok olur.
    /// </summary>
    private void HandleEnemyHitsPlayer(GameObject enemyObj, GameObject playerObj)
    {
        if (_playerHealth == null || !_playerHealth.IsAlive) return;

        Enemy enemy = enemyObj.GetComponent<Enemy>();
        if (enemy == null || !enemy.IsAlive) return;

        // Hasar vermeyi dene (iFrame aktifse false döner)
        bool damageApplied = _playerHealth.TakeDamage(_enemyBodyDamage);

        if (damageApplied)
        {
            Vector3 contactPos = enemyObj.transform.position;

            // Düşmanı iade et
            enemy.TakeDamage(float.MaxValue); // Anında öldür → pool'a iade

            // Patlama efekti (düşman rengiyle)
            Color enemyColor = enemyObj.GetComponent<SpriteRenderer>()?.color ?? Color.red;
            if (ParticleManager.Instance != null)
                ParticleManager.Instance.PlayExplosion(contactPos, enemyColor);

            // Event: Oyuncu hasar aldı
            OnPlayerHit?.Invoke(contactPos);
        }
    }

    // ════════════════════════════════════════════════════════════════
    //  PUBLIC API
    // ════════════════════════════════════════════════════════════════

    /// <summary>
    /// Mermi base hasarını günceller. Upgrade sistemi tarafından çağrılır.
    /// </summary>
    public void SetBulletDamage(float newDamage)
    {
        _bulletBaseDamage = Mathf.Max(0.1f, newDamage);
    }

    /// <summary>Mevcut mermi hasarı.</summary>
    public float BulletDamage => _bulletBaseDamage;

    // ════════════════════════════════════════════════════════════════
    //  DOĞRULAMA
    // ════════════════════════════════════════════════════════════════

    [System.Diagnostics.Conditional("UNITY_EDITOR"),
     System.Diagnostics.Conditional("DEVELOPMENT_BUILD")]
    private void ValidateSetup()
    {
        if (_playerHealth == null)
            Debug.LogError(
                "[CombatManager] PlayerHealth referansı atanmamış! " +
                "Inspector'da _playerHealth alanını doldurun.", this);
    }
}

// ════════════════════════════════════════════════════════════════════════
//  COMBAT RELAY — Prefab'lara Eklenen Hafif Çarpışma Yönlendirici
// ════════════════════════════════════════════════════════════════════════

/// <summary>
/// ╔══════════════════════════════════════════════════════════════════╗
///   CombatRelay — Çarpışma Event'i Yönlendirici
///   
///   Bullet, Enemy ve Player prefab'larına eklenen minimal bileşen.
///   Tek görevi: OnTriggerEnter2D'yi CombatManager'a yönlendirmek.
/// ╚══════════════════════════════════════════════════════════════════╝
/// 
/// Neden bu bileşen gerekli?
/// ─────────────────────────
/// Unity'de OnTriggerEnter2D, çarpışan objelerin BİRİNDE olmalıdır.
/// CombatManager sahnede ayrı bir obje — collider'lar ona bildirim göndermez.
/// Bu relay, çarpışmayı yakalayıp merkeze iletir.
/// 
/// Bellek maliyeti: Obje başına ~16 byte (MonoBehaviour overhead).
/// Logic maliyeti: Tek bir null check + method call.
/// GC maliyeti: SIFIR.
/// 
/// KURULUM:
/// ────────
/// Bu bileşeni şu prefab'lara ekle:
///   ✓ Bullet prefab    (Tag: "PlayerBullet")
///   ✓ Enemy prefab     (Tag: "Enemy")
///   ✓ Player objesi    (Tag: "Player")
/// </summary>

