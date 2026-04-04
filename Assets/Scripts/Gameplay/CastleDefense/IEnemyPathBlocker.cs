using UnityEngine;

public interface IEnemyPathBlocker
{
    bool IsBlocking { get; }
    Vector2 WorldPosition { get; }
    float BlockRadius { get; }
    int PathPriority { get; }
    void ReceiveBlockDamage(int amount, UnitHealth attacker);
}

