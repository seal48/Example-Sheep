using System;
using DG.Tweening;
using GameFramework.Business;
using GameFramework.Config;
using GameFramework.UI;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 奖励发放弹窗：展示奖励的名字 / 图片 / 数量，光芒背景常驻旋转。
/// 支持两种奖励：
///   · 道具奖励（Item 表 + UISprite）：显示「X + 数量」；
///   · 通关羊奖励（SheepPet 表 + SheepIcon 图集）：不显示数量（羊是唯一的，只给一只）。
///
/// 节点字段（title / iconName / iconImage / num / rewardGlow / certainlyBtn）
/// 由 RewardPanel.Bindings.g.cs 生成并绑定到预制体节点。
///
/// 触发方式：
///   · 获得道具（BagChangedEvent.Delta &gt; 0）时自己弹出；
///   · 通关奖励由 GamePanel 调 ShowPet(...) 弹出（可带关闭回调，关掉后回大厅）。
/// </summary>
[UIPanel("RewardPanel", UILayer.Popup)]
public partial class RewardPanel : Panel
{
    /// <summary>光芒旋转一圈的时长（秒）。</summary>
    private const float GlowSpinDuration = 6f;

    private RectTransform glowRect;
    private Tween glowSpin;

    /// <summary>面板关闭时要回调一次（通关奖励用它回大厅）。</summary>
    private Action onClosed;

    protected override void OnInit()
    {
        // 1. 固定标题
        if (title != null) title.text = "奖励发放";

        if (certainlyBtn != null) certainlyBtn.onClick.AddListener(Close);

        // 每次获得道具（背包增量 Delta > 0）就弹奖励
        Listen<BagChangedEvent>(OnBagChanged);
    }

    protected override void OnClose()
    {
        if (certainlyBtn != null) certainlyBtn.onClick.RemoveListener(Close);
    }

    protected override void OnShow(object userData)
    {
        RewardData data = userData as RewardData;
        onClosed = data != null ? data.OnClosed : null;

        if (data != null)
        {
            if (data.IsPet) ApplyPet(data.PetId);
            else ApplyItem(data.ItemId, data.Count);
        }

        StartGlow();
    }

    protected override void OnHide()
    {
        StopGlow();

        // 只回调一次（面板可能被重复 Hide）
        Action callback = onClosed;
        onClosed = null;
        if (callback != null) callback();
    }

    private void OnBagChanged(BagChangedEvent e)
    {
        if (e.Delta <= 0) return;   // 只关心「获得」，消耗不弹
        // 显示**本次获得的数量**（Delta），不是背包里的总量（Count）
        ShowItem(e.ItemId, e.Delta);
    }

    /// <summary>弹道具奖励（已预加载则直接刷新显示）。</summary>
    public static void ShowItem(string itemId, int count)
    {
        var controller = GameController.Instance;
        if (controller == null || controller.UI == null) return;

        controller.UI.Open<RewardPanel>(new RewardData { ItemId = itemId, Count = count });
    }

    /// <summary>弹羊奖励（通关最后一关）。onClosed 在玩家点确定 / 面板关闭时回调一次。</summary>
    public static void ShowPet(int petId, Action onClosed = null)
    {
        var controller = GameController.Instance;
        if (controller == null || controller.UI == null) return;

        controller.UI.Open<RewardPanel>(new RewardData { IsPet = true, PetId = petId, Count = 1, OnClosed = onClosed });
    }

    // ============================== 填充 ==============================

    /// <summary>道具奖励：Item 表取名字 + SpriteId，UISprite 图集取图，数量显示 X + 数量。</summary>
    private void ApplyItem(string itemId, int count)
    {
        int id;
        if (!int.TryParse(itemId, out id)) id = 0;

        string name = "???";
        string spriteId = null;

        var controller = GameController.Instance;
        if (controller != null && controller.Config != null && controller.Config.Database != null
            && controller.Config.Database.Item != null)
        {
            ItemConfig item = controller.Config.Database.Item.Get(id);
            if (item != null)
            {
                name = item.Name;
                spriteId = item.SpriteId;
            }
        }

        if (iconName != null) iconName.text = name;
        if (num != null) num.text = "X" + count;
        LoadIcon(spriteId);
    }

    /// <summary>羊奖励：SheepPet 表取名字 + SpriteId（SheepIcon 图集），不显示数量。</summary>
    private void ApplyPet(int petId)
    {
        string name = "???";
        string spriteId = null;

        var controller = GameController.Instance;
        if (controller != null && controller.Config != null && controller.Config.Database != null
            && controller.Config.Database.SheepPet != null)
        {
            SheepPetConfig pet = controller.Config.Database.SheepPet.Get(petId);
            if (pet != null)
            {
                name = pet.Name;
                spriteId = pet.SpriteId;
            }
        }

        if (iconName != null) iconName.text = name;
        if (num != null) num.text = string.Empty;   // 羊是唯一的，不显示数量
        LoadIcon(spriteId);
    }

    private void LoadIcon(string spriteId)
    {
        var controller = GameController.Instance;
        if (iconImage == null) return;

        iconImage.sprite = null;
        if (string.IsNullOrEmpty(spriteId) || controller == null || controller.SpriteAtlases == null)
            return;

        string capturedSpriteId = spriteId;
        controller.SpriteAtlases.GetSpriteByIdAsync(capturedSpriteId, sprite =>
        {
            if (iconImage != null) iconImage.sprite = sprite;
        });
    }

    // ============================== 光芒旋转 ==============================

    private void StartGlow()
    {
        if (rewardGlow == null) return;

        glowRect = rewardGlow.rectTransform;
        StopGlow();

        glowSpin = glowRect
            .DORotate(new Vector3(0f, 0f, -360f), GlowSpinDuration, RotateMode.FastBeyond360)
            .SetLoops(-1, LoopType.Incremental)   // 一直转
            .SetEase(Ease.Linear)
            .SetUpdate(true);                     // 忽略 timeScale，与 UIAnim 一致
        glowSpin.SetLink(glowRect.gameObject);    // 销毁自动 Kill
    }

    private void StopGlow()
    {
        if (glowSpin != null && glowSpin.IsActive())
            glowSpin.Kill();
        glowSpin = null;
    }

    /// <summary>奖励数据：道具奖励用 ItemId/Count，羊奖励用 IsPet/PetId。</summary>
    private sealed class RewardData
    {
        public bool IsPet;
        public string ItemId;
        public int PetId;
        public int Count;
        public Action OnClosed;
    }
}
