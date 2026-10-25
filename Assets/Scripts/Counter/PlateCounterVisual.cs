using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 盘子堆叠的「纯视觉」表现：每生成一个盘子，就在台面上摞一个模型。
///
/// 2026-10 改动：不再 Instantiate/Destroy，改成走 TransformPool。
/// 原因：出餐高峰期盘子会快速增减，每次都新建/销毁 GameObject 会产生托管分配，
/// Profiler 里表现为周期性 GC Alloc 峰值。池化之后稳态下不再新建对象。
/// </summary>
public class PlateCounterVisual : MonoBehaviour
{
    [SerializeField] private PlatesCounter platesCounter;
    [SerializeField] private Transform counterTopPoint;
    [SerializeField] private Transform plateVisualPrefab;

    private readonly List<Transform> plateVisualList = new List<Transform>();
    private TransformPool platePool;

    private void Awake()
    {
        platePool = new TransformPool(plateVisualPrefab, transform, "PlateVisualPool");
    }

    private void Start()
    {
        platesCounter.OnPlateSpawned += PlatesCounter_OnPlateSpawned;
        platesCounter.OnPlateRemoved += PlatesCounter_OnPlateRemoved;
    }

    private void OnDestroy()
    {
        // 静态/长生命周期事件要退订，否则切场景后旧实例仍会被回调
        if (platesCounter == null)
        {
            return;
        }

        platesCounter.OnPlateSpawned -= PlatesCounter_OnPlateSpawned;
        platesCounter.OnPlateRemoved -= PlatesCounter_OnPlateRemoved;
    }

    private void PlatesCounter_OnPlateRemoved(object sender, System.EventArgs e)
    {
        if (plateVisualList.Count == 0)
        {
            return;
        }

        int lastIndex = plateVisualList.Count - 1;
        Transform plateVisual = plateVisualList[lastIndex];
        plateVisualList.RemoveAt(lastIndex);

        // 归还而不是销毁：下一次生成直接复用
        platePool.Release(plateVisual);
    }

    private void PlatesCounter_OnPlateSpawned(object sender, System.EventArgs e)
    {
        Transform plateVisualTransform = platePool.Get(counterTopPoint);

        float plateOffsetY = .1f;
        plateVisualTransform.localPosition = new Vector3(0, plateOffsetY * plateVisualList.Count, 0);

        plateVisualList.Add(plateVisualTransform);
    }
}
