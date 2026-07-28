using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Helpers;
using MegaCrit.Sts2.Core.Nodes.Combat;
using MegaCrit.sts2.Core.Nodes.TopBar;

namespace TDBank.TDBankCode.UI;

[HarmonyPatch(typeof(NTopBarGold), nameof(NTopBarGold._Ready))]
internal static class TopBarBankPatch
{
    [HarmonyPostfix]
    private static void AfterReady(NTopBarGold __instance)
    {
        BankUiBridge.Attach(__instance);

        if (__instance.GetNodeOrNull<CanvasLayer>("TDBankQuickLayer") is not null)
        {
            return;
        }

        var quickLayer = new CanvasLayer
        {
            Name = "TDBankQuickLayer",
            Layer = 89,
        };
        var button = new Button
        {
            Name = "TDBankQuickButton",
            Text = "TD",
            TooltipText = "TD Bank — Tower Debt",
            MouseFilter = Control.MouseFilterEnum.Stop,
            FocusMode = Control.FocusModeEnum.All,
            ZIndex = 25,
        };
        BankUiTheme.ApplyPrimaryButton(button);
        var logo = BankUiAssets.Logo;
        if (logo is not null)
        {
            button.Text = string.Empty;
            button.Icon = logo;
            button.ExpandIcon = true;
            button.IconAlignment = HorizontalAlignment.Center;
            button.VerticalIconAlignment = VerticalAlignment.Center;
        }
        button.CustomMinimumSize = new Vector2(66, 66);
        button.AnchorLeft = 1;
        button.AnchorRight = 1;
        button.AnchorTop = 0;
        button.AnchorBottom = 0;
        button.OffsetLeft = -96;
        button.OffsetRight = -30;
        button.OffsetTop = 102;
        button.OffsetBottom = 168;
        button.AddThemeFontSizeOverride("font_size", 26);
        button.Pressed += () => BankUiBridge.Open(__instance);
        GuardDuringNativeTargeting(button);
        quickLayer.AddChild(button);
        __instance.AddChild(quickLayer);

#if DEBUG
        if (CommandLineHelper.HasArg("tdbank-ui-smoke"))
        {
            _ = RunUiSmokeCapture(__instance);
        }
#endif
    }

    private static void GuardDuringNativeTargeting(Button button)
    {
        try
        {
            NTargetManager targetManager = NTargetManager.Instance;

            void OnTargetingBegan()
            {
                if (GodotObject.IsInstanceValid(button))
                {
                    button.Disabled = true;
                }
            }

            void OnTargetingEnded()
            {
                if (GodotObject.IsInstanceValid(button))
                {
                    button.Disabled = false;
                }
            }

            button.Disabled = targetManager.IsInSelection;
            targetManager.TargetingBegan += OnTargetingBegan;
            targetManager.TargetingEnded += OnTargetingEnded;
            button.TreeExiting += () =>
            {
                try
                {
                    targetManager.TargetingBegan -= OnTargetingBegan;
                    targetManager.TargetingEnded -= OnTargetingEnded;
                }
                catch
                {

                }
            };
        }
        catch
        {


        }
    }

#if DEBUG
    private static async Task RunUiSmokeCapture(NTopBarGold context)
    {
        SceneTree tree = context.GetTree();
        await context.ToSignal(tree, SceneTree.SignalName.ProcessFrame);
        if (CommandLineHelper.HasArg("tdbank-ui-smoke-en"))
        {
            BankUiBridge.Language = BankUiLanguage.English;
        }
        else if (CommandLineHelper.HasArg("tdbank-ui-smoke-zh"))
        {
            BankUiBridge.Language = BankUiLanguage.SimplifiedChinese;
        }
        BankUiBridge.Open(context);


        for (int frame = 0; frame < 8; frame++)
        {
            await context.ToSignal(tree, SceneTree.SignalName.ProcessFrame);
        }

        string path = ProjectSettings.GlobalizePath("user://tdbank-ui-smoke.png");
        Error result = context.GetViewport().GetTexture().GetImage().SavePng(path);
        MainFile.Logger.Info($"TD Bank UI smoke screenshot: {result} at {path}");

        if (CommandLineHelper.HasArg("tdbank-ui-smoke-quit"))
        {
            tree.Quit();
        }
    }
#endif
}
