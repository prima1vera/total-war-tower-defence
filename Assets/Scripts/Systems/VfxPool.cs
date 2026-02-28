using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class VfxPool : MonoBehaviour
{
    private const string RuntimeObjectName = "[VfxPool]";

    private static VfxPool instance;

    [SerializeField, Min(1)] private int initialPoolSizePerPrefab = 8;
    [SerializeField, Min(0.1f)] private float fallbackLifetime = 2f;

    private readonly Dictionary<GameObject, Stack<GameObject>> pooledByPrefab = new Dictionary<GameObject, Stack<GameObject>>(8);
    private readonly Dictionary<GameObject, GameObject> instanceToPrefab = new Dictionary<GameObject, GameObject>(64);

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

        GameObject instanceObject = pool.Count > 0 ? pool.Pop() : CreatePooledInstance(prefab);
        if (instanceObject == null)
            return null;

        instanceObject.transform.SetPositionAndRotation(position, rotation);
        instanceObject.SetActive(true);

        PooledVfxAutoReturn autoReturn = instanceObject.GetComponent<PooledVfxAutoReturn>();
        if (autoReturn == null)
            autoReturn = instanceObject.AddComponent<PooledVfxAutoReturn>();

        autoReturn.Initialize(this, fallbackLifetime);
        autoReturn.Arm();

        return instanceObject;
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

        instanceObject.SetActive(false);
        instanceObject.transform.SetParent(transform, false);
        pool.Push(instanceObject);
    }

    private GameObject CreatePooledInstance(GameObject prefab)
    {
        GameObject instanceObject = Instantiate(prefab, transform);
        instanceToPrefab[instanceObject] = prefab;
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

    public void Initialize(VfxPool pool, float fallbackDuration)
    {
        ownerPool = pool;
        fallbackLifetime = Mathf.Max(0.1f, fallbackDuration);

        if (particleSystems == null || particleSystems.Length == 0)
            particleSystems = GetComponentsInChildren<ParticleSystem>(true);

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

        if (particleSystems != null && particleSystems.Length > 0)
        {
            float particlesDuration = 0f;
            for (int i = 0; i < particleSystems.Length; i++)
            {
                if (particleSystems[i] == null)
                    continue;

                ParticleSystem.MainModule main = particleSystems[i].main;
                particlesDuration = Mathf.Max(particlesDuration, main.duration + main.startLifetime.constantMax);
                particleSystems[i].Clear(true);
                particleSystems[i].Play(true);
            }

            releaseDelay = Mathf.Max(releaseDelay, particlesDuration);
        }

        fallbackCoroutine = StartCoroutine(ReturnAfterDelay(releaseDelay));
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
