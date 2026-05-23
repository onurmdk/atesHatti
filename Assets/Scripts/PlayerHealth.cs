using UnityEngine;
using System;

/// <summary>
/// ╔══════════════════════════════════════════════════════════════════╗
///   ATEŞ HATTI — PlayerHealth (Oyuncu Sağlık Sistemi)
///   
///   Oyuncunun HP yönetimi, hasar alma, iFrame (dokunulmazlık) 
///   ve ölüm tetiklemesini yönetir.
/// ╚══════════════════════════════════════════════════════════════════╝
/// 
/// Sorumluluk (SRP): SADECE HP + iFrame + Ölüm.
/// Hareket PlayerController'da, ateş PlayerShooting'de.
/// 
/// iFrame (Invincibility Frame) Sistemi:
/// ─────────────────────────────────────
/// Oyuncu hasar alınca kısa bir süre dokunulmaz olur.
/// Bu olmadan düşman-oyuncu overlap'inde her frame hasar verilir
/// ve HP tek frame'de sıfırlanır — oynanamaz.
/// 
/// Görsel geri bildirim: iFrame süresince SpriteRenderer
/// açılıp kapatılarak "blink" efekti oluşturulur.
/// Blink frequency sabit — frame-rate bağımsız (Time.time tabanlı).
/// </summary>
[RequireComponent(typeof(SpriteRenderer))]
public class PlayerHealth : MonoBehaviour
{
    // ════════════════════════════════════════════════════════════════
    //  INSPECTOR AYARLARI
    // ════════════════════════════════════════════════════════════════

    [Header("─── Sağlık ───")]
    [Tooltip("Başlangıç / maksimum HP. Upgrade sistemi bu değeri artıracak.")]
    [SerializeField]
    private float _maxHp = 10f;

    [Header("─── Dokunulmazlık (iFrame) ───")]
    [Tooltip("Hasar aldıktan sonra dokunulmaz kalınan süre (saniye).")]
    [SerializeField, Range(0.1f, 2f)]
    private float _iFrameDuration = 0.6f;

    [Tooltip("Blink hızı (saniyede kaç kez yanıp söner).\n" +
             "12 = saniyede 12 blink → 60fps'de her 5 frame'de bir.")]
    [SerializeField, Range(4f, 24f)]
    private float _blinkFrequency = 12f;

    // ════════════════════════════════════════════════════════════════
    //  EVENTS — Decoupled İletişim
    // ════════════════════════════════════════════════════════════════

    /// <summary>
    /// Oyuncu hasar aldığında tetiklenir.
    /// Parametreler: (currentHp, maxHp)
    /// Dinleyiciler: HUDController (HP bar güncelleme), kamera shake vb.
    /// 
    /// Neden C# Action ve UnityEvent değil?
    /// → Action: Zero GC, compile-time type safety, minimal overhead.
    /// → UnityEvent: Inspector'dan bağlanabilir ama her Invoke'ta
    ///   reflection + boxing yapar, GC üretir.
    ///   Mobilde her hasar alımında GC spike istemiyoruz.
    /// </summary>
    public event Action<float, float> OnDamageTaken;

    /// <summary>
    /// Oyuncu öldüğünde tetiklenir.
    /// Dinleyiciler: GameManager (Game Over state), UI, Analytics.
    /// </summary>
    public event Action OnPlayerDeath;

    /// <summary>
    /// HP değiştiğinde tetiklenir (hasar, heal, upgrade).
    /// Parametreler: (currentHp, maxHp)
    /// Dinleyiciler: HUD HP göstergesi.
    /// </summary>
    public event Action<float, float> OnHpChanged;

    // ════════════════════════════════════════════════════════════════
    //  STATE
    // ════════════════════════════════════════════════════════════════

    private float _currentHp;
    private float _iFrameTimer;     // Kalan dokunulmazlık süresi
    private bool  _isInvincible;    // Hızlı kontrol flag'i
    private bool  _isDead;

    // ════════════════════════════════════════════════════════════════
    //  CACHE
    // ════════════════════════════════════════════════════════════════

    private SpriteRenderer _spriteRenderer;

    // ════════════════════════════════════════════════════════════════
    //  UNITY LIFECYCLE
    // ════════════════════════════════════════════════════════════════

    private void Awake()
    {
        _spriteRenderer = GetComponent<SpriteRenderer>();
        InitializeHealth();
    }

    /// <summary>
    /// iFrame countdown ve blink efekti.
    /// GC Allocation: SIFIR.
    /// </summary>
    private void Update()
    {
        if (!_isInvincible) return;

        _iFrameTimer -= Time.deltaTime;

        if (_iFrameTimer <= 0f)
        {
            // Dokunulmazlık bitti
            _isInvincible = false;
            _spriteRenderer.enabled = true; // Sprite'ı kesinlikle aç
            return;
        }

        // ── Blink efekti ──
        // Time.time * frequency → sürekli artan değer
        // Mathf.Sin → -1 ile +1 arası sinüs dalgası
        // > 0 kontrolü → sprite açık/kapalı (boolean toggle)
        // 
        // Neden frame counter değil de Time.time?
        // → Frame counter frame-rate bağımlı: 30fps'de yavaş blink, 60fps'de hızlı.
        //   Time.time her FPS'de aynı görsel hızda blink verir.
        // 
        // Neden Mathf.Floor(Time.time * freq) % 2 değil?
        // → Sin daha yumuşak geçiş sağlar ve branching cost'u aynı.
        //   Integer modulo'da edge case'ler var (float precision).
        _spriteRenderer.enabled = Mathf.Sin(Time.time * _blinkFrequency * Mathf.PI * 2f) > 0f;
    }

    // ════════════════════════════════════════════════════════════════
    //  SAĞLIK YÖNETİMİ
    // ════════════════════════════════════════════════════════════════

    /// <summary>HP'yi başlangıç değerine set eder.</summary>
    private void InitializeHealth()
    {
        _currentHp   = _maxHp;
        _isDead      = false;
        _isInvincible = false;
        _iFrameTimer = 0f;
    }

    /// <summary>
    /// Oyuncuya hasar verir.
    /// 
    /// CombatManager tarafından çağrılır.
    /// 
    /// Dönüş: true = hasar verildi, false = dokunulmazlık aktifti veya zaten ölü.
    /// Bu bilgi CombatManager'da "düşmanı pool'a iade et mi" kararı için kullanılır.
    /// </summary>
    public bool TakeDamage(float damage)
    {
        // Zaten ölüyse veya dokunulmaz ise hasar verme
        if (_isDead || _isInvincible)
            return false;

        _currentHp -= damage;

        // Event: Hasar alındı (UI güncelleme vb.)
        OnDamageTaken?.Invoke(_currentHp, _maxHp);
        OnHpChanged?.Invoke(_currentHp, _maxHp);

        if (_currentHp <= 0f)
        {
            _currentHp = 0f;
            Die();
            return true;
        }

        // Dokunulmazlık başlat
        StartIFrames();
        return true;
    }

    /// <summary>
    /// iFrame süresini başlatır.
    /// </summary>
    private void StartIFrames()
    {
        _isInvincible = true;
        _iFrameTimer  = _iFrameDuration;
    }

    /// <summary>
    /// Ölüm işlemi.
    /// Event tetikler, objeyi deaktif eder.
    /// GameManager bu event'i dinleyip Game Over state'ine geçecek.
    /// </summary>
    private void Die()
    {
        _isDead = true;
        _isInvincible = false;
        _spriteRenderer.enabled = true; // Blink'i durdur

        #if UNITY_EDITOR || DEVELOPMENT_BUILD
        Debug.Log("[PlayerHealth] GAME OVER — Oyuncu öldü!");
        #endif

        // Event: Ölüm (GameManager dinler)
        OnPlayerDeath?.Invoke();

        // Objeyi deaktif et (PlayerController.Update ve PlayerShooting.Update durur)
        gameObject.SetActive(false);
    }

    // ════════════════════════════════════════════════════════════════
    //  PUBLIC API
    // ════════════════════════════════════════════════════════════════

    /// <summary>Mevcut HP</summary>
    public float CurrentHp => _currentHp;

    /// <summary>Maksimum HP</summary>
    public float MaxHp => _maxHp;

    /// <summary>Oyuncu hayatta mı?</summary>
    public bool IsAlive => !_isDead;

    /// <summary>Oyuncu şu an dokunulmaz mı?</summary>
    public bool IsInvincible => _isInvincible;

    /// <summary>
    /// Maksimum HP'yi günceller (Upgrade sistemi için).
    /// Fark kadar mevcut HP'yi de artırır — upgrade anında HP dolsun.
    /// </summary>
    public void UpgradeMaxHp(float newMaxHp)
    {
        float diff = newMaxHp - _maxHp;
        _maxHp = newMaxHp;

        if (diff > 0f)
            _currentHp = Mathf.Min(_maxHp, _currentHp + diff);

        OnHpChanged?.Invoke(_currentHp, _maxHp);
    }

    /// <summary>
    /// Tüm sağlığı sıfırlar. Yeni oyun başlangıcında çağrılır.
    /// </summary>
    public void ResetHealth(float maxHp)
    {
        _maxHp = maxHp;
        InitializeHealth();
        _spriteRenderer.enabled = true;
        gameObject.SetActive(true);
        OnHpChanged?.Invoke(_currentHp, _maxHp);
    }

}
