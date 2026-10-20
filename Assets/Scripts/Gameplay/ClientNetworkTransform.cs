using Unity.Netcode.Components;
using UnityEngine;

namespace FpsGame.Gameplay
{
    /// <summary>
    /// 所有者权威的 NetworkTransform（NGO 官方 ClientNetworkTransform 模式）。
    /// 玩家移动/视角由本人客户端驱动并复制给其他端；若用服务端权威 NetworkTransform，
    /// 纯客户端的本地移动会被服务端快照不停拉回，表现为"打包客户端动不了"。
    /// </summary>
    public class ClientNetworkTransform : NetworkTransform
    {
        protected override bool OnIsServerAuthoritative() => false;
    }
}
