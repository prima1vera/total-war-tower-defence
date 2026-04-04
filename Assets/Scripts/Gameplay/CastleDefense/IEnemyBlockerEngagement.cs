public interface IEnemyBlockerEngagement
{
    bool CanAcceptBlockerAttacker(UnitHealth attacker);
    bool HasBlockerAttacker(UnitHealth attacker);
    bool TryAcquireBlockerAttacker(UnitHealth attacker);
    void ReleaseBlockerAttacker(UnitHealth attacker);
}

