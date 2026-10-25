using System;
using Aprillz.MewUI;
using Aprillz.MewUI.Controls;
using OpenPredator.Core.Enums;

namespace OpenPredator.Gui;

internal static class FansView
{
    public static UIElement BuildFansPage()
    {
        return new StackPanel()
            .Vertical()
            .Spacing(12)
            .Children(
                // 1. Dual Fan Cards with Lock Toggle in Center
                new Grid()
                    .Columns("*,Auto,*")
                    .Rows("Auto")
                    .Children(
                        // CPU Fan Card (Left Tabs)
                        BuildCpuFanCard(),

                        // Center Lock Column
                        new StackPanel()
                            .Column(1)
                            .Vertical()
                            .CenterVertical()
                            .Margin(8, 0, 8, 0)
                            .Spacing(4)
                            .Children(
                                new Button()
                                    .Content(new TextBlock().BindText(AppState.FansLocked, locked => locked ? "LINK" : "UNLINK").FontSize(10).Bold().Center())
                                    .Width(56)
                                    .Height(32)
                                    .OnClick(() => ToggleFanLock()),
                                new TextBlock()
                                    .BindText(AppState.FansLocked, locked => locked ? "Locked" : "Separate")
                                    .FontSize(10)
                                    .Bold()
                                    .CenterHorizontal()
                            ),

                        // GPU Fan Card (Right Tabs)
                        new Border().Column(2).Child(BuildGpuFanCard())
                    ),

                // 2. Acer CoolBoost™ Card
                BuildCoolBoostCard(),

                // 3. Hardware Monitor (Visual Gauge Tiles)
                CommonComponents.CreateCard(
                    new StackPanel()
                        .Vertical()
                        .Spacing(10)
                        .Children(
                            new TextBlock().Text("Hardware Monitor").Bold().FontSize(15),
                            new Grid()
                                .Columns("*,*,*,*")
                                .Spacing(10)
                                .Children(
                                    // CPU Temperature
                                    CreateTelemetryTile("CPU Temp", AppState.CpuTempText, AppState.CpuTempStatus, AppState.CpuTempProgress, "30°C", "100°C"),

                                    // CPU Load
                                    new Border().Column(1).Child(
                                        CreateTelemetryTile("CPU Load", AppState.CpuLoadText, AppState.CpuLoadStatus, AppState.CpuLoadProgress, "0%", "100%")
                                    ),

                                    // GPU Temperature
                                    new Border().Column(2).Child(
                                        CreateTelemetryTile("GPU Temp", AppState.GpuTempText, AppState.GpuTempStatus, AppState.GpuTempProgress, "30°C", "100°C")
                                    ),

                                    // GPU Utilization
                                    new Border().Column(3).Child(
                                        CreateTelemetryTile("GPU Load", AppState.GpuLoadText, AppState.GpuLoadStatus, AppState.GpuLoadProgress, "0%", "100%")
                                    )
                                )
                        )
                )
            );
    }

    public static UIElement CreateTelemetryTile(
        string title,
        ObservableValue<string> valueText,
        ObservableValue<string> statusText,
        ObservableValue<double> progressVal,
        string minLabel,
        string maxLabel)
    {
        return new Border()
            .Padding(12)
            .WithTheme((theme, border) =>
            {
                border.Background(theme.Palette.ControlBackground);
                border.BorderBrush(theme.Palette.ControlBorder);
                border.BorderThickness(theme.Metrics.ControlBorderThickness);
                border.CornerRadius(8);
            })
            .Child(
                new StackPanel()
                    .Vertical()
                    .Spacing(6)
                    .Children(
                        // Header: Title + Status Pill
                        new Grid()
                            .Columns("*,Auto")
                            .Rows("Auto")
                            .Children(
                                new TextBlock().Text(title).Bold().FontSize(12).CenterVertical(),
                                new Border()
                                    .Column(1)
                                    .Padding(6, 2, 6, 2)
                                    .CornerRadius(6)
                                    .WithTheme((theme, badge) =>
                                    {
                                        badge.Background(theme.Palette.ControlBorder);
                                    })
                                    .Child(
                                        new TextBlock()
                                            .BindText(statusText)
                                            .FontSize(10)
                                            .Bold()
                                    )
                            ),

                        // Prominent Metric
                        new TextBlock()
                            .BindText(valueText)
                            .FontSize(22)
                            .Bold()
                            .Margin(0, 2, 0, 2),

                        // Visual Gauge Meter
                        new ProgressBar()
                            .Height(6)
                            .Minimum(0)
                            .Maximum(100)
                            .Bind(RangeBase.ValueProperty, progressVal),

                        // Min/Max Footers
                        new Grid()
                            .Columns("*,Auto")
                            .Rows("Auto")
                            .Children(
                                new TextBlock().Text(minLabel).FontSize(9).WithTheme((t, tb) => tb.Foreground(t.Palette.DisabledText)),
                                new TextBlock().Column(1).Text(maxLabel).FontSize(9).WithTheme((t, tb) => tb.Foreground(t.Palette.DisabledText))
                            )
                    )
            );
    }

    public static void ToggleFanLock()
    {
        AppState.FansLocked.Value = !AppState.FansLocked.Value;
        if (AppState.FansLocked.Value)
        {
            // When locking, sync GPU mode and duty to match CPU
            AppState.CurrentGpuFanMode = AppState.CurrentCpuFanMode;
            int syncDuty = AppState.CpuDutyValue.Value;
            AppState.GpuDutyValue.Value = syncDuty;
            AppState.GpuDutyText.Value = $"{syncDuty}%";

            if (AppState.GpuTabControl != null && AppState.CpuTabControl != null)
            {
                AppState.GpuTabControl.SelectedIndex = AppState.CpuTabControl.SelectedIndex;
            }

            if (AppState.GpuSlider != null && (int)Math.Round(AppState.GpuSlider.Value) != syncDuty)
            {
                AppState.IsSyncingDuty = true;
                try
                {
                    AppState.GpuSlider.Value = syncDuty;
                }
                finally
                {
                    AppState.IsSyncingDuty = false;
                }
            }

            HardwareService.ApplyFanSpeeds();
        }
        GuiSettingsManager.SaveGuiSettings();
    }

    public static UIElement BuildCpuFanCard()
    {
        AppState.CpuTabControl = new TabControl()
            .TabPlacement(TabPlacement.Left)
            .Tab(CreateTabHeader("Auto"), BuildCpuFanTabContent(AppState.CpuFanRpmText, null, "Automatic Mode"))
            .Tab(CreateTabHeader("Max"), BuildCpuFanTabContent(AppState.CpuFanRpmText, null, "Maximum Speed"))
            .Tab(CreateTabHeader("Custom"), BuildCpuFanTabContent(AppState.CpuFanRpmText, CreateCpuDutySlider(), null));

        AppState.CpuTabControl.SelectedIndex = AppState.CurrentCpuFanMode switch
        {
            FanMode.Auto => 0,
            FanMode.Max => 1,
            FanMode.Custom => 2,
            _ => 0
        };

        AppState.CpuTabControl.OnSelectionChanged(sel =>
        {
            int idx = AppState.CpuTabControl.SelectedIndex;
            AppState.CurrentCpuFanMode = idx switch
            {
                0 => FanMode.Auto,
                1 => FanMode.Max,
                2 => FanMode.Custom,
                _ => FanMode.Auto
            };

            if (AppState.FansLocked.Value && AppState.GpuTabControl != null && AppState.GpuTabControl.SelectedIndex != idx)
            {
                AppState.CurrentGpuFanMode = AppState.CurrentCpuFanMode;
                AppState.GpuTabControl.SelectedIndex = idx;
            }

            HardwareService.ApplyFanSpeeds();
        });

        return CommonComponents.CreateCard(
            new StackPanel()
                .Vertical()
                .Spacing(8)
                .Children(
                    new TextBlock().Text("CPU Fan Control").Bold().FontSize(15),
                    AppState.CpuTabControl
                )
        );
    }

    public static UIElement BuildGpuFanCard()
    {
        // Right side tabs as requested for symmetrical NitroSense dual-fan layout
        AppState.GpuTabControl = new TabControl()
            .TabPlacement(TabPlacement.Right)
            .Tab(CreateTabHeader("Auto"), BuildGpuFanTabContent(AppState.GpuFanRpmText, null, "Automatic Mode"))
            .Tab(CreateTabHeader("Max"), BuildGpuFanTabContent(AppState.GpuFanRpmText, null, "Maximum Speed"))
            .Tab(CreateTabHeader("Custom"), BuildGpuFanTabContent(AppState.GpuFanRpmText, CreateGpuDutySlider(), null));

        AppState.GpuTabControl.SelectedIndex = AppState.CurrentGpuFanMode switch
        {
            FanMode.Auto => 0,
            FanMode.Max => 1,
            FanMode.Custom => 2,
            _ => 0
        };

        AppState.GpuTabControl.OnSelectionChanged(sel =>
        {
            int idx = AppState.GpuTabControl.SelectedIndex;
            AppState.CurrentGpuFanMode = idx switch
            {
                0 => FanMode.Auto,
                1 => FanMode.Max,
                2 => FanMode.Custom,
                _ => FanMode.Auto
            };

            if (AppState.FansLocked.Value && AppState.CpuTabControl != null && AppState.CpuTabControl.SelectedIndex != idx)
            {
                AppState.CurrentCpuFanMode = AppState.CurrentGpuFanMode;
                AppState.CpuTabControl.SelectedIndex = idx;
            }

            HardwareService.ApplyFanSpeeds();
        });

        return CommonComponents.CreateCard(
            new StackPanel()
                .Vertical()
                .Spacing(8)
                .Children(
                    new TextBlock().Text("GPU Fan Control").Bold().FontSize(15),
                    AppState.GpuTabControl
                )
        );
    }

    public static Element CreateTabHeader(string title)
    {
        return new TextBlock()
            .Text(title)
            .Bold()
            .FontSize(12)
            .Width(62)
            .Center();
    }

    public static UIElement BuildCpuFanTabContent(ObservableValue<string> rpm, UIElement? slider, string? modeHint)
    {
        var bottomSlot = new Border()
            .Width(190)
            .Height(52)
            .CenterHorizontal();

        if (slider != null)
        {
            bottomSlot.Child(slider);
        }
        else if (modeHint != null)
        {
            bottomSlot.Child(
                new TextBlock()
                    .Text(modeHint)
                    .FontSize(11)
                    .WithTheme((t, tb) => tb.Foreground(t.Palette.DisabledText))
                    .Center()
            );
        }

        return new StackPanel()
            .Vertical()
            .Spacing(8)
            .Margin(8, 4, 8, 4)
            .Width(190)
            .CenterHorizontal()
            .Children(
                CreateFanDial(rpm),
                // Inline Thermal Pill (CPU Temp only)
                new Border()
                    .Padding(8, 3, 8, 3)
                    .CornerRadius(10)
                    .CenterHorizontal()
                    .WithTheme((theme, border) =>
                    {
                        border.Background(theme.Palette.ControlBackground);
                        border.BorderBrush(theme.Palette.ControlBorder);
                        border.BorderThickness(1);
                    })
                    .Child(
                        new StackPanel()
                            .Horizontal()
                            .Spacing(5)
                            .Center()
                            .Children(
                                new TextBlock().Text("CPU:").FontSize(11).Bold().CenterVertical(),
                                new TextBlock().BindText(AppState.CpuTempText).FontSize(11).Bold().CenterVertical(),
                                new TextBlock().BindText(AppState.CpuTempStatus, s => $"({s})").FontSize(10).WithTheme((t, tb) => tb.Foreground(t.Palette.DisabledText)).CenterVertical()
                            )
                    ),
                bottomSlot
            );
    }

    public static UIElement BuildGpuFanTabContent(ObservableValue<string> rpm, UIElement? slider, string? modeHint)
    {
        var bottomSlot = new Border()
            .Width(190)
            .Height(52)
            .CenterHorizontal();

        if (slider != null)
        {
            bottomSlot.Child(slider);
        }
        else if (modeHint != null)
        {
            bottomSlot.Child(
                new TextBlock()
                    .Text(modeHint)
                    .FontSize(11)
                    .WithTheme((t, tb) => tb.Foreground(t.Palette.DisabledText))
                    .Center()
            );
        }

        return new StackPanel()
            .Vertical()
            .Spacing(8)
            .Margin(8, 4, 8, 4)
            .Width(190)
            .CenterHorizontal()
            .Children(
                CreateFanDial(rpm),
                // Inline Thermal Pill (GPU Temp only, symmetrical with CPU)
                new Border()
                    .Padding(8, 3, 8, 3)
                    .CornerRadius(10)
                    .CenterHorizontal()
                    .WithTheme((theme, border) =>
                    {
                        border.Background(theme.Palette.ControlBackground);
                        border.BorderBrush(theme.Palette.ControlBorder);
                        border.BorderThickness(1);
                    })
                    .Child(
                        new StackPanel()
                            .Horizontal()
                            .Spacing(5)
                            .Center()
                            .Children(
                                new TextBlock().Text("GPU:").FontSize(11).Bold().CenterVertical(),
                                new TextBlock().BindText(AppState.GpuTempText).FontSize(11).Bold().CenterVertical(),
                                new TextBlock().BindText(AppState.GpuTempStatus, s => $"({s})").FontSize(10).WithTheme((t, tb) => tb.Foreground(t.Palette.DisabledText)).CenterVertical()
                            )
                    ),
                bottomSlot
            );
    }

    public static UIElement CreateFanDial(ObservableValue<string> rpm)
    {
        return new Border()
            .Width(130)
            .Height(130)
            .CornerRadius(65)
            .CenterHorizontal()
            .WithTheme((theme, border) =>
            {
                border.Background(theme.Palette.ControlBackground);
                border.BorderBrush(theme.Palette.Accent);
                border.BorderThickness(3);
            })
            .Child(
                new StackPanel()
                    .Vertical()
                    .Center()
                    .Spacing(4)
                    .Children(
                        new TextBlock()
                            .BindText(rpm)
                            .FontSize(18)
                            .Bold()
                            .CenterHorizontal()
                    )
            );
    }

    public static UIElement CreateCpuDutySlider()
    {
        AppState.CpuSlider = new Slider()
            .Minimum(0)
            .Maximum(100)
            .Value(AppState.CpuDutyValue.Value)
            .OnValueChanged(v =>
            {
                if (AppState.IsSyncingDuty) return;

                int duty = (int)Math.Round(v);
                AppState.CpuDutyValue.Value = duty;
                AppState.CpuDutyText.Value = $"{duty}%";

                if (AppState.FansLocked.Value)
                {
                    AppState.IsSyncingDuty = true;
                    try
                    {
                        AppState.GpuDutyValue.Value = duty;
                        AppState.GpuDutyText.Value = $"{duty}%";
                        if (AppState.GpuSlider != null && (int)Math.Round(AppState.GpuSlider.Value) != duty)
                        {
                            AppState.GpuSlider.Value = duty;
                        }
                    }
                    finally
                    {
                        AppState.IsSyncingDuty = false;
                    }
                }

                HardwareService.ScheduleApplyFanSpeeds(150);
            });

        return new StackPanel()
            .Vertical()
            .Spacing(4)
            .Width(180)
            .CenterHorizontal()
            .Children(
                new StackPanel()
                    .Horizontal()
                    .Spacing(6)
                    .CenterHorizontal()
                    .Children(
                        new TextBlock().Text("Speed").FontSize(11).Bold(),
                        new TextBlock().BindText(AppState.CpuDutyText).FontSize(11)
                    ),
                AppState.CpuSlider
            );
    }

    public static UIElement CreateGpuDutySlider()
    {
        AppState.GpuSlider = new Slider()
            .Minimum(0)
            .Maximum(100)
            .Value(AppState.GpuDutyValue.Value)
            .OnValueChanged(v =>
            {
                if (AppState.IsSyncingDuty) return;

                int duty = (int)Math.Round(v);
                AppState.GpuDutyValue.Value = duty;
                AppState.GpuDutyText.Value = $"{duty}%";

                if (AppState.FansLocked.Value)
                {
                    AppState.IsSyncingDuty = true;
                    try
                    {
                        AppState.CpuDutyValue.Value = duty;
                        AppState.CpuDutyText.Value = $"{duty}%";
                        if (AppState.CpuSlider != null && (int)Math.Round(AppState.CpuSlider.Value) != duty)
                        {
                            AppState.CpuSlider.Value = duty;
                        }
                    }
                    finally
                    {
                        AppState.IsSyncingDuty = false;
                    }
                }

                HardwareService.ScheduleApplyFanSpeeds(150);
            });

        return new StackPanel()
            .Vertical()
            .Spacing(4)
            .Width(180)
            .CenterHorizontal()
            .Children(
                new StackPanel()
                    .Horizontal()
                    .Spacing(6)
                    .CenterHorizontal()
                    .Children(
                        new TextBlock().Text("Speed").FontSize(11).Bold(),
                        new TextBlock().BindText(AppState.GpuDutyText).FontSize(11)
                    ),
                AppState.GpuSlider
            );
    }

    public static UIElement BuildCoolBoostCard()
    {
        var toggle = new ToggleSwitch()
            .CenterVertical()
            .BindIsEnabled(AppState.CoolBoostCanToggle)
            .BindIsChecked(AppState.CoolBoostState)
            .OnCheckedChanged(enabled => HardwareService.SetCoolBoost(enabled));

        AppState.CoolBoostToggle = toggle;
        HardwareService.UpdateCoolBoostCompatibility();

        return CommonComponents.CreateCard(
            new Grid()
                .Columns("*,Auto")
                .Rows("Auto")
                .Children(
                    new StackPanel()
                        .Vertical()
                        .Spacing(3)
                        .Children(
                            new TextBlock().Text("Acer CoolBoost™ Technology").Bold().FontSize(14),
                            new TextBlock().BindText(AppState.CoolBoostCardSubtitle).FontSize(11)
                        ),
                    new StackPanel()
                        .Column(1)
                        .Horizontal()
                        .Spacing(8)
                        .CenterVertical()
                        .Children(
                            new Button()
                                .Content(new TextBlock().Text("🔓 Unlock").FontSize(11).Bold())
                                .Padding(8, 4)
                                .WithTheme((t, b) => b.CornerRadius(4))
                                .BindIsVisible(AppState.ShowCoolBoostUnlockButton)
                                .OnClick(() => HardwareService.UnlockExperimentalCoolBoost()),
                            toggle
                        )
                )
        );
    }
}
