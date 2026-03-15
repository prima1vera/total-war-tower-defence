using System;
using UnityEngine;

public enum TowerArcherAimBand
{
    Down = 0,
    Side = 1,
    Up = 2
}

[CreateAssetMenu(fileName = "TowerArcherVisualProfile", menuName = "TWTD/Towers/Archer Visual Profile")]
public sealed class TowerArcherVisualProfile : ScriptableObject
{
    [SerializeField, Min(1f)] private float idleFps = 6f;
    [SerializeField, Min(1f)] private float preAttackFps = 12f;
    [SerializeField, Min(1f)] private float attackFps = 14f;
    [SerializeField] private TowerArcherLevelVisual[] levels = Array.Empty<TowerArcherLevelVisual>();

    public float IdleFps => Mathf.Max(1f, idleFps);
    public float PreAttackFps => Mathf.Max(1f, preAttackFps);
    public float AttackFps => Mathf.Max(1f, attackFps);

    public bool TryGetLevel(int requestedLevel, out TowerArcherLevelVisual levelVisual)
    {
        levelVisual = null;

        if (levels == null || levels.Length == 0)
            return false;

        int targetLevel = Mathf.Max(1, requestedLevel);

        TowerArcherLevelVisual exact = null;
        TowerArcherLevelVisual fallback = null;

        for (int i = 0; i < levels.Length; i++)
        {
            TowerArcherLevelVisual current = levels[i];
            if (current == null)
                continue;

            if (current.Level == targetLevel)
            {
                exact = current;
                break;
            }

            if (current.Level <= targetLevel)
            {
                if (fallback == null || current.Level > fallback.Level)
                    fallback = current;
            }
        }

        if (exact != null)
        {
            levelVisual = exact;
            return true;
        }

        if (fallback != null)
        {
            levelVisual = fallback;
            return true;
        }

        levelVisual = levels[0];
        return levelVisual != null;
    }

#if UNITY_EDITOR
    public void EditorSetLevelSprites(
        int level,
        Sprite[] downIdle,
        Sprite[] downPreAttack,
        Sprite[] downAttack,
        Sprite[] sideIdle,
        Sprite[] sidePreAttack,
        Sprite[] sideAttack,
        Sprite[] upIdle,
        Sprite[] upPreAttack,
        Sprite[] upAttack)
    {
        int safeLevel = Mathf.Max(1, level);

        if (levels == null)
            levels = Array.Empty<TowerArcherLevelVisual>();

        TowerArcherLevelVisual target = null;
        for (int i = 0; i < levels.Length; i++)
        {
            TowerArcherLevelVisual candidate = levels[i];
            if (candidate != null && candidate.Level == safeLevel)
            {
                target = candidate;
                break;
            }
        }

        if (target == null)
        {
            Array.Resize(ref levels, levels.Length + 1);
            target = new TowerArcherLevelVisual { Level = safeLevel };
            levels[levels.Length - 1] = target;
        }

        if (target.Down == null)
            target.Down = new TowerArcherDirectionalSprites();

        if (target.Side == null)
            target.Side = new TowerArcherDirectionalSprites();

        if (target.Up == null)
            target.Up = new TowerArcherDirectionalSprites();

        target.Down.Set(downIdle, downPreAttack, downAttack);
        target.Side.Set(sideIdle, sidePreAttack, sideAttack);
        target.Up.Set(upIdle, upPreAttack, upAttack);
    }
#endif
}

[Serializable]
public sealed class TowerArcherLevelVisual
{
    [SerializeField, Min(1)] private int level = 1;
    [SerializeField] private TowerArcherDirectionalSprites down = new TowerArcherDirectionalSprites();
    [SerializeField] private TowerArcherDirectionalSprites side = new TowerArcherDirectionalSprites();
    [SerializeField] private TowerArcherDirectionalSprites up = new TowerArcherDirectionalSprites();

    public int Level
    {
        get => Mathf.Max(1, level);
        set => level = Mathf.Max(1, value);
    }

    public TowerArcherDirectionalSprites Down
    {
        get => down;
        set => down = value;
    }

    public TowerArcherDirectionalSprites Side
    {
        get => side;
        set => side = value;
    }

    public TowerArcherDirectionalSprites Up
    {
        get => up;
        set => up = value;
    }

    public TowerArcherDirectionalSprites GetBand(TowerArcherAimBand band)
    {
        switch (band)
        {
            case TowerArcherAimBand.Down:
                return down;
            case TowerArcherAimBand.Up:
                return up;
            default:
                return side;
        }
    }
}

[Serializable]
public sealed class TowerArcherDirectionalSprites
{
    [SerializeField] private Sprite[] idle = Array.Empty<Sprite>();
    [SerializeField] private Sprite[] preAttack = Array.Empty<Sprite>();
    [SerializeField] private Sprite[] attack = Array.Empty<Sprite>();

    public Sprite[] Idle => idle ?? Array.Empty<Sprite>();
    public Sprite[] PreAttack => preAttack ?? Array.Empty<Sprite>();
    public Sprite[] Attack => attack ?? Array.Empty<Sprite>();

#if UNITY_EDITOR
    public void Set(Sprite[] idleFrames, Sprite[] preAttackFrames, Sprite[] attackFrames)
    {
        idle = idleFrames ?? Array.Empty<Sprite>();
        preAttack = preAttackFrames ?? Array.Empty<Sprite>();
        attack = attackFrames ?? Array.Empty<Sprite>();
    }
#endif
}