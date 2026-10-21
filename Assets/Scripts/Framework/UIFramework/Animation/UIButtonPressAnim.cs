using DG.Tweening;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace GameFramework.UI
{
    /// <summary>
    /// 按钮点击动画：按下缩小一点、松开弹回（跟随 UIAnim 那套 DOTween 约定：
    /// SetLink 销毁自动 Kill、SetUpdate(true) 忽略 timeScale、按一下不叠加）。
    ///
    /// 挂到任意按钮上即可，面板创建时会自动给所有 Button 挂上（见 Panel.InternalInitialize）。
    /// 运行时动态创建的按钮也可以自己 AddComponent。
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class UIButtonPressAnim : MonoBehaviour,
        IPointerDownHandler, IPointerUpHandler, IPointerExitHandler
    {
        [Tooltip("按下时缩放到这个比例")]
        public float PressedScale = 0.92f;

        [Tooltip("按下 / 松开动画时长（秒）")]
        public float Duration = 0.08f;

        [Tooltip("是否忽略 Time.timeScale（暂停时界面动画照常播，与 UIAnim 一致）")]
        public bool UseUnscaledTime = true;

        private Vector3 restScale = Vector3.one;
        private Tween tween;

        private void Awake()
        {
            // 记录原始缩放（只记一次；如果按钮初始不是 1 也以它为准）
            restScale = transform.localScale;
        }

        private void OnEnable()
        {
            // 重新显示时把缩放复原（面板隐藏期间可能停在半途）
            transform.localScale = restScale;
        }

        private void OnDisable()
        {
            KillTween();
            transform.localScale = restScale;
        }

        private void OnDestroy()
        {
            KillTween();
        }

        public void OnPointerDown(PointerEventData eventData)
        {
            if (eventData == null) return;

            KillTween();
            tween = transform.DOScale(restScale * PressedScale, Duration)
                .SetUpdate(UseUnscaledTime)
                .SetEase(Ease.OutQuad);
            tween.SetLink(gameObject);
        }

        public void OnPointerUp(PointerEventData eventData)
        {
            PlayRestore();
        }

        public void OnPointerExit(PointerEventData eventData)
        {
            PlayRestore();
        }

        private void PlayRestore()
        {
            KillTween();
            tween = transform.DOScale(restScale, Duration)
                .SetUpdate(UseUnscaledTime)
                .SetEase(Ease.OutQuad);
            tween.SetLink(gameObject);
        }

        private void KillTween()
        {
            if (tween != null && tween.IsActive())
                tween.Kill();
            tween = null;
        }
    }
}
