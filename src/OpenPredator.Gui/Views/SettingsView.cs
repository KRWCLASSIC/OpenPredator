using Aprillz.MewUI;
using Aprillz.MewUI.Controls;

namespace OpenPredator.Gui;

internal static class SettingsView
{
    public static UIElement BuildSettingsPage()
    {
        return new StackPanel()
            .Vertical()
            .Spacing(12)
            .Children(
                // 1. Application Preferences
                CommonComponents.CreateCard(
                    new StackPanel()
                        .Vertical()
                        .Spacing(10)
                        .Children(
                            new TextBlock().Text("Application Preferences").Bold().FontSize(15),
                            new Grid()
                                .Columns("*,Auto")
                                .Rows("Auto")
                                .Children(
                                    new StackPanel().Vertical().Spacing(2).Children(
                                        new TextBlock().Text("Start OpenSense with Windows").Bold().FontSize(12),
                                        new TextBlock().Text("Choose automatic startup: Off, Current User, or System-Wide.").FontSize(10)
                                    ),
                                    (AppState.AutostartComboBox = new ComboBox())
                                        .Column(1)
                                        .CenterVertical()
                                        .Width(140)
                                        .Items("Off", "Current User", "System-Wide")
                                        .SelectedIndex(AppState.AutostartModeIndex.Value)
                                        .OnSelectionChanged(GuiSettingsManager.HandleAutostartSelectionChanged)
                                ),

                            new Grid()
                                .Columns("*,Auto")
                                .Rows("Auto")
                                .Children(
                                    new StackPanel().Vertical().Spacing(2).Children(
                                        new TextBlock().Text("Telemetry Refresh Rate").Bold().FontSize(12),
                                        new TextBlock().Text("Adjust how frequently sensor readings and fan speeds update (higher intervals save GPU/battery).").FontSize(10)
                                    ),
                                    (AppState.SettingsTelemetryComboBox = new ComboBox())
                                        .Column(1)
                                        .CenterVertical()
                                        .Width(140)
                                        .Items(AppState.TelemetryIntervalLabels)
                                        .SelectedIndex(AppState.TelemetryIntervalIndex.Value)
                                        .OnSelectionChanged(GuiSettingsManager.HandleTelemetryIntervalChanged)
                                ),

                            new Grid()
                                .Columns("*,Auto")
                                .Rows("Auto")
                                .Children(
                                    new StackPanel().Vertical().Spacing(2).Children(
                                        new TextBlock().Text("Minimize to System Notification Tray").Bold().FontSize(12),
                                        new TextBlock().Text("Keep OpenSense running in the notification area when the window is closed.").FontSize(10)
                                    ),
                                    new ToggleSwitch()
                                        .Column(1)
                                        .CenterVertical()
                                        .BindIsChecked(AppState.MinimizeToTrayState)
                                        .OnCheckedChanged(GuiSettingsManager.SetMinimizeToTrayPreference)
                                ),
                            new Grid()
                                .Columns("*,Auto")
                                .Rows("Auto")
                                .Children(
                                    new StackPanel().Vertical().Spacing(2).Children(
                                        new TextBlock().Text("Start Minimized in Background").Bold().FontSize(12),
                                        new TextBlock().Text("Start silently in the notification area without opening the main window.").FontSize(10)
                                    ),
                                    new ToggleSwitch()
                                        .Column(1)
                                        .CenterVertical()
                                        .BindIsChecked(AppState.StartMinimizedState)
                                        .OnCheckedChanged(GuiSettingsManager.SetStartMinimizedPreference)
                                ),
                            new StackPanel()
                                .Horizontal()
                                .Spacing(10)
                                .Margin(0, 6, 0, 0)
                                .Children(
                                    new Button()
                                        .Content("Exit OpenSense")
                                        .OnClick(Program.ExitApplication)
                                )
                        )
                ),

                // 2. Hardware Diagnostics & SMBIOS Probing Information
                CommonComponents.CreateCard(
                    new StackPanel()
                        .Vertical()
                        .Spacing(8)
                        .Children(
                            new TextBlock().Text("Hardware & Daemon Diagnostics").Bold().FontSize(15),
                            CommonComponents.CreateDiagRow("Laptop Model", AppState.Caps.ModelName),
                            CommonComponents.CreateDiagRow("Hardware Platform", AppState.Caps.IsPredator ? "Acer Predator Series" : "Acer Nitro Series"),
                            CommonComponents.CreateDiagRow("Keyboard Backlight", AppState.Caps.IsRgbKeyboard ? "4-Zone RGB Matrix" : "Monochrome 5-State Zone"),
                            CommonComponents.CreateDiagRow("Installed Cooling", $"{AppState.Caps.FanCount} Fans (CPU, GPU{(AppState.Caps.HasSystemFan ? ", Aux/System" : "")})"),
                            CommonComponents.CreateDiagRow("Operating Modes", AppState.Caps.SupportsOperatingModes ? "Firmware Verified (4 Profiles)" : (AppState.Caps.SupportsCoolBoost ? "CoolBoost Fan Table" : "Standard Control")),
                            CommonComponents.CreateDiagRow("IPC Pipe Channel", "predatorsense_service_namedpipe"),
                            CommonComponents.CreateDiagRow("Process Privilege", GuiSettingsManager.IsAdministrator() ? "[ADMIN] Elevated" : "Standard User"),
                            CommonComponents.CreateDiagRow("Engine Version", typeof(SettingsView).Assembly.GetName().Version is { } v ? $"v{v.Major}.{v.Minor}.{v.Build}" : "Undefined")
                        )
                )
            );
    }
}
