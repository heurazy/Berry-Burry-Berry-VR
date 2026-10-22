// Berry Bury Berry VR - one-file installer.
//
// Built with the C# compiler that ships with Windows (.NET Framework 4.x), so
// players need nothing else installed. The mod files (BepInEx, openvr_api.dll
// and the BerryVR plugin) are embedded as "payload.zip" and extracted into
// the game folder, which is found through Steam's library list.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.Win32;

internal static class Installer
{
    private const string AppId = "3370870";
    private const string GameExe = "BerryBerryBerryBerryBerryBerryBerryBerryBerry.exe";
    private const string PluginDir = @"BepInEx\plugins\BerryVR";

    private static int Main(string[] args)
    {
        try { Console.OutputEncoding = Encoding.UTF8; } catch { }
        Console.Title = "Berry Bury Berry VR - Installateur";

        Say("==============================================", ConsoleColor.Magenta);
        Say("   Berry Bury Berry VR - Installateur", ConsoleColor.Magenta);
        Say("==============================================", ConsoleColor.Magenta);
        Console.WriteLine();

        bool uninstall = false;
        string game = null;
        foreach (var a in args)
        {
            if (a.Equals("/uninstall", StringComparison.OrdinalIgnoreCase) || a.Equals("--uninstall", StringComparison.OrdinalIgnoreCase))
                uninstall = true;
            else if (IsGameDir(a))
                game = a;
        }

        try
        {
            if (game == null) game = FindGame();
            if (game == null) game = AskForGame();
            if (game == null)
            {
                Say("Installation annulée.", ConsoleColor.Yellow);
                return Pause(1);
            }
            Say("Jeu trouvé : " + game, ConsoleColor.Green);
            Console.WriteLine();

            if (!uninstall)
            {
                Console.WriteLine("  [1] Installer / mettre à jour le mod VR");
                Console.WriteLine("  [2] Désinstaller le mod VR");
                Console.Write("Choix (1 par défaut) : ");
                string c = (Console.ReadLine() ?? "").Trim();
                uninstall = c == "2";
                Console.WriteLine();
            }

            WaitForGameClosed();

            if (uninstall) Uninstall(game);
            else Install(game);
            return Pause(0);
        }
        catch (Exception e)
        {
            Console.WriteLine();
            Say("Erreur : " + e.Message, ConsoleColor.Red);
            return Pause(1);
        }
    }

    // ------------------------------------------------------------------
    // Install / uninstall
    // ------------------------------------------------------------------

    private static void Install(string game)
    {
        Say("Installation en cours...", ConsoleColor.Cyan);
        int written = 0, kept = 0;

        using (var res = Assembly.GetExecutingAssembly().GetManifestResourceStream("payload.zip"))
        {
            if (res == null) throw new InvalidOperationException("payload.zip manquant dans l'installateur.");
            using (var zip = new ZipArchive(res, ZipArchiveMode.Read))
            {
                foreach (var entry in zip.Entries)
                {
                    if (string.IsNullOrEmpty(entry.Name)) continue; // directory
                    string rel = entry.FullName.Replace('/', '\\');
                    string dest = Path.Combine(game, rel);

                    // Never overwrite the player's settings.
                    if (rel.StartsWith(@"BepInEx\config\", StringComparison.OrdinalIgnoreCase) && File.Exists(dest))
                    {
                        kept++;
                        continue;
                    }

                    Directory.CreateDirectory(Path.GetDirectoryName(dest));
                    using (var src = entry.Open())
                    using (var dst = File.Create(dest))
                        src.CopyTo(dst);
                    written++;
                }
            }
        }

        Console.WriteLine("  " + written + " fichiers installés" + (kept > 0 ? ", " + kept + " réglages conservés." : "."));
        Console.WriteLine();
        Say("Installation terminée !", ConsoleColor.Green);
        Console.WriteLine();
        Console.WriteLine("Pour jouer :");
        Console.WriteLine("  1. Lance SteamVR et mets ton casque (Virtual Desktop, Link, ...).");
        Console.WriteLine("  2. Lance Berry Bury Berry depuis Steam.");
        Console.WriteLine("  Sans SteamVR, le jeu démarre normalement sur l'écran.");
        Console.WriteLine();
        Console.WriteLine("Contrôles et réglages : " + Path.Combine(game, PluginDir, "README_CONTROLES.txt"));
    }

    private static void Uninstall(string game)
    {
        Say("Désinstallation en cours...", ConsoleColor.Cyan);
        DeleteDir(Path.Combine(game, PluginDir));
        DeleteFile(Path.Combine(game, "openvr_api.dll"));

        Console.Write("Supprimer aussi BepInEx (le chargeur de mods) ? Garde-le si tu as d'autres mods. (o/N) : ");
        string c = (Console.ReadLine() ?? "").Trim().ToLowerInvariant();
        if (c == "o" || c == "y" || c == "oui" || c == "yes")
        {
            DeleteFile(Path.Combine(game, "winhttp.dll"));
            DeleteFile(Path.Combine(game, "doorstop_config.ini"));
            DeleteFile(Path.Combine(game, ".doorstop_version"));
            DeleteFile(Path.Combine(game, "changelog.txt"));
            DeleteDir(Path.Combine(game, "BepInEx"));
        }
        Console.WriteLine();
        Say("Mod VR désinstallé.", ConsoleColor.Green);
    }

    private static void DeleteFile(string path)
    {
        if (File.Exists(path)) File.Delete(path);
    }

    private static void DeleteDir(string path)
    {
        if (Directory.Exists(path)) Directory.Delete(path, true);
    }

    private static void WaitForGameClosed()
    {
        string name = Path.GetFileNameWithoutExtension(GameExe);
        while (Process.GetProcessesByName(name).Length > 0)
        {
            Say("Le jeu est ouvert : ferme-le puis appuie sur Entrée.", ConsoleColor.Yellow);
            Console.ReadLine();
        }
    }

    // ------------------------------------------------------------------
    // Finding the game
    // ------------------------------------------------------------------

    private static bool IsGameDir(string dir)
    {
        try { return !string.IsNullOrEmpty(dir) && File.Exists(Path.Combine(dir, GameExe)); }
        catch { return false; }
    }

    private static string FindGame()
    {
        var libraries = new List<string>();
        string steam = SteamPath();
        if (steam != null)
        {
            libraries.Add(steam);
            string vdf = Path.Combine(steam, @"steamapps\libraryfolders.vdf");
            if (File.Exists(vdf))
            {
                foreach (Match m in Regex.Matches(File.ReadAllText(vdf), "\"path\"\\s*\"([^\"]+)\""))
                    libraries.Add(m.Groups[1].Value.Replace(@"\\", @"\"));
            }
        }

        foreach (var lib in libraries)
        {
            string manifest = Path.Combine(lib, @"steamapps\appmanifest_" + AppId + ".acf");
            if (!File.Exists(manifest)) continue;
            var m = Regex.Match(File.ReadAllText(manifest), "\"installdir\"\\s*\"([^\"]+)\"");
            if (!m.Success) continue;
            string dir = Path.Combine(lib, @"steamapps\common", m.Groups[1].Value);
            if (IsGameDir(dir)) return dir;
        }

        // Fallback: the default folder name in every library.
        foreach (var lib in libraries)
        {
            string dir = Path.Combine(lib, @"steamapps\common\BerryBerryBerry");
            if (IsGameDir(dir)) return dir;
        }
        return null;
    }

    private static string SteamPath()
    {
        foreach (var key in new[] { @"HKEY_CURRENT_USER\Software\Valve\Steam", @"HKEY_LOCAL_MACHINE\SOFTWARE\WOW6432Node\Valve\Steam" })
        {
            foreach (var value in new[] { "SteamPath", "InstallPath" })
            {
                var p = Registry.GetValue(key, value, null) as string;
                if (!string.IsNullOrEmpty(p) && Directory.Exists(p)) return p.Replace('/', '\\');
            }
        }
        return null;
    }

    private static string AskForGame()
    {
        Say("Je n'ai pas trouvé le jeu automatiquement.", ConsoleColor.Yellow);
        while (true)
        {
            Console.WriteLine("Colle le chemin du dossier du jeu (celui qui contient " + GameExe + ")");
            Console.Write("ou laisse vide pour annuler : ");
            string p = (Console.ReadLine() ?? "").Trim().Trim('"');
            if (p.Length == 0) return null;
            if (IsGameDir(p)) return p;
            Say("Ce dossier ne contient pas le jeu.", ConsoleColor.Red);
        }
    }

    // ------------------------------------------------------------------

    private static void Say(string text, ConsoleColor color)
    {
        var prev = Console.ForegroundColor;
        Console.ForegroundColor = color;
        Console.WriteLine(text);
        Console.ForegroundColor = prev;
    }

    private static int Pause(int code)
    {
        Console.WriteLine();
        Console.Write("Appuie sur Entrée pour fermer.");
        Console.ReadLine();
        return code;
    }
}
