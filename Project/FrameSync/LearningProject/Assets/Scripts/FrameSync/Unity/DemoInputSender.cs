using FrameSync.Protocol;
using UnityEngine;

namespace FrameSync
{
    /// <summary>
    /// 联调用的本地输入。WASD 移动，J 攻击，K 技能，空格跳跃。
    /// </summary>
    public sealed class DemoInputSender : MonoBehaviour
    {
        public FrameSyncSession session;

        private void Awake()
        {
            if (session == null)
                session = GetComponent<FrameSyncSession>();
        }

        private void Update()
        {
            if (session == null || !session.IsPlaying)
                return;

            int moveX = 0;
            int moveY = 0;
            if (Input.GetKey(KeyCode.A)) moveX -= 1;
            if (Input.GetKey(KeyCode.D)) moveX += 1;
            if (Input.GetKey(KeyCode.S)) moveY -= 1;
            if (Input.GetKey(KeyCode.W)) moveY += 1;

            ushort buttons = 0;
            if (Input.GetKey(KeyCode.J)) buttons |= InputButtons.Attack;
            if (Input.GetKey(KeyCode.K)) buttons |= InputButtons.Skill1;
            if (Input.GetKey(KeyCode.Space)) buttons |= InputButtons.Jump;

            session.SendInput((sbyte)moveX, (sbyte)moveY, buttons);
        }
    }
}
