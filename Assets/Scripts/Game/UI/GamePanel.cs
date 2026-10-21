using System;
using System.Collections.Generic;
using DG.Tweening;
using GameFramework.Audio;
using GameFramework.Config;
using GameFramework.Log;
using GameFramework.Net.Protocol;
using GameFramework.Pool;
using GameFramework.Resource;
using GameFramework.Gameplay;
using GameFramework.Startup;
using GameFramework.UI;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 游戏主界面：羊了个羊玩法。
/// 牌阵 / 卡槽 / 牌视图在运行时动态创建（挂到 BoardRoot / SlotRoot 下面）。
/// 每次都是从第 1 关（简单关）开始，不记录通关进度；结算后自动返回大厅。
/// </summary>
[UIPanel("GamePanel", UILayer.Normal)]
public partial class GamePanel : Panel
{
    /// <summary>默认起始关卡（始终从简单关开始）。</summary>
    public const int DefaultLevelId = 1;

    /// <summary>结算后停留多久再自动返回大厅（秒）。</summary>
    private const float ResultDelay = 1.6f;

    private const float TileSize = 80f;          // 兜底牌格大小（正常走自动撑满）
    private const float TileGap = 8f;
    private const float DefaultSlotCell = 88f;   // 兜底卡槽格大小

    // 牌格自动撑满：给标题 / 卡槽 / 左右留的边（设计像素，按预制体实际布局留 10px 余量）
    // 标题底边在设计高度 1080 下位于 y=1000 → 上边距 90；卡槽顶边在 y=160 → 下边距 170
    private const float BoardTopGap = 90f;
    private const float BoardBottomGapBase = 170f;
    private const float BoardSideGap = 24f;
    private const float MinBoardCell = 40f;
    // 上限别太大：否则 3x3 那种小关卡撑满后会比 7x9 大好几倍，两关看着不协调
    private const float MaxBoardCell = 150f;
    private const float SlotGap = 10f;

    // ---- 卡槽 / 移出暂存区布局（有「移出」道具的关卡才启用暂存区）----
    private const float MaxSlotCell = 140f;
    private const float ButtonSlotGap = 12f;      // 道具按钮顶边 ↔ 卡槽底边（卡槽下移时的下限）
    private const float BoardSlotGap = 10f;       // 卡槽 / 暂存区顶边 ↔ 牌阵底边
    private const float HoldingGap = 12f;         // 卡槽顶边 ↔ 暂存区底边
    private const float HoldingCellMax = 64f;     // 暂存区格子最大边长
    private const float HoldingCellRatio = 0.72f; // 暂存区格子相对卡槽格子的比例

    private static readonly Color[] IconColors =
    {
        new Color(0.95f, 0.55f, 0.40f),   // 1 橙
        new Color(0.45f, 0.75f, 0.95f),   // 2 蓝
        new Color(0.55f, 0.85f, 0.50f),   // 3 绿
        new Color(0.95f, 0.85f, 0.40f),   // 4 黄
        new Color(0.75f, 0.55f, 0.95f),   // 5 紫
        new Color(0.95f, 0.45f, 0.70f),   // 6 粉
        new Color(0.40f, 0.85f, 0.85f),   // 7 青
        new Color(0.70f, 0.60f, 0.45f),   // 8 棕
        new Color(0.85f, 0.45f, 0.45f),   // 9 红
        new Color(0.60f, 0.75f, 0.95f),   // 10 淡蓝
    };

    /// <summary>砖块图鉴的配置行缓存（UISprite 表 Atlas == "Brick"，按 Id 排序），第一次用时懒加载。</summary>
    private List<UISpriteConfig> brickRows;

    private SheepGame game;
    private bool returningToLobby;

    /// <summary>当前正在玩的关卡号（通关后决定进下一关还是回大厅）。</summary>
    private int playingLevelId = DefaultLevelId;

    /// <summary>牌格自适应尺寸（Refresh 算，保证整个牌桌放进可用区域）。</summary>
    private float boardCell = TileSize + TileGap;

    /// <summary>卡槽格大小（按可用宽度自动算，见 ComputeBoardMetrics）。</summary>
    private float slotCell = DefaultSlotCell;

    /// <summary>所有牌（含层偏移）的视觉中心（Refresh 里算），用于居中牌桌。</summary>
    private Vector2 boardCenterCell = Vector2.zero;

    /// <summary>牌阵底边离屏幕底的距离（有暂存区的关卡要往上让位，开局算一次）。</summary>
    private float boardBottomGap = BoardBottomGapBase;

    /// <summary>卡槽中心 Y（有暂存区的关卡会下移到按钮上方，开局算一次）。</summary>
    private float slotCenterY = 194f;

    /// <summary>预制体里 SlotRoot 的原始 Y（OnInit 读一次；没暂存区的关卡保持原位）。</summary>
    private float slotPrefabY = 194f;

    /// <summary>道具按钮的顶边 Y（OnInit 按预制体实际位置算：卡槽下移不能压到按钮）。</summary>
    private float buttonTopY = 101f;

    /// <summary>移出暂存区格子边长（开局算一次）。</summary>
    private float holdingCell = HoldingCellMax;

    /// <summary>堆叠样式的每层视觉偏移（像素；方格样式为 0），开局算一次。</summary>
    private float layerOffsetPx;

    /// <summary>是否有牌正在飞向槽位（飞行中屏蔽点击/道具，避免状态错乱）。</summary>
    private bool flightActive;

    /// <summary>牌 / 槽位预制体（运行时从资源加载，走 YooAsset 可热更；只含 UGUI 内置组件）。</summary>
    private GameObject tilePrefab;
    private GameObject slotPrefab;

    /// <summary>预制体资源句柄（面板关闭时释放）。</summary>
    private ResourceAsset<GameObject> tilePrefabAsset;
    private ResourceAsset<GameObject> slotPrefabAsset;

    private readonly List<GameObject> tileViews = new List<GameObject>();
    /// <summary>槽位框（固定 SlotSize 个，整局不回收；背景，不做动画）。</summary>
    private readonly List<GameObject> slotFrames = new List<GameObject>();

    /// <summary>槽位里的牌（内容，消除/前移只动画这些）。</summary>
    private readonly List<GameObject> slotContents = new List<GameObject>();

    /// <summary>移出暂存区容器（运行时创建，挂在 SlotRoot 后面；不是预制体节点）。</summary>
    private RectTransform holdingRoot;

    /// <summary>暂存区底板（运行时创建的纯色 Image，指示这是一块临时存放区）。</summary>
    private Image holdingBack;

    /// <summary>暂存区里的牌视图（点一下能飞回卡槽）。</summary>
    private readonly List<GameObject> holdingViews = new List<GameObject>();

    /// <summary>
    /// 对象池引用（OnInit 缓存一次）。
    /// 退出游戏时 GameController.OnDestroy 会先把 Instance 置空，面板的 OnClose 再走
    /// GameController.Instance.Pool 就拿不到池了 → 归还静默失败，视图残留在场景里。
    /// 缓存起来之后清理不再依赖 Instance，退出播放也就不会报「not cleaned up」。
    /// </summary>
    private ObjectPoolManager viewPool;

    /// <summary>通关奖励回包订阅（面板关闭时退订）。</summary>
    private IDisposable rewardSub;

    /// <summary>已经向服务端要过奖励（防止重复请求）。</summary>
    private bool rewardRequested;

    /// <summary>回大厅已经发起（奖励关闭 + 兜底定时器可能同时触发，只走一次）。</summary>
    private bool lobbyExitStarted;

    protected override void OnInit()
    {
        // 缓存对象池引用：退出游戏时 Instance 会被先置空，清理视图必须用缓存的引用而不是 GameController.Instance
        viewPool = GameController.Instance != null ? GameController.Instance.Pool : null;

        // 收服务端的通关奖励回包（随机一只未拥有的羊）
        var business = GameController.Instance != null ? GameController.Instance.Business : null;
        if (business != null)
            rewardSub = business.Hub.Register<PetRewardResponse>(ProtocolId.PetRewardResponse, OnPetRewardResponse, "GamePanel");

        // 记下预制体里卡槽 / 道具按钮的真实位置：卡槽下移不能压到按钮，牌阵也不能压到卡槽
        if (slotRoot != null) slotPrefabY = slotRoot.rectTransform.anchoredPosition.y;
        if (undoButton != null)
        {
            RectTransform btnRect = undoButton.transform as RectTransform;
            if (btnRect != null)
            {
                float btnHeight = btnRect.rect.height;
                if (btnHeight <= 0f) btnHeight = btnRect.sizeDelta.y;
                buttonTopY = btnRect.anchoredPosition.y + btnHeight * 0.5f;
            }
        }

        backButton.onClick.AddListener(BackToLobby);
        undoButton.onClick.AddListener(OnUndo);
        removeButton.onClick.AddListener(OnRemove);
        shuffleButton.onClick.AddListener(OnShuffle);

        LoadViewPrefabs();
    }

    /// <summary>加载牌 / 槽位预制体（对象池按地址建池，预制体可热更）。加载完成后若已在局内，重建一次视图。</summary>
    private void LoadViewPrefabs()
    {
        GameController controller = GameController.Instance;
        if (controller == null || controller.Resource == null) return;

        controller.Resource.LoadAssetAsync<GameObject>("Assets/Prefabs/SheepTile.prefab", asset =>
        {
            if (asset == null || this == null) return;
            tilePrefabAsset = asset;
            tilePrefab = asset.Asset;
            if (game != null && IsShown) Refresh();
        });

        controller.Resource.LoadAssetAsync<GameObject>("Assets/Prefabs/SheepSlot.prefab", asset =>
        {
            if (asset == null || this == null) return;
            slotPrefabAsset = asset;
            slotPrefab = asset.Asset;
            if (game != null && IsShown) Refresh();
        });
    }

    protected override void OnShow(object userData)
    {
        // 进关卡开始循环放背景音乐。
        // 用带扩展名的完整地址：短名会依次探测 .wav/.ogg/.mp3，不存在的候选会被资源服务各打一条
        // "资源地址不存在"的错误（虽然最终能加载成功，但日志很吵）。
        AudioKit.PlayBgm("Assets/Audio/Bgm/GameBgm.mp3");

        // 始终从简单关（第 1 关）开始，不记录进度
        StartLevel(DefaultLevelId);
    }


    protected override void OnClose()
    {
        if (rewardSub != null) { rewardSub.Dispose(); rewardSub = null; }

        backButton.onClick.RemoveListener(BackToLobby);
        undoButton.onClick.RemoveListener(OnUndo);
        removeButton.onClick.RemoveListener(OnRemove);
        shuffleButton.onClick.RemoveListener(OnShuffle);

        DetachGame();
        ClearViews();
        game = null;

        if (tilePrefabAsset != null) { tilePrefabAsset.Dispose(); tilePrefabAsset = null; }
        if (slotPrefabAsset != null) { slotPrefabAsset.Dispose(); slotPrefabAsset = null; }
        tilePrefab = null;
        slotPrefab = null;

        DespawnSlotViews();
    }

    // ============================== 开局 ==============================

    private void StartLevel(int levelId)
    {
        DetachGame();
        playingLevelId = levelId;
        ClearViews();
        returningToLobby = false;
        flightActive = false;
        rewardRequested = false;
        lobbyExitStarted = false;
        // 复位结算层：面板是缓存的，上次通关/失败的遮罩必须清掉，否则盖住新对局（看起来像卡死）
        resultOverlay.gameObject.SetActive(false);

        SheepLevelData level = SheepLevelLoader.Get(levelId);
        if (level == null)
        {
            SetText(titleText, "羊了个羊：星球（关卡 " + levelId + " 配置缺失）");
            resultOverlay.gameObject.SetActive(true);
            SetText(resultText, "关卡 " + levelId + " 没有配置，请先导出 SheepLevel 表");
            restartButton.gameObject.SetActive(false);
            return;
        }

        game = new SheepGame(level);
        game.StateChanged += OnStateChanged;
        game.GameOver += OnGameOver;

        // 手工布局（Style=4）写错时给出提示（尺寸不匹配 / 空布局回退等）
        if (!string.IsNullOrEmpty(game.LastGenerateError))
            GameLog.Warn("Sheep", "第 " + levelId + " 关布局：" + game.LastGenerateError);

        // 开局算一次牌桌居中（整局固定，消牌时牌组不移动）
        ComputeBoardMetrics();

        SetText(titleText, "羊了个羊：星球（第 " + levelId + " 关）");
        Refresh();
    }

    private void DetachGame()
    {
        if (game != null)
        {
            game.StateChanged -= OnStateChanged;
            game.GameOver -= OnGameOver;
        }
    }

    /// <summary>
    /// 开局算一次：牌格大小**自动撑满可用区域**（面板尺寸去掉顶部标题 / 底部卡槽 / 左右留边），
    /// 再算牌桌视觉中心与卡槽格大小。整局不变，消牌时牌组不会移动。
    /// </summary>
    private void ComputeBoardMetrics()
    {
        if (game == null) return;

        float offRatio = game.LayerOffsetRatio;

        // 1) 内容跨度（格为单位，含每层堆叠偏移）
        float minX = float.MaxValue, maxX = float.MinValue;
        float minY = float.MaxValue, maxY = float.MinValue;
        for (int i = 0; i < game.Tiles.Count; i++)
        {
            SheepTile t = game.Tiles[i];
            float cx = t.X + t.SubX + t.Layer * offRatio * t.StackX;
            float cy = t.Y + t.SubY + t.Layer * offRatio * t.StackY;
            if (cx < minX) minX = cx;
            if (cx > maxX) maxX = cx;
            if (cy < minY) minY = cy;
            if (cy > maxY) maxY = cy;
        }

        if (minX > maxX)
        {
            boardCell = TileSize + TileGap;
            slotCell = DefaultSlotCell;
            layerOffsetPx = offRatio * boardCell;
            boardCenterCell = Vector2.zero;
            slotCenterY = slotPrefabY;
            boardBottomGap = slotCenterY + slotCell * 0.5f + BoardSlotGap;
            ApplyBottomLayout();
            return;
        }

        float spanX = (maxX - minX) + 1f;
        float spanY = (maxY - minY) + 1f;

        // 2) 可用区域：面板尺寸去掉顶部标题 / 底部（卡槽 + 暂存区）/ 左右留边
        RectTransform selfRect = transform as RectTransform;
        float panelW = selfRect != null ? selfRect.rect.width : 1920f;
        float panelH = selfRect != null ? selfRect.rect.height : 1080f;
        float availW = panelW - BoardSideGap * 2f;

        // 卡槽格：跟着牌格走，但不超过屏幕可用宽度
        float slotFit = (panelW - BoardSideGap * 2f - (game.SlotSize - 1) * SlotGap) / game.SlotSize;

        // 移出暂存区：只有配了移出次数的关卡才有（牌数上限 = 移出次数 × 3）
        int holdingCap = game.HoldingCapacity;
        float holdingFit = holdingCap > 0
            ? (panelW - BoardSideGap * 2f - (holdingCap - 1) * SlotGap) / holdingCap
            : HoldingCellMax;

        // 3) 从下往上堆：道具按钮 → 卡槽 → 暂存区 → 牌阵。
        //    牌格 / 卡槽格 / 底部留白互相依赖（格子越大底部越高），迭代几次收敛。
        float bottomGap = BoardBottomGapBase;
        for (int iter = 0; iter < 8; iter++)
        {
            float availH = panelH - BoardTopGap - bottomGap;
            // 牌宽 = boardCell - TileGap，所以算上限时补回 TileGap
            float fit = Mathf.Min((availW + TileGap) / spanX, (availH + TileGap) / spanY);
            boardCell = Mathf.Clamp(fit, MinBoardCell, MaxBoardCell);
            slotCell = Mathf.Clamp(Mathf.Min(boardCell, slotFit), MinBoardCell, MaxSlotCell);

            // 卡槽：有暂存区时下移到"道具按钮正上方"（省下的高度让给暂存区），否则保持预制体原位
            slotCenterY = holdingCap > 0 ? (buttonTopY + ButtonSlotGap + slotCell * 0.5f) : slotPrefabY;

            float next;
            if (holdingCap > 0)
            {
                holdingCell = Mathf.Clamp(Mathf.Min(slotCell * HoldingCellRatio, holdingFit), MinBoardCell, HoldingCellMax);
                next = slotCenterY + slotCell * 0.5f + HoldingGap + holdingCell + BoardSlotGap;
            }
            else
            {
                next = slotCenterY + slotCell * 0.5f + BoardSlotGap;
            }

            bool converged = Mathf.Abs(next - bottomGap) < 0.5f;
            bottomGap = next;
            if (converged) break;
        }
        boardBottomGap = bottomGap;

        // 4) 每层视觉偏移（像素）
        layerOffsetPx = offRatio * boardCell;

        // 5) 视觉中心（格为单位）
        boardCenterCell = new Vector2((minX + maxX) * 0.5f, (minY + maxY) * 0.5f);

        // 6) 卡槽 / 暂存区落位 + 把牌桌容器挪到"标题和底部之间"的正中
        ApplyBottomLayout();
    }

    /// <summary>
    /// 把卡槽 / 暂存区 / 牌阵容器按当前算好的尺寸落位。
    /// 牌桌容器在预制体里固定在面板中心 +130（比空闲带中心偏高，会顶到标题），所以运行时重算。
    /// </summary>
    private void ApplyBottomLayout()
    {
        if (game == null) return;

        RectTransform selfRect = transform as RectTransform;
        float panelH = selfRect != null ? selfRect.rect.height : 1080f;

        if (slotRoot != null)
            slotRoot.rectTransform.anchoredPosition = new Vector2(0f, slotCenterY);

        if (boardRoot != null)
        {
            float bandCenter = (boardBottomGap + (panelH - BoardTopGap)) * 0.5f;
            boardRoot.rectTransform.anchoredPosition = new Vector2(0f, bandCenter - panelH * 0.5f);
        }

        EnsureHoldingRoot();
        LayoutHoldingRoot();
    }

    /// <summary>
    /// 建移出暂存区容器（运行时创建，不是预制体节点：热更程序集不能被预制体序列化，
    /// 这样改代码也不用重建资源包）。紧跟在 SlotRoot 后面 —— 盖住牌阵，但在道具按钮 / 结算层之下。
    /// </summary>
    private void EnsureHoldingRoot()
    {
        if (holdingRoot != null || slotRoot == null) return;

        GameObject go = new GameObject("HoldingRoot", typeof(RectTransform));
        holdingRoot = go.transform as RectTransform;
        holdingRoot.SetParent(slotRoot.transform.parent, false);
        holdingRoot.anchorMin = new Vector2(0.5f, 0f);
        holdingRoot.anchorMax = new Vector2(0.5f, 0f);
        holdingRoot.pivot = new Vector2(0.5f, 0.5f);
        holdingRoot.sizeDelta = new Vector2(HoldingCellMax, HoldingCellMax);
        holdingRoot.SetSiblingIndex(slotRoot.transform.GetSiblingIndex() + 1);

        GameObject back = new GameObject("HoldingBack", typeof(RectTransform), typeof(Image));
        RectTransform brt = back.transform as RectTransform;
        brt.SetParent(holdingRoot, false);
        brt.anchorMin = new Vector2(0.5f, 0.5f);
        brt.anchorMax = new Vector2(0.5f, 0.5f);
        brt.pivot = new Vector2(0.5f, 0.5f);
        brt.anchoredPosition = Vector2.zero;
        holdingBack = back.GetComponent<Image>();
        holdingBack.color = new Color(0.12f, 0.16f, 0.22f, 0.55f);
        holdingBack.raycastTarget = false;
    }

    /// <summary>暂存区容器位置 / 大小（卡槽正上方）。</summary>
    private void LayoutHoldingRoot()
    {
        if (holdingRoot == null || game == null) return;

        bool visible = game.HoldingCapacity > 0;
        if (holdingRoot.gameObject.activeSelf != visible)
            holdingRoot.gameObject.SetActive(visible);
        if (!visible) return;

        float rowWidth = HoldingRowWidth();
        holdingRoot.anchoredPosition = new Vector2(0f, slotCenterY + slotCell * 0.5f + HoldingGap + holdingCell * 0.5f);
        holdingRoot.sizeDelta = new Vector2(rowWidth, holdingCell);

        if (holdingBack != null)
        {
            RectTransform brt = holdingBack.transform as RectTransform;
            brt.sizeDelta = new Vector2(rowWidth + 12f, holdingCell + 12f);
            brt.anchoredPosition = Vector2.zero;
        }
    }

    /// <summary>暂存区整排宽度（按容量算：空着也占位，玩家能看到"这里能临时放牌"）。</summary>
    private float HoldingRowWidth()
    {
        int cap = game != null ? game.HoldingCapacity : 0;
        if (cap <= 0) return holdingCell;
        return cap * holdingCell + (cap - 1) * SlotGap;
    }

    /// <summary>暂存区第 index 格的本地 X 坐标。</summary>
    private float HoldingIndexX(int index)
    {
        float total = HoldingRowWidth();
        return -total * 0.5f + holdingCell * 0.5f + index * (holdingCell + SlotGap);
    }

    /// <summary>关卡总数（SheepLevel 表行数；读不到表时兜底 2）。</summary>
    private static int LevelCount
    {
        get
        {
            GameController controller = GameController.Instance;
            var table = controller != null && controller.Config != null && controller.Config.Database != null
                ? controller.Config.Database.SheepLevel
                : null;
            return table != null && table.Count > 0 ? table.Count : 2;
        }
    }

    // ============================== 交互 ==============================

    private void OnTileClicked(SheepTile tile, GameObject view)
    {
        if (game == null || game.IsOver || returningToLobby) return;
        if (flightActive) return;   // 动画期间不再响应点击

        // 这张是从移出暂存区点进来的？起点就是它现在待的位置，落地流程完全一样
        bool fromHolding = tile.State == SheepTileState.Parked;

        // 计算这张牌进槽后落在哪个槽位（TryTap 前，槽还没变）：插到最后一个同类的后面
        int slotIndex = game.Slot.Count;
        for (int i = game.Slot.Count - 1; i >= 0; i--)
        {
            if (game.Slot[i].IconId == tile.IconId) { slotIndex = i + 1; break; }
        }
        Vector3 startWorld = fromHolding && view != null ? view.transform.position : BoardTileWorldPos(tile);
        Vector3 endWorld = SlotCellWorldPos(slotIndex);

        // 把这张原牌视图"拿出来"：脱离原层级 + 从列表移除，Refresh 的清理不会收回它，让它继续飞
        if (view != null)
        {
            view.transform.SetParent(transform, true);   // worldPositionStays：保持世界坐标不变
            if (fromHolding) holdingViews.Remove(view);
            else tileViews.Remove(view);
        }

        SheepGame captured = game;
        flightActive = true;

        // 数据延迟消除：TryTap 只插入不消（数据里先留着 3 张，等飞完查数据再决定播消除动画）
        game.TryTap(tile);   // 触发 StateChanged -> Refresh：牌阵遮挡立即更新，槽位显示新牌 + 待消组

        // 飞原牌视图到槽位；落地后查数据决定是否播消除动画
        FlyOriginalView(view, startWorld, endWorld, () =>
        {
            if (this == null || game == null || game != captured || returningToLobby)
            {
                flightActive = false;
                return;
            }

            // 查槽位数据：有没有 >=3 的连消
            int matchStart, matchCount;
            if (FindSlotMatch(out matchStart, out matchCount))
            {
                // 有：先播消除动画（各自原地缩小），播完前移补齐，最后才提交数据消除
                PlaySlotClearAndShift(matchStart, matchCount, () =>
                {
                    if (this == null || game == null || game != captured || returningToLobby) return;
                    game.CompletePendingClear();   // 真正从数据移除 + 胜负判定 + StateChanged -> Refresh
                    RenderSlotContents();         // 最终同步（位置已对，无跳变）
                    flightActive = false;
                });
            }
            else
            {
                // 没有三合一：什么都不用做，牌安静待在槽里
                flightActive = false;
            }
        });
    }

    /// <summary>在槽位数据里找第一组 >=3 的连消，返回起始下标和数量。没有返回 false。</summary>
    private bool FindSlotMatch(out int start, out int count)
    {
        start = 0;
        count = 0;
        if (game == null) return false;

        for (int i = 0; i < game.Slot.Count; i++)
        {
            int icon = game.Slot[i].IconId;
            int s = i;
            while (s > 0 && game.Slot[s - 1].IconId == icon) s--;
            int e = i;
            while (e < game.Slot.Count - 1 && game.Slot[e + 1].IconId == icon) e++;
            int run = e - s + 1;
            if (run >= 3)
            {
                start = s;
                count = run;
                return true;
            }
            i = e;   // 跳过这一组
        }
        return false;
    }

    /// <summary>
    /// 消除动画：start 起 count 个槽位"内容牌"各自原地缩小消失（背景槽位框不动），
    /// 播完把这些内容牌归还池、移除出 slotContents，并把后面剩余的内容牌前移补齐。全部播完回调 onDone。
    /// </summary>
    private void PlaySlotClearAndShift(int start, int count, System.Action onDone)
    {
        // 1) 各自原地缩小消失（第一个 Append，其余 Join 并行）
        Sequence seq = Anim.NewSequence();
        bool firstScale = true;
        for (int i = start; i < start + count && i < slotContents.Count; i++)
        {
            GameObject content = slotContents[i];
            if (content == null) continue;
            RectTransform rt = content.transform as RectTransform;
            Tween scale = rt.DOScale(Vector3.zero, 0.18f).SetEase(Ease.InBack);
            if (firstScale) { seq.Append(scale); firstScale = false; }
            else seq.Join(scale);
        }

        // 2) 缩小播完：移除并归还这 count 个内容牌
        seq.AppendCallback(() =>
        {
            ObjectPoolManager pool = GameController.Instance != null ? GameController.Instance.Pool : null;
            int removeEnd = System.Math.Min(start + count, slotContents.Count);
            for (int i = removeEnd - 1; i >= start; i--)
            {
                GameObject content = slotContents[i];
                if (content != null && pool != null) pool.Despawn(content);
                slotContents.RemoveAt(i);
            }
        });

        // 3) 后面剩余的内容牌（原下标 >= start+count）前移补齐（平移动画，join 到回调后立即开始）
        for (int i = start + count; i < slotContents.Count; i++)
        {
            GameObject content = slotContents[i];
            if (content == null) continue;
            RectTransform rt = content.transform as RectTransform;
            // 中心锚点下 localPosition == anchoredPosition，用核心 DOLocalMove（Game 程序集无 DOTween UI 模块）
            Vector3 target = new Vector3(SlotIndexX(i - count), rt.localPosition.y, rt.localPosition.z);
            seq.Join(rt.DOLocalMove(target, 0.15f).SetEase(Ease.OutQuad));
        }
        seq.OnComplete(() => { if (onDone != null) onDone(); });
    }

    private void OnUndo()
    {
        if (game == null || returningToLobby || flightActive) return;
        game.TryUndo();
    }

    private void OnRemove()
    {
        if (game == null || returningToLobby || flightActive) return;
        game.TryRemove();
    }

    private void OnShuffle()
    {
        if (game == null || returningToLobby || flightActive) return;
        game.TryShuffle();
    }

    /// <summary>牌桌上某张牌的世界位置（与 CreateTileView 的摆放一致）。</summary>
    private Vector3 BoardTileWorldPos(SheepTile tile)
    {
        float cell = boardCell;
        float x = (tile.X + tile.SubX - boardCenterCell.x) * cell + tile.Layer * layerOffsetPx * tile.StackX;
        float y = (tile.Y + tile.SubY - boardCenterCell.y) * cell + tile.Layer * layerOffsetPx * tile.StackY;
        return boardRoot.transform.TransformPoint(x, y, 0);
    }

    /// <summary>槽位第 index 格的世界位置。</summary>
    private Vector3 SlotCellWorldPos(int index)
    {
        float total = game.SlotSize * slotCell + (game.SlotSize - 1) * SlotGap;
        float x = -total * 0.5f + slotCell * 0.5f + index * (slotCell + SlotGap);
        return slotRoot.transform.TransformPoint(x, 0, 0);
    }

    /// <summary>
    /// 飞原牌视图：飞行的就是牌桌上那张牌本身（不是复制品），用框架动画插件
    /// （DOTween，Anim.NewSequence 已挂面板生命周期，关闭/隐藏自动 Kill）从 startWorld 飞到 endWorld，
    /// 落地归还对象池。
    /// </summary>
    private void FlyOriginalView(GameObject view, Vector3 startWorld, Vector3 endWorld, System.Action onDone)
    {
        if (view == null)
        {
            if (onDone != null) onDone();
            return;
        }

        RectTransform rt = view.transform as RectTransform;

        bool despawned = false;
        TweenCallback despawnView = () =>
        {
            if (despawned) return;
            despawned = true;
            if (view != null)
                ReturnView(view);
        };

        // 框架动画器：NewSequence 已登记到面板，面板隐藏/销毁时自动 Kill，不会打在半路状态
        Sequence seq = Anim.NewSequence();
        seq.Append(rt.DOMove(endWorld, 0.30f).SetEase(Ease.OutQuad));
        seq.OnComplete(() =>
        {
            despawnView();
            if (onDone != null) onDone();
        });
        // 被中途 Kill（面板隐藏/关闭）时也归还视图
        seq.OnKill(despawnView);
    }

    private void BackToLobby()
    {
        if (lobbyExitStarted) return;
        lobbyExitStarted = true;

        var controller = GameController.Instance;
        if (controller == null || controller.Procedures == null) return;

        Anim.Pop("BackButton");
        controller.Procedures.ChangeState<ProcedureEnterLobby>();
    }

    // ============================== 事件 ==============================

    private void OnStateChanged()
    {
        Refresh();
    }

    /// <summary>
    /// 结算：
    ///   · 通关第 1 关（非最后一关）→ 短暂提示后自动开下一关（**不给奖励**）；
    ///   · 通关最后一关 → 向服务端请求奖励（随机一只未拥有的羊），弹奖励窗，关掉后回大厅；
    ///   · 失败 → 回大厅（不给奖励）。
    /// 中途退出（返回大厅按钮）走的是 BackToLobby，不经过结算，所以也不会发奖励。
    /// 每次从大厅进入都是第 1 关，不记录进度。
    /// </summary>
    private void OnGameOver(bool won)
    {
        if (returningToLobby) return;

        Refresh();
        returningToLobby = true;

        resultOverlay.gameObject.SetActive(true);
        restartButton.gameObject.SetActive(false);

        if (won)
        {
            if (playingLevelId < LevelCount)
            {
                // 通关（非最后一关）：提示后自动进下一关 —— 这一关不给奖励
                SetText(resultText, "第 " + playingLevelId + " 关通过！进入下一关");
                int next = playingLevelId + 1;
                Timers.Delay(this, ResultDelay, () => StartLevel(next));
                return;
            }

            // 通关最后一关：向服务端要奖励（随机一只还没有的羊），等回包再回大厅
            SetText(resultText, "通关成功！");
            RequestPetReward(playingLevelId);
            // 兜底：网络没回包也不能卡在结算界面
            Timers.Delay(this, 6f, BackToLobby);
            return;
        }

        // 失败：不给奖励
        SetText(resultText, "游戏结束");
        Timers.Delay(this, ResultDelay, BackToLobby);
    }

    /// <summary>向服务端请求通关奖励（只发一次，面板关闭后回包会被忽略）。</summary>
    private void RequestPetReward(int levelId)
    {
        if (rewardRequested) return;
        rewardRequested = true;

        var client = GameController.Instance != null ? GameController.Instance.gameClient : null;
        if (client == null || client.Client == null)
        {
            GameLog.Warn("Sheep", "[奖励] 客户端没连上服务器，跳过通关奖励");
            return;
        }

        client.Send(ProtocolId.PetRewardRequest, new PetRewardRequest { LevelId = levelId });
    }

    /// <summary>服务端回包：有羊就弹奖励窗（关掉后回大厅）；全部集齐则不弹。</summary>
    private void OnPetRewardResponse(PetRewardResponse response)
    {
        if (response == null) return;
        if (!rewardRequested) return;          // 不是这次通关触发的，忽略

        if (response.Success && response.PetId > 0)
        {
            RewardPanel.ShowPet(response.PetId, BackToLobby);
            return;
        }

        if (response.AllOwned)
            GameLog.Info("Sheep", "[奖励] 已集齐全部羊，本次无奖励");
        else
            GameLog.Warn("Sheep", "[奖励] 未发放：" + response.Message);

        // 没奖励就直接回大厅
        BackToLobby();
    }

    // ============================== 渲染 ==============================

    private void Refresh()
    {
        if (game == null) return;

        ClearViews();

        // 牌阵：先按"绘制顺序"排序再创建（UGUI 里后创建的画在上面）
        // 顺序 = 层号升序（盖住别人的牌层号一定更高，必须画在被盖的牌之上）+ 同层屏幕 Y 降序（下面的画在前面）
        List<SheepTile> onBoard = new List<SheepTile>();
        for (int i = 0; i < game.Tiles.Count; i++)
        {
            SheepTile tile = game.Tiles[i];
            if (tile.State == SheepTileState.OnBoard) onBoard.Add(tile);
        }
        onBoard.Sort(CompareTileDrawOrder);
        for (int i = 0; i < onBoard.Count; i++)
        {
            GameObject view = CreateTileView(onBoard[i]);
            if (view != null) tileViews.Add(view);
        }

        // 卡槽：固定槽位框 + 按数据渲染槽位内容
        EnsureSlotFrames();
        RenderSlotContents();

        // 移出暂存区（卡槽上方那一排）
        RenderHoldingViews();

        // 道具按钮
        SetText(undoText, "撤回 " + game.UndoCount);
        SetText(removeText, "移出 " + game.RemoveCount);
        SetText(shuffleText, "洗牌 " + game.ShuffleCount);

        undoButton.interactable = !game.IsOver && game.UndoCount > 0;
        // 移出固定挪 3 张：槽里不足 3 张时点了也没意义，直接置灰（玩家永远知道会移出几张）
        removeButton.interactable = !game.IsOver && game.RemoveCount > 0 && game.Slot.Count >= SheepGame.RemoveBatch;
        shuffleButton.interactable = !game.IsOver && game.ShuffleCount > 0;
    }

    /// <summary>
    /// 牌的绘制顺序（UGUI 后画的在上面）：
    ///   1) 层号升序 —— 盖住别人的牌层号一定更高，保证它画在被盖的牌之上；
    ///   2) 同层按屏幕 Y 降序 —— 屏幕上更靠下的牌画在前面（符合俯视遮挡直觉）。
    /// </summary>
    private int CompareTileDrawOrder(SheepTile a, SheepTile b)
    {
        int c = a.Layer.CompareTo(b.Layer);
        if (c != 0) return c;

        float ay = (a.Y + a.SubY) * boardCell + a.Layer * layerOffsetPx * a.StackY;
        float by = (b.Y + b.SubY) * boardCell + b.Layer * layerOffsetPx * b.StackY;
        if (ay > by) return -1;
        if (ay < by) return 1;
        return 0;
    }

    private GameObject CreateTileView(SheepTile tile)
    {
        if (tilePrefab == null) return null;   // 预制体还没加载好（加载完成会再 Refresh）
        GameObject go = GameController.Instance.Pool.Spawn(tilePrefab, boardRoot.transform);
        if (go == null) return null;

        RectTransform rt = go.transform as RectTransform;
        rt.localScale = Vector3.one;   // 池不重置状态：防御性复位
        bool covered = game.IsCovered(tile);
        bool clickable = !covered && !game.IsOver && !returningToLobby;
        int icon = tile.VisibleIcon;

        // 位置：按牌桌视觉中心居中；每层按这张牌所在叠的方向加视觉偏移，露出"厚度"
        float cell = boardCell;
        float x = (tile.X + tile.SubX - boardCenterCell.x) * cell + tile.Layer * layerOffsetPx * tile.StackX;
        float y = (tile.Y + tile.SubY - boardCenterCell.y) * cell + tile.Layer * layerOffsetPx * tile.StackY;
        rt.anchoredPosition = new Vector2(x, y);
        rt.sizeDelta = new Vector2(cell - TileGap, cell - TileGap);

        Image image = go.GetComponent<Image>();
        ApplyTileIcon(image, icon, covered);
        // 关键：Button 的点击靠 targetGraphic 的 raycastTarget 接收射线。
        // 可点的牌必须开 raycastTarget；被盖住的牌关掉（不挡下层、也不响应）。
        image.raycastTarget = clickable;

        // 图标文字（盲盒未点开 = ?；普通牌已显示砖块图，不再画数字）；预制体带 Label 子节点
        Text text = go.GetComponentInChildren<Text>(true);
        text.text = icon <= 0 ? "?" : "";
        text.fontSize = Mathf.Max(16, (int)(boardCell * 0.4f));
        // 被盖住的牌：文字也调暗，整体呈现"灰掉不可点"的观感
        text.color = covered ? new Color(0.75f, 0.75f, 0.78f) : Color.white;

        // 按钮（预制体自带）：清掉旧监听，可点的牌才响应
        Button button = go.GetComponent<Button>();
        button.onClick.RemoveAllListeners();
        button.interactable = clickable;
        if (clickable)
        {
            SheepTile captured = tile;
            GameObject view = go;
            button.onClick.AddListener(() => OnTileClicked(captured, view));
        }

        return go;
    }

    // ============================== 牌面图标 ==============================

    /// <summary>取 UISprite 表里 Atlas == "Brick" 的全部行（按 Id 排序），缓存起来。</summary>
    private List<UISpriteConfig> GetBrickRows()
    {
        if (brickRows != null) return brickRows;

        brickRows = new List<UISpriteConfig>();
        var db = GameController.Instance != null && GameController.Instance.Config != null
            ? GameController.Instance.Config.Database : null;
        if (db != null && db.UISprite != null && db.UISprite.Rows != null)
        {
            for (int i = 0; i < db.UISprite.Rows.Count; i++)
            {
                UISpriteConfig row = db.UISprite.Rows[i];
                if (row != null && row.Atlas == "Brick")
                    brickRows.Add(row);
            }
            brickRows.Sort((a, b) => string.CompareOrdinal(a.Id, b.Id));
        }
        return brickRows;
    }

    /// <summary>
    /// 给一张牌设图标：普通牌 = 砖块图（icon 1 起，按 (icon-1) % 砖块数 映射）；
    /// 盲盒未点开（icon<=0）= 灰色底（留给"?"文字）。盖住的牌整体压暗。
    /// 图还没加载到时先留透明，避免闪色块。
    /// </summary>
    private void ApplyTileIcon(Image image, int icon, bool covered)
    {
        if (image == null) return;

        if (icon <= 0)
        {
            // 盲盒未点开：灰色底 + "?"（文字单独画）
            image.sprite = null;
            image.color = covered ? new Color(0.30f, 0.30f, 0.35f) : new Color(0.45f, 0.45f, 0.50f);
            return;
        }

        var rows = GetBrickRows();
        if (rows.Count == 0)
        {
            // 表里没有砖块：退回原来的纯色方案（不至于全黑）
            image.sprite = null;
            Color baseColor = IconColors[(icon - 1) % IconColors.Length];
            image.color = covered ? Color.Lerp(baseColor, Color.black, 0.45f) : baseColor;
            return;
        }

        UISpriteConfig row = rows[(icon - 1) % rows.Count];
        var atlases = GameController.Instance != null ? GameController.Instance.SpriteAtlases : null;
        if (atlases == null) return;

        image.sprite = null;
        image.color = covered ? new Color(1f, 1f, 1f, 0.5f) : new Color(1f, 1f, 1f, 0f);   // 先透明，加载到再上色
        string atlas = row.Atlas;
        string sprite = row.Sprite;
        atlases.GetSpriteAsync(atlas, sprite, sp =>
        {
            if (image == null) return;
            image.sprite = sp;
            // 盖住的牌整体压暗一点，保留"不可点/在下面"的观感
            image.color = covered ? Color.Lerp(Color.white, Color.black, 0.45f) : Color.white;
        });
    }

    /// <summary>槽位第 index 格的本地 X 坐标（槽位格子布局）。</summary>
    private float SlotIndexX(int index)
    {
        float total = game.SlotSize * slotCell + (game.SlotSize - 1) * SlotGap;
        return -total * 0.5f + slotCell * 0.5f + index * (slotCell + SlotGap);
    }

    /// <summary>
    /// 确保固定数量的槽位框存在（只创建一次，整局不回收；背景，不做动画）。
    /// 槽位数 = 配表 SlotSize，整场不变。
    /// </summary>
    private void EnsureSlotFrames()
    {
        if (slotPrefab == null || game == null) return;
        if (slotFrames.Count == game.SlotSize)
        {
            // 数量对：仍要同步大小 / 位置（牌格大小会随分辨率变化）
            for (int i = 0; i < slotFrames.Count; i++)
            {
                if (slotFrames[i] == null) continue;
                RectTransform frt = slotFrames[i].transform as RectTransform;
                frt.localScale = Vector3.one;
                frt.sizeDelta = new Vector2(slotCell, slotCell);
                frt.anchoredPosition = new Vector2(SlotIndexX(i), 0);
            }
            return;
        }

        ObjectPoolManager pool = GameController.Instance != null ? GameController.Instance.Pool : null;
        if (pool == null) return;

        // 数量不对时重建（关卡 SlotSize 变化时）
        for (int i = 0; i < slotFrames.Count; i++)
            if (slotFrames[i] != null) pool.Despawn(slotFrames[i]);
        slotFrames.Clear();

        for (int i = 0; i < game.SlotSize; i++)
        {
            GameObject frame = pool.Spawn(slotPrefab, slotRoot.transform);
            if (frame == null) break;
            RectTransform rt = frame.transform as RectTransform;
            rt.localScale = Vector3.one;
            rt.sizeDelta = new Vector2(slotCell, slotCell);
            rt.anchoredPosition = new Vector2(SlotIndexX(i), 0);
            Image image = frame.GetComponent<Image>();
            image.color = new Color(0.15f, 0.17f, 0.22f, 0.85f);   // 框永远是暗底
            image.raycastTarget = false;
            Text label = frame.GetComponentInChildren<Text>(true);   // 框自带 Label 不用，内容由 slotContents 显示
            if (label != null) label.gameObject.SetActive(false);
            slotFrames.Add(frame);
        }
    }

    /// <summary>按数据静态渲染槽位内容（开局/撤回/移出/洗牌/最终同步用）。</summary>
    private void RenderSlotContents()
    {
        if (tilePrefab == null || game == null) return;

        ObjectPoolManager pool = GameController.Instance != null ? GameController.Instance.Pool : null;
        if (pool == null) return;

        while (slotContents.Count < game.Slot.Count)
        {
            GameObject go = pool.Spawn(tilePrefab, slotRoot.transform);
            if (go == null) break;
            slotContents.Add(go);
        }
        while (slotContents.Count > game.Slot.Count)
        {
            GameObject go = slotContents[slotContents.Count - 1];
            slotContents.RemoveAt(slotContents.Count - 1);
            if (go != null) pool.Despawn(go);
        }

        for (int i = 0; i < slotContents.Count; i++)
        {
            GameObject go = slotContents[i];
            if (go == null) continue;
            RectTransform rt = go.transform as RectTransform;
            rt.localScale = Vector3.one;
            rt.sizeDelta = new Vector2(slotCell - 6, slotCell - 6);
            rt.anchoredPosition = new Vector2(SlotIndexX(i), 0);

            SheepTile tile = game.Slot[i];
            int icon = tile.IconId;
            Image image = go.GetComponent<Image>();
            ApplyTileIcon(image, icon, false);
            image.raycastTarget = false;
            Text text = go.GetComponentInChildren<Text>(true);
            text.text = "";   // 槽位牌显示砖块图，不画数字
            text.fontSize = 30;
            text.color = Color.white;

            Button btn = go.GetComponent<Button>();
            btn.onClick.RemoveAllListeners();
            btn.interactable = false;
        }
    }

    /// <summary>
    /// 渲染卡槽上方的「移出暂存区」：从槽里挪出来的牌排成一行。
    /// 这些牌还在牌局里（跟牌面上的牌一样），点一下就飞回卡槽。
    /// </summary>
    private void RenderHoldingViews()
    {
        if (game == null || holdingRoot == null || tilePrefab == null) return;

        ObjectPoolManager pool = GameController.Instance != null ? GameController.Instance.Pool : null;
        if (pool == null) return;

        while (holdingViews.Count < game.Holding.Count)
        {
            GameObject go = pool.Spawn(tilePrefab, holdingRoot);
            if (go == null) break;
            holdingViews.Add(go);
        }
        while (holdingViews.Count > game.Holding.Count)
        {
            GameObject go = holdingViews[holdingViews.Count - 1];
            holdingViews.RemoveAt(holdingViews.Count - 1);
            if (go != null) pool.Despawn(go);
        }

        bool clickable = !game.IsOver && !returningToLobby;
        for (int i = 0; i < holdingViews.Count; i++)
        {
            GameObject go = holdingViews[i];
            if (go == null) continue;
            RectTransform rt = go.transform as RectTransform;
            rt.localScale = Vector3.one;   // 池不重置状态：防御性复位
            rt.sizeDelta = new Vector2(holdingCell - 4f, holdingCell - 4f);
            rt.anchoredPosition = new Vector2(HoldingIndexX(i), 0f);

            SheepTile tile = game.Holding[i];
            int icon = tile.VisibleIcon;
            Image image = go.GetComponent<Image>();
            ApplyTileIcon(image, icon, false);
            image.raycastTarget = clickable;

            Text text = go.GetComponentInChildren<Text>(true);
            text.text = icon <= 0 ? "?" : "";   // 盲盒 = "?"；普通牌显示砖块图，不画数字
            text.fontSize = Mathf.Max(14, (int)(holdingCell * 0.45f));
            text.color = Color.white;

            Button button = go.GetComponent<Button>();
            button.onClick.RemoveAllListeners();
            button.interactable = clickable;
            if (clickable)
            {
                SheepTile captured = tile;
                GameObject view = go;
                button.onClick.AddListener(() => OnTileClicked(captured, view));
            }
        }
    }

    /// <summary>
    /// 归还一个池化视图。优先走缓存的池引用（退出游戏时 GameController.Instance 已被置空，缓存引用才拿得到池）；
    /// 缓存失效时再退回 GameController.Instance；两者都拿不到池、或归还失败（池已拆）就直接销毁，
    /// 避免对象残留在场景里（否则 Unity 退出播放时报「Some objects were not cleaned up」）。
    /// </summary>
    private void ReturnView(GameObject go)
    {
        if (go == null) return;

        ObjectPoolManager pool = viewPool != null ? viewPool
            : (GameController.Instance != null ? GameController.Instance.Pool : null);
        if (pool != null && pool.Despawn(go)) return;

        GameLog.Warn("Sheep", "归还池化视图失败（池不可达/已拆），直接销毁：" + go.name);
        UnityEngine.Object.Destroy(go);
    }

    /// <summary>面板关闭时统一归还槽位框和槽位内容。</summary>
    private void DespawnSlotViews()
    {
        for (int i = 0; i < slotFrames.Count; i++)
            ReturnView(slotFrames[i]);
        slotFrames.Clear();
        for (int i = 0; i < slotContents.Count; i++)
            ReturnView(slotContents[i]);
        slotContents.Clear();
        for (int i = 0; i < holdingViews.Count; i++)
            ReturnView(holdingViews[i]);
        holdingViews.Clear();
    }

    private void ClearViews()
    {
        for (int i = 0; i < tileViews.Count; i++)
        {
            GameObject go = tileViews[i];
            if (go == null) continue;
            Button b = go.GetComponent<Button>();
            if (b != null) b.onClick.RemoveAllListeners();
            ReturnView(go);
        }
        tileViews.Clear();

        // 槽位框/内容是持久视图，这里不销毁（动画要用）；面板关闭时由 DespawnSlotViews 统一归还
    }

    private static void SetText(Text text, string value)
    {
        if (text != null) text.text = value;
    }
}