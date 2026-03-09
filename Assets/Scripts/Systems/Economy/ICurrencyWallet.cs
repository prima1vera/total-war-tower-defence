using System;

public interface ICurrencyWallet
{
    int Balance { get; }
    event Action<int> BalanceChanged;
    bool CanAfford(int amount);
    bool TrySpend(int amount);
    void Add(int amount);
}
