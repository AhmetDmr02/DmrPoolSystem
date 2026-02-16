using UnityEngine;
using DmrPoolSystem;

public class PoolTestSphere : MonoBehaviour, IPoolableGameObject
{
    public static DmrPoolInstance<PoolTestSphere> SharedPool;
    public static PerformanceSpawner Spawner;
    public static bool UsePoolingMode;

    private Rigidbody _rb;

    private void Awake()
    {
        _rb = GetComponent<Rigidbody>();
    }

    private void Update()
    {
        transform.Translate(Vector3.down * Time.deltaTime * 10f);
    }

    public void OnPoolGet()
    {
        if (_rb != null)
        {
            _rb.linearVelocity = Vector3.zero;
            _rb.angularVelocity = Vector3.zero;
        }
    }

    public void OnPoolReturn() { }

    private void OnTriggerEnter(Collider other)
    {
        if (other.CompareTag("Finish"))
        {
            if (Spawner != null) Spawner.NotifyObjectRemoved();

            if (UsePoolingMode && SharedPool != null)
            {
                SharedPool.ReturnPoolObject(this);
            }
            else
            {
                Destroy(gameObject);
            }
        }
    }
}