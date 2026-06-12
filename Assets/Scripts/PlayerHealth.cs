using UnityEngine;
using System;

[RequireComponent(typeof(SpriteRenderer))]
public class PlayerHealth : MonoBehaviour
{
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

    public event Action<float, float> OnDamageTaken;

    public event Action OnPlayerDeath;

    public event Action<float, float> OnHpChanged;

    private float _currentHp;
    private float _iFrameTimer;     
    private bool  _isInvincible;    
    private bool  _isDead;

    private SpriteRenderer _spriteRenderer;

    private void Awake()
    {
        _spriteRenderer = GetComponent<SpriteRenderer>();
        InitializeHealth();
    }

    private void Update()
    {
        if (!_isInvincible) return;

        _iFrameTimer -= Time.deltaTime;

        if (_iFrameTimer <= 0f)
        {
            _isInvincible = false;
            _spriteRenderer.enabled = true; 
            return;
        }

        _spriteRenderer.enabled = Mathf.Sin(Time.time * _blinkFrequency * Mathf.PI * 2f) > 0f;
    }

    private void InitializeHealth()
    {
        _currentHp   = _maxHp;
        _isDead      = false;
        _isInvincible = false;
        _iFrameTimer = 0f;
    }

    public bool TakeDamage(float damage)
    {
        if (_isDead || _isInvincible)
            return false;

        _currentHp -= damage;

        OnDamageTaken?.Invoke(_currentHp, _maxHp);
        OnHpChanged?.Invoke(_currentHp, _maxHp);

        if (_currentHp <= 0f)
        {
            _currentHp = 0f;
            Die();
            return true;
        }

        StartIFrames();
        return true;
    }

    private void StartIFrames()
    {
        _isInvincible = true;
        _iFrameTimer  = _iFrameDuration;
    }

    private void Die()
    {
        _isDead = true;
        _isInvincible = false;
        _spriteRenderer.enabled = true; 

        #if UNITY_EDITOR || DEVELOPMENT_BUILD
        Debug.Log("[PlayerHealth] GAME OVER — Oyuncu öldü!");
        #endif

        OnPlayerDeath?.Invoke();

        gameObject.SetActive(false);
    }

    public float CurrentHp => _currentHp;

    public float MaxHp => _maxHp;

    public bool IsAlive => !_isDead;

    public bool IsInvincible => _isInvincible;

    public void UpgradeMaxHp(float newMaxHp)
    {
        float diff = newMaxHp - _maxHp;
        _maxHp = newMaxHp;

        if (diff > 0f)
            _currentHp = Mathf.Min(_maxHp, _currentHp + diff);

        OnHpChanged?.Invoke(_currentHp, _maxHp);
    }

    public void ResetHealth(float maxHp)
    {
        _maxHp = maxHp;
        InitializeHealth();
        _spriteRenderer.enabled = true;
        gameObject.SetActive(true);
        OnHpChanged?.Invoke(_currentHp, _maxHp);
    }
}