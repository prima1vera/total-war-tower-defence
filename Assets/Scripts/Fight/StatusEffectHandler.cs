using UnityEngine;
using System.Collections;

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
            StopCoroutine(burnRoutine);

        if (freezeRoutine != null)
            StopCoroutine(freezeRoutine);

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

        burnRoutine = StartCoroutine(Burn(duration, tickDamage, tickRate));
    }

    IEnumerator Burn(float duration, int tickDamage, float tickRate)
    {
        visuals?.SetFireVisual(true);

        float timer = 0f;

        while (timer < duration)
        {
            health.TakePureDamage(tickDamage);
            yield return new WaitForSeconds(tickRate);
            timer += tickRate;
        }

        visuals?.SetFireVisual(false);
    }

    // FREEZE
    public void ApplyFreeze(float duration, float slowMultiplier)
    {
        if (freezeRoutine != null)
            StopCoroutine(freezeRoutine);

        freezeRoutine = StartCoroutine(Freeze(duration, slowMultiplier));
    }

    IEnumerator Freeze(float duration, float slowMultiplier)
    {
        visuals?.SetFreezeVisual(true);

        if (movement != null)
            movement.SetSpeedMultiplier(slowMultiplier);

        yield return new WaitForSeconds(duration);

        if (movement != null)
            movement.SetSpeedMultiplier(1f);

        visuals?.SetFreezeVisual(false);
    }
}
