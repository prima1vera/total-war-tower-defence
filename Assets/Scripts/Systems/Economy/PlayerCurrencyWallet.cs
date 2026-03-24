using System;
using UnityEngine;

public class PlayerCurrencyWallet : MonoBehaviour, ICurrencyWallet
{
    [SerializeField, Min(0)] private int startingBalance = 120;

    public int StartingBalance => Mathf.Max(0, startingBalance);
    public int Balance { get; private set; }
    public event Action<int> BalanceChanged;

    private bool initialized;

    private void Awake()
    {
        if (initialized)
            return;

        Balance = StartingBalance;
        initialized = true;
        BalanceChanged?.Invoke(Balance);
    }

    public bool CanAfford(int amount)
    {
        return Balance >= Mathf.Max(0, amount);
    }

    public bool TrySpend(int amount)
    {
        int cost = Mathf.Max(0, amount);
        if (cost == 0)
            return true;

        if (Balance < cost)
            return false;

        Balance -= cost;
        BalanceChanged?.Invoke(Balance);
        return true;
    }

    public void Add(int amount)
    {
        int delta = Mathf.Max(0, amount);
        if (delta == 0)
            return;

        Balance += delta;
        BalanceChanged?.Invoke(Balance);
    }

    /// <summary>
    /// Restores balance from persistent save data.
    /// </summary>
    public void RestoreBalance(int amount, bool notify = true)
    {
        Balance = Mathf.Max(0, amount);
        initialized = true;

        if (notify)
            BalanceChanged?.Invoke(Balance);
    }

#if UNITY_EDITOR
    [ContextMenu("Debug/Add 500 Gold")]
    private void DebugAddGold()
    {
        Add(500);
    }

    [ContextMenu("Debug/Reset Balance To Starting")]
    private void DebugResetBalance()
    {
        RestoreBalance(StartingBalance);
    }
#endif
}
