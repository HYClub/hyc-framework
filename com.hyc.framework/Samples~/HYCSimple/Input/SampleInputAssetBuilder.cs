using UnityEngine;
using UnityEngine.InputSystem;

namespace HYC.Simple.Input
{
    /// <summary>
    /// 示例 InputActionAsset 构建器（运行时与编辑器生成器共用）。
    /// 两个 map：UI（Submit/NavMove/Scroll）、Sense（Look/Fire）。
    /// 真实项目里这份资产应改为在 Unity 编辑器里用 Input System 可视化编辑并存成 .inputactions，
    /// 再用 InputComponentMapping 指向它来生成组件——这里仅作样例自洽。
    /// </summary>
    public static class SampleInputAssetBuilder
    {
        public static InputActionAsset Build()
        {
            var asset = ScriptableObject.CreateInstance<InputActionAsset>();

            var ui = asset.AddActionMap("UI");
            var submit = ui.AddAction("Submit", InputActionType.Button);
            submit.AddBinding("<Keyboard>/enter");
            submit.AddBinding("<Gamepad>/buttonSouth");
            var nav = ui.AddAction("NavMove", InputActionType.Value);
            nav.AddBinding("<Keyboard>/w");
            nav.AddBinding("<Keyboard>/upArrow");
            nav.AddBinding("<Gamepad>/leftStick");
            var scroll = ui.AddAction("Scroll", InputActionType.Value);
            scroll.AddBinding("<Mouse>/scroll");

            var sense = asset.AddActionMap("Sense");
            var look = sense.AddAction("Look", InputActionType.Value);
            look.AddBinding("<Mouse>/delta");
            look.AddBinding("<Gamepad>/rightStick");
            var fire = sense.AddAction("Fire", InputActionType.Button);
            fire.AddBinding("<Keyboard>/space");
            fire.AddBinding("<Gamepad>/buttonWest");

            return asset;
        }
    }
}
