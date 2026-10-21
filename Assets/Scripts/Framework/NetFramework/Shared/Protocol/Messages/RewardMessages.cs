using System;

namespace GameFramework.Net.Protocol
{
    /// <summary>
    /// 通关奖励请求：客户端通关**最后一关**时发。
    /// LevelId 交给服务端校验（必须是最后一关，防止只过第一关也领奖）。
    /// </summary>
    [Serializable]
    public class PetRewardRequest
    {
        public int LevelId;
    }

    /// <summary>
    /// 通关奖励结果：服务端随机挑一只**还没拥有**的羊发放。
    ///   · Success = true → PetId 是本次发放的羊（服务端同时会推 PetChangedPush，图鉴自动刷新）；
    ///   · AllOwned = true → 全部羊都已集齐，这次没有可发放的。
    /// </summary>
    [Serializable]
    public class PetRewardResponse
    {
        public bool Success;
        public int PetId;
        public bool AllOwned;
        public string Message;
    }
}
