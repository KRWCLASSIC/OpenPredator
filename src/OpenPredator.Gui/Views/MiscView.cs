using Aprillz.MewUI;
using Aprillz.MewUI.Controls;
using OpenPredator.Core.Enums;
using OpenPredator.Core.Models;

namespace OpenPredator.Gui;

internal static class MiscView
{
    public static UIElement BuildMiscPage()
    {
        var panel = new StackPanel()
            .Vertical()
            .Spacing(12);

        // 1. Operating Power Profiles Card (Dynamic Predator vs Nitro filter)
        var modeButtons = new StackPanel()
            .Horizontal()
            .Spacing(8)
            .Children(
                new Button().Content("Quiet").OnClick(() => HardwareService.SetPowerMode(PowerMode.Quiet)),
                new Button().Content("Balanced").OnClick(() => HardwareService.SetPowerMode(PowerMode.Default)),
                new Button().Content("Performance").OnClick(() => HardwareService.SetPowerMode(PowerMode.Performance))
            );

        // Only add Turbo mode if laptop hardware supports it (Predator series)
        if (AppState.Caps.IsPredator)
        {
            modeButtons.Children(
                new Button().Content("Turbo").OnClick(() => HardwareService.SetPowerMode(PowerMode.Turbo))
            );
        }

        panel.Children(
            CommonComponents.CreateCard(
                new StackPanel()
                    .Vertical()
                    .Spacing(8)
                    .Children(
                        new TextBlock().Text("Power & Performance Profile").Bold().FontSize(15),
                        new TextBlock().Text("Select a performance profile to adjust system speed, power limits, and fan acoustics:").FontSize(11),
                        modeButtons,
                        new StackPanel()
                            .Horizontal()
                            .Spacing(8)
                            .Margin(0, 4, 0, 0)
                            .Children(
                                new TextBlock().Text("Active Profile:").FontSize(12),
                                new TextBlock().BindText(AppState.CurrentModeText).Bold().FontSize(12)
                            )
                    )
            )
        );

        // 2. Keyboard Backlight Intensity Card (Locked strictly to 5 states: 0, 25, 50, 75, 100)
        panel.Children(
            CommonComponents.CreateCard(
                new StackPanel()
                    .Vertical()
                    .Spacing(10)
                    .Children(
                        new TextBlock().Text("Keyboard Backlight Intensity").Bold().FontSize(15),
                        new TextBlock().Text("Choose keyboard backlight brightness:").FontSize(11),
                        new StackPanel()
                            .Horizontal()
                            .Spacing(8)
                            .Children(
                                new Button().Content("0% (Off)").OnClick(() => HardwareService.SetBrightness(0)),
                                new Button().Content("25%").OnClick(() => HardwareService.SetBrightness(25)),
                                new Button().Content("50%").OnClick(() => HardwareService.SetBrightness(50)),
                                new Button().Content("75%").OnClick(() => HardwareService.SetBrightness(75)),
                                new Button().Content("100% (Max)").OnClick(() => HardwareService.SetBrightness(100))
                            ),
                        new StackPanel()
                            .Horizontal()
                            .Spacing(8)
                            .Children(
                                new TextBlock().Text("Active Brightness:").FontSize(12),
                                new TextBlock().BindText(AppState.BrightnessText).Bold().FontSize(12)
                            ),
                        // 30-Second Backlight Timeout
                        new Grid()
                            .Columns("*,Auto")
                            .Rows("Auto")
                            .Margin(0, 6, 0, 0)
                            .Children(
                                new StackPanel()
                                    .Vertical()
                                    .Spacing(2)
                                    .Children(
                                        new TextBlock().Text("30-Second Backlight Idle Timeout").Bold().FontSize(12),
                                        new TextBlock().Text("Automatically turn off keyboard lighting after 30 seconds of inactivity.").FontSize(10)
                                    ),
                                new ToggleSwitch()
                                    .Column(1)
                                    .CenterVertical()
                                    .BindIsChecked(AppState.KbTimeoutState)
                                    .OnCheckedChanged(enabled => HardwareService.SetTimeout(enabled))
                            )
                    )
            )
        );

        // 3. 4-Zone RGB Keyboard Lighting (Hardware Probing Filter)
        if (AppState.Caps.IsRgbKeyboard)
        {
            panel.Children(
                CommonComponents.CreateCard(
                    new StackPanel()
                        .Vertical()
                        .Spacing(10)
                        .Children(
                            new TextBlock().Text("4-Zone RGB Lighting").Bold().FontSize(15),
                            new TextBlock().Text("Customize keyboard colors, zone lighting, and dynamic lighting effects:").FontSize(11),
                            new StackPanel()
                                .Horizontal()
                                .Spacing(8)
                                .Children(
                                    new Button().Content("Predator Cyan").OnClick(() => HardwareService.SetRgbPreset(new(0, 230, 255))),
                                    new Button().Content("Crimson Red").OnClick(() => HardwareService.SetRgbPreset(new(255, 20, 20))),
                                    new Button().Content("Neon Purple").OnClick(() => HardwareService.SetRgbPreset(new(180, 0, 255))),
                                    new Button().Content("Emerald Green").OnClick(() => HardwareService.SetRgbPreset(new(0, 255, 60))),
                                    new Button().Content("Ice White").OnClick(() => HardwareService.SetRgbPreset(new(255, 255, 255)))
                                ),
                            new StackPanel()
                                .Horizontal()
                                .Spacing(8)
                                .Margin(0, 4, 0, 0)
                                .Children(
                                    new TextBlock().Text("Animation:").CenterVertical().FontSize(12),
                                    new Button().Content("Static").OnClick(() => HardwareService.SetRgbEffect(RgbEffectType.Static)),
                                    new Button().Content("Breathing").OnClick(() => HardwareService.SetRgbEffect(RgbEffectType.Breathing)),
                                    new Button().Content("Wave").OnClick(() => HardwareService.SetRgbEffect(RgbEffectType.Wave)),
                                    new Button().Content("Zoom").OnClick(() => HardwareService.SetRgbEffect(RgbEffectType.Zoom))
                                )
                        )
                )
            );
        }

        // 4. Hardware Enhancements (LCD Overdrive & Windows Key Lock)
        var hardwareEnhancements = new StackPanel()
            .Vertical()
            .Spacing(8)
            .Children(
                new TextBlock().Text("Hardware Enhancements").Bold().FontSize(15),
                // Windows Key Lock
                new Grid()
                    .Columns("*,Auto")
                    .Rows("Auto")
                    .Children(
                        new StackPanel()
                            .Vertical()
                            .Spacing(2)
                            .Children(
                                new TextBlock().Text("Lock Windows Key (Gaming Mode)").Bold().FontSize(12),
                                new TextBlock().Text("Disable the Windows key to prevent accidental presses while gaming.").FontSize(10)
                            ),
                        new ToggleSwitch()
                            .Column(1)
                            .CenterVertical()
                            .BindIsChecked(AppState.WinKeyLockState)
                            .OnCheckedChanged(locked => HardwareService.SetWinKeyLock(locked))
                    )
            );

        if (AppState.Caps.SupportsLcdOverdrive)
        {
            hardwareEnhancements.Children(
                new Grid()
                    .Columns("*,Auto")
                    .Rows("Auto")
                    .Margin(0, 4, 0, 0)
                    .Children(
                        new StackPanel()
                            .Vertical()
                            .Spacing(2)
                            .Children(
                                new TextBlock().Text("LCD Overdrive (3ms Fast Response)").Bold().FontSize(12),
                                new TextBlock().Text("Faster screen response time (3ms) to reduce motion blur in games.").FontSize(10)
                            ),
                        new ToggleSwitch()
                            .Column(1)
                            .CenterVertical()
                            .BindIsChecked(AppState.LcdOverdriveState)
                            .OnCheckedChanged(enabled => HardwareService.SetLcdOverdrive(enabled))
                    )
            );
        }

        panel.Children(CommonComponents.CreateCard(hardwareEnhancements));

        return panel;
    }
}
