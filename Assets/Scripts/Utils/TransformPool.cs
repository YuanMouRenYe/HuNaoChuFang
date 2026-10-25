using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 轻量对象池，只用于「纯视觉、不参与网络同步」的临时对象。
///
/// 为什么需要：盘子堆叠、订单图标这类对象在游戏过程中会反复 Instantiate/Destroy，
/// 每次都会产生托管分配，积累下来就是 GC Spikes（Profiler 里能看到 GC Alloc 峰值）。
/// 池化之后稳态下不再分配新对象，只是 SetActive 切换。
///
/// 注意：这里刻意不池化 NetworkObject —— NGO 的生成/销毁由 NetworkManager 管，
/// 自己池化会和 NetworkObject 的生命周期冲突。
/// </summary>
public class TransformPool
{
    private readonly Transform prefab;
    private readonly Transform poolRoot;
    private readonly Stack<Transform> freeInstances = new Stack<Transform>();

    /// <summary>累计创建过的实例数，用来验证「池化之后不再增长」。</summary>
    public int CreatedCount { get; private set; }

    /// <summary>当前借出、还没归还的数量。</summary>
    public int ActiveCount { get; private set; }

    /// <summary>池里空闲的数量。</summary>
    public int FreeCount => freeInstances.Count;

    public TransformPool(Transform prefab, Transform parent, string poolName = "Pool")
    {
        this.prefab = prefab;

        GameObject root = new GameObject(poolName);
        poolRoot = root.transform;
        poolRoot.SetParent(parent, false);
        // 池根节点常驻失活：空闲实例挂在它下面，天然不可见。
        root.SetActive(false);
    }

    public Transform Get(Transform parent)
    {
        Transform instance;
        if (freeInstances.Count > 0)
        {
            instance = freeInstances.Pop();
        }
        else
        {
            instance = Object.Instantiate(prefab, poolRoot);
            CreatedCount++;
        }

        instance.SetParent(parent, false);
        instance.localPosition = Vector3.zero;
        instance.localRotation = Quaternion.identity;
        instance.localScale = Vector3.one;
        instance.gameObject.SetActive(true);

        ActiveCount++;
        return instance;
    }

    public void Release(Transform instance)
    {
        if (instance == null)
        {
            return;
        }

        instance.gameObject.SetActive(false);
        instance.SetParent(poolRoot, false);
        freeInstances.Push(instance);

        ActiveCount = Mathf.Max(0, ActiveCount - 1);
    }
}
