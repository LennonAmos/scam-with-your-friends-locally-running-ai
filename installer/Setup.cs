using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Web.Script.Serialization;
using Microsoft.Win32;

[assembly: AssemblyTitle("Scam With Your Friends - Ollama Mod Setup")]
[assembly: AssemblyDescription("Console installer, rollback and local Ollama setup instructions")]
[assembly: AssemblyProduct("ScamOllama")]
[assembly: AssemblyVersion("1.0.2.0")]
[assembly: AssemblyFileVersion("1.0.2.0")]
[assembly: System.Runtime.Versioning.TargetFramework(".NETFramework,Version=v4.7.2")]

internal static class Setup
{
    const string GameExe = "Scam With Your Friends.exe";
    const string ManagedRelative = "Scam With Your Friends_Data\\Managed";
    const string PluginHash = "AC81B9288D4D09626D3A7C840FBCC44E095C0F376937B73737D5F939F39B56D3";
    static readonly Dictionary<string, string> Originals = new Dictionary<string, string> {
        { "Assembly-CSharp.dll", "53BD673F796EA21F8B1512C3F113E6BB1997CE62665AB3EF2C1A72B4BED490EC" },
        { "ScriptsAssDef.dll", "B73088146D09E75D00B074AC05CE822F9911D181EB0D184F7DBAA5FC8406C2F9" }
    };
    static readonly JavaScriptSerializer Json = new JavaScriptSerializer();
    static readonly object OutputLock = new object();
    static string temporaryDirectory;
    static string logPath;
    static bool noPause;

    static int Main(string[] args)
    {
        Console.OutputEncoding = new UTF8Encoding(false);
        noPause = args.Contains("--no-pause") || Console.IsInputRedirected;
        string gamePath = null;
        bool check = false, uninstall = false;
        int result = 0;
        try
        {
            for (int i = 0; i < args.Length; i++)
            {
                switch (args[i])
                {
                    case "--game-path":
                        if (++i >= args.Length) throw new ArgumentException("--game-path needs the game folder.");
                        gamePath = args[i]; break;
                    case "--check": check = true; break;
                    case "--uninstall": uninstall = true; break;
                    case "--no-pause": break;
                    case "--instructions": Banner(); Instructions(); Pause(); return 0;
                    case "--help": Help(); Pause(); return 0;
                    default: throw new ArgumentException("Unknown option: " + args[i] + ". Use --help.");
                }
            }
            if (check && uninstall) throw new ArgumentException("Choose either --check or --uninstall.");
            Banner();
            Write("[1/5] Finding Scam With Your Friends...");
            gamePath = ChooseGame(gamePath);
            Write("       Game folder: " + gamePath);
            if (check)
            {
                Write("[2/5] Checking the game version and original backups (no installation)...");
                Validate(gamePath, uninstall: false);
                Write("       Compatible with this installer.");
                Write(GameRunning(gamePath) ? "       The game is running. Close it before installing." : "       The game is closed.");
                Write("[3/5] Embedded mod package: " + VerifyEmbeddedPayload());
                Write("[4/5] Checking local Ollama..."); OllamaStatus();
                Write("[5/5] Read-only check complete.");
                Instructions();
            }
            else if (uninstall)
            {
                WaitForGameClosed(gamePath);
                Write("[2/5] Verifying installed mod and rollback backups...");
                Uninstall(gamePath);
                Write("DONE: Original game DLLs restored. Settings and backups were preserved.");
            }
            else
            {
                Write("[2/5] Verifying the game version and any existing installation...");
                var record = Validate(gamePath, uninstall: false);
                if (AlreadyInstalled(gamePath, record))
                {
                    Write("[3/5] This mod version is already installed; original backups verified.");
                    Write("[4/5] Checking local Ollama..."); OllamaStatus();
                    Write("[5/5] Setup complete. No game files needed changing.");
                }
                else
                {
                    WaitForGameClosed(gamePath);
                    record = Validate(gamePath, uninstall: false);
                    PrepareTemporaryDirectory();
                    Write("[3/5] Unpacking the mod and preparing the patches...");
                    ExtractPayload(temporaryDirectory);
                    Install(gamePath, record);
                    Write("[4/5] Checking local Ollama..."); OllamaStatus();
                    Write("[5/5] Verifying the installed files...");
                    var installed = Validate(gamePath, uninstall: false);
                    if (!AlreadyInstalled(gamePath, installed)) throw new IOException("Installed-file verification did not pass.");
                    Write("DONE: Ollama mod installed successfully.");
                    string savedLog = Path.Combine(gamePath, "ScamOllama", "setup.log");
                    File.Copy(logPath, savedLog, true);
                    Write("Setup log: " + savedLog);
                }
                Instructions();
            }
        }
        catch (Exception e)
        {
            result = 1;
            Write("");
            Write("SETUP STOPPED: " + e.Message, ConsoleColor.Red);
            if (e is UnauthorizedAccessException) Write("Windows denied write access. Check the game folder's permissions; use Run as administrator if that Steam library requires it.");
            Write("No unsupported game build will be patched. Close the game before retrying.");
            if (logPath != null) Write("Diagnostic log: " + logPath);
        }
        finally
        {
            // Only remove this invocation's generated temporary directory.
            if (result == 0 && temporaryDirectory != null)
            {
                string allowed = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "ScamOllamaSetup")) + Path.DirectorySeparatorChar;
                string target = Path.GetFullPath(temporaryDirectory);
                if (target.StartsWith(allowed, StringComparison.OrdinalIgnoreCase))
                    try { Directory.Delete(target, true); } catch (IOException) { } catch (UnauthorizedAccessException) { }
            }
        }
        Pause();
        return result;
    }

    static void Banner()
    {
        Write("============================================================", ConsoleColor.Cyan);
        Write("  SCAM WITH YOUR FRIENDS - OLLAMA MOD SETUP 1.0.2", ConsoleColor.Cyan);
        Write("============================================================", ConsoleColor.Cyan);
        Write("Adds Ollama AI (local or Tailscale) to Settings > Host > AI Backend.");
        Write("Supports game build 0.1.71-swyf (Windows x64 Mono).");
        Write("Original game files are backed up for removal later.");
        Write("");
    }
    static void Write(string text, ConsoleColor? color = null)
    {
        lock (OutputLock)
        {
        ConsoleColor original = Console.ForegroundColor;
        if (color.HasValue) Console.ForegroundColor = color.Value;
        Console.WriteLine(text);
        Console.ForegroundColor = original;
        if (logPath != null) File.AppendAllText(logPath, text + Environment.NewLine, Encoding.UTF8);
        }
    }
    static void Pause()
    {
        if (!noPause) { Console.WriteLine("\nPress Enter to close this window."); Console.ReadLine(); }
    }
    static string NormalizeGame(string path)
    {
        path = path.Trim().Trim('"');
        if (File.Exists(path) && string.Equals(Path.GetFileName(path), GameExe, StringComparison.OrdinalIgnoreCase)) path = Path.GetDirectoryName(path);
        return Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
    }
    static bool IsGame(string path)
    { return File.Exists(Path.Combine(path, GameExe)) && Directory.Exists(Path.Combine(path, ManagedRelative)); }

    internal static IEnumerable<string> LibraryPaths(string vdf)
    {
        foreach (Match match in Regex.Matches(vdf, "\"path\"\\s*\"((?:\\\\.|[^\"])*)\"", RegexOptions.IgnoreCase))
            yield return match.Groups[1].Value.Replace("\\\\", "\\").Replace("\\\"", "\"");
    }
    static IEnumerable<string> DiscoverGames()
    {
        var steamRoots = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var item in new[] {
            new[] { @"HKEY_CURRENT_USER\Software\Valve\Steam", "SteamPath" },
            new[] { @"HKEY_LOCAL_MACHINE\SOFTWARE\WOW6432Node\Valve\Steam", "InstallPath" },
            new[] { @"HKEY_LOCAL_MACHINE\SOFTWARE\Valve\Steam", "InstallPath" }
        })
        {
            try { var path = Registry.GetValue(item[0], item[1], null) as string; if (!string.IsNullOrWhiteSpace(path)) steamRoots.Add(path); }
            catch (System.Security.SecurityException) { }
        }
        steamRoots.Add(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "Steam"));
        var libraries = new HashSet<string>(steamRoots, StringComparer.OrdinalIgnoreCase);
        foreach (string steam in steamRoots)
        {
            string vdf = Path.Combine(steam, "steamapps", "libraryfolders.vdf");
            if (File.Exists(vdf)) foreach (string path in LibraryPaths(File.ReadAllText(vdf))) libraries.Add(path);
        }
        var result = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (string library in libraries)
        {
            string folder = "Scam With Your Friends";
            string manifest = Path.Combine(library, "steamapps", "appmanifest_4954910.acf");
            if (File.Exists(manifest))
            {
                Match match = Regex.Match(File.ReadAllText(manifest), "\"installdir\"\\s*\"([^\"]+)\"", RegexOptions.IgnoreCase);
                if (match.Success && Path.GetFileName(match.Groups[1].Value) == match.Groups[1].Value) folder = match.Groups[1].Value;
            }
            string candidate = Path.Combine(library, "steamapps", "common", folder);
            if (IsGame(candidate)) result.Add(NormalizeGame(candidate));
        }
        return result;
    }
    static string ChooseGame(string explicitPath)
    {
        if (explicitPath != null)
        {
            string path = NormalizeGame(explicitPath);
            if (!IsGame(path)) throw new DirectoryNotFoundException("That folder does not contain " + GameExe + " and its Managed folder.");
            return path;
        }
        string[] candidates = DiscoverGames().ToArray();
        if (candidates.Length == 1) return candidates[0];
        if (candidates.Length > 1)
        {
            Write("Multiple Steam copies were found:");
            for (int i = 0; i < candidates.Length; i++) Write("  " + (i + 1) + ": " + candidates[i]);
            if (Console.IsInputRedirected) throw new InvalidOperationException("Choose a copy with --game-path.");
            Write("Enter its number, or paste a different game folder:");
            string answer = Console.ReadLine(); int index;
            if (int.TryParse(answer, out index) && index > 0 && index <= candidates.Length) return candidates[index - 1];
            explicitPath = answer;
        }
        else
        {
            Write("Steam did not expose the game folder automatically.");
            Write("In Steam: right-click the game > Manage > Browse local files.");
            Write("Paste that folder's full path below:");
            if (Console.IsInputRedirected) throw new DirectoryNotFoundException("Use --game-path to provide the game folder.");
            explicitPath = Console.ReadLine();
        }
        if (string.IsNullOrWhiteSpace(explicitPath)) throw new OperationCanceledException("No game folder selected.");
        return ChooseGame(explicitPath);
    }
    static bool GameRunning(string game)
    {
        string expected = Path.Combine(game, GameExe);
        foreach (Process process in Process.GetProcessesByName(Path.GetFileNameWithoutExtension(GameExe)))
        using (process)
        {
            try { if (string.Equals(process.MainModule.FileName, expected, StringComparison.OrdinalIgnoreCase)) return true; }
            catch (InvalidOperationException) { }
            catch (System.ComponentModel.Win32Exception) { return true; }
        }
        return false;
    }
    static void WaitForGameClosed(string game)
    {
        while (GameRunning(game))
        {
            Write("The game is open. Save/finish your session and close it.", ConsoleColor.Yellow);
            if (Console.IsInputRedirected) throw new InvalidOperationException("Close the running game before installing or removing the mod.");
            Write("Press Enter after closing it, or type Q to cancel:");
            if (string.Equals(Console.ReadLine(), "q", StringComparison.OrdinalIgnoreCase)) throw new OperationCanceledException("Canceled. Game files were not changed.");
        }
    }
    static string Hash(string path)
    { using (var sha = SHA256.Create()) using (var stream = File.OpenRead(path)) return BitConverter.ToString(sha.ComputeHash(stream)).Replace("-", ""); }
    static Dictionary<string, object> ReadRecord(string path)
    { return File.Exists(path) ? Json.Deserialize<Dictionary<string, object>>(File.ReadAllText(path)) : null; }
    static string NestedHash(Dictionary<string, object> record, string group, string key)
    {
        object value, child;
        if (record == null || !record.TryGetValue(group, out value)) return null;
        var map = value as Dictionary<string, object>;
        return map != null && map.TryGetValue(key, out child) ? child as string : null;
    }
    static string Value(Dictionary<string, object> record, string key)
    { object value; return record != null && record.TryGetValue(key, out value) ? value as string : null; }

    static Dictionary<string, object> Validate(string game, bool uninstall)
    {
        string managed = Path.Combine(game, ManagedRelative), root = Path.Combine(game, "ScamOllama");
        var record = ReadRecord(Path.Combine(root, "install.json"));
        if (uninstall && record == null) throw new InvalidOperationException("No direct Ollama mod installation record was found.");
        bool originalsInstalled = Originals.All(p => Hash(Path.Combine(managed, p.Key)) == p.Value);
        bool currentModInstalled = record != null && Originals.All(p =>
            NestedHash(record, "originalHashes", p.Key) == p.Value &&
            Hash(Path.Combine(managed, p.Key)) == NestedHash(record, "patchedHashes", p.Key));
        if (uninstall ? !currentModInstalled : !originalsInstalled && !currentModInstalled)
            throw new InvalidOperationException("This game build is not supported, or its files were modified. Update the game and use the matching mod installer.");
        foreach (var original in Originals)
        {
            string actual = Hash(Path.Combine(managed, original.Key));
            string expectedPatched = NestedHash(record, "patchedHashes", original.Key);
            if (uninstall ? actual != expectedPatched : actual != original.Value && actual != expectedPatched)
                throw new InvalidOperationException(original.Key + " is a different game build or has an unknown modification. Setup will not overwrite it.");
            string saved = Path.Combine(root, "originals", original.Key);
            if (File.Exists(saved))
            {
                string savedHash = Hash(saved);
                bool recordedOldBackup = originalsInstalled && savedHash == NestedHash(record, "originalHashes", original.Key);
                if (savedHash != original.Value && !recordedOldBackup)
                    throw new IOException("The original backup has an unexpected hash: " + original.Key);
            }
            else if (actual != original.Value) throw new IOException("An original backup is missing: " + original.Key);
        }
        string plugin = Path.Combine(managed, "ScamOllama.dll");
        if (File.Exists(plugin) && Hash(plugin) != PluginHash && Hash(plugin) != Value(record, "pluginHash"))
            throw new IOException("An unrecognized ScamOllama.dll is already present. It was left untouched.");
        return record;
    }
    static bool AlreadyInstalled(string game, Dictionary<string, object> record)
    {
        if (record == null || Value(record, "pluginHash") != PluginHash) return false;
        string managed = Path.Combine(game, ManagedRelative), plugin = Path.Combine(managed, "ScamOllama.dll");
        return File.Exists(plugin) && Hash(plugin) == PluginHash && Originals.All(p => Hash(Path.Combine(managed, p.Key)) == NestedHash(record, "patchedHashes", p.Key));
    }
    static void PrepareTemporaryDirectory()
    {
        temporaryDirectory = Path.Combine(Path.GetTempPath(), "ScamOllamaSetup", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(temporaryDirectory);
        logPath = Path.Combine(temporaryDirectory, "setup.log");
        Write("       Preparing installer files in a temporary folder.");
    }
    static string VerifyEmbeddedPayload()
    {
        using (var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("ScamOllama.Payload"))
        {
            if (stream == null) throw new IOException("The embedded mod package is missing.");
            using (var zip = new ZipArchive(stream, ZipArchiveMode.Read))
            {
                var plugin = zip.GetEntry("payload/ScamOllama.dll");
                if (plugin == null || zip.GetEntry("tools/Patcher.exe") == null || zip.GetEntry("tools/Mono.Cecil.dll") == null) throw new IOException("The embedded mod package is incomplete.");
                using (var sha = SHA256.Create()) using (var input = plugin.Open())
                    if (BitConverter.ToString(sha.ComputeHash(input)).Replace("-", "") != PluginHash) throw new IOException("The embedded mod hash is invalid.");
                return "verified (" + zip.Entries.Count + " files)";
            }
        }
    }
    static void ExtractPayload(string directory)
    {
        VerifyEmbeddedPayload();
        string root = Path.GetFullPath(directory).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        using (var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("ScamOllama.Payload"))
        using (var zip = new ZipArchive(stream, ZipArchiveMode.Read))
        foreach (var entry in zip.Entries)
        {
            string target = Path.GetFullPath(Path.Combine(root, entry.FullName.Replace('/', Path.DirectorySeparatorChar)));
            if (!target.StartsWith(root, StringComparison.OrdinalIgnoreCase)) throw new IOException("Invalid embedded archive path.");
            if (entry.Name.Length == 0) { Directory.CreateDirectory(target); continue; }
            Directory.CreateDirectory(Path.GetDirectoryName(target));
            using (var input = entry.Open()) using (var output = File.Create(target)) input.CopyTo(output);
        }
    }
    internal static string QuoteArgument(string value)
    {
        if (value.IndexOf('\0') >= 0) throw new ArgumentException("A path contains a null character.");
        return "\"" + Regex.Replace(value, @"(\\*)\""", "$1$1\\\"") + new string('\\', value.Reverse().TakeWhile(c => c == '\\').Count()) + "\"";
    }
    static void RunPatcher(string originals, string plugin, string stage)
    {
        var info = new ProcessStartInfo(Path.Combine(temporaryDirectory, "tools", "Patcher.exe"), string.Join(" ", new[] { originals, plugin, stage }.Select(QuoteArgument)))
        { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true, WorkingDirectory = temporaryDirectory };
        using (var process = new Process { StartInfo = info })
        {
            process.OutputDataReceived += (s, e) => { if (e.Data != null) Write("       " + e.Data); };
            process.ErrorDataReceived += (s, e) => { if (e.Data != null) Write("       " + e.Data); };
            process.Start(); process.BeginOutputReadLine(); process.BeginErrorReadLine(); process.WaitForExit();
            if (process.ExitCode != 0) throw new IOException("Patch preparation failed; installed game DLLs were not changed.");
        }
    }
    static void ReplaceFile(string source, string target)
    {
        string pending = target + ".ScamOllama-new";
        try
        {
            File.Copy(source, pending, true);
            if (File.Exists(target)) File.Replace(pending, target, null); else File.Move(pending, target);
        }
        finally { if (File.Exists(pending)) File.Delete(pending); }
    }
    static void Install(string game, Dictionary<string, object> record)
    {
        string root = Path.Combine(game, "ScamOllama"), managed = Path.Combine(game, ManagedRelative);
        string backup = Path.Combine(root, "originals"), stage = Path.Combine(temporaryDirectory, "patched");
        Directory.CreateDirectory(backup);
        bool outdatedBackups = Originals.Any(p => File.Exists(Path.Combine(backup, p.Key)) && Hash(Path.Combine(backup, p.Key)) != p.Value);
        if (outdatedBackups)
        {
            // Validate has established that the current files are pristine and
            // each stale backup belongs to the prior recorded installation.
            string archive = Path.Combine(root, "previous-installs", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(archive);
            foreach (string name in Originals.Keys)
            {
                string saved = Path.Combine(backup, name);
                if (File.Exists(saved)) File.Copy(saved, Path.Combine(archive, name));
            }
            string oldRecord = Path.Combine(root, "install.json");
            if (File.Exists(oldRecord)) File.Copy(oldRecord, Path.Combine(archive, "install.json"));
            Write("       Game update detected. Preserved the previous build's backups.");
        }
        foreach (var original in Originals)
        {
            string saved = Path.Combine(backup, original.Key);
            if (!File.Exists(saved) || Hash(saved) != original.Value)
            {
                string current = Path.Combine(managed, original.Key);
                if (Hash(current) != original.Value) throw new IOException("Cannot back up modified game files: " + original.Key);
                File.Copy(current, saved, true);
            }
            Write("       Verified original backup: " + original.Key);
        }
        string plugin = Path.Combine(temporaryDirectory, "payload", "ScamOllama.dll");
        RunPatcher(backup, plugin, stage);
        if (GameRunning(game)) throw new IOException("The game opened during preparation. Close it and run setup again.");
        DisableLegacyLoader(game, root);
        string[] names = Originals.Keys.Concat(new[] { "ScamOllama.dll" }).ToArray();
        var previous = names.ToDictionary(n => n, n => File.Exists(Path.Combine(managed, n)) ? File.ReadAllBytes(Path.Combine(managed, n)) : null);
        try
        {
            ReplaceFile(plugin, Path.Combine(managed, "ScamOllama.dll"));
            foreach (string name in Originals.Keys) { ReplaceFile(Path.Combine(stage, name), Path.Combine(managed, name)); Write("       Installed: " + name); }
            var hashes = Originals.Keys.ToDictionary(n => n, n => Hash(Path.Combine(managed, n)));
            var installation = new Dictionary<string, object> { { "version", "1.0.2" }, { "originalHashes", Originals }, { "patchedHashes", hashes }, { "pluginHash", PluginHash }, { "installedAt", DateTime.UtcNow.ToString("o") } };
            File.WriteAllText(Path.Combine(root, "install.json"), Json.Serialize(installation), Encoding.UTF8);
        }
        catch
        {
            Write("       Installation did not finish. Restoring the previous game files...", ConsoleColor.Yellow);
            foreach (var old in previous) { string target = Path.Combine(managed, old.Key); if (old.Value != null) File.WriteAllBytes(target, old.Value); else if (File.Exists(target)) File.Delete(target); }
            throw;
        }
        File.WriteAllText(Path.Combine(root, "HOW-TO-USE.txt"), InstructionText(), Encoding.UTF8);
        File.Copy(Path.Combine(temporaryDirectory, "Mono.Cecil-LICENSE.txt"), Path.Combine(root, "Mono.Cecil-LICENSE.txt"), true);
    }
    static void DisableLegacyLoader(string game, string root)
    {
        string marker = Path.Combine(game, "BepInEx", "ScamOllama-install.json");
        if (!File.Exists(marker)) return;
        var legacy = ReadRecord(marker);
        string old = Path.Combine(game, "BepInEx", "plugins", "ScamOllama", "ScamOllama.dll");
        if (File.Exists(old))
        {
            if (Hash(old) != NestedHash(legacy, "hashes", @"BepInEx\plugins\ScamOllama\ScamOllama.dll")) throw new IOException("The legacy Ollama plugin was modified. Disable it before installing the direct mod.");
            string saved = Path.GetFullPath(Path.Combine(root, "legacy-ScamOllama.dll.disabled"));
            if (!saved.StartsWith(Path.GetFullPath(game) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)) throw new IOException("Invalid legacy backup path.");
            File.Copy(old, saved, true); File.Delete(old);
        }
        string path = Path.Combine(game, "doorstop_config.ini");
        if (!File.Exists(path)) return;
        string config = File.ReadAllText(path);
        File.WriteAllText(Path.Combine(root, "legacy-doorstop-config.ini"), config, Encoding.UTF8);
        config = Regex.Replace(config, @"(?m)^enabled\s*=\s*true\s*$", "enabled = false");
        config = Regex.Replace(config, @"(?m)^dll_search_path_override\s*=[^\r\n]*", "dll_search_path_override =");
        File.WriteAllText(path, config, Encoding.UTF8);
        Write("       Disabled the incompatible legacy Ollama loader.");
    }
    static void Uninstall(string game)
    {
        var record = Validate(game, uninstall: true);
        string root = Path.Combine(game, "ScamOllama"), managed = Path.Combine(game, ManagedRelative);
        foreach (string name in Originals.Keys) { ReplaceFile(Path.Combine(root, "originals", name), Path.Combine(managed, name)); Write("       Restored " + name); }
        string plugin = Path.Combine(managed, "ScamOllama.dll");
        if (File.Exists(plugin) && Hash(plugin) == Value(record, "pluginHash")) File.Delete(plugin);
        File.Copy(Path.Combine(root, "install.json"), Path.Combine(root, "last-install.json"), true);
        File.Delete(Path.Combine(root, "install.json"));
    }
    static void OllamaStatus()
    {
        try
        {
            var request = (HttpWebRequest)WebRequest.Create("http://127.0.0.1:11434/api/tags");
            request.Proxy = null; request.AllowAutoRedirect = false; request.Timeout = 2500; request.ReadWriteTimeout = 2500;
            using (var response = request.GetResponse()) using (var reader = new StreamReader(response.GetResponseStream()))
            {
                var data = Json.Deserialize<Dictionary<string, object>>(reader.ReadToEnd());
                var models = data.ContainsKey("models") ? data["models"] as System.Collections.IEnumerable : null;
                var names = new List<string>();
                foreach (var entry in models ?? new object[0])
                {
                    var item = entry as Dictionary<string, object>;
                    string name = Value(item, "name") ?? Value(item, "model");
                    if (name != null && !name.EndsWith(":cloud", StringComparison.OrdinalIgnoreCase) && Value(item, "remote_host") == null && Value(item, "remote_model") == null) names.Add(name);
                }
                Write("       Ollama is running. Local models: " + (names.Count == 0 ? "none yet" : string.Join(", ", names)));
            }
        }
        catch (Exception e) when (e is WebException || e is IOException || e is InvalidOperationException || e is ArgumentException)
        { Write("       Ollama is not responding on the default local address. The mod can still be installed; follow the steps below."); }
    }
    static string InstructionText()
    {
        return @"INSTALL OLLAMA AND A MODEL
1. Download Ollama for Windows: https://ollama.com/download/windows
2. Run its installer and start Ollama. It normally runs in the background.
3. Open a NEW Terminal or PowerShell window after installation.
4. Download the model that passed this mod's local test:

       ollama pull qwen2.5:7b

   This is a download of about 4.7 GB. No model is bundled with this mod.
   To use another local chat model: ollama pull MODEL_NAME
   Browse model names at https://ollama.com/search
5. See installed models with: ollama list
   If Ollama is not running, start its app, or run: ollama serve

USE THE MOD IN THE GAME
1. Start the game and host a lobby.
2. Open Settings > Host > AI Backend.
3. Select Ollama (Local / Tailscale).
4. Use http://127.0.0.1:11434 for this PC, or the GPU server's Tailscale URL.
5. Click Refresh models, select your model, then Test local AI.
6. Start with context 16384 and maximum output 2048 tokens.
   Scroll down for sampling controls, thinking, timeout and other options.
   Settings save automatically. The first response can take time to load.

The 256K setting requires a model and hardware that support that context.
Only local chat/text models are suitable. Cloud models are excluded.
The selected Ollama server runs inference on its GPU. Live multiplayer remains unverified.

TAILSCALE SERVER SETUP (OPTIONAL)
Both PCs need Tailscale access to the GPU server.
On that server, keep Ollama running and run:
       tailscale serve --bg --tcp=11434 tcp://127.0.0.1:11434
On the game host PC, enter http://<server-tailscale-ip>:11434 in Ollama address.
Click Refresh models. The URL now saves immediately and stays as entered.

REMOVE THE MOD
Close the game, then run this same installer from a terminal with:
       setup.exe --uninstall
It checks and restores your original game DLL backups.
Steam verification or a game update can remove the patch.
Only game build 0.1.71-swyf is supported by this installer.

HELP AND SOURCES
https://docs.ollama.com/windows
https://docs.ollama.com/cli
https://ollama.com/library/qwen2.5:7b
";
    }
    static void Instructions()
    { Write(""); Write("=================== NEXT STEPS ===================", ConsoleColor.Green); Write(InstructionText()); }
    static void Help()
    {
        Banner();
        Write("Double-click this EXE to install the mod and see setup instructions.");
        Write("No companion ZIP or PowerShell script is required.");
        Write("--game-path <folder>  Choose a game folder manually.");
        Write("--check               Check detection/compatibility without installing.");
        Write("--instructions        Show Ollama, model and in-game instructions only.");
        Write("--uninstall           Restore the original game DLLs.");
        Write("--no-pause            Exit without waiting for Enter.");
    }
}
