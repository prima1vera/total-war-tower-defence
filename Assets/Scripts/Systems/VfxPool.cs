using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class VfxPool : MonoBehaviour
{
    private const string RuntimeObjectName = "[VfxPool]";

    private static VfxPool instance;

    [SerializeField, Min(1)] private int initialPoolSizePerPrefab = 8;
    [SerializeField, Min(0.1f)] private float fallbackLifetime = 2f;
    [SerializeField] private bool enableDebugLogs;

    private readonly Dictionary<GameObject, Stack<GameObject>> pooledByPrefab = new Dictionary<GameObject, Stack<GameObject>>(8);
    private readonly Dictionary<GameObject, GameObject> instanceToPrefab = new Dictionary<GameObject, GameObject>(64);
    private readonly Dictionary<GameObject, int> prewarmedByPrefab = new Dictionary<GameObject, int>(8);

    private int totalSpawnRequests;
    private int totalReusedInstances;
    private int totalCreatedInstances;
    private int totalReleasedInstances;
    private int activeInstances;

    public static VfxPool Instance
    {
        get
        {
            if (instance != null)
                return instance;

            instance = FindObjectOfType<VfxPool>();
            if (instance != null)
                return instance;

            GameObject runtimeObject = new GameObject(RuntimeObjectName);
            instance = runtimeObject.AddComponent<VfxPool>();
            DontDestroyOnLoad(runtimeObject);
            return instance;
        }
    }

    private void Awake()
    {
        if (instance != null && instance != this)
        {
            Destroy(gameObject);
            return;
        }

        instance = this;
        DontDestroyOnLoad(gameObject);
    }

    public GameObject Spawn(GameObject prefab, Vector3 position, Quaternion rotation)
    {
        if (prefab == null)
            return null;

        if (!pooledByPrefab.TryGetValue(prefab, out Stack<GameObject> pool))
        {
            pool = new Stack<GameObject>(Mathf.Max(1, initialPoolSizePerPrefab));
            pooledByPrefab.Add(prefab, pool);
        }

        totalSpawnRequests++;

        bool reusedFromPool = pool.Count > 0;
        GameObject instanceObject = reusedFromPool ? pool.Pop() : CreatePooledInstance(prefab);
        if (instanceObject == null)
            return null;

        if (instanceObject.activeSelf)
            instanceObject.SetActive(false);

        if (reusedFromPool)
            totalReusedInstances++;

        instanceObject.transform.SetPositionAndRotation(position, rotation);
        instanceObject.SetActive(true);

        PooledVfxAutoReturn autoReturn = instanceObject.GetComponent<PooledVfxAutoReturn>();
        if (autoReturn == null)
            autoReturn = instanceObject.AddComponent<PooledVfxAutoReturn>();

        autoReturn.Initialize(this, fallbackLifetime);
        autoReturn.Arm();
        activeInstances++;

        return instanceObject;
    }

    public void Prewarm(GameObject prefab, int count)
    {
        if (prefab == null || count <= 0)
            return;

        if (!pooledByPrefab.TryGetValue(prefab, out Stack<GameObject> pool))
        {
            pool = new Stack<GameObject>(Mathf.Max(initialPoolSizePerPrefab, count));
            pooledByPrefab.Add(prefab, pool);
        }

        int currentCount = 0;
        prewarmedByPrefab.TryGetValue(prefab, out currentCount);
        int targetCount = Mathf.Max(currentCount, count);

        int toCreate = Mathf.Max(0, targetCount - currentCount);
        for (int i = 0; i < toCreate; i++)
        {
            GameObject instanceObject = CreatePooledInstance(prefab);
            if (instanceObject == null)
                continue;

            pool.Push(instanceObject);
        }

        prewarmedByPrefab[prefab] = targetCount;
    }

    public void Release(GameObject instanceObject)
    {
        if (instanceObject == null)
            return;

        if (!instanceToPrefab.TryGetValue(instanceObject, out GameObject prefab) || prefab == null)
        {
            Destroy(instanceObject);
            return;
        }

        if (!pooledByPrefab.TryGetValue(prefab, out Stack<GameObject> pool))
        {
            pool = new Stack<GameObject>(Mathf.Max(1, initialPoolSizePerPrefab));
            pooledByPrefab.Add(prefab, pool);
        }

        PooledVfxAutoReturn autoReturn = instanceObject.GetComponent<PooledVfxAutoReturn>();
        if (autoReturn != null)
            autoReturn.ResetState();

        instanceObject.SetActive(false);
        instanceObject.transform.SetParent(transform, false);
        pool.Push(instanceObject);
        totalReleasedInstances++;
        activeInstances = Mathf.Max(0, activeInstances - 1);
    }

    [ContextMenu("Log VFX Pool Stats")]
    public void LogStats()
    {
        Debug.Log($"[VfxPool] spawns={totalSpawnRequests}, reused={totalReusedInstances}, created={totalCreatedInstances}, released={totalReleasedInstances}, active={activeInstances}, trackedPrefabs={pooledByPrefab.Count}");
    }

    private void LateUpdate()
    {
        if (!enableDebugLogs)
            return;

        if (Time.frameCount % 300 == 0)
            LogStats();
    }

    private GameObject CreatePooledInstance(GameObject prefab)
    {
        GameObject instanceObject = Instantiate(prefab, transform);
        instanceToPrefab[instanceObject] = prefab;
        totalCreatedInstances++;
        instanceObject.SetActive(false);
        return instanceObject;
    }
}

public class PooledVfxAutoReturn : MonoBehaviour
{
    private VfxPool ownerPool;
    private float fallbackLifetime;
    private ParticleSystem[] particleSystems;
    private Animator animator;
    private Coroutine fallbackCoroutine;
    private int pendingParticleStopCallbacks;

    public void Initialize(VfxPool pool, float fallbackDuration)
    {
        ownerPool = pool;
        fallbackLifetime = Mathf.Max(0.1f, fallbackDuration);

        if (particleSystems == null || particleSystems.Length == 0)
        {
            particleSystems = GetComponentsInChildren<ParticleSystem>(true);
            for (int i = 0; i < particleSystems.Length; i++)
            {
                if (particleSystems[i] == null)
                    continue;

                PooledVfxParticleStopRelay relay = particleSystems[i].gameObject.GetComponent<PooledVfxParticleStopRelay>();
                if (relay == null)
                    relay = particleSystems[i].gameObject.AddComponent<PooledVfxParticleStopRelay>();

                relay.Bind(this);
            }
        }

        if (animator == null)
            animator = GetComponentInChildren<Animator>(true);
    }

    public void Arm()
    {
        if (fallbackCoroutine != null)
        {
            StopCoroutine(fallbackCoroutine);
            fallbackCoroutine = null;
        }

        float releaseDelay = ResolveAnimationDuration();
        pendingParticleStopCallbacks = 0;

        if (particleSystems != null && particleSystems.Length > 0)
        {
            float particlesDuration = 0f;
            for (int i = 0; i < particleSystems.Length; i++)
            {
                if (particleSystems[i] == null)
                    continue;

                ParticleSystem.MainModule main = particleSystems[i].main;
                main.stopAction = ParticleSystemStopAction.Callback;

                float particleLifetime = Mathf.Max(0f, main.startLifetime.constantMax);
                particlesDuration = Mathf.Max(particlesDuration, main.duration + particleLifetime);

                particleSystems[i].Clear(true);
                particleSystems[i].Play(true);
                pendingParticleStopCallbacks++;
            }

            releaseDelay = Mathf.Max(releaseDelay, particlesDuration);
        }

        fallbackCoroutine = StartCoroutine(ReturnAfterDelay(releaseDelay));
    }

    public void OnParticleStopped(ParticleSystem particleSystem)
    {
        if (!isActiveAndEnabled || ownerPool == null)
            return;

        pendingParticleStopCallbacks = Mathf.Max(0, pendingParticleStopCallbacks - 1);
        if (pendingParticleStopCallbacks > 0)
            return;

        if (fallbackCoroutine != null)
        {
            StopCoroutine(fallbackCoroutine);
            fallbackCoroutine = null;
        }

        ownerPool.Release(gameObject);
    }

    public void ResetState()
    {
        pendingParticleStopCallbacks = 0;
        if (fallbackCoroutine != null)
        {
            StopCoroutine(fallbackCoroutine);
            fallbackCoroutine = null;
        }
    }

    private float ResolveAnimationDuration()
    {
        if (animator == null || animator.runtimeAnimatorController == null)
            return fallbackLifetime;

        AnimationClip[] clips = animator.runtimeAnimatorController.animationClips;
        if (clips == null || clips.Length == 0)
            return fallbackLifetime;

        float maxLength = 0f;
        for (int i = 0; i < clips.Length; i++)
        {
            if (clips[i] == null)
                continue;

            maxLength = Mathf.Max(maxLength, clips[i].length);
        }

        return Mathf.Max(0.1f, maxLength);
    }

    private IEnumerator ReturnAfterDelay(float delay)
    {
        yield return new WaitForSeconds(Mathf.Max(0.1f, delay));

        if (ownerPool != null)
            ownerPool.Release(gameObject);
    }

    private void OnDisable()
    {
        if (fallbackCoroutine != null)
        {
            StopCoroutine(fallbackCoroutine);
            fallbackCoroutine = null;
        }
    }
}

public class PooledVfxParticleStopRelay : MonoBehaviour
{
    private PooledVfxAutoReturn owner;

    public void Bind(PooledVfxAutoReturn autoReturn)
    {
        owner = autoReturn;
    }

    private void OnParticleSystemStopped()
    {
        if (owner == null)
            return;

        ParticleSystem ps = GetComponent<ParticleSystem>();
        if (ps == null)
            return;

        owner.OnParticleStopped(ps);
    }
}
