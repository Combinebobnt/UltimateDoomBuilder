#region ================== Namespaces

using System;
using System.Collections;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using CodeImp.DoomBuilder.IO;

#endregion

namespace CodeImp.DoomBuilder.Config
{
	internal enum SettingsGroup
	{
		Preferences,
		Colors,
		Shortcuts,
		GameConfigurations,
		Plugins,
		WindowLayout
	}

	// One replaceable piece of the settings tree: an import swaps it wholesale
	internal sealed class SettingsUnit
	{
		public readonly SettingsGroup Group;
		public readonly string Path;
		public readonly string Name;

		public SettingsUnit(SettingsGroup group, string path, string name)
		{
			Group = group;
			Path = path;
			Name = name;
		}

		public override string ToString() { return Path; }
	}

	// The export header of a settings file; a raw UDBuilder.cfg has none
	internal sealed class SettingsFileInfo
	{
		public bool IsExport;
		public int FormatVersion;
		public string Program;
		public string Platform;
		public string HomePath;
		public string AppPath;
	}

	// A settings path value found in the units being imported
	internal sealed class SettingsPathEntry
	{
		public readonly string Key;
		public readonly string Value;
		public readonly string Folder;
		public readonly string Leaf;

		public SettingsPathEntry(string key, string value)
		{
			Key = key;
			Value = value;
			SettingsTransfer.SplitForeignPath(value, out Folder, out Leaf);
		}
	}

	internal enum SettingsImportState { None, Applied, Failed }

	internal sealed class SettingsImportResult
	{
		public SettingsImportState State;
		public string Message;
		public int UnitCount;
		public string Groups;

		public static readonly SettingsImportResult None = new SettingsImportResult { State = SettingsImportState.None };
	}

	internal static class SettingsTransfer
	{
		#region ================== Constants

		internal const string HEADER = "udbsettingsexport";
		internal const int FORMAT_VERSION = 1;
		internal const string FILE_EXTENSION = "udbsettings";
		internal const string SETTINGS_FILE = "UDBuilder.cfg";
		internal const string PENDING_FILE = "UDBuilder.import-pending.cfg";
		internal const string FAILED_FILE = "UDBuilder.import-failed.cfg";
		internal const string BACKUP_FILE = "UDBuilder.before-import.cfg";
		internal const string PLATFORM_WINDOWS = "windows";
		internal const string PLATFORM_LINUX = "linux";
		private const string SHORTCUTS = "shortcuts";
		private const string SHORTCUTS_ALT = "shortcutsalt";

		// Machine or session state, never exported or imported
		internal static readonly string[] EXCLUDED = {
			HEADER, "currentversion", "lastusedmapfolder", "lastusedconfigname", "recentfiles",
			"ignoredremoterevision", "ignoredforkrelease", InteractionLog.SETTING,
			"colordialogcustomcolors", "lightmapbaker.cost"
		};

		// Skipped when the file comes from another platform
		internal static readonly string[] PLATFORM_KEYS = { "nativefiledialogs", "visualframechain", "visualminframetime" };

		// Absolute script paths and path hashes, only valid on the install that wrote them
		internal static readonly string[] UDBSCRIPT_KEYS = {
			"plugins.udbscript.scriptslots", "plugins.udbscript.scripts", "plugins.udbscript.directoryexpand"
		};

		private static readonly string[] WINDOW_KEYS = { "mainwindow", "windows", "browserwindow", "scriptspanel", "dockerswidth" };

		// Top-level keys whose sub-keys are the units
		private static readonly Dictionary<string, SettingsGroup> PER_SUBKEY = new Dictionary<string, SettingsGroup>(StringComparer.Ordinal) {
			{ "colors", SettingsGroup.Colors },
			{ SHORTCUTS, SettingsGroup.Shortcuts },
			{ SHORTCUTS_ALT, SettingsGroup.Shortcuts },
			{ "configurations", SettingsGroup.GameConfigurations },
			{ "plugins", SettingsGroup.Plugins },
			{ "windows", SettingsGroup.WindowLayout },
			{ "lightmapbaker", SettingsGroup.Preferences },
		};

		// Settings holding file or folder paths, "*" matching one key
		internal static readonly string[] PATH_PATTERNS = {
			"configurations.*.resources.*.location", "configurations.*.engines.*.testprogram",
			"configurations.*.testprogram", "lightmapbaker.path", "screenshotspath", "compileroverrides.*.program"
		};

		internal const string SCREENSHOTS_KEY = "screenshotspath";

		private static readonly Regex WINDOWS_PATH = new Regex(@"^([A-Za-z]:[\\/]|\\\\)", RegexOptions.Compiled);

		#endregion

		#region ================== Properties

		internal static string LocalPlatform { get { return (Path.DirectorySeparatorChar == '\\') ? PLATFORM_WINDOWS : PLATFORM_LINUX; } }

		#endregion

		#region ================== Groups and units

		internal static string GroupTitle(SettingsGroup group)
		{
			switch(group)
			{
				case SettingsGroup.Preferences: return "Preferences";
				case SettingsGroup.Colors: return "Colors";
				case SettingsGroup.Shortcuts: return "Keyboard shortcuts";
				case SettingsGroup.GameConfigurations: return "Game configurations";
				case SettingsGroup.Plugins: return "Plugin settings";
				case SettingsGroup.WindowLayout: return "Window layout";
				default: return group.ToString();
			}
		}

		internal static string GroupKey(SettingsGroup group)
		{
			switch(group)
			{
				case SettingsGroup.GameConfigurations: return "gameconfigs";
				case SettingsGroup.WindowLayout: return "windowlayout";
				default: return group.ToString().ToLowerInvariant();
			}
		}

		internal static bool DefaultSelected(SettingsGroup group) { return group != SettingsGroup.WindowLayout; }

		internal static bool IsExcluded(string path) { return Array.IndexOf(EXCLUDED, path) != -1; }

		// Every unit present in the tree, in tree order, excluded keys left out
		internal static List<SettingsUnit> EnumerateUnits(Configuration cfg)
		{
			List<SettingsUnit> units = new List<SettingsUnit>();
			HashSet<string> seen = new HashSet<string>(StringComparer.Ordinal);
			foreach(DictionaryEntry e in cfg.Root)
			{
				string key = e.Key.ToString();
				if(IsExcluded(key)) continue;

				SettingsGroup group;
				if(PER_SUBKEY.TryGetValue(key, out group) && (e.Value is IDictionary))
				{
					// Both shortcut trees share one unit per action
					string prefix = (key == SHORTCUTS_ALT) ? SHORTCUTS : key;
					foreach(DictionaryEntry se in (IDictionary)e.Value)
					{
						string path = prefix + "." + se.Key;
						if(IsExcluded(key + "." + se.Key) || !seen.Add(path)) continue;
						units.Add(new SettingsUnit(group, path, se.Key.ToString()));
					}
				}
				else if(seen.Add(key))
				{
					group = (Array.IndexOf(WINDOW_KEYS, key) != -1) ? SettingsGroup.WindowLayout : SettingsGroup.Preferences;
					units.Add(new SettingsUnit(group, key, key));
				}
			}
			return units;
		}

		#endregion

		#region ================== Tree helpers

		internal static object Clone(object value)
		{
			IDictionary dict = value as IDictionary;
			if(dict == null) return value;
			IDictionary copy = new ListDictionary();
			foreach(DictionaryEntry e in dict) copy.Add(e.Key, Clone(e.Value));
			return copy;
		}

		internal static Configuration CloneConfiguration(Configuration cfg)
		{
			Configuration copy = new Configuration(true);
			copy.Root = (IDictionary)Clone(cfg.Root);
			return copy;
		}

		// DeleteSetting creates missing parents, so this checks first
		private static void DeleteIfExists(Configuration cfg, string path)
		{
			if(cfg.SettingExists(path)) cfg.DeleteSetting(path);
		}

		private static void CopySetting(Configuration from, Configuration to, string path)
		{
			if(!from.SettingExists(path)) return;
			if(!to.WriteSetting(path, Clone(from.ReadSettingObject(path, null))))
				throw new InvalidDataException("Could not write setting \"" + path + "\".");
		}

		// Copies a unit; a shortcut unit carries its alternate key along
		private static void CopyUnit(Configuration from, Configuration to, string path)
		{
			CopySetting(from, to, path);
			string alt = AlternateShortcutPath(path);
			if(alt != null) CopySetting(from, to, alt);
		}

		private static string AlternateShortcutPath(string unitpath)
		{
			return unitpath.StartsWith(SHORTCUTS + ".", StringComparison.Ordinal) ? SHORTCUTS_ALT + unitpath.Substring(SHORTCUTS.Length) : null;
		}

		private static string[] SplitList(string list)
		{
			return string.IsNullOrEmpty(list) ? new string[0] : list.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries);
		}

		private static void RemoveEmptySection(Configuration cfg, string path)
		{
			IDictionary d = cfg.SettingExists(path) ? cfg.ReadSettingObject(path, null) as IDictionary : null;
			if(d != null && d.Count == 0) cfg.DeleteSetting(path);
		}

		#endregion

		#region ================== Header

		internal static SettingsFileInfo ReadHeader(Configuration cfg)
		{
			SettingsFileInfo info = new SettingsFileInfo();
			info.IsExport = cfg.SettingExists(HEADER) && (cfg.ReadSettingObject(HEADER, null) is IDictionary);
			if(info.IsExport)
			{
				info.FormatVersion = cfg.ReadSetting(HEADER + ".formatversion", 0);
				info.Program = cfg.ReadSetting(HEADER + ".program", "");
				info.Platform = cfg.ReadSetting(HEADER + ".platform", "");
				info.HomePath = cfg.ReadSetting(HEADER + ".homepath", "");
				info.AppPath = cfg.ReadSetting(HEADER + ".apppath", "");
			}
			else
			{
				info.Platform = GuessPlatform(cfg);
			}
			return info;
		}

		// Null when the file can be imported, else the reason it can't
		internal static string CheckImportable(SettingsFileInfo info)
		{
			if(info.IsExport && info.FormatVersion > FORMAT_VERSION)
				return "This settings file was written by a newer version of Ultimate Doom Builder (format version " + info.FormatVersion + ") and can't be imported.";
			return null;
		}

		// A raw settings file says nothing about its platform; its paths usually do
		private static string GuessPlatform(Configuration cfg)
		{
			foreach(KeyValuePair<string, string> p in FindPathValues(cfg))
			{
				if(IsWindowsPath(p.Value)) return PLATFORM_WINDOWS;
				if(p.Value.StartsWith("/", StringComparison.Ordinal)) return PLATFORM_LINUX;
			}
			return "";
		}

		internal static bool IsCrossPlatform(SettingsFileInfo info, string localplatform)
		{
			return !string.IsNullOrEmpty(info.Platform) && !string.Equals(info.Platform, localplatform, StringComparison.OrdinalIgnoreCase);
		}

		// UDBScript's path-keyed settings only carry over onto the same install
		internal static bool KeepsUDBScriptPaths(SettingsFileInfo info, string localapppath)
		{
			return !string.IsNullOrEmpty(info.AppPath) && string.Equals(NormalizeFolder(info.AppPath), NormalizeFolder(localapppath), StringComparison.Ordinal);
		}

		private static string NormalizeFolder(string path)
		{
			return (path ?? "").TrimEnd('/', '\\');
		}

		#endregion

		#region ================== Export

		internal static Configuration BuildExport(Configuration live, ICollection<SettingsGroup> groups, string program, string platform, string homepath, string apppath)
		{
			Configuration export = new Configuration(true);
			List<string> names = new List<string>();
			foreach(SettingsGroup g in Enum.GetValues(typeof(SettingsGroup)))
				if(groups.Contains(g)) names.Add(GroupKey(g));

			export.WriteSetting(HEADER + ".formatversion", FORMAT_VERSION);
			export.WriteSetting(HEADER + ".program", program ?? "");
			export.WriteSetting(HEADER + ".platform", platform ?? "");
			export.WriteSetting(HEADER + ".homepath", homepath ?? "");
			export.WriteSetting(HEADER + ".apppath", apppath ?? "");
			export.WriteSetting(HEADER + ".groups", string.Join(",", names.ToArray()));

			// A fresh tree only gets what is copied in, so no empty sections are left behind
			foreach(SettingsUnit u in EnumerateUnits(live))
				if(groups.Contains(u.Group)) CopyUnit(live, export, u.Path);

			return export;
		}

		internal static void WriteFile(Configuration cfg, string path)
		{
			byte[] data = Encoding.UTF8.GetBytes(cfg.OutputConfiguration("\r\n", true));
			using(SiblingFileSwap swap = new SiblingFileSwap(path))
			{
				File.WriteAllBytes(swap.SiblingPath, data);
				swap.CopyMetadataFromTarget();
				swap.Swap();
			}
		}

		#endregion

		#region ================== Paths

		internal static bool IsWindowsPath(string value)
		{
			if(string.IsNullOrEmpty(value)) return false;
			return WINDOWS_PATH.IsMatch(value) || (value.IndexOf('\\') != -1 && value.IndexOf('/') == -1);
		}

		// Splits on both separators for a Windows path, which Path.GetDirectoryName won't do on Mono
		internal static void SplitForeignPath(string value, out string folder, out string leaf)
		{
			int i = IsWindowsPath(value) ? value.LastIndexOfAny(new[] { '\\', '/' }) : value.LastIndexOf('/');
			if(i < 0)
			{
				folder = "";
				leaf = value;
			}
			else
			{
				folder = (i == 0 || (i == 2 && value[1] == ':')) ? value.Substring(0, i + 1) : value.Substring(0, i);
				leaf = value.Substring(i + 1);
			}
		}

		// Joins a relative path in either separator style onto a local folder
		internal static string JoinLocal(string folder, string relative)
		{
			string[] parts = relative.Split(new[] { '\\', '/' }, StringSplitOptions.RemoveEmptyEntries);
			string result = folder;
			foreach(string p in parts) result = Path.Combine(result, p);
			return result;
		}

		// This machine's home plus what follows the source home in the old folder, or null
		internal static string SuggestFolder(string oldfolder, string sourcehome, string localhome)
		{
			if(string.IsNullOrEmpty(sourcehome) || string.IsNullOrEmpty(localhome) || string.IsNullOrEmpty(oldfolder)) return null;
			string home = NormalizeFolder(sourcehome);
			StringComparison cmp = IsWindowsPath(home) ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
			if(!oldfolder.StartsWith(home, cmp)) return null;
			string rest = oldfolder.Substring(home.Length);
			if(rest.Length > 0 && rest[0] != '/' && rest[0] != '\\') return null;
			return JoinLocal(localhome, rest);
		}

		private static bool MatchesPattern(string[] pattern, List<string> path)
		{
			if(pattern.Length != path.Count) return false;
			for(int i = 0; i < pattern.Length; i++)
				if(pattern[i] != "*" && pattern[i] != path[i]) return false;
			return true;
		}

		private static void CollectPaths(IDictionary dict, List<string> path, List<string[]> patterns, List<KeyValuePair<string, string>> result)
		{
			foreach(DictionaryEntry e in dict)
			{
				path.Add(e.Key.ToString());
				if(e.Value is IDictionary)
				{
					CollectPaths((IDictionary)e.Value, path, patterns, result);
				}
				else if(e.Value is string && ((string)e.Value).Length > 0)
				{
					foreach(string[] p in patterns)
					{
						if(MatchesPattern(p, path))
						{
							result.Add(new KeyValuePair<string, string>(string.Join(".", path.ToArray()), (string)e.Value));
							break;
						}
					}
				}
				path.RemoveAt(path.Count - 1);
			}
		}

		// Every non-empty path setting in the tree, in tree order
		internal static List<KeyValuePair<string, string>> FindPathValues(Configuration cfg)
		{
			List<string[]> patterns = new List<string[]>();
			foreach(string p in PATH_PATTERNS) patterns.Add(p.Split('.'));
			List<KeyValuePair<string, string>> result = new List<KeyValuePair<string, string>>();
			CollectPaths(cfg.Root, new List<string>(), patterns, result);
			return result;
		}

		// Path settings whose file or folder doesn't exist on this machine
		internal static List<SettingsPathEntry> FindMissingPaths(Configuration cfg, Func<string, bool> exists)
		{
			List<SettingsPathEntry> missing = new List<SettingsPathEntry>();
			foreach(KeyValuePair<string, string> p in FindPathValues(cfg))
				if(!exists(p.Value)) missing.Add(new SettingsPathEntry(p.Key, p.Value));
			return missing;
		}

		internal static bool PathExists(string path)
		{
			try { return File.Exists(path) || Directory.Exists(path); }
			catch(Exception) { return false; }
		}

		// The remapped value for a path whose folder moved
		internal static string RemapPath(SettingsPathEntry entry, string newfolder)
		{
			return JoinLocal(newfolder, entry.Leaf);
		}

		#endregion

		#region ================== Import

		// Selected units from the file, filtered for this machine, plus the header apply needs
		internal static Configuration BuildPending(Configuration source, ICollection<string> unitpaths, SettingsFileInfo info, string localplatform, string localapppath)
		{
			Configuration pending = new Configuration(true);
			bool crossplatform = IsCrossPlatform(info, localplatform);
			List<string> units = new List<string>();
			List<string> preserve = new List<string>();
			List<string> groups = new List<string>();

			foreach(SettingsUnit u in EnumerateUnits(source))
			{
				if(!unitpaths.Contains(u.Path)) continue;
				if(crossplatform && Array.IndexOf(PLATFORM_KEYS, u.Path) != -1) continue;
				CopyUnit(source, pending, u.Path);
				units.Add(u.Path);
				string gk = GroupKey(u.Group);
				if(!groups.Contains(gk)) groups.Add(gk);
			}

			if(!KeepsUDBScriptPaths(info, localapppath))
			{
				foreach(string k in UDBSCRIPT_KEYS)
				{
					string unit = k.Substring(0, k.LastIndexOf('.'));
					if(!units.Contains(unit)) continue;
					DeleteIfExists(pending, k);
					preserve.Add(k);
				}
			}

			pending.WriteSetting(HEADER + ".formatversion", FORMAT_VERSION);
			pending.WriteSetting(HEADER + ".program", info.Program ?? "");
			pending.WriteSetting(HEADER + ".platform", info.Platform ?? "");
			pending.WriteSetting(HEADER + ".groups", string.Join(",", groups.ToArray()));
			pending.WriteSetting(HEADER + ".units", string.Join(",", units.ToArray()));
			pending.WriteSetting(HEADER + ".preserve", string.Join(",", preserve.ToArray()));
			return pending;
		}

		// Replaces each listed unit of the target with the pending one; returns the unit count
		internal static int Merge(Configuration target, Configuration pending)
		{
			string[] units = SplitList(pending.ReadSetting(HEADER + ".units", ""));
			string[] preserve = SplitList(pending.ReadSetting(HEADER + ".preserve", ""));
			int count = 0;

			foreach(string unit in units)
			{
				if(IsExcluded(unit) || IsExcluded(unit.Split('.')[0])) continue;

				// Keep the target's values under preserved keys inside this unit
				Dictionary<string, object> kept = new Dictionary<string, object>(StringComparer.Ordinal);
				foreach(string p in preserve)
					if(p.StartsWith(unit + ".", StringComparison.Ordinal) && target.SettingExists(p))
						kept[p] = Clone(target.ReadSettingObject(p, null));

				if(pending.SettingExists(unit))
				{
					// WriteSetting replaces in place, so the key keeps its position
					if(!target.WriteSetting(unit, Clone(pending.ReadSettingObject(unit, null))))
						throw new InvalidDataException("Could not write setting \"" + unit + "\".");
				}
				else
				{
					DeleteIfExists(target, unit);
				}

				string alt = AlternateShortcutPath(unit);
				if(alt != null)
				{
					if(pending.SettingExists(alt)) CopySetting(pending, target, alt);
					else DeleteIfExists(target, alt);
				}

				foreach(KeyValuePair<string, object> k in kept)
					if(!target.WriteSetting(k.Key, k.Value))
						throw new InvalidDataException("Could not write setting \"" + k.Key + "\".");

				count++;
			}

			RemoveEmptySection(target, SHORTCUTS_ALT);
			return count;
		}

		// Applies a pending import to the loaded settings and the settings file; never throws
		internal static SettingsImportResult ApplyPending(ref Configuration cfg, string settingsdir)
		{
			string pendingfile = Path.Combine(settingsdir, PENDING_FILE);
			if(!File.Exists(pendingfile)) return SettingsImportResult.None;

			string settingsfile = Path.Combine(settingsdir, SETTINGS_FILE);
			try
			{
				Configuration pending = new Configuration(true);
				if(!pending.LoadConfiguration(pendingfile, true))
					throw new InvalidDataException("Error in the pending settings import near line " + pending.ErrorLine + ": " + pending.ErrorDescription);
				string error = CheckImportable(ReadHeader(pending));
				if(error != null) throw new InvalidDataException(error);

				if(File.Exists(settingsfile)) File.Copy(settingsfile, Path.Combine(settingsdir, BACKUP_FILE), true);

				Configuration merged = CloneConfiguration(cfg);
				int count = Merge(merged, pending);
				WriteFile(merged, settingsfile);
				cfg = merged;
				File.Delete(pendingfile);

				string groups = pending.ReadSetting(HEADER + ".groups", "");
				General.WriteLogLine("Settings import applied: " + groups + ", " + count + " units");
				return new SettingsImportResult { State = SettingsImportState.Applied, UnitCount = count, Groups = groups };
			}
			catch(Exception e)
			{
				General.WriteLogLine("Settings import failed: " + e.GetType().Name + ": " + e.Message);
				string failedfile = Path.Combine(settingsdir, FAILED_FILE);
				try
				{
					if(File.Exists(failedfile)) File.Delete(failedfile);
					File.Move(pendingfile, failedfile);
				}
				catch(Exception) { }
				return new SettingsImportResult {
					State = SettingsImportState.Failed,
					Message = "Importing settings failed: " + e.Message + "\n\nYour settings were not changed. The import file was kept as \"" + failedfile + "\"."
				};
			}
		}

		#endregion
	}
}
