using System;
using System.Collections;
using Unity.Netcode;
using UnityEngine;

/// <summary>
/// 断线看护：客户端意外掉线后按指数退避尝试重连，服务端/主机则只做清理。
///
/// 为什么需要：NGO 在连接断开后不会自己重连，玩家的表现就是「画面卡住，什么也没发生」。
/// 这里把「掉线 → 提示 → 重连 N 次 → 放弃」变成一个有边界的流程，而不是无限循环。
///
/// 边界说明：
///   - 只处理「我自己掉线」，别人的掉线由 KitchenGameMultiplayer / GameManager 处理；
///   - 主机（Host/Server）不重连 —— 主机一掉，整个房间就没了，重连没有意义；
///   - 重连次数和间隔都可在 Inspector 调，默认 3 次、1.5s 起翻倍、上限 8s。
/// </summary>
public class ConnectionWatchdog : MonoBehaviour
{
    [SerializeField] private int maxRetries = 3;
    [SerializeField] private float baseDelaySeconds = 1.5f;
    [SerializeField] private float maxDelaySeconds = 8f;
    [SerializeField] private bool autoReconnect = true;

    /// <summary>第 N 次重连时触发，参数是当前第几次（从 1 开始）。</summary>
    public event Action<int> OnReconnectAttempt;

    /// <summary>重连成功。</summary>
    public event Action OnReconnected;

    /// <summary>重试次数用尽，彻底放弃。</summary>
    public event Action OnReconnectFailed;

    public bool IsReconnecting => reconnectRoutine != null;

    private NetworkManager networkManager;
    private Coroutine reconnectRoutine;
    private int attempts;
    private bool bound;

    public void Bind(NetworkManager manager)
    {
        if (manager == null || bound)
        {
            return;
        }

        networkManager = manager;
        networkManager.OnClientDisconnectCallback += HandleClientDisconnect;
        networkManager.OnClientConnectedCallback += HandleClientConnected;
        bound = true;
    }

    private void OnDestroy()
    {
        if (networkManager == null)
        {
            return;
        }

        networkManager.OnClientDisconnectCallback -= HandleClientDisconnect;
        networkManager.OnClientConnectedCallback -= HandleClientConnected;
    }

    private void HandleClientConnected(ulong clientId)
    {
        if (networkManager == null || clientId != networkManager.LocalClientId)
        {
            return;
        }

        // 只有「重连过程中连上了」才算重连成功；
        // 首次进入房间时 attempts 还是 0，不应该触发 OnReconnected。
        if (attempts > 0)
        {
            attempts = 0;
            Debug.Log("[ConnectionWatchdog] 重连成功");
            OnReconnected?.Invoke();
        }
    }

    private void HandleClientDisconnect(ulong clientId)
    {
        if (networkManager == null)
        {
            return;
        }

        // 主机掉线 = 房间结束，交给 UI 提示，不重连
        if (networkManager.IsServer)
        {
            return;
        }

        // 只关心自己
        if (clientId != networkManager.LocalClientId)
        {
            return;
        }

        if (!autoReconnect)
        {
            OnReconnectFailed?.Invoke();
            return;
        }

        if (reconnectRoutine != null)
        {
            return;
        }

        reconnectRoutine = StartCoroutine(ReconnectRoutine());
    }

    private IEnumerator ReconnectRoutine()
    {
        attempts = 0;

        while (attempts < maxRetries)
        {
            attempts++;

            float delay = Mathf.Min(baseDelaySeconds * Mathf.Pow(2f, attempts - 1), maxDelaySeconds);
            Debug.Log($"[ConnectionWatchdog] 第 {attempts}/{maxRetries} 次重连，{delay:F1}s 后开始");
            OnReconnectAttempt?.Invoke(attempts);

            yield return new WaitForSecondsRealtime(delay);

            if (networkManager == null)
            {
                break;
            }

            // 已经连上了（比如退避期间网络自己恢复）
            if (networkManager.IsListening && networkManager.IsConnectedClient)
            {
                reconnectRoutine = null;
                OnReconnected?.Invoke();
                yield break;
            }

            networkManager.StartClient();

            // 给一次握手的窗口，再判断结果
            yield return new WaitForSecondsRealtime(2f);

            if (networkManager != null && networkManager.IsConnectedClient)
            {
                reconnectRoutine = null;
                attempts = 0;
                Debug.Log("[ConnectionWatchdog] 重连成功");
                OnReconnected?.Invoke();
                yield break;
            }
        }

        reconnectRoutine = null;
        Debug.LogWarning("[ConnectionWatchdog] 重连次数用尽，已放弃");
        OnReconnectFailed?.Invoke();
    }
}
