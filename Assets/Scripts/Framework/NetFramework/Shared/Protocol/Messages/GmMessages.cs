using System;

namespace GameFramework.Net.Protocol
{
    /// <summary>
    /// 客户端 GM 面板发来的命令请求。Command 是一整条命令字符串，例如：
    ///   "additem 1001 5"   （加道具：道具id 数量）
    ///   "addpet 1001"      （加宠物收藏）
    ///   "gold 1000"        （加金币）
    /// 账号不需要填 —— 服务端用当前登录会话的账号执行。
    /// </summary>
    [Serializable]
    public class GmRequest
    {
        public string Command;
    }

    /// <summary>GM 命令执行结果。</summary>
    [Serializable]
    public class GmResponse
    {
        public bool Success;
        public string Message;
    }
}
