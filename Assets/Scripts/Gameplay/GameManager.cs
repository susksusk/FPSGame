using System;
using UnityEngine;

namespace FpsGame.Gameplay
{
    /// <summary>
    /// 场景级游戏管理：出生点、击杀信息广播（普通 MonoBehaviour，网络部分由 PlayerNetwork 承担）。
    /// </summary>
    public class GameManager : MonoBehaviour
    {
        public static GameManager Instance { get; private set; }

        /// <summary>(击杀者, 被击杀者) 由网络层在所有客户端上触发。</summary>
        public static event Action<string, string> KillFeedAdded;

        /// <summary>由场景搭建脚本赋值。</summary>
        public Transform[] spawnPoints;

        private void Awake()
        {
            Instance = this;
        }

        private void OnDestroy()
        {
            if (Instance == this) Instance = null;
        }

        /// <summary>按客户端 Id 确定性选择出生点，所有机器结果一致。</summary>
        public Vector3 GetSpawnForClientId(ulong clientId)
        {
            if (spawnPoints == null || spawnPoints.Length == 0) return Vector3.up;
            ulong hash = clientId * 2654435761u;
            return spawnPoints[hash % (ulong)spawnPoints.Length].position;
        }

        /// <summary>服务端重生时随机选点。</summary>
        public Vector3 GetRandomSpawn()
        {
            if (spawnPoints == null || spawnPoints.Length == 0) return Vector3.up;
            return spawnPoints[UnityEngine.Random.Range(0, spawnPoints.Length)].position;
        }

        /// <summary>由网络层调用，在所有客户端触发击杀信息 UI。</summary>
        public static void BroadcastKillFeed(string killer, string victim)
        {
            KillFeedAdded?.Invoke(killer, victim);
        }
    }
}
