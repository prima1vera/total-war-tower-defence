using System.Collections.Generic;
using UnityEngine;

public static class EnemyPathBlockerRegistry
{
    private static readonly List<IEnemyPathBlocker> Blockers = new List<IEnemyPathBlocker>(64);
    private static readonly HashSet<IEnemyPathBlocker> BlockerSet = new HashSet<IEnemyPathBlocker>();

    public static int Version { get; private set; }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void Initialize()
    {
        Clear();
    }

    public static void Register(IEnemyPathBlocker blocker)
    {
        if (blocker == null || !BlockerSet.Add(blocker))
            return;

        Blockers.Add(blocker);
        Version++;
    }

    public static void Unregister(IEnemyPathBlocker blocker)
    {
        if (blocker == null || !BlockerSet.Remove(blocker))
            return;

        if (Blockers.Remove(blocker))
            Version++;
    }

    public static void Clear()
    {
        if (Blockers.Count == 0 && BlockerSet.Count == 0)
            return;

        Blockers.Clear();
        BlockerSet.Clear();
        Version++;
    }

    public static bool TryGetFirstBlockingTarget(
        Vector2 origin,
        Vector2 moveDirection,
        float scanRange,
        float laneHalfWidth,
        out IEnemyPathBlocker blocker)
    {
        blocker = null;
        if (scanRange <= 0f || laneHalfWidth < 0f || Blockers.Count == 0)
            return false;

        Vector2 forward = moveDirection.sqrMagnitude > 0.0001f ? moveDirection.normalized : Vector2.down;
        float bestProjection = float.MaxValue;
        int bestPriority = int.MinValue;
        bool removedInvalidEntries = false;

        for (int i = Blockers.Count - 1; i >= 0; i--)
        {
            IEnemyPathBlocker candidate = Blockers[i];
            if (candidate == null)
            {
                Blockers.RemoveAt(i);
                removedInvalidEntries = true;
                continue;
            }

            if (!candidate.IsBlocking)
                continue;

            float candidateRadius = Mathf.Max(0.05f, candidate.BlockRadius);
            Vector2 toCandidate = candidate.WorldPosition - origin;

            float projection = Vector2.Dot(toCandidate, forward);
            if (projection < -candidateRadius || projection > scanRange)
                continue;

            float perpendicular = Mathf.Abs(Cross(forward, toCandidate));
            float allowedHalfWidth = laneHalfWidth + candidateRadius;
            if (perpendicular > allowedHalfWidth)
                continue;

            if (projection < bestProjection - 0.0001f)
            {
                blocker = candidate;
                bestProjection = projection;
                bestPriority = candidate.PathPriority;
                continue;
            }

            if (Mathf.Abs(projection - bestProjection) <= 0.0001f && candidate.PathPriority > bestPriority)
            {
                blocker = candidate;
                bestPriority = candidate.PathPriority;
            }
        }

        if (removedInvalidEntries)
            RebuildSetAndVersion();

        return blocker != null;
    }

    private static float Cross(Vector2 a, Vector2 b)
    {
        return a.x * b.y - a.y * b.x;
    }

    private static void RebuildSetAndVersion()
    {
        BlockerSet.Clear();
        for (int i = 0; i < Blockers.Count; i++)
        {
            IEnemyPathBlocker blocker = Blockers[i];
            if (blocker == null)
                continue;

            BlockerSet.Add(blocker);
        }

        Version++;
    }
}

