using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Windows;
using CheapLoc;
using Microsoft.Win32;
using Newtonsoft.Json;
using Serilog;
using XIVLauncher.Common;
using XIVLauncher.Common.Util;
using XIVLauncher.Windows;

namespace XIVLauncher.Game
{
    static class ProblemCheck
    {
        private static string GetCmdPath() => Path.Combine(Environment.ExpandEnvironmentVariables("%WINDIR%"), "System32", "cmd.exe");

        public static void RunCheck(Window parentWindow)
        {
            if (EnvironmentSettings.IsWine)
                return;

            var compatFlagKey = Registry.CurrentUser.OpenSubKey(
                "Software\\Microsoft\\Windows NT\\CurrentVersion\\AppCompatFlags\\Layers", true);

            if (compatFlagKey != null && !EnvironmentSettings.IsWine && !App.Settings.HasComplainedAboutAdmin.GetValueOrDefault(false))
            {
                var compatEntries = compatFlagKey.GetValueNames();

                var entriesToFix = new Stack<string>();

                foreach (var compatEntry in compatEntries)
                {
                    if ((compatEntry.Contains("ffxiv_dx11") || compatEntry.Contains("XIVLauncher")) && ((string)compatFlagKey.GetValue(compatEntry, string.Empty)).Contains("RUNASADMIN"))
                        entriesToFix.Push(compatEntry);
                }

                if (entriesToFix.Count > 0)
                {
                    var result = CustomMessageBox.Show(
                        Loc.Localize("AdminCheck",
                            "XIVLauncher and/or the game are set to run as administrator.\nThis can cause various issues, including addons failing to launch and hotkey applications failing to respond.\n\nDo you want to fix this issue automatically?"),
                        "XIVLauncher", MessageBoxButton.OKCancel, MessageBoxImage.Exclamation, parentWindow: parentWindow);

                    if (result != MessageBoxResult.OK)
                        return;

                    while (entriesToFix.Count > 0)
                    {
                        compatFlagKey.DeleteValue(entriesToFix.Pop());
                    }

                    return;
                }

                App.Settings.HasComplainedAboutAdmin = true;
            }

            if (PlatformHelpers.IsElevated() && !App.Settings.HasComplainedAboutAdmin.GetValueOrDefault(false) && !EnvironmentSettings.IsWine)
            {
                CustomMessageBox.Show(
                    Loc.Localize("AdminCheckNag",
                        "XIVLauncher is running as administrator.\nThis can cause various issues, including addons failing to launch and hotkey applications failing to respond.\n\nPlease take care to avoid running XIVLauncher as admin."),
                    "XIVLauncher Problem", MessageBoxButton.OK, MessageBoxImage.Exclamation, parentWindow: parentWindow);
                App.Settings.HasComplainedAboutAdmin = true;
            }

            var procModules = Process.GetCurrentProcess().Modules.Cast<ProcessModule>();

            if (procModules.Any(x => x.ModuleName == "MacType.dll" || x.ModuleName == "MacType64.dll"))
            {
                CustomMessageBox.Show(
                    Loc.Localize("MacTypeNag",
                        "MacType was detected on this PC.\nIt will cause problems with the game; both on the official launcher and XIVLauncher.\n\nPlease exclude XIVLauncher, ffxivboot, ffxivlauncher, ffxivupdater and ffxiv_dx11 from MacType."),
                    "XIVLauncher Problem", MessageBoxButton.OK, MessageBoxImage.Error, parentWindow: parentWindow);
                Environment.Exit(-1);
            }

            if (!CheckMyGamesWriteAccess())
            {
                CustomMessageBox.Show(
                    Loc.Localize("MyGamesWriteAccessNag",
                        "You do not have permission to write to the game's My Games folder.\nThis will prevent screenshots and some character data from being saved.\n\nThis may be caused by either your antivirus or a permissions error. Please check your My Games folder permissions."),
                    "XIVLauncher Problem", MessageBoxButton.OK, MessageBoxImage.Exclamation, parentWindow: parentWindow);
            }

            if (App.Settings.GamePath == null)
                return;

            var gameFolderPath = Path.Combine(App.Settings.GamePath.FullName, "game");

            var d3d11 = new FileInfo(Path.Combine(gameFolderPath, "d3d11.dll"));
            var dxgi = new FileInfo(Path.Combine(gameFolderPath, "dxgi.dll"));
            var dinput8 = new FileInfo(Path.Combine(gameFolderPath, "dinput8.dll"));

            if (!CheckSymlinkValid(d3d11) || !CheckSymlinkValid(dxgi) || !CheckSymlinkValid(dinput8))
            {
                if (CustomMessageBox.Builder
                                    .NewFrom(Loc.Localize("GShadeSymlinks",
                                        "GShade symbolic links are corrupted.\n\nThe game cannot start. Do you want XIVLauncher to fix this? You will need to reinstall GShade."))
                                    .WithButtons(MessageBoxButton.YesNo)
                                    .WithImage(MessageBoxImage.Error)
                                    .WithParentWindow(parentWindow)
                                    .Show() == MessageBoxResult.Yes)
                {
                    try
                    {
                        if (d3d11.Exists)
                            ElevatedDelete(d3d11);

                        if (dxgi.Exists)
                            ElevatedDelete(dxgi);

                        if (dinput8.Exists)
                            ElevatedDelete(dinput8);
                    }
                    catch (Exception ex)
                    {
                        Log.Error(ex, "Could not delete broken GShade symlinks");
                    }
                }
            }

            d3d11.Refresh();
            dinput8.Refresh();
            dxgi.Refresh();

            if (d3d11.Exists && dxgi.Exists)
            {
                var dxgiInfo = FileVersionInfo.GetVersionInfo(dxgi.FullName);
                var d3d11Info = FileVersionInfo.GetVersionInfo(d3d11.FullName);

                if (dxgiInfo.ProductName?.Equals("GShade", StringComparison.OrdinalIgnoreCase) == true &&
                    d3d11Info.ProductName?.Equals("GShade", StringComparison.OrdinalIgnoreCase) == true)
                {
                    if (CustomMessageBox.Builder
                                        .NewFrom(Loc.Localize("GShadeError",
                                            "A broken GShade installation was detected.\n\nThe game cannot start. Do you want XIVLauncher to fix this? You will need to reinstall GShade."))
                                        .WithButtons(MessageBoxButton.YesNo)
                                        .WithImage(MessageBoxImage.Error)
                                        .WithParentWindow(parentWindow)
                                        .Show() == MessageBoxResult.Yes)
                    {
                        try
                        {
                            ElevatedDelete(d3d11, dxgi);
                        }
                        catch (Exception ex)
                        {
                            Log.Error(ex, "Could not delete duplicate GShade");
                        }
                    }
                }
            }

            d3d11.Refresh();
            dinput8.Refresh();
            dxgi.Refresh();

            if ((d3d11.Exists || dinput8.Exists) && !App.Settings.HasComplainedAboutGShadeDxgi.GetValueOrDefault(false))
            {
                FileVersionInfo? d3d11Info = null;
                FileVersionInfo? dinput8Info = null;

                if (d3d11.Exists)
                    d3d11Info = FileVersionInfo.GetVersionInfo(d3d11.FullName);

                if (dinput8.Exists)
                    dinput8Info = FileVersionInfo.GetVersionInfo(dinput8.FullName);

                if ((d3d11Info?.ProductName?.Equals("GShade", StringComparison.OrdinalIgnoreCase) ?? false) ||
                    (dinput8Info?.ProductName?.Equals("GShade", StringComparison.OrdinalIgnoreCase) ?? false))
                {
                    if (CustomMessageBox.Builder
                                        .NewFrom(Loc.Localize("GShadeWrongMode",
                                            "You installed GShade in a mode that isn't optimal for use together with XIVLauncher. Do you want XIVLauncher to fix this for you?\n\nThis will not change your presets or settings, it will merely improve compatibility with XIVLauncher features."))
                                        .WithButtons(MessageBoxButton.YesNo)
                                        .WithImage(MessageBoxImage.Warning)
                                        .WithParentWindow(parentWindow)
                                        .Show() == MessageBoxResult.Yes)
                    {
                        try
                        {
                            var toMove = d3d11.Exists ? d3d11 : dinput8;

                            var psi = new ProcessStartInfo
                            {
                                Verb = "runas",
                                FileName = GetCmdPath(),
                                WorkingDirectory = Paths.ResourcesPath,
                                Arguments = $"/C \"move \"{Path.Combine(gameFolderPath, toMove.Name)}\" \"{Path.Combine(gameFolderPath, "dxgi.dll")}\"\"",
                                UseShellExecute = true,
                                CreateNoWindow = true,
                                WindowStyle = ProcessWindowStyle.Hidden
                            };

                            var process = Process.Start(psi);

                            if (process == null)
                            {
                                throw new Exception("Could not spawn CMD when fixing GShade");
                            }

                            process.WaitForExit();

                            var gshadeInstKey = Registry.LocalMachine.OpenSubKey(
                                "SOFTWARE\\GShade\\Installations", false);

                            if (gshadeInstKey != null)
                            {
                                var gshadeInstSubKeys = gshadeInstKey.GetSubKeyNames();

                                var gshadeInstsToFix = new Stack<string>();

                                foreach (var gshadeInst in gshadeInstSubKeys)
                                {
                                    if (gshadeInst.Contains("ffxiv_dx11.exe"))
                                    {
                                        gshadeInstsToFix.Push(gshadeInst);
                                    }
                                }

                                if (gshadeInstsToFix.Count > 0)
                                {
                                    while (gshadeInstsToFix.Count > 0)
                                    {
                                        var gshadePsi = new ProcessStartInfo
                                        {
                                            Verb = "runas",
                                            FileName = "reg.exe",
                                            WorkingDirectory = Environment.SystemDirectory,
                                            Arguments = $"add \"HKLM\\SOFTWARE\\GShade\\Installations\\{gshadeInstsToFix.Pop()}\" /v \"altdxmode\" /t \"REG_SZ\" /d \"0\" /f",
                                            UseShellExecute = true,
                                            CreateNoWindow = true,
                                            WindowStyle = ProcessWindowStyle.Hidden
                                        };

                                        var gshadeProcess = Process.Start(gshadePsi);

                                        if (gshadeProcess == null)
                                        {
                                            throw new Exception("Could not spawn reg when fixing GShade");
                                        }

                                        gshadeProcess.WaitForExit();
                                    }
                                }
                            }
                        }
                        catch (Exception ex)
                        {
                            Log.Error(ex, "Could not fix GShade incompatibility");
                        }
                    }
                    else
                    {
                        App.Settings.HasComplainedAboutGShadeDxgi = true;
                    }
                }
            }

            if ((dxgi.Exists || d3d11.Exists || dinput8.Exists) && !App.Settings.HasComplainedAboutGShadeOutOfDate.GetValueOrDefault(false))
            {
                // If we can't guarantee it's GShade, don't even bother
                var dxgiInfo = dxgi.Exists ? FileVersionInfo.GetVersionInfo(dxgi.FullName) : null;
                var d3d11Info = d3d11.Exists ? FileVersionInfo.GetVersionInfo(d3d11.FullName) : null;
                var dinput8Info = dinput8.Exists ? FileVersionInfo.GetVersionInfo(dinput8.FullName) : null;

                if ((dxgiInfo.ProductName?.Equals("GShade", StringComparison.OrdinalIgnoreCase) == true ||
                    d3d11Info.ProductName?.Equals("GShade", StringComparison.OrdinalIgnoreCase) == true ||
                    dinput8Info.ProductName?.Equals("GShade", StringComparison.OrdinalIgnoreCase) == true) &&
                    App.Settings.LastGShadeVersionCheckTimestamp.AddMinutes(30).ToUniversalTime() < DateTime.UtcNow)
                {
                    // make sure we open the x64 registry
                    string gshaderegpath = @"SOFTWARE\GShade";
                    var localMachineX64View = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64);
                    var gshadereg = localMachineX64View.OpenSubKey(gshaderegpath);

                    // registry entry does not exist
                    if (gshadereg == null)
                    {
                        Log.Debug("No registry entry for GShade found. XIVLauncher won't check anymore.");
                        App.Settings.HasComplainedAboutGShadeOutOfDate = true;
                        return; // if there's a better way to exit the if, we can switch to that.
                    }
                    Log.Debug("Found proof of GShade installation. Running manual version check on it.");

                    // pull the key/value we want from the registry. This is what we'll use for version checking.
                    var gshadeinstallver = gshadereg.GetValue("instver") ?? "";
                    Log.Debug($"GShade Registry version: {gshadeinstallver}");

                    // we should cache this
                    string gshadevercheckurl = "https://api.github.com/repos/mortalitas/gshade/tags";

                    // pull latest tags from github (adjust for cached response in future)
                    List<GithubTagEntry> gshadetags;
                    using var client = new HttpClient()
                    {
                        DefaultRequestHeaders =
                        {
                            UserAgent = { new ProductInfoHeaderValue("XIVLauncher", AppUtil.GetGitHash())}
                        }
                    };
                    var request = new HttpRequestMessage(HttpMethod.Get, gshadevercheckurl);
                    var resp = client.SendAsync(request).Result;
                    resp.EnsureSuccessStatusCode();
                    gshadetags = JsonConvert.DeserializeObject<List<GithubTagEntry>>(resp.Content.ReadAsStringAsync().Result);
                    var latestgshade = gshadetags.FirstOrDefault();

                    string ghtagver = latestgshade.name;
                    Log.Debug($"Latest GShade tag: {ghtagver}");

                    if ($"v{gshadeinstallver}" != ghtagver)
                    {
                        // version mismatch
                        Log.Information("GShade version mismatch! Prompt to run the GShade updater.");


                        if (CustomMessageBox.Builder
                                        .NewFrom(Loc.Localize("GShadeOutOfDate",
                                            "Your copy of GShade is out of date. This will result in it being disabled if you proceed to launch anyways, per GShade policies.\n\nWould you like to run the GShade updater before launching FFXIV? This will exit XIVLauncher in order to continue."))
                                        .WithButtons(MessageBoxButton.YesNo)
                                        .WithImage(MessageBoxImage.Warning)
                                        .WithParentWindow(parentWindow)
                                        .Show() == MessageBoxResult.Yes)
                        {
                            ProcessStartInfo startInfo = new ProcessStartInfo();
                            startInfo.CreateNoWindow = false;
                            startInfo.UseShellExecute = true;
                            startInfo.FileName = Environment.ExpandEnvironmentVariables(
                                "%ProgramFiles%\\GShade\\GShade Control Panel.exe");
                            startInfo.WindowStyle = ProcessWindowStyle.Normal;
                            startInfo.Arguments = "/U";
                            startInfo.Verb = "runas";

                            try
                            {
                                using (Process exeProcess = Process.Start(startInfo))
                                {
                                    // exeProcess.WaitForExit(); //GSHade won't run if XIVLauncher is.
                                    Environment.Exit(0);
                                }
                                // Assume we updated or the user dismissed it. Set cooldown.
                                App.Settings.LastGShadeVersionCheckTimestamp = DateTime.Now;
                            }
                            catch (Exception ex)
                            {
                                Log.Error(ex, "Running GShade updater failed. Continuing with launch.");
                            }
                        }
                        else
                        {
                            // disable the check. The user doesn't want it.
                            App.Settings.HasComplainedAboutGShadeOutOfDate= true;
                        }
                    }
                    else
                    {
                        Log.Information("Passed GShade version check.");
                        // we should cache our last check time as cooldown
                        App.Settings.LastGShadeVersionCheckTimestamp = DateTime.Now;
                    }
                }


                
            }
        }

        private static void ElevatedDelete(params FileInfo[] info)
        {
            var pathsToDelete = info.Select(x => $"\"{x.FullName}\"").Aggregate("", (current, name) => current + $"{name} ");

            var psi = new ProcessStartInfo
            {
                Verb = "runas",
                FileName = GetCmdPath(),
                Arguments = $"/C \"del {pathsToDelete}\"",
                UseShellExecute = true,
                CreateNoWindow = true,
                WindowStyle = ProcessWindowStyle.Hidden
            };

            var process = Process.Start(psi);

            if (process == null)
            {
                throw new Exception("Could not spawn CMD for elevated delete");
            }

            process.WaitForExit();
        }

        private static bool CheckMyGamesWriteAccess()
        {
            // Create a randomly-named file in the game's user data folder and make sure we don't
            // get a permissions error.
            var myGames = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "my games");
            if (!Directory.Exists(myGames))
                return true;

            var targetPath = Directory.GetDirectories(myGames).FirstOrDefault(x => Path.GetDirectoryName(x)?.Length == 34);
            if (targetPath == null)
                return true;

            var tempFile = Path.Combine(targetPath, Guid.NewGuid().ToString());

            try
            {
                var file = File.Create(tempFile);
                file.Dispose();
                File.Delete(tempFile);
            }
            catch (UnauthorizedAccessException)
            {
                return false;
            }
            catch (Exception)
            {
                return true;
            }

            return true;
        }

        private static bool CheckSymlinkValid(FileInfo file)
        {
            if (!file.Exists)
                return true;

            try
            {
                file.OpenRead();
            }
            catch (IOException)
            {
                return false;
            }

            return true;
        }
    }

    internal class GithubTagEntry
    {
        internal class GHTCommit
        {
            string sha { get; set; }
            string url { get; set; }
        }
        public string name { get; set; }
        public string zipball_url { get; set; }
        public string tarball_url { get; set; }
        public GHTCommit commit { get; set; }
        public string node_id { get; set; }
    }

    
}