using GameFramework.Log;
using GameFramework.Procedure;

namespace GameFramework.Startup
{
    /// <summary>
    /// 游戏阶段：进入 Game 场景后常驻。以后在这里驱动羊了个羊玩法逻辑。
    /// </summary>
    public sealed class ProcedureGame : ProcedureBase
    {
        /// <summary>游戏主界面名（[UIPanel] 里注册的名字）。</summary>
        public const string GamePanelName = "GamePanel";

        public override void OnEnter(ProcedureBase from)
        {
            var controller = GameController.Instance;
            if (controller != null && controller.UI != null)
                controller.UI.Open(GamePanelName);

            GameLog.Info(LogTag.Startup, "已进入游戏场景，准备开始玩法…");
        }
    }
}
