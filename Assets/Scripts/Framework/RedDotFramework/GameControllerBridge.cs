using System.Reflection;
using UnityEngine;

namespace GameFramework.RedDot
{
    /// <summary>
    /// 框架 → Game 层桥（AOT）。框架不能静态引用热更的 GameController，
    /// 通过反射按名字拿 <c>GameController.Instance.RedDot</c>。
    ///
    /// 若业务层改了 GameController 的 RedDot 属性名或入口类名，改这里即可。
    /// </summary>
    public static class GameControllerBridge
    {
        private const string GameControllerTypeName = "GameController";
        private const string RedDotPropertyName = "RedDot";
        private const string InstancePropertyName = "Instance";

        private static PropertyInfo redDotProperty;
        private static bool resolved;

        /// <summary>GameController.Instance.RedDot（拿不到返回 null）。</summary>
        public static RedDotManager Instance
        {
            get
            {
                object controller = GetController();
                if (controller == null) return null;
                Resolve();
                if (redDotProperty == null) return null;
                return redDotProperty.GetValue(controller, null) as RedDotManager;
            }
        }

        private static object GetController()
        {
            System.Type type = FindControllerType();
            if (type == null) return null;
            PropertyInfo prop = type.GetProperty(InstancePropertyName, BindingFlags.Public | BindingFlags.Static);
            if (prop == null) return null;
            return prop.GetValue(null, null);
        }

        private static System.Type FindControllerType()
        {
            Assembly[] asms = System.AppDomain.CurrentDomain.GetAssemblies();
            for (int i = 0; i < asms.Length; i++)
            {
                System.Type t = asms[i].GetType(GameControllerTypeName, false);
                if (t != null) return t;
                // 带命名空间尝试
                t = asms[i].GetType("Game." + GameControllerTypeName, false);
                if (t != null) return t;
            }
            return null;
        }

        private static void Resolve()
        {
            if (resolved) return;
            resolved = true;
            System.Type type = FindControllerType();
            if (type != null)
            {
                redDotProperty = type.GetProperty(RedDotPropertyName, BindingFlags.Public | BindingFlags.Instance);
                if (redDotProperty == null)
                {
                    Debug.LogWarning("[RedDot] GameController 上没有找到 RedDot 属性，RedDotBadge 不会工作。");
                }
            }
        }
    }
}
