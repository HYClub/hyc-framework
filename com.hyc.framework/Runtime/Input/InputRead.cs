using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using Unity.Mathematics;
using HYC.Framework.Input;

namespace HYC.Framework.Input
{
    /// <summary>
    /// 把 Input System 的 <see cref="InputAction"/> 当前值读成框架富状态 struct，并算好边沿
    /// （justPressed/justReleased）。由生成的写入器（XxxInputComponentWriter）调用，宿主不直接用。
    /// <paramref name="prev"/> 是「每玩家一份」的上一帧代理值表，key 由写入器拼成 "mapName:actionName"，
    /// 代理值取标量原值（按钮/轴）或模长（向量），用于跨帧比较得出边沿。
    /// </summary>
    public static class InputRead
    {
        public const float ButtonThreshold = 0.5f;
        public const float Eps = 1e-5f;

        public static InputActionPhaseKind FromPhase(InputActionPhase p)
        {
            switch (p)
            {
                case InputActionPhase.Started: return InputActionPhaseKind.Started;
                case InputActionPhase.Performed: return InputActionPhaseKind.Performed;
                case InputActionPhase.Canceled: return InputActionPhaseKind.Canceled;
                case InputActionPhase.Waiting: return InputActionPhaseKind.Waiting;
                default: return InputActionPhaseKind.Disabled;
            }
        }

        public static InputButton ReadButton(InputAction a, Dictionary<string, float> prev, string key)
        {
            if (a == null) return default;
            float v = a.ReadValue<float>();
            float last = prev.TryGetValue(key, out var l) ? l : 0f;
            bool jp = last < ButtonThreshold && v >= ButtonThreshold;
            bool jr = last >= ButtonThreshold && v < ButtonThreshold;
            prev[key] = v;
            return new InputButton { value = v, phase = (byte)FromPhase(a.phase), justPressed = jp, justReleased = jr };
        }

        public static InputAxis ReadAxis(InputAction a, Dictionary<string, float> prev, string key)
        {
            if (a == null) return default;
            float v = a.ReadValue<float>();
            float last = prev.TryGetValue(key, out var l) ? l : 0f;
            bool jp = math.abs(last) < Eps && math.abs(v) >= Eps;
            bool jr = math.abs(last) >= Eps && math.abs(v) < Eps;
            prev[key] = v;
            return new InputAxis { value = v, phase = (byte)FromPhase(a.phase), justPressed = jp, justReleased = jr };
        }

        public static InputVector2 ReadVector2(InputAction a, Dictionary<string, float> prev, string key)
        {
            if (a == null) return default;
            Vector2 v = a.ReadValue<Vector2>();
            float2 f = new float2(v.x, v.y);
            float mag = math.length(f);
            float last = prev.TryGetValue(key, out var l) ? l : 0f;
            bool jp = last < Eps && mag >= Eps;
            bool jr = last >= Eps && mag < Eps;
            prev[key] = mag;
            return new InputVector2 { value = f, phase = (byte)FromPhase(a.phase), justPressed = jp, justReleased = jr };
        }

        public static InputVector3 ReadVector3(InputAction a, Dictionary<string, float> prev, string key)
        {
            if (a == null) return default;
            Vector3 v = a.ReadValue<Vector3>();
            float3 f = new float3(v.x, v.y, v.z);
            float mag = math.length(f);
            float last = prev.TryGetValue(key, out var l) ? l : 0f;
            bool jp = last < Eps && mag >= Eps;
            bool jr = last >= Eps && mag < Eps;
            prev[key] = mag;
            return new InputVector3 { value = f, phase = (byte)FromPhase(a.phase), justPressed = jp, justReleased = jr };
        }

        public static InputQuaternion ReadQuaternion(InputAction a, Dictionary<string, float> prev, string key)
        {
            if (a == null) return default;
            Quaternion v = a.ReadValue<Quaternion>();
            return new InputQuaternion { value = new quaternion(v.x, v.y, v.z, v.w), phase = (byte)FromPhase(a.phase) };
        }

        public static InputInteger ReadInteger(InputAction a, Dictionary<string, float> prev, string key)
        {
            if (a == null) return default;
            int v = a.ReadValue<int>();
            float fv = v;
            bool jp = false;
            if (prev.TryGetValue(key, out var l))
            {
                if (l != fv) jp = true;
                prev[key] = fv;
            }
            else
            {
                prev[key] = fv; // 首帧不触发边沿
            }
            return new InputInteger { value = v, phase = (byte)FromPhase(a.phase), justPressed = jp, justReleased = jp };
        }
    }
}
