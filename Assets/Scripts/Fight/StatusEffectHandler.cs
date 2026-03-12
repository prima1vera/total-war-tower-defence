using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class StatusEffectHandler : MonoBehaviour
{
    private UnitHealth health;
    private UnitMovement movement;
    private UnitEffects visuals;

    private Coroutine burnRoutine;
    private Coroutine freezeRoutine;

    void Awake()
    {
        health = GetComponent<UnitHealth>();
        movement = GetComponent<UnitMovement>();
        visuals = GetComponent<UnitEffects>();
    }

    void OnDisable()
    {
        StopAllEffects();
    }

    public void StopAllEffects()
    {
        if (burnRoutine != null)
        {
            StopCoroutine(burnRoutine);
            burnRoutine = null;
        }

        if (freezeRoutine != null)
        {
            StopCoroutine(freezeRoutine);
            freezeRoutine = null;
        }

        visuals?.SetFireVisual(false);
        visuals?.SetFreezeVisual(false);

        if (movement != null)
            movement.SetSpeedMultiplier(1f);
    }

    // FIRE
    public void ApplyBurn(float duration, int tickDamage, float tickRate)
    {
        if (burnRoutine != null)
            StopCoroutine(burnRoutine);

        float safeDuration = Mathf.Max(0f, duration);
        float safeTickRate = Mathf.Max(0.01f, tickRate);

        burnRoutine = StartCoroutine(Burn(safeDuration, tickDamage, safeTickRate));
    }

    IEnumerator Burn(float duration, int tickDamage, float tickRate)
    {
        visuals?.SetFireVisual(true);

        WaitForSeconds tickWait = YieldCache.Get(tickRate);
        float timer = 0f;

        while (timer < duration)
        {
            if (health == null || health.IsDead)
                break;

            health.TakePureDamage(tickDamage, DamageType.Fire, DamageFeedbackKind.BurnTick);

            timer += tickRate;
            if (timer < duration)
                yield return tickWait;
        }

        visuals?.SetFireVisual(false);
        burnRoutine = null;
    }

    // FREEZE
    public void ApplyFreeze(float duration, float slowMultiplier)
    {
        if (freezeRoutine != null)
            StopCoroutine(freezeRoutine);

        freezeRoutine = StartCoroutine(Freeze(Mathf.Max(0f, duration), slowMultiplier));
    }

    IEnumerator Freeze(float duration, float slowMultiplier)
    {
        visuals?.SetFreezeVisual(true);

        if (movement != null)
            movement.SetSpeedMultiplier(slowMultiplier);

        if (duration > 0f)
            yield return YieldCache.Get(duration);

        if (movement != null)
            movement.SetSpeedMultiplier(1f);

        visuals?.SetFreezeVisual(false);
        freezeRoutine = null;
    }

    private static class YieldCache
    {
        private static readonly Dictionary<float, WaitForSeconds> Cache = new Dictionary<float, WaitForSeconds>(16);

        public static WaitForSeconds Get(float seconds)
        {
            float roundedSeconds = Mathf.Round(seconds * 100f) * 0.01f;
            if (roundedSeconds <= 0f)
                roundedSeconds = 0.01f;

            if (Cache.TryGetValue(roundedSeconds, out WaitForSeconds cached))
                return cached;

            WaitForSeconds created = new WaitForSeconds(roundedSeconds);
            Cache[roundedSeconds] = created;
            return created;
        }
    }
}
