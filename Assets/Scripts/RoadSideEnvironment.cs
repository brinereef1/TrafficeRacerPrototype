
using System.Collections.Generic;
using UnityEngine;

public class RoadsideEnvironment : MonoBehaviour
{
    [Header("References")]
    [SerializeField] private RoadManager roadManager;

    [Tooltip("Trees, rocks, bushes, or other roadside props.")]
    [SerializeField] private GameObject[] environmentPrefabs;

    [Header("Road Dimensions")]
    [Tooltip("Your road is 15 units wide, so start with 7.5.")]
    [SerializeField] private float roadHalfWidth = 7.5f;

    [SerializeField] private Vector2 distanceFromRoad =
        new Vector2(2f, 8f);

    [Header("Spawning")]
    [SerializeField] private float minSpacing = 8f;
    [SerializeField] private float maxSpacing = 14f;
    [SerializeField] private float spawnAheadDistance = 100f;

    [Range(0f, 1f)]
    [SerializeField] private float spawnChancePerSide = 0.85f;

    [Header("Appearance")]
    [SerializeField] private Vector2 randomScale =
        new Vector2(0.8f, 1.4f);

    [SerializeField] private float heightOffset = 0f;

    [Header("Cleanup")]
    [SerializeField] private float despawnBehindDistance = 35f;

    [Header("Object Pool")]
    [Tooltip("How many objects of each prefab to create at the start.")]
    [SerializeField] private int initialPoolSizePerPrefab = 10;

    [Tooltip("Create additional objects if the pool runs out.")]
    [SerializeField] private bool expandPoolIfNeeded = true;

    private class SpawnedProp
    {
        public GameObject instance;
        public float trackDistance;
    }

    private readonly List<SpawnedProp> spawnedProps =
        new List<SpawnedProp>();

    // One pool for each prefab.
    private readonly Dictionary<GameObject, Queue<GameObject>> pools =
        new Dictionary<GameObject, Queue<GameObject>>();

    // Keep track of which prefab each pooled object came from.
    private readonly Dictionary<GameObject, GameObject> prefabByInstance =
        new Dictionary<GameObject, GameObject>();

    // Do not add the same object to the pool twice.
    private readonly HashSet<GameObject> pooledInstances =
        new HashSet<GameObject>();

    private float nextSpawnDistance;
    private float previousPlayerDistance;

    private void Start()
    {
        if (roadManager == null)
        {
            Debug.LogError(
                "RoadsideEnvironment: Assign your RoadManager.",
                this
            );

            enabled = false;
            return;
        }

        if (environmentPrefabs == null ||
            environmentPrefabs.Length == 0)
        {
            Debug.LogWarning(
                "RoadsideEnvironment: Add some environment prefabs.",
                this
            );

            enabled = false;
            return;
        }

        InitializePools();

        nextSpawnDistance = roadManager.PlayerDistance + 5f;
        previousPlayerDistance = roadManager.PlayerDistance;
    }

    private void InitializePools()
    {
        foreach (GameObject prefab in environmentPrefabs)
        {
            if (prefab == null || pools.ContainsKey(prefab))
                continue;

            pools.Add(prefab, new Queue<GameObject>());

            int poolSize = Mathf.Max(0, initialPoolSizePerPrefab);

            for (int i = 0; i < poolSize; i++)
            {
                GameObject prop = CreatePooledProp(prefab);

                if (prop != null)
                    ReturnToPool(prop);
            }
        }
    }

    private void LateUpdate()
    {
        if (roadManager == null)
            return;

        float playerDistance = roadManager.PlayerDistance;

        // Check for a restart that reset the track distance.
        if (playerDistance < previousPlayerDistance - 5f)
        {
            ClearEnvironment();
            nextSpawnDistance = playerDistance + 5f;
        }

        previousPlayerDistance = playerDistance;

        SpawnAhead();
        RemoveOldProps();
    }

    private void SpawnAhead()
    {
        float desiredEnd =
            roadManager.PlayerDistance + spawnAheadDistance;

        // Do not spawn past the end of the built road.
        float spawnLimit = Mathf.Min(
            desiredEnd,
            roadManager.GeneratedEndDistance - 0.1f
        );

        while (nextSpawnDistance <= spawnLimit)
        {
            if (Random.value < spawnChancePerSide)
                SpawnOnSide(nextSpawnDistance, -1);

            if (Random.value < spawnChancePerSide)
                SpawnOnSide(nextSpawnDistance, 1);

            float min = Mathf.Max(0.1f, minSpacing);
            float max = Mathf.Max(min, maxSpacing);

            nextSpawnDistance += Random.Range(min, max);
        }
    }

    private void SpawnOnSide(float distance, int side)
    {
        GameObject prefab = environmentPrefabs[
            Random.Range(0, environmentPrefabs.Length)
        ];

        if (prefab == null)
            return;

        GameObject prop = GetPooledProp(prefab);

        // No object is available in the pool.
        if (prop == null)
            return;

        float gap = Random.Range(
            distanceFromRoad.x,
            Mathf.Max(distanceFromRoad.x, distanceFromRoad.y)
        );

        float lateralOffset =
            side * (roadHalfWidth + gap);

        roadManager.GetPose(
            distance,
            lateralOffset,
            out Vector3 position,
            out Quaternion roadRotation
        );

        position.y += heightOffset;

        // Rotate the prop a little so it looks natural.
        Quaternion randomRotation =
            roadRotation *
            Quaternion.Euler(
                0f,
                Random.Range(0f, 360f),
                0f
            );

        float scaleMultiplier = Random.Range(
            randomScale.x,
            Mathf.Max(randomScale.x, randomScale.y)
        );

        // Configure the object before enabling it.
        prop.transform.SetPositionAndRotation(
            position,
            randomRotation
        );

        prop.transform.localScale =
            prefab.transform.localScale * scaleMultiplier;

        prop.SetActive(true);

        spawnedProps.Add(new SpawnedProp
        {
            instance = prop,
            trackDistance = distance
        });
    }

    private GameObject GetPooledProp(GameObject prefab)
    {
        if (!pools.TryGetValue(
                prefab,
                out Queue<GameObject> pool))
        {
            return null;
        }

        // Take an inactive prop from the pool.
        while (pool.Count > 0)
        {
            GameObject prop = pool.Dequeue();

            if (prop == null)
                continue;

            pooledInstances.Remove(prop);
            return prop;
        }

        // Create a new prop only if the pool is empty and expansion is allowed.
        if (expandPoolIfNeeded)
            return CreatePooledProp(prefab);

        return null;
    }

    private GameObject CreatePooledProp(GameObject prefab)
    {
        if (prefab == null)
            return null;

        GameObject prop = Instantiate(prefab, transform);

        prop.SetActive(false);

        prefabByInstance[prop] = prefab;

        return prop;
    }

    private void ReturnToPool(GameObject prop)
    {
        if (prop == null)
            return;

        // Only handle objects created by this pool.
        if (!prefabByInstance.TryGetValue(
                prop,
                out GameObject prefab))
        {
            prop.SetActive(false);
            return;
        }

        if (!pools.TryGetValue(
                prefab,
                out Queue<GameObject> pool))
        {
            prop.SetActive(false);
            return;
        }

        // Do not add the same object twice.
        if (!pooledInstances.Add(prop))
            return;

        prop.SetActive(false);
        pool.Enqueue(prop);
    }

    private void RemoveOldProps()
    {
        float cleanupDistance =
            roadManager.PlayerDistance - despawnBehindDistance;

        for (int i = spawnedProps.Count - 1; i >= 0; i--)
        {
            SpawnedProp prop = spawnedProps[i];

            if (prop.instance == null ||
                prop.trackDistance < cleanupDistance)
            {
                if (prop.instance != null)
                    ReturnToPool(prop.instance);

                spawnedProps.RemoveAt(i);
            }
        }
    }

    private void ClearEnvironment()
    {
        // Send props back to the pool when the scene restarts.
        foreach (SpawnedProp prop in spawnedProps)
        {
            if (prop.instance != null)
                ReturnToPool(prop.instance);
        }

        spawnedProps.Clear();
    }
}
