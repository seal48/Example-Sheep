using GameFramework.Log;
using GameFramework.Procedure;
using GameFramework.Scenes;

namespace GameFramework.Startup
{
    /// <summary>
    /// 进游戏：关掉大厅主界面，用场景管理异步切到 Game 场景（带进度条），加载完进入常驻的游戏流程。
    /// </summary>
    public sealed class ProcedureEnterGame : ProcedureBase
    {
        /// <summary>游戏场景（短名，真实地址 = SceneInitOptions.SceneFolder + 它 + .unity）。</summary>
        public const string GameSceneName = "Game";

        public override void OnEnter(ProcedureBase from)
        {
            var controller = GameController.Instance;
            if (controller == null || controller.Scene == null)
            {
                GameLog.Error(LogTag.Startup, "场景管理不可用，没法切到游戏场景。");
                return;
            }

            if (controller.UI != null && controller.UI.IsOpen(ProcedureLobby.MainPanelName))
                controller.UI.Close(ProcedureLobby.MainPanelName);

            GameLog.Info(LogTag.Startup, "开始加载游戏场景…");

            SceneLoadOptions options = new SceneLoadOptions();
            options.Tip = "正在进入游戏…";

            controller.Scene.LoadAsync(GameSceneName, options, OnGameLoaded);
        }

        private void OnGameLoaded(bool success, string error)
        {
            if (!success)
            {
                GameLog.Error(LogTag.Startup, "游戏场景加载失败：" + error);
                return;
            }

            GameLog.Info(LogTag.Startup, "游戏场景就绪。");
            ChangeState<ProcedureGame>();
        }
    }
}
