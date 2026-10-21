using GameFramework.Log;
using GameFramework.RedDot;

namespace Game
{
    /// <summary>
    /// 红点系统演示（Game 层 / 热更程序集）：展示拉模式 + 推模式的真实用法。
    ///
    /// 真实项目里：这里应该从你的业务数据源（邮件服务、活动服务等）取条件。
    /// 演示里用静态计数模拟「有几封未读 / 有没有可领取」。
    /// </summary>
    public static class RedDotDemo
    {
        // 演示用的模拟数据（真实项目换成业务服务）
        public static int UnreadMailCount = 2;
        public static int ClaimableActivities = 1;
        public static bool HasFriendRequest = true;

        /// <summary>在红点管理器可用后注册所有条件（登录后 / 数据就绪后调用）。</summary>
        public static void RegisterAll()
        {
            var redDot = GameController.Instance.RedDot;
            if (redDot == null)
            {
                GameLog.Warn(LogTag.RedDot, "[RedDotDemo] 红点管理器不可用，跳过注册。");
                return;
            }

            // 拉模式：条件函数每次刷新时被调用
            redDot.RegisterCondition("main.mail.unread", () => UnreadMailCount);
            redDot.RegisterCondition("main.activity.limited", () => ClaimableActivities);
            redDot.RegisterCondition("main.friend.request", () => HasFriendRequest ? 1 : 0);

            // 推模式：事件驱动直接设置（演示：2 秒后模拟「新邮件到了」，再 2 秒后模拟「读完了」）
            GameController.Instance.Timer.Delay(2f, () =>
            {
                UnreadMailCount += 3;
                redDot.NotifyChanged("main.mail");
                GameLog.Info(LogTag.RedDot, "[RedDotDemo] 模拟来了 3 封新邮件，main.mail 应亮且计数=5");
            });
            GameController.Instance.Timer.Delay(4f, () =>
            {
                UnreadMailCount = 0;
                redDot.NotifyChanged("main.mail");
                GameLog.Info(LogTag.RedDot, "[RedDotDemo] 邮件读完，main.mail 应灭（若活动/好友还有红点，main 仍亮）");
            });

            GameLog.Info(LogTag.RedDot, "[RedDotDemo] 已注册红点条件：mail/activity/friend。");
        }
    }
}
