using System;

namespace GameFramework.Net.Protocol
{
    /// <summary>商店购买请求：ShopItemId = ShopItem 表的商品 Id。</summary>
    [Serializable]
    public class ShopBuyRequest
    {
        public int ShopItemId;
    }

    /// <summary>商店购买结果。</summary>
    [Serializable]
    public class ShopBuyResponse
    {
        public bool Success;
        public string Message;
        public int ShopItemId;
    }
}
