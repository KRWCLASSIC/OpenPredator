using System;
using Aprillz.MewUI;
using Aprillz.MewUI.Controls;
using OpenPredator.Core.Enums;

namespace OpenPredator.Gui;

internal static class LdsView
{
    public static UIElement BuildLdsPage()
    {
        return new StackPanel()
            .Vertical()
            .Spacing(12)
            .Children(
                // 1. LDS Mode Status Banner
                CommonComponents.CreateCard(
                    new Grid()
                        .Columns("*,Auto")
                        .Rows("Auto")
                        .Children(
                            new StackPanel()
                                .Vertical()
                                .Spacing(4)
                                .Children(
                                    new StackPanel()
                                        .Horizontal()
                                        .Spacing(8)
                                        .Children(
                                            new TextBlock().Text("LDS Overrides & Emergency Console").Bold().FontSize(15),
                                            new Border()
                                                .Padding(6, 2, 6, 2)
                                                .CornerRadius(4)
                                                .WithTheme((theme, badge) => badge.Background(theme.Palette.Accent))
                                                .Child(new TextBlock().Text("OVERRIDE ACTIVE").FontSize(9).Bold())
                                        ),
                                    new TextBlock().BindText(AppState.LdsStatusMessage).FontSize(11)
                                ),
                            new Button()
                                .Column(1)
                                .Content("Exit LDS")
                                .CenterVertical()
                                .OnClick(MainNavigation.ToggleLdsMode)
                        )
                ),

                // 2. Hardware Capabilities Overrides
                CommonComponents.CreateCard(
                    new StackPanel()
                        .Vertical()
                        .Spacing(8)
                        .Children(
                            new TextBlock().Text("Hardware Feature Overrides").Bold().FontSize(15),
                            new TextBlock().Text("Manually toggle options if automatic detection missed something (updates apply immediately):").FontSize(11),

                            CreateLdsToggleRow(
                                "Predator Series (Unlocks Turbo Mode)",
                                "Switch between Acer Nitro and Predator profiles to enable the Turbo power mode.",
                                AppState.LdsIsPredator,
                                v => GuiSettingsManager.UpdateCaps(isPredator: v)),

                            CreateLdsToggleRow(
                                "4-Zone RGB Keyboard",
                                "Switch keyboard type to 4-zone RGB to unlock color and effect customization.",
                                AppState.LdsIsRgbKeyboard,
                                v => GuiSettingsManager.UpdateCaps(isRgbKeyboard: v)),

                            CreateLdsToggleRow(
                                "LCD Overdrive (3ms Fast Response)",
                                "Show the fast-response LCD overdrive toggle on the Misc page.",
                                AppState.LdsSupportsLcdOverdrive,
                                v => GuiSettingsManager.UpdateCaps(supportsLcdOverdrive: v)),

                            CreateLdsToggleRow(
                                "Dedicated GPU MUX Switch",
                                "Enable support for direct GPU display output switching.",
                                AppState.LdsSupportsDiscreteGpuSwitch,
                                v => GuiSettingsManager.UpdateCaps(supportsDiscreteGpuSwitch: v)),

                            CreateLdsToggleRow(
                                "CPU Boost & Overclock Support",
                                "Mark system as capable of CPU performance boost & overclocking.",
                                AppState.LdsSupportsCpuOverclock,
                                v => GuiSettingsManager.UpdateCaps(supportsCpuOverclock: v)),

                            CreateLdsToggleRow(
                                "GPU Boost & Overclock Support",
                                "Mark system as capable of GPU frequency boost.",
                                AppState.LdsSupportsGpuOverclock,
                                v => GuiSettingsManager.UpdateCaps(supportsGpuOverclock: v))
                        )
                ),

                // 3. Emergency Force Power Profiles (Direct hardware bypass)
                CommonComponents.CreateCard(
                    new StackPanel()
                        .Vertical()
                        .Spacing(10)
                        .Children(
                            new TextBlock().Text("Emergency Force Power Profile").Bold().FontSize(15),
                            new TextBlock().Text("Directly send a power profile command to hardware, bypassing all detection checks:").FontSize(11),
                            new StackPanel()
                                .Horizontal()
                                .Spacing(8)
                                .Children(
                                    new Button().Content("Force Quiet").OnClick(() => HardwareService.SetPowerMode(PowerMode.Quiet)),
                                    new Button().Content("Force Balanced").OnClick(() => HardwareService.SetPowerMode(PowerMode.Default)),
                                    new Button().Content("Force Performance").OnClick(() => HardwareService.SetPowerMode(PowerMode.Performance)),
                                    new Button().Content("Force Turbo").OnClick(() => HardwareService.SetPowerMode(PowerMode.Turbo))
                                ),
                            new StackPanel()
                                .Horizontal()
                                .Spacing(8)
                                .Margin(0, 4, 0, 0)
                                .Children(
                                    new TextBlock().Text("Current Active Profile:").FontSize(12),
                                    new TextBlock().BindText(AppState.CurrentModeText).Bold().FontSize(12)
                                )
                        )
                ),

                // 4. Persistence & Reset Controls
                CommonComponents.CreateCard(
                    new StackPanel()
                        .Vertical()
                        .Spacing(10)
                        .Children(
                            new TextBlock().Text("LDS Settings Persistence & Recovery").Bold().FontSize(15),
                            CreateLdsToggleRow(
                                "Remember LDS overrides across restarts",
                                "Save your custom hardware overrides so they remain active next time OpenSense starts.",
                                AppState.LdsPersistState,
                                v => GuiSettingsManager.SetLdsPersist(v)),
                            new StackPanel()
                                .Horizontal()
                                .Spacing(8)
                                .Margin(0, 4, 0, 0)
                                .Children(
                                    new Button()
                                        .Content("Reset to Auto-Detected Defaults")
                                        .OnClick(GuiSettingsManager.ResetLdsToDefaults)
                                )
                        )
                )
            );
    }

    public static UIElement CreateLdsToggleRow(string title, string description, ObservableValue<bool> observable, Action<bool> onToggle)
    {
        return new Grid()
            .Columns("*,Auto")
            .Rows("Auto")
            .Margin(0, 4, 0, 4)
            .Children(
                new StackPanel().Vertical().Spacing(2).Children(
                    new TextBlock().Text(title).Bold().FontSize(12),
                    new TextBlock().Text(description).FontSize(10)
                ),
                new ToggleSwitch()
                    .Column(1)
                    .CenterVertical()
                    .BindIsChecked(observable)
                    .OnCheckedChanged(onToggle)
            );
    }
}
