using System.Numerics;
using ImGuiNET;
using XIVLauncher.Core.Components.SettingsPage.Tabs;

namespace XIVLauncher.Core.Components.SettingsPage;

public class SettingsPage : Page
{
    private readonly SettingsTab[] tabs =
    {
        new SettingsTabGame(),
        new SettingsTabWine(),
        new SettingsTabAbout(),
    };

    private string searchInput = string.Empty;

    public SettingsPage(LauncherApp app)
        : base(app)
    {
    }

    public override void OnShow()
    {
        foreach (var settingsTab in this.tabs)
        {
            settingsTab.Load();
        }

        base.OnShow();
    }

    public override void Draw()
    {
        if (ImGui.BeginTabBar("###settingsTabs"))
        {
            if (string.IsNullOrEmpty(this.searchInput))
            {
                foreach (SettingsTab settingsTab in this.tabs)
                {
                    if (settingsTab.IsLinux && !OperatingSystem.IsLinux())
                        continue;

                    if (ImGui.BeginTabItem(settingsTab.Title))
                    {
                        settingsTab.Draw();
                        ImGui.EndTabItem();
                    }
                }
            }
            else
            {
                if (ImGui.BeginTabItem("Search Results"))
                {
                    foreach (SettingsTab settingsTab in this.tabs)
                    {
                        if (settingsTab.IsLinux && !OperatingSystem.IsLinux())
                            continue;

                        var eligible = settingsTab.Entries.Where(x => x.Name.ToLower().Contains(this.searchInput.ToLower())).ToArray();

                        if (!eligible.Any())
                            continue;

                        ImGui.TextColored(ImGuiColors.DalamudGrey, settingsTab.Title);
                        ImGui.Dummy(new Vector2(5));

                        foreach (SettingsEntry settingsTabEntry in settingsTab.Entries)
                        {
                            if (!settingsTabEntry.Name.ToLower().Contains(this.searchInput.ToLower()))
                                continue;

                            settingsTabEntry.Draw();
                        }

                        ImGui.Separator();

                        ImGui.Dummy(new Vector2(10));
                    }

                    ImGui.EndTabItem();
                }
            }
        }

        ImGui.SetCursorPos(ImGuiHelpers.ViewportSize - new Vector2(60));
        ImGui.PushStyleVar(ImGuiStyleVar.FrameRounding, 100f);
        ImGui.PushFont(FontManager.IconFont);

        if (ImGui.Button(FontAwesomeIcon.Check.ToIconString(), new Vector2(40)))
        {
            foreach (var settingsTab in this.tabs)
            {
                settingsTab.Save();
            }

            this.App.State = LauncherApp.LauncherState.Main;
        }

        ImGui.PopStyleVar();
        ImGui.PopFont();

        var vpSize = ImGuiHelpers.ViewportSize;
        ImGui.SetCursorPos(new Vector2(vpSize.X - 250, 4));
        ImGui.SetNextItemWidth(240);
        ImGui.InputTextWithHint("###searchInput", "Search for settings...", ref this.searchInput, 100);

        base.Draw();
    }
}