using UnityEngine;

public class PooledParticleAutoReturn : MonoBehaviour
{
    [SerializeField] private bool includeChildren = true;
    [SerializeField] private float extraDelay = 0.05f;

    private ParticleSystem[] particleSystems;
    private float timer;
    private float releaseAfter;
    private bool armed;

    private void Awake()
    {
        CacheParticleSystems();
    }

    private void OnEnable()
    {
        if (particleSystems == null || particleSystems.Length == 0)
            CacheParticleSystems();

        RestartParticles();
        CalculateLifetime();

        timer = 0f;
        armed = true;
    }

    private void Update()
    {
        if (!armed)
            return;

        timer += Time.deltaTime;
        if (timer >= releaseAfter)
        {
            armed = false;

            if (VfxPool.TryGetInstance(out VfxPool vfxPool))
                vfxPool.Release(gameObject);
            else
                Destroy(gameObject);
        }
    }

    private void OnDisable()
    {
        armed = false;
    }

    private void CacheParticleSystems()
    {
        if (includeChildren)
            particleSystems = GetComponentsInChildren<ParticleSystem>(true);
        else
            particleSystems = GetComponents<ParticleSystem>();
    }

    private void RestartParticles()
    {
        if (particleSystems == null)
            return;

        for (int i = 0; i < particleSystems.Length; i++)
        {
            ParticleSystem ps = particleSystems[i];
            if (ps == null)
                continue;

            ps.Clear(true);
            ps.Play(true);
        }
    }

    private void CalculateLifetime()
    {
        releaseAfter = 0.1f;

        if (particleSystems == null || particleSystems.Length == 0)
        {
            releaseAfter = 1f;
            return;
        }

        float maxLifetime = 0f;

        for (int i = 0; i < particleSystems.Length; i++)
        {
            ParticleSystem ps = particleSystems[i];
            if (ps == null)
                continue;

            var main = ps.main;

            float duration = main.duration;
            float startLifetimeMax = GetMaxStartLifetime(main);
            float total = duration + startLifetimeMax;

            if (main.loop)
                total = Mathf.Max(total, 1f);

            maxLifetime = Mathf.Max(maxLifetime, total);
        }

        releaseAfter = Mathf.Max(0.1f, maxLifetime + extraDelay);
    }

    private float GetMaxStartLifetime(ParticleSystem.MainModule main)
    {
        switch (main.startLifetime.mode)
        {
            case ParticleSystemCurveMode.Constant:
                return main.startLifetime.constant;

            case ParticleSystemCurveMode.TwoConstants:
                return main.startLifetime.constantMax;

            case ParticleSystemCurveMode.Curve:
                return main.startLifetime.curveMultiplier;

            case ParticleSystemCurveMode.TwoCurves:
                return main.startLifetime.curveMultiplier;

            default:
                return 0f;
        }
    }
}
