using System.Collections.Generic;
using UnityEngine;

public class VfxPool : MonoBehaviour
{
    private const string RuntimeObjectName = "[VfxPool]";
    private static VfxPool instance;

    [SerializeField, Min(1)] private int initialPoolSizePerPrefab = 8;
    [SerializeField] private List<PrewarmEntry> prewarmOnAwake = new List<PrewarmEntry>(4);

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
        PrewarmConfiguredPrefabs();
    }

    public void Prewarm(GameObject prefab, int count)
    {
        if (prefab == null)
            return;

        int warmCount = Mathf.Max(0, count);
        if (warmCount == 0)
            return;

        Stack<GameObject> pool = GetOrCreatePool(prefab);
        while (pool.Count < warmCount)
            pool.Push(CreatePooledInstance(prefab));
    }

    public GameObject Spawn(GameObject prefab, Vector3 position, Quaternion rotation)
    {
        if (prefab == null)
            return null;

        Stack<GameObject> pool = GetOrCreatePool(prefab);
        GameObject instanceObject = pool.Count > 0 ? pool.Pop() : CreatePooledInstance(prefab);
        if (instanceObject == null)
            return null;

        instanceObject.transform.SetParent(null, false);
        instanceObject.transform.SetPositionAndRotation(position, rotation);
        instanceObject.SetActive(true);

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

        Stack<GameObject> pool = GetOrCreatePool(prefab);

        instanceObject.SetActive(false);
        instanceObject.transform.SetParent(transform, false);
        pool.Push(instanceObject);
    }

    private void PrewarmConfiguredPrefabs()
    {
        for (int i = 0; i < prewarmOnAwake.Count; i++)
        {
            PrewarmEntry entry = prewarmOnAwake[i];
            if (entry.Prefab == null || entry.Count <= 0)
                continue;

            Prewarm(entry.Prefab, entry.Count);
        }
    }

    private Stack<GameObject> GetOrCreatePool(GameObject prefab)
    {
        if (pooledByPrefab.TryGetValue(prefab, out Stack<GameObject> existingPool))
            return existingPool;

        Stack<GameObject> newPool = new Stack<GameObject>(Mathf.Max(1, initialPoolSizePerPrefab));
        pooledByPrefab.Add(prefab, newPool);
        return newPool;
    }

    private GameObject CreatePooledInstance(GameObject prefab)
    {
        GameObject instanceObject = Instantiate(prefab, transform);
        instanceToPrefab[instanceObject] = prefab;
        instanceObject.SetActive(false);
        return instanceObject;
    }

    [System.Serializable]
    public struct PrewarmEntry
    {
        public GameObject Prefab;
        [Min(1)] public int Count;
    }
}