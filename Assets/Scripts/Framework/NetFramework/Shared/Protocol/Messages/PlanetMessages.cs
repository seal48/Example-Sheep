using System;
using System.Collections.Generic;

namespace GameFramework.Net.Protocol
{
    /// <summary>星球排行请求（空请求：服务端返回前 10 名 + 自己）。</summary>
    [Serializable]
    public class PlanetRankRequest
    {
        /// <summary>要取前几名（0 = 默认 10）。</summary>
        public int TopN;
    }

    /// <summary>一个星球（一个玩家）：排名、名字、拥有的羊。</summary>
    [Serializable]
    public class PlanetRankEntry
    {
        /// <summary>排名（1 开始）。</summary>
        public int Rank;

        /// <summary>玩家 Id。</summary>
        public string PlayerId;

        /// <summary>玩家名（展示用）。</summary>
        public string PlayerName;

        /// <summary>羊的总数（排名依据）。</summary>
        public int PetCount;

        /// <summary>拥有的羊 id 列表（刷星球上的羊）。</summary>
        public List<int> Pets;

        /// <summary>这一条是不是自己。</summary>
        public bool IsSelf;
    }

    /// <summary>星球排行结果。</summary>
    [Serializable]
    public class PlanetRankResponse
    {
        public bool Success;
        public string Message;

        /// <summary>排行列表（按羊数从多到少）。前 10 名 + 自己（自己不在前 10 时追加）。</summary>
        public List<PlanetRankEntry> Entries;
    }
}
