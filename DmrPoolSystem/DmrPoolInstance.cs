using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace DmrPoolSystem
{
    public class DmrPoolInstance<T> where T : MonoBehaviour
    {
        private readonly Dictionary<GameObject, Stack<T>> _currentPool = new(25);
        private readonly Dictionary<GameObject, GameObject> _liveInstanceMap;

        //To prevent double return protection 
        private readonly HashSet<GameObject> _activeObjects;

        // Internal Stack handles recursion safely (nested spawns get their own list)
        private readonly Stack<List<IPoolableGameObject>> _listPool = new(4);

        public bool DisableRememberBufferCheck = false;

        private int _rememberBufferMaxSize;
        private readonly bool _sendWarning;

        private Transform _mainParentTransform;

        /// <param name="rememberBufferWarningSize">
        /// If pool object remember buffer is bigger than this value,
        /// it will do a null object scan to clean it and send a warning if sendWarning is true.
        /// liveInstanceMap capacity will be set to 0.5x rememberBufferWarningSize
        /// </param>
        public DmrPoolInstance(string name = "DmrPoolSystem", bool dontDestroyOnLoad = false, int rememberBufferWarningSize = 500, bool sendWarning = true)
        {
            _rememberBufferMaxSize = rememberBufferWarningSize;
            _sendWarning = sendWarning;
            _liveInstanceMap = new Dictionary<GameObject, GameObject>(Mathf.CeilToInt(0.5f * rememberBufferWarningSize));
            _activeObjects = new HashSet<GameObject>(Mathf.CeilToInt(0.5f * rememberBufferWarningSize));

            _mainParentTransform = new GameObject(name).transform;

            if (dontDestroyOnLoad)
            {
                GameObject.DontDestroyOnLoad(_mainParentTransform.gameObject);
            }
        }

        #region Public Methods
        public T GetPoolObject(GameObject prefab)
        {
            if (prefab == null)
            {
                Debug.LogError("GetPoolObject called with a null prefab");

                CheckForRemovedPrefabs();
                return null;
            }

            bool isRegistered = _currentPool.TryGetValue(prefab, out var pool);

            if (!isRegistered && prefab.GetComponent<T>() == null)
            {
                Debug.LogError($"Prefab {prefab.name} does not have the required component {typeof(T).Name}. Cannot get from pool.");
                return null;
            }

            if (_mainParentTransform == null)
            {
                //DDOL is not activated but scene is changed
                _mainParentTransform = new GameObject("DmrPoolSystem").transform;
            }

            if (!isRegistered)
            {
                RegisterPoolObject(prefab);
                pool = _currentPool[prefab];
            }

            //if references went null we need to clean that up
            //it can happen on scene transitions
            while (pool.Count > 0 && pool.Peek() == null)
            {
                pool.Pop();
            }

            if (pool.Count == 0)
            {
                PopulatePool(prefab, 1);

                if (pool.Count == 0)
                {
                    Debug.LogError($"PopulatePool failed for prefab {prefab.name}. Aborting GetPoolObject.");
                    return null;
                }
            }

            T instance = pool.Pop();
            instance.gameObject.SetActive(true);

            NotifyLifecycle(instance.gameObject, true);

            _liveInstanceMap[instance.gameObject] = prefab;
            _activeObjects.Add(instance.gameObject);

            if (Mathf.Max(_activeObjects.Count, _liveInstanceMap.Count) > _rememberBufferMaxSize)
            {
                CleanUpPoolGarbage();
            }

            return instance;
        }
        public void ReturnPoolObject(T createdComponent)
        {
            if (createdComponent == null) 
            {
                Debug.LogWarning("ReturnPoolObject called with a null component");
                return;
            }

            GameObject createdObject = createdComponent.gameObject;

            if (createdObject == null) return;

            if (!_activeObjects.Contains(createdObject))
            {
                Debug.LogWarning($"Object {createdObject.name} is not active in pool system (Could be indicating double return!). Destroying the object.");

                NotifyLifecycle(createdObject, false);

                //To safe guard against destroyed objects
                createdObject.SetActive(false);

                GameObject.Destroy(createdObject);

                return;
            }

            NotifyLifecycle(createdObject, false);

            if (_mainParentTransform != null && createdObject.transform.parent != _mainParentTransform)
                createdObject.transform.SetParent(_mainParentTransform, false);

            createdObject.SetActive(false);
            _activeObjects.Remove(createdObject); // Remove from active tracking

            if (_liveInstanceMap.TryGetValue(createdObject, out var prefab))
            {
                // Safeguard against destroyed prefabs (rare case)
                if (prefab == null)
                {
                    Debug.LogWarning($"Prefab for {createdObject.name} is missing. Destroying instance.");
                    GameObject.Destroy(createdObject);
                    _liveInstanceMap.Remove(createdObject);

                    CheckForRemovedPrefabs();
                    return;
                }

                // Reset transform scale to original
                createdObject.transform.localScale = prefab.transform.localScale;

                // Reset transform rotation to original
                createdObject.transform.localRotation = prefab.transform.localRotation;

                _currentPool[prefab].Push(createdComponent);
                _liveInstanceMap.Remove(createdObject);

                if (Mathf.Max(_activeObjects.Count, _liveInstanceMap.Count) > _rememberBufferMaxSize)
                {
                    CleanUpPoolGarbage();
                }
            }
            else
            {
                Debug.LogWarning("liveInstanceMap does not contain this object. Destroying: " + createdObject.name);
                GameObject.Destroy(createdObject);
            }
        }
        public void RegisterPoolObject(GameObject prefab, int warmupAmount = 1)
        {
            if (prefab == null)
            {
                Debug.LogWarning("RegisterPoolObject called with a null prefab");
                return;
            }

            if (prefab.GetComponent<T>() == null)
            {
                Debug.LogWarning($"Prefab {prefab.name} does not have the required component {typeof(T).Name}. Skipping registration.");
                return;
            }

            if (_currentPool.ContainsKey(prefab))
            {
                Debug.LogWarning("Pool already contains " + prefab.name);
                return;
            }

            PopulatePool(prefab, warmupAmount);
        }
        #endregion

        #region Private Methods
        private void PopulatePool(GameObject prefab, int count = 1)
        {
            if (!_currentPool.TryGetValue(prefab, out var pool))
            {
                pool = new Stack<T>(count);
                _currentPool[prefab] = pool;
            }

            for (int i = 0; i < count; i++)
            {
                if (_mainParentTransform == null)
                {
                    _mainParentTransform = new GameObject("DmrPoolSystem").transform;
                }

                GameObject instance = GameObject.Instantiate(prefab, _mainParentTransform, false);
                var instanceComponent = instance.GetComponent<T>();
                if (instanceComponent == null)
                {
                    Debug.LogError($"Prefab {prefab.name} does not have the required component {typeof(T).Name}. Skipping registration.");
                    GameObject.Destroy(instance);
                    return;
                }

                instance.SetActive(false);
                pool.Push(instanceComponent);
            }
        }
        private void CleanUpPoolGarbage()
        {
            if (DisableRememberBufferCheck) return;

            Debug.LogWarning("liveInstanceMap reached cleanup threshold: " + _liveInstanceMap.Count);

            int countBeforeCleanup = _liveInstanceMap.Count;

            var keys = _liveInstanceMap.Keys.ToList();
            foreach (var instance in keys)
            {
                if (instance == null)
                {
                    _liveInstanceMap.Remove(instance);
                    if (_sendWarning)
                        Debug.LogWarning("Removed null entry from liveInstanceMap");
                }
            }

            _activeObjects.RemoveWhere(obj => obj == null);
			
            int countAfterCleanup = _liveInstanceMap.Count;
            int removedCount = countBeforeCleanup - countAfterCleanup;
			
			if (removedCount == 0)
            {
	            _rememberBufferMaxSize = _rememberBufferMaxSize * 2;
				
				if (_sendWarning)
                { 
                    Debug.Log($"Pool buffer legitimate load detected. Auto-expanding limit to {_rememberBufferMaxSize} to prevent performance stutter.");
                }
            }
        }
        private void NotifyLifecycle(GameObject obj, bool isGet)
        {
            List<IPoolableGameObject> poolables = (_listPool.Count > 0) ? _listPool.Pop() : new List<IPoolableGameObject>(8);

            try
            {
                obj.GetComponents(poolables);

                int count = poolables.Count;
                for (int i = 0; i < count; i++)
                {
                    if (isGet) poolables[i].OnPoolGet();
                    else poolables[i].OnPoolReturn();
                }
            }
            finally
            {
                poolables.Clear();
                _listPool.Push(poolables);
            }
        }
        private void CheckForRemovedPrefabs()
        {
            var nullKeys = new List<GameObject>();

            foreach (var key in _currentPool.Keys)
            {
                if (key == null)
                    nullKeys.Add(key);
            }

            foreach (var key in nullKeys)
            {
                Debug.LogWarning("Destroyed prefab has been detected all pooled objects of this main prefab will be removed");

                if (!_currentPool.TryGetValue(key, out var pool))
                {
                    _currentPool.Remove(key);
                    continue;
                }

                while (pool.Count > 0)
                {
                    var inst = pool.Pop();
                    if (inst != null) GameObject.Destroy(inst.gameObject);
                }

                _currentPool.Remove(key);
            }
        }
        #endregion
    }
}