using Game;
using GameFramework.Log;
using GameFramework.Procedure;
using UnityEngine;

namespace GameFramework.Startup
{
    /// <summary>UI 初始化：等资源系统就绪后，加载 UIRoot 预制体并注册所有面板。</summary>
    public sealed class ProcedureInitUI : ProcedureBase
    {
        public override void OnEnter(ProcedureBase from)
        {
            var controller = GameController.Instance;
            if (controller == null)
            {
                GameLog.Error(LogTag.Startup, "找不到 GameController，无法初始化 UI。");
                return;
            }

            GameLog.Info(LogTag.Startup, "开始初始化 UI…");
            controller.InitUI(OnUIReady);
        }

        private void OnUIReady(bool success, string message)
        {
            if (!success)
            {
                GameLog.Error(LogTag.Startup, "UI 初始化失败：" + message);
                return;
            }

            GameLog.Info(LogTag.Startup, "UI 就绪：" + message);

            // 红点演示：注册业务红点条件（真实项目在登录/数据就绪后注册）
            RedDotDemo.RegisterAll();

            ChangeState<ProcedureInitScene>();
        }
    }
}
