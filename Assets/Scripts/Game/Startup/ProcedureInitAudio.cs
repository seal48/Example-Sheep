using GameFramework.Audio;
using GameFramework.Log;
using GameFramework.Procedure;
using GameFramework.UI;
using UnityEngine;
using UnityEngine.UI;

namespace GameFramework.Startup
{
    /// <summary>音频初始化：资源系统就绪后建 [Audio] 根节点、铺好 BGM / 音效通道。放在 UI 之后，界面一打开就能播声音。</summary>
    public sealed class ProcedureInitAudio : ProcedureBase
    {
        /// <summary>总音量（0~1）。1 = 不衰减。</summary>
        private const float MasterVolume = 0.5f;

        /// <summary>背景音乐音量（0~1）。</summary>
        private const float BgmVolume = 0.1f;

        /// <summary>音效音量（0~1）。</summary>
        private const float SfxVolume = 0.5f;

        public override void OnEnter(ProcedureBase from)
        {
            var controller = GameController.Instance;
            if (controller == null)
            {
                GameLog.Error(LogTag.Startup, "找不到 GameController，无法初始化音频。");
                return;
            }

            GameLog.Info(LogTag.Startup, "开始初始化音频…");
            controller.InitAudio(OnAudioReady);
        }

        private void OnAudioReady(bool success, string message)
        {
            if (!success)
            {
                GameLog.Error(LogTag.Startup, "音频初始化失败：" + message);
                return;
            }

            GameLog.Info(LogTag.Startup, "音频就绪：" + message);

            // 统一音量（必须放在 InitAudio 之后：初始化时存档里的旧音量会覆盖配置默认值）。
            // 改音量会自动写回存档，下次启动也保持。
            AudioKit.MasterVolume = MasterVolume;
            AudioKit.BgmVolume = BgmVolume;
            AudioKit.SfxVolume = SfxVolume;

            // 全局按钮点击音效：框架的 Panel 会把任意按钮点击转发到 ButtonClicked
            Panel.ButtonClicked = OnAnyButtonClicked;

            ChangeState<ProcedureInitPlatform>();
        }

        /// <summary>任意按钮被点击 → 播点击音效。</summary>
        private static void OnAnyButtonClicked(Button button)
        {
            AudioKit.PlaySfx("ButtonClick");
        }
    }
}