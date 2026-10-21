using System;
using System.Collections.Generic;
using GameFramework.Audio;
using GameFramework.Business;
using GameFramework.Config;
using GameFramework.Net.Protocol;
using GameFramework.Resource;
using GameFramework.UI;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 商店面板：顶部显示金币/钻石（图标读 Item 表 + UISprite，数量读后端背包），
/// 下方 ItemView 里按 ShopItem 表刷商品（每行 2 个、带间隔）。
///
/// 商品项节点（Assets/Prefabs/ShopItem.prefab）：
///   Description  商品描述（ShopItem.Name）
///   ItemBg/ItemIcon 商品图标（Item.SpriteId）
///   BuyBtn/BuyNum 消耗数量；免费商品显示「免费获得」
///   BuyBtn/BuyIcon 消耗道具图标；免费商品隐藏
///
/// 购买走服务端校验：客户端只发 shop.buy.request（商品 Id），服务端扣费 + 发放 + 推背包。
/// </summary>
[UIPanel("ShopPanel", UILayer.Popup)]
public partial class ShopPanel : Panel
{
    /// <summary>金币 / 钻石在 Item 表里的道具 Id。</summary>
    private const int CoinItemId = 2001;
    private const int DiamondItemId = 2002;

    private const string ShopItemPrefabLocation = "Assets/Prefabs/ShopItem.prefab";

    /// <summary>每行几个商品。</summary>
    private const int PerRow = 2;

    /// <summary>商品格间距。</summary>
    private const float CellGap = 20f;

    /// <summary>商品格高度（按预制体视觉留余量）。</summary>
    private const float CellHeight = 360f;

    private Image coinIcon;
    private Text coinNum;
    private Image diamondIcon;
    private Text diamondNum;
    private RectTransform itemView;
    private Button closeBtn;

    private ResourceAsset<GameObject> shopItemAsset;
    private GameObject shopItemPrefab;
    private readonly List<GameObject> itemViews = new List<GameObject>();
    private IDisposable buySub;

    protected override void OnInit()
    {
        coinIcon = Get<Image>("CoinIcon");
        coinNum = Get<Text>("CoinNum");
        diamondIcon = Get<Image>("DiamondIcon");
        diamondNum = Get<Text>("DiamondNum");
        itemView = Get<RectTransform>("ItemView");
        closeBtn = Get<Button>("CloseBtn");

        if (closeBtn != null) closeBtn.onClick.AddListener(Close);

        // 背包变化（买/获得道具）→ 刷新货币数量
        Listen<BagChangedEvent>(e => RefreshCurrency());

        var business = GameController.Instance != null ? GameController.Instance.Business : null;
        if (business != null)
            buySub = business.Hub.Register<ShopBuyResponse>(ProtocolId.ShopBuyResponse, OnBuyResponse, "ShopPanel");

        LoadShopItemPrefab();
    }

    protected override void OnClose()
    {
        if (closeBtn != null) closeBtn.onClick.RemoveListener(Close);
        if (buySub != null) { buySub.Dispose(); buySub = null; }
        if (shopItemAsset != null) { shopItemAsset.Dispose(); shopItemAsset = null; }
        shopItemPrefab = null;
        ClearItems();
    }

    protected override void OnShow(object userData)
    {
        RefreshCurrency();
        BuildItems();
    }

    // ============================== 货币 ==============================

    /// <summary>刷新金币/钻石：图标读 Item 表 → UISprite，数量读本地背包（后端推送同步）。</summary>
    private void RefreshCurrency()
    {
        var controller = GameController.Instance;
        var player = controller != null && controller.Business != null ? controller.Business.Player : null;

        if (coinNum != null) coinNum.text = GetBagCount(player, CoinItemId).ToString();
        if (diamondNum != null) diamondNum.text = GetBagCount(player, DiamondItemId).ToString();

        SetIcon(coinIcon, CoinItemId);
        SetIcon(diamondIcon, DiamondItemId);
    }

    private static int GetBagCount(PlayerModel player, int itemId)
    {
        if (player == null) return 0;
        int count;
        return player.Bag.TryGetValue(itemId.ToString(), out count) ? count : 0;
    }

    /// <summary>按道具 Id 设置图片（道具表 SpriteId → UISprite 图集）。</summary>
    private void SetIcon(Image img, int itemId)
    {
        if (img == null) return;

        string spriteId = GetItemSpriteId(itemId);
        var controller = GameController.Instance;
        if (string.IsNullOrEmpty(spriteId) || controller == null || controller.SpriteAtlases == null) return;

        string captured = spriteId;
        controller.SpriteAtlases.GetSpriteByIdAsync(captured, sprite =>
        {
            if (img != null) img.sprite = sprite;
        });
    }

    private static string GetItemSpriteId(int itemId)
    {
        var controller = GameController.Instance;
        if (controller == null || controller.Config == null || controller.Config.Database == null) return null;
        ItemConfig item = controller.Config.Database.Item.Get(itemId);
        return item != null ? item.SpriteId : null;
    }

    // ============================== 商品列表 ==============================

    private void LoadShopItemPrefab()
    {
        var controller = GameController.Instance;
        if (controller == null || controller.Resource == null) return;

        controller.Resource.LoadAssetAsync<GameObject>(ShopItemPrefabLocation, asset =>
        {
            if (asset == null || this == null) return;
            shopItemAsset = asset;
            shopItemPrefab = asset.Asset;
            BuildItems();   // 预制体到齐后重建列表
        });
    }

    /// <summary>按 ShopItem 表刷商品：每行 2 个，左上开始，行/列间隔 CellGap。</summary>
    private void BuildItems()
    {
        ClearItems();
        if (shopItemPrefab == null || itemView == null) return;

        var controller = GameController.Instance;
        var table = controller != null && controller.Config != null && controller.Config.Database != null
            ? controller.Config.Database.ShopItem
            : null;
        if (table == null || table.Rows == null || table.Rows.Count == 0) return;

        var rows = new List<ShopItemConfig>(table.Rows);
        rows.Sort((a, b) => a.Id.CompareTo(b.Id));

        Rect area = itemView.rect;
        float cellW = (area.width - CellGap * (PerRow - 1)) / PerRow;
        if (cellW <= 1f) cellW = 320f;

        for (int i = 0; i < rows.Count; i++)
        {
            GameObject go = Instantiate(shopItemPrefab, itemView);
            go.name = "ShopItem_" + rows[i].Id;

            RectTransform rt = go.transform as RectTransform;
            // 锚点放左上角，anchoredPosition 按"格子中心"算
            rt.anchorMin = new Vector2(0f, 1f);
            rt.anchorMax = new Vector2(0f, 1f);
            rt.pivot = new Vector2(0.5f, 0.5f);

            int row = i / PerRow;
            int col = i % PerRow;
            float cx = cellW * 0.5f + col * (cellW + CellGap);
            float cy = CellHeight * 0.5f + row * (CellHeight + CellGap);
            rt.anchoredPosition = new Vector2(cx, -cy);

            ConfigureItem(go, rows[i]);
            itemViews.Add(go);
        }
    }

    /// <summary>
    /// 填充一个商品项：描述、商品图、消耗数量/图标（免费则隐藏图标、写"免费获得"）。
    /// 节点层级：ShopItem/Description、ShopItem/ItemBg/ItemIcon、ShopItem/BuyBtn、ShopItem/BuyBtn/BuyNum、ShopItem/BuyBtn/BuyIcon
    /// </summary>
    private void ConfigureItem(GameObject go, ShopItemConfig cfg)
    {
        bool free = cfg.CostItemId <= 0 || cfg.CostNum <= 0;

        Transform t = go.transform.Find("Description");
        if (t != null)
        {
            Text text = t.GetComponent<Text>();
            if (text != null) text.text = cfg.Name;
        }

        t = go.transform.Find("ItemBg/ItemIcon");
        if (t != null)
        {
            Image img = t.GetComponent<Image>();
            if (img != null) SetIcon(img, cfg.ItemId);
        }

        t = go.transform.Find("BuyBtn/BuyNum");
        if (t != null)
        {
            Text text = t.GetComponent<Text>();
            if (text != null) text.text = free ? "免费获得" : cfg.CostNum.ToString();
        }

        t = go.transform.Find("BuyBtn/BuyIcon");
        if (t != null)
        {
            t.gameObject.SetActive(!free);
            if (!free)
            {
                Image img = t.GetComponent<Image>();
                if (img != null) SetIcon(img, cfg.CostItemId);
            }
        }

        t = go.transform.Find("BuyBtn");
        if (t != null)
        {
            Button btn = t.GetComponent<Button>();
            if (btn != null)
            {
                btn.onClick.RemoveAllListeners();
                int shopItemId = cfg.Id;
                btn.onClick.AddListener(() => Buy(shopItemId));
            }
        }
    }

    private void Buy(int shopItemId)
    {
        AudioKit.PlaySfx("ButtonClick");   // 运行时实例化的按钮不被面板自动扫描，手动补点击音效

        var client = GameController.Instance != null ? GameController.Instance.gameClient : null;
        if (client == null || client.Client == null) return;

        client.Send(ProtocolId.ShopBuyRequest, new ShopBuyRequest { ShopItemId = shopItemId });
    }

    /// <summary>购买回包：成功与否先刷新货币（服务端已推背包，这里兜底再刷一次）。</summary>
    private void OnBuyResponse(ShopBuyResponse response)
    {
        if (response == null) return;
        RefreshCurrency();
    }

    private void ClearItems()
    {
        for (int i = 0; i < itemViews.Count; i++)
            if (itemViews[i] != null) Destroy(itemViews[i]);
        itemViews.Clear();
    }
}
