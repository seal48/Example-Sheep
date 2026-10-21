using GameFramework.Config;
using UnityEngine;

namespace GameFramework.Gameplay
{
    /// <summary>
    /// 从 SheepLevel 配置表读关卡参数，转成 <see cref="SheepLevelData"/>。
    /// 配置表由 Assets/Config/Excel/SheepLevel.xlsx 导出（Tools/配置表/导出全部表），
    /// 生成代码在 Framework/Config/Generated/SheepLevel.g.cs。
    /// </summary>
    public static class SheepLevelLoader
    {
        /// <summary>读不到配置 / 表 / 行时返回 null。</summary>
        public static SheepLevelData Get(int levelId)
        {
            GameController controller = GameController.Instance;
            if (controller == null || controller.Config == null || controller.Config.Database == null)
            {
                Debug.LogWarning("[Sheep] 配置表还没就绪，读不到关卡 " + levelId);
                return null;
            }

            TbSheepLevel table = controller.Config.Database.SheepLevel;
            SheepLevelConfig row = table != null ? table.Get(levelId) : null;
            if (row == null)
            {
                Debug.LogWarning("[Sheep] 关卡 " + levelId + " 不在 SheepLevel 表里");
                return null;
            }

            return new SheepLevelData
            {
                Id = row.Id,
                GridWidth = row.GridWidth,
                GridHeight = row.GridHeight,
                LayerCount = row.LayerCount,
                Style = row.Style,
                IconKinds = row.IconKinds,
                BlindCount = row.BlindCount,
                SlotSize = row.SlotSize,
                UndoLimit = row.UndoLimit,
                RemoveLimit = row.RemoveLimit,
                ShuffleLimit = row.ShuffleLimit,
                Shape = row.Shape,
                MinLayers = row.MinLayers,
                EdgeNoise = row.EdgeNoise,
                OffsetPercent = row.OffsetPercent,
                HorizPercent = row.HorizPercent,
                TallPercent = row.TallPercent,
                Layout = row.Layout,
                LayoutDir = row.LayoutDir,
                LayoutHalf = row.LayoutHalf,
                LayoutHalfDir = row.LayoutHalfDir,
            };
        }
    }
}
