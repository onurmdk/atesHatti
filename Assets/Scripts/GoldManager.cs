using UnityEngine;
using System;

public class GoldManager : MonoBehaviour
{
    public static GoldManager Instance { get; private set; }

    public event Action<int> OnGoldChanged;

    public event Action<int> OnRunGoldChanged;

    private int _currentGold;

    private int _currentRunGold;

    private int _totalGoldEarned;

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;
    }

    private void Start()
    {
        if (SaveManager.Instance != null)
        {
            _currentGold = SaveManager.Instance.PersistentGold;

            #if UNITY_EDITOR || DEVELOPMENT_BUILD
            Debug.Log($"[GoldManager] Kayıtlı altın yüklendi: {_currentGold}");
            #endif

            OnGoldChanged?.Invoke(_currentGold);
        }

        SubscribeToCombatEvents();
    }

    private void OnDestroy()
    {
        UnsubscribeFromCombatEvents();

        if (Instance == this)
            Instance = null;
    }

    private void SubscribeToCombatEvents()
    {
        if (CombatManager.Instance != null)
        {
            CombatManager.Instance.OnEnemyKilled += HandleEnemyKilled;
        }
        else
        {
            #if UNITY_EDITOR || DEVELOPMENT_BUILD
            Debug.LogWarning("[GoldManager] CombatManager.Instance bulunamadı! " +
                             "Altın toplama çalışmayacak.", this);
            #endif
        }
    }

    private void UnsubscribeFromCombatEvents()
    {
        if (CombatManager.Instance != null)
        {
            CombatManager.Instance.OnEnemyKilled -= HandleEnemyKilled;
        }
    }

    private void HandleEnemyKilled(int goldValue, Vector3 position)
    {
        AddGold(goldValue);
    }

    public void AddGold(int amount)
    {
        if (amount <= 0) return;

        _currentGold     += amount;
        _totalGoldEarned += amount;
        _currentRunGold  += amount;

        PersistCurrentGold();

        OnGoldChanged?.Invoke(_currentGold);
        OnRunGoldChanged?.Invoke(_currentRunGold);
    }

    public bool TrySpendGold(int amount)
    {
        if (amount <= 0 || _currentGold < amount)
            return false;

        _currentGold -= amount;

        PersistCurrentGold();

        OnGoldChanged?.Invoke(_currentGold);
        return true;
    }

    private void PersistCurrentGold()
    {
        if (SaveManager.Instance != null)
        {
            SaveManager.Instance.SetPersistentGold(_currentGold);
            SaveManager.Instance.Save();
        }
    }

    public int CurrentGold => _currentGold;

    public int CurrentRunGold => _currentRunGold;

    public int TotalGoldEarned => _totalGoldEarned;

    public bool CanAfford(int amount) => _currentGold >= amount;

    public void ResetGold()
    {
        _currentGold     = 0;
        _totalGoldEarned = 0;
        _currentRunGold  = 0;

        OnGoldChanged?.Invoke(_currentGold);
        OnRunGoldChanged?.Invoke(0);
    }
}