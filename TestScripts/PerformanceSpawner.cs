using UnityEngine;
using DmrPoolSystem;

public class PerformanceSpawner : MonoBehaviour
{
    [Header("Test Configuration")]
    public GameObject SpherePrefab;
    public bool EnablePooling = true;
    public int TargetActiveCount = 500;

    [Header("Rain Settings")]
    [Tooltip("How many spheres to drop per second until we hit the cap")]
    public float SpawnsPerSecond = 25f;
    public float YOffset = -1f;

    private DmrPoolInstance<PoolTestSphere> _poolSystem;
    [SerializeField] private BoxCollider _spawnArea;

    private int _currentActiveCount = 0;
    private float _spawnTimer = 0f;

    private void Start()
    {
        PoolTestSphere.UsePoolingMode = EnablePooling;
        PoolTestSphere.Spawner = this;

        if (EnablePooling)
        {
            _poolSystem = new DmrPoolInstance<PoolTestSphere>("PerformancePool", false, TargetActiveCount + 50, true);
            PoolTestSphere.SharedPool = _poolSystem;

            _poolSystem.RegisterPoolObject(SpherePrefab, TargetActiveCount);
        }
    }

    private void Update()
    {
        if (_currentActiveCount < TargetActiveCount)
        {
            _spawnTimer += Time.deltaTime;
            float spawnInterval = 1f / SpawnsPerSecond;

            while (_spawnTimer >= spawnInterval && _currentActiveCount < TargetActiveCount)
            {
                SpawnObject();
                _currentActiveCount++;
                _spawnTimer -= spawnInterval;
            }
        }
    }

    public void NotifyObjectRemoved()
    {
        _currentActiveCount--;
    }

    private void SpawnObject()
    {
        Bounds b = _spawnArea.bounds;
        Vector3 spawnPos = new Vector3(
            Random.Range(b.min.x, b.max.x),
            b.min.y + YOffset,
            Random.Range(b.min.z, b.max.z)
        );

        if (EnablePooling)
        {
            var sphere = _poolSystem.GetPoolObject(SpherePrefab);
            if (sphere != null) sphere.transform.position = spawnPos;
        }
        else
        {
            Instantiate(SpherePrefab, spawnPos, Quaternion.identity);
        }
    }
}