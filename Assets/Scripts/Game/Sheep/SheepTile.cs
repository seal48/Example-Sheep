namespace GameFramework.Gameplay
{
    /// <summary>牌的状态。</summary>
    public enum SheepTileState
    {
        /// <summary>在牌面上。</summary>
        OnBoard,

        /// <summary>在卡槽里。</summary>
        InSlot,

        /// <summary>
        /// 被「移出」道具暂存在卡槽上方的暂存区（没离开牌局）。
        /// 还能再点一下飞回卡槽，等价于"从槽位移回了游戏内"。
        /// </summary>
        Parked,

        /// <summary>已被消除（离开牌局）。</summary>
        Removed,
    }

    /// <summary>
    /// 一张牌。位置在生成时定死，图标可以在洗牌时重排。
    ///
    /// 位置 = (X + SubX, Y + SubY) + 层偏移：
    ///   · X/Y      网格格号；
    ///   · SubX/SubY 亚格偏移（格为单位，0 或 0.5）—— 0.5 就是"落在四格中心"，一张能盖住四张；
    ///   · StackX/StackY 这一叠的视觉偏移方向（横向 / 纵向扇开）。
    /// </summary>
    public sealed class SheepTile
    {
        /// <summary>实例唯一 Id。</summary>
        public readonly int Id;

        /// <summary>图标类型（1 开始）。洗牌会重排这个值。</summary>
        public int IconId;

        /// <summary>所在层（0 = 最底层）。</summary>
        public readonly int Layer;

        /// <summary>网格坐标。</summary>
        public readonly int X;

        public readonly int Y;

        /// <summary>亚格偏移 X（格为单位，0 / 0.5）。</summary>
        public readonly float SubX;

        /// <summary>亚格偏移 Y（格为单位，0 / 0.5）。</summary>
        public readonly float SubY;

        /// <summary>这一叠的视觉偏移方向 X（-1/0/1）。</summary>
        public readonly int StackX;

        /// <summary>这一叠的视觉偏移方向 Y（-1/0/1）。</summary>
        public readonly int StackY;

        /// <summary>是否是盲盒牌（点击前看不到图标）。</summary>
        public bool IsBlind;

        /// <summary>盲盒被点开过（图标已展示）。</summary>
        public bool Revealed;

        /// <summary>当前状态。</summary>
        public SheepTileState State;

        public SheepTile(int id, int iconId, int layer, int x, int y)
            : this(id, iconId, layer, x, y, 0, 0, 0f, 0f)
        {
        }

        public SheepTile(int id, int iconId, int layer, int x, int y, int stackX, int stackY)
            : this(id, iconId, layer, x, y, stackX, stackY, 0f, 0f)
        {
        }

        public SheepTile(int id, int iconId, int layer, int x, int y, int stackX, int stackY, float subX, float subY)
        {
            Id = id;
            IconId = iconId;
            Layer = layer;
            X = x;
            Y = y;
            SubX = subX;
            SubY = subY;
            StackX = stackX;
            StackY = stackY;
            State = SheepTileState.OnBoard;
        }

        /// <summary>当前对外可见的图标（盲盒没点开时是 0 = 未知）。</summary>
        public int VisibleIcon
        {
            get { return (IsBlind && !Revealed) ? 0 : IconId; }
        }
    }
}
