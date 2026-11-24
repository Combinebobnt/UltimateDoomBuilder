using CodeImp.DoomBuilder.Config;
using CodeImp.DoomBuilder.IO;
using System.Collections;

namespace Builder.Tests
{
	[Collection("GeneralStatics")]
	public class SettingsTransferTests
	{
		private const string FIXTURE = @"
currentversion = 4000;
lastusedconfigname = ""Doom 2"";
lastusedmapfolder = ""C:\\Maps"";
recentfiles { file0 = ""C:\\Maps\\a.wad""; }
interactionlog = false;
blackbrowsers = true;
nativefiledialogs = true;
screenshotspath = ""C:\\UDB\\Screenshots"";
dockerswidth = 300;
lightmapbaker { path = ""C:\\Tools\\zdray.exe""; cost = 1.5; samples = 8; }
colors { background = -16777216; vertices = -1; }
shortcuts { builder_preferences = 123; builder_newmap = 131150; }
shortcutsalt { builder_newmap = 78; }
configurations
{
	gzdoom_doomudmf
	{
		enabled = true;
		resources
		{
			resource0 { location = ""C:\\Games\\Doom \""x\""\\doom2.wad""; type = 0; }
			resource1 { location = ""C:\\Games\\mods""; type = 1; }
		}
		engines { engine0 { testprogram = ""C:\\Games\\gzdoom.exe""; testparameters = ""-iwad C:\\x.wad""; } }
		thingsfilters { filter0 { name = ""All""; } }
	}
	boom_doomdoom { enabled = false; testprogram = ""C:\\Games\\prboom.exe""; }
}
plugins
{
	buildermodes { showvisualthings = 2; }
	udbscript
	{
		scriptslots { slot1 = ""C:\\UDB\\UDBScript\\Scripts\\a.js""; }
		scripts { abc { x = 1; } }
		directoryexpand { d = true; }
		lastscript = 3;
	}
}
windows { preferencesform { positionx = 10; } }
browserwindow { sizewidth = 400; }
mainwindow { positionx = 0; }
";

		private static readonly SettingsGroup[] ALL_GROUPS = (SettingsGroup[])Enum.GetValues(typeof(SettingsGroup));

		private static Configuration Parse(string text)
		{
			Configuration cfg = new Configuration(true);
			Assert.True(cfg.InputConfiguration(text, true), cfg.ErrorDescription + " line " + cfg.ErrorLine);
			return cfg;
		}

		private static Configuration Export(Configuration cfg, params SettingsGroup[] groups)
		{
			return SettingsTransfer.BuildExport(cfg, groups, "R1-test", SettingsTransfer.PLATFORM_WINDOWS, @"C:\Users\x", @"C:\UDB");
		}

		private static List<string> TopKeys(Configuration cfg)
		{
			List<string> keys = new List<string>();
			foreach(DictionaryEntry e in cfg.Root) keys.Add(e.Key.ToString());
			return keys;
		}

		private static List<string> UnitPaths(Configuration cfg)
		{
			return SettingsTransfer.EnumerateUnits(cfg).ConvertAll(u => u.Path);
		}

		private static string Text(Configuration cfg, string path)
		{
			object v = cfg.ReadSettingObject(path, null);
			if(!(v is IDictionary)) return Convert.ToString(v, System.Globalization.CultureInfo.InvariantCulture);
			Configuration c = new Configuration(true);
			c.Root = (IDictionary)v;
			return c.OutputConfiguration("\n", true);
		}

		[Fact]
		public void EachGroupExportsExactlyItsUnits()
		{
			Configuration cfg = Parse(FIXTURE);
			List<SettingsUnit> all = SettingsTransfer.EnumerateUnits(cfg);
			foreach(SettingsGroup g in ALL_GROUPS)
			{
				Configuration export = Export(cfg, g);
				List<string> expected = all.FindAll(u => u.Group == g).ConvertAll(u => u.Path);
				Assert.True(expected.Count > 0, g + " has fixture units");
				Assert.Equal(expected, UnitPaths(export));

				// No empty leftover sections
				foreach(DictionaryEntry e in export.Root)
					if(e.Value is IDictionary) Assert.True(((IDictionary)e.Value).Count > 0, g + ": empty " + e.Key);
			}
		}

		[Fact]
		public void ExcludedKeysNeverExportAndHeaderIsSet()
		{
			Configuration export = Export(Parse(FIXTURE), ALL_GROUPS);
			foreach(string x in SettingsTransfer.EXCLUDED)
				if(x != SettingsTransfer.HEADER) Assert.False(export.SettingExists(x), x);
			Assert.True(export.SettingExists("lightmapbaker.path"));
			Assert.True(export.SettingExists("shortcutsalt.builder_newmap"));

			SettingsFileInfo info = SettingsTransfer.ReadHeader(export);
			Assert.True(info.IsExport);
			Assert.Equal(SettingsTransfer.FORMAT_VERSION, info.FormatVersion);
			Assert.Equal("R1-test", info.Program);
			Assert.Equal(SettingsTransfer.PLATFORM_WINDOWS, info.Platform);
			Assert.Equal(@"C:\Users\x", info.HomePath);
			Assert.Equal(@"C:\UDB", info.AppPath);
			Assert.Equal("preferences,colors,shortcuts,gameconfigs,plugins,windowlayout", export.ReadSetting(SettingsTransfer.HEADER + ".groups", ""));
		}

		[Fact]
		public void ExportSurvivesAWriteAndReadBack()
		{
			Configuration export = Export(Parse(FIXTURE), ALL_GROUPS);
			string path = Path.Combine(Path.GetTempPath(), "settings-export-" + Guid.NewGuid().ToString("N") + ".udbsettings");
			try
			{
				SettingsTransfer.WriteFile(export, path);
				Configuration back = new Configuration(path, true);
				Assert.False(back.ErrorResult);
				Assert.Equal("C:\\Games\\Doom \"x\"\\doom2.wad", back.ReadSetting("configurations.gzdoom_doomudmf.resources.resource0.location", ""));
				Assert.Equal(export.OutputConfiguration(), back.OutputConfiguration());
			}
			finally { File.Delete(path); }
		}

		[Fact]
		public void RawConfigIsDetectedAndNewerFormatsRefused()
		{
			SettingsFileInfo raw = SettingsTransfer.ReadHeader(Parse(FIXTURE));
			Assert.False(raw.IsExport);
			Assert.Equal(SettingsTransfer.PLATFORM_WINDOWS, raw.Platform);
			Assert.Null(SettingsTransfer.CheckImportable(raw));
			Assert.Equal(SettingsTransfer.PLATFORM_LINUX, SettingsTransfer.ReadHeader(Parse("screenshotspath = \"/srv/u/shots\";")).Platform);

			Assert.Null(SettingsTransfer.CheckImportable(SettingsTransfer.ReadHeader(Parse("udbsettingsexport { formatversion = 1; }"))));
			Assert.NotNull(SettingsTransfer.CheckImportable(SettingsTransfer.ReadHeader(Parse("udbsettingsexport { formatversion = 2; }"))));
			Assert.NotNull(SettingsTransfer.CheckImportable(SettingsTransfer.ReadHeader(Parse("udbsettingsexport { formatversion = \"1a\"; }"))));
			Assert.NotNull(SettingsTransfer.CheckImportable(SettingsTransfer.ReadHeader(Parse("udbsettingsexport { program = \"x\"; }"))));
		}

		private static Configuration Pending(Configuration source, ICollection<string> units, string platform = SettingsTransfer.PLATFORM_WINDOWS, string apppath = @"C:\UDB")
		{
			return SettingsTransfer.BuildPending(source, units, SettingsTransfer.ReadHeader(source), platform, apppath);
		}

		[Fact]
		public void MergeReplacesUnitsWholesaleAndKeepsTheRest()
		{
			Configuration local = Parse(@"
currentversion = 3999;
lightmapbaker { cost = 9.5; samples = 4; }
shortcuts { builder_mine = 5; builder_preferences = 1; }
configurations
{
	gzdoom_doomudmf { resources { resource0 { location = ""a""; } resource1 { location = ""b""; } resource2 { location = ""c""; } resource3 { location = ""d""; } } }
	localonly_doom { enabled = true; }
}
plugins { mine { x = 1; } }
");
			Configuration source = Export(Parse(FIXTURE), ALL_GROUPS);
			int count = SettingsTransfer.Merge(local, Pending(source, new HashSet<string>(UnitPaths(source))));
			Assert.Equal(UnitPaths(source).Count, count);

			Assert.Equal(2, local.ReadSetting("configurations.gzdoom_doomudmf.resources", new Hashtable()).Count);
			Assert.True(local.SettingExists("configurations.localonly_doom.enabled"));
			Assert.True(local.SettingExists("plugins.mine.x"));
			Assert.Equal(5, local.ReadSetting("shortcuts.builder_mine", 0));
			Assert.Equal(123, local.ReadSetting("shortcuts.builder_preferences", 0));
			Assert.Equal(3999, local.ReadSetting("currentversion", 0));
			Assert.Equal(9.5f, local.ReadSetting("lightmapbaker.cost", 0f));
			Assert.Equal(8, local.ReadSetting("lightmapbaker.samples", 0));
		}

		[Fact]
		public void ImportedShortcutDropsTheLocalAlternate()
		{
			Configuration local = Parse("shortcuts { builder_preferences = 1; } shortcutsalt { builder_preferences = 2; builder_other = 3; }");
			Configuration source = Parse("shortcuts { builder_preferences = 123; builder_newmap = 7; } shortcutsalt { builder_newmap = 8; }");
			SettingsTransfer.Merge(local, Pending(source, new HashSet<string> { "shortcuts.builder_preferences", "shortcuts.builder_newmap" }));
			Assert.Equal(123, local.ReadSetting("shortcuts.builder_preferences", 0));
			Assert.False(local.SettingExists("shortcutsalt.builder_preferences"));
			Assert.Equal(3, local.ReadSetting("shortcutsalt.builder_other", 0));
			Assert.Equal(8, local.ReadSetting("shortcutsalt.builder_newmap", 0));

			// An alternate without a primary leaves the local primary alone
			local = Parse("shortcuts { builder_x = 5; }");
			SettingsTransfer.Merge(local, Pending(Parse("shortcutsalt { builder_x = 9; }"), new HashSet<string> { "shortcuts.builder_x" }));
			Assert.Equal(5, local.ReadSetting("shortcuts.builder_x", 0));
			Assert.Equal(9, local.ReadSetting("shortcutsalt.builder_x", 0));
		}

		[Fact]
		public void ListedUnitMissingFromThePendingFileIsDeleted()
		{
			// The path remap's "use this install's default" drops screenshotspath after the unit was listed
			Configuration pending = Pending(Parse(FIXTURE), new HashSet<string> { "screenshotspath", "blackbrowsers" });
			pending.DeleteSetting(SettingsTransfer.SCREENSHOTS_KEY);
			Configuration local = Parse("screenshotspath = \"/srv/shots\"; blackbrowsers = false;");
			Assert.Equal(2, SettingsTransfer.Merge(local, pending));
			Assert.False(local.SettingExists("screenshotspath"));
			Assert.True(local.ReadSetting("blackbrowsers", false));
		}

		[Fact]
		public void CrossPlatformAndForeignInstallSkips()
		{
			Configuration source = Export(Parse(FIXTURE), ALL_GROUPS);
			HashSet<string> all = new HashSet<string>(UnitPaths(source));
			Configuration local = Parse("nativefiledialogs = false; plugins { udbscript { scriptslots { slot1 = \"/mine.js\"; } lastscript = 1; } }");

			// Other platform, other install
			Configuration pending = Pending(source, all, SettingsTransfer.PLATFORM_LINUX, "/opt/udb");
			Assert.False(pending.SettingExists("nativefiledialogs"));
			foreach(string k in SettingsTransfer.UDBSCRIPT_KEYS) Assert.False(pending.SettingExists(k), k);
			SettingsTransfer.Merge(local, pending);
			Assert.False(local.ReadSetting("nativefiledialogs", true));
			Assert.Equal("/mine.js", local.ReadSetting("plugins.udbscript.scriptslots.slot1", ""));
			Assert.Equal(3, local.ReadSetting("plugins.udbscript.lastscript", 0));
			Assert.False(local.SettingExists("plugins.udbscript.scripts"));

			// Same platform, same install
			pending = Pending(source, all, SettingsTransfer.PLATFORM_WINDOWS, @"C:\UDB\");
			Assert.True(pending.SettingExists("nativefiledialogs"));
			SettingsTransfer.Merge(local, pending);
			Assert.Equal(@"C:\UDB\UDBScript\Scripts\a.js", local.ReadSetting("plugins.udbscript.scriptslots.slot1", ""));
			Assert.True(local.SettingExists("plugins.udbscript.scripts.abc.x"));
		}

		[Fact]
		public void PathSettingsAreFound()
		{
			List<SettingsPathEntry> missing = SettingsTransfer.FindMissingPaths(Parse(FIXTURE), p => false);
			List<string> keys = missing.ConvertAll(m => m.Key);
			Assert.Equal(new List<string> {
				"screenshotspath", "lightmapbaker.path",
				"configurations.gzdoom_doomudmf.resources.resource0.location",
				"configurations.gzdoom_doomudmf.resources.resource1.location",
				"configurations.gzdoom_doomudmf.engines.engine0.testprogram",
				"configurations.boom_doomdoom.testprogram",
			}, keys);
			Assert.Empty(SettingsTransfer.FindMissingPaths(Parse(FIXTURE), p => true));
		}

		[Fact]
		public void ForeignPathsRemapInBothDirections()
		{
			char sep = Path.DirectorySeparatorChar;

			SettingsPathEntry win = new SettingsPathEntry("k", @"C:\Games\Doom ""x""\doom2.wad");
			Assert.Equal(@"C:\Games\Doom ""x""", win.Folder);
			Assert.Equal("doom2.wad", win.Leaf);
			Assert.Equal("/srv/u/doom" + sep + "doom2.wad", SettingsTransfer.RemapPath(win, "/srv/u/doom"));

			SettingsPathEntry root = new SettingsPathEntry("k", @"D:\doom2.wad");
			Assert.Equal(@"D:\", root.Folder);

			SettingsPathEntry lin = new SettingsPathEntry("k", "/srv/a/doom/doom2.wad");
			Assert.Equal("/srv/a/doom", lin.Folder);
			Assert.Equal("doom2.wad", lin.Leaf);
			Assert.Equal(@"D:\Doom" + sep + "doom2.wad", SettingsTransfer.RemapPath(lin, @"D:\Doom"));

			// A folder under the source home moves under this home
			Assert.Equal("/srv/u" + sep + "Games" + sep + "Doom", SettingsTransfer.SuggestFolder(@"C:\Users\x\Games\Doom", @"C:\Users\x", "/srv/u"));
			Assert.Equal(@"C:\Users\y" + sep + "doom", SettingsTransfer.SuggestFolder("/srv/a/doom", "/srv/a/", @"C:\Users\y"));
			Assert.Null(SettingsTransfer.SuggestFolder(@"C:\Users\xy\Doom", @"C:\Users\x", "/srv/u"));
			Assert.Null(SettingsTransfer.SuggestFolder(@"D:\Doom", @"C:\Users\x", "/srv/u"));
			Assert.Null(SettingsTransfer.SuggestFolder(@"D:\Doom", "", "/srv/u"));
		}

		private static string NewSettingsDir()
		{
			string dir = Path.Combine(Path.GetTempPath(), "settings-import-" + Guid.NewGuid().ToString("N"));
			Directory.CreateDirectory(dir);
			return dir;
		}

		private static void WritePending(string dir, Configuration pending)
		{
			SettingsTransfer.WriteFile(pending, Path.Combine(dir, SettingsTransfer.PENDING_FILE));
		}

		[Fact]
		public void ApplyPendingMergesWritesAndBacksUp()
		{
			string dir = NewSettingsDir();
			try
			{
				string settings = Path.Combine(dir, SettingsTransfer.SETTINGS_FILE);
				File.WriteAllText(settings, "currentversion = 3999;\nblackbrowsers = false;\n");
				Configuration source = Parse(FIXTURE);
				WritePending(dir, Pending(source, new HashSet<string> { "blackbrowsers", "colors.background" }));

				Configuration cfg = new Configuration(settings, true);
				Configuration before = cfg;
				SettingsImportResult result = SettingsTransfer.ApplyPending(ref cfg, dir);
				Assert.Equal(SettingsImportState.Applied, result.State);
				Assert.Equal(2, result.UnitCount);
				Assert.False(ReferenceEquals(before, cfg));
				Assert.True(cfg.ReadSetting("blackbrowsers", false));
				Assert.False(File.Exists(Path.Combine(dir, SettingsTransfer.PENDING_FILE)));
				Assert.Contains("blackbrowsers = false", File.ReadAllText(Path.Combine(dir, SettingsTransfer.BACKUP_FILE)));

				Configuration written = new Configuration(settings, true);
				Assert.True(written.ReadSetting("blackbrowsers", false));
				Assert.Equal(-16777216, written.ReadSetting("colors.background", 0));
				Assert.Equal(3999, written.ReadSetting("currentversion", 0));

				// Nothing pending is a no-op
				Assert.Equal(SettingsImportState.None, SettingsTransfer.ApplyPending(ref cfg, dir).State);
			}
			finally { Directory.Delete(dir, true); }
		}

		[Fact]
		public void CorruptPendingIsSetAsideAndChangesNothing()
		{
			string dir = NewSettingsDir();
			try
			{
				string settings = Path.Combine(dir, SettingsTransfer.SETTINGS_FILE);
				File.WriteAllText(settings, "blackbrowsers = false;\n");
				File.WriteAllText(Path.Combine(dir, SettingsTransfer.PENDING_FILE), "udbsettingsexport { units = \"blackbrowsers\"; }\nbad key = 1;\n");

				Configuration cfg = new Configuration(settings, true);
				Configuration before = cfg;
				SettingsImportResult result = SettingsTransfer.ApplyPending(ref cfg, dir);
				Assert.Equal(SettingsImportState.Failed, result.State);
				Assert.True(ReferenceEquals(before, cfg));
				Assert.False(File.Exists(Path.Combine(dir, SettingsTransfer.PENDING_FILE)));
				Assert.True(File.Exists(Path.Combine(dir, SettingsTransfer.FAILED_FILE)));
				Assert.Equal("blackbrowsers = false;\n", File.ReadAllText(settings));
			}
			finally { Directory.Delete(dir, true); }
		}

		[Fact]
		public void FailedWriteLeavesTheSettingsFileAndTreeAlone()
		{
			string dir = NewSettingsDir();
			try
			{
				string settings = Path.Combine(dir, SettingsTransfer.SETTINGS_FILE);
				File.WriteAllText(settings, "blackbrowsers = false;\n");
				WritePending(dir, Pending(Parse(FIXTURE), new HashSet<string> { "blackbrowsers" }));

				// A directory where the swap's sibling file goes makes the write fail
				Directory.CreateDirectory(settings + SiblingFileSwap.SIBLING_SUFFIX);
				string backup = Path.Combine(dir, SettingsTransfer.BACKUP_FILE);
				File.WriteAllText(backup, "earlier backup\n");

				Configuration cfg = new Configuration(settings, true);
				Configuration before = cfg;
				SettingsImportResult result = SettingsTransfer.ApplyPending(ref cfg, dir);
				Assert.Equal(SettingsImportState.Failed, result.State);
				Assert.True(ReferenceEquals(before, cfg));
				Assert.False(cfg.ReadSetting("blackbrowsers", true));
				Assert.Equal("blackbrowsers = false;\n", File.ReadAllText(settings));
				Assert.Equal("earlier backup\n", File.ReadAllText(backup));
				Assert.False(File.Exists(backup + ".new"));
			}
			finally { Directory.Delete(dir, true); }
		}

		[Fact]
		public void ProgramConfigurationAppliesAPendingImportOnLoad()
		{
			string dir = NewSettingsDir();
			try
			{
				string settings = Path.Combine(dir, SettingsTransfer.SETTINGS_FILE);
				File.WriteAllText(settings, "currentversion = 3999;\nblackbrowsers = false;\n");
				WritePending(dir, Pending(Parse(FIXTURE), new HashSet<string> { "blackbrowsers" }));

				ProgramConfiguration plain = new ProgramConfiguration();
				Assert.True(plain.Load(settings, settings, settings));
				Assert.False(plain.BlackBrowsers);
				Assert.Equal(SettingsImportState.None, plain.ImportResult.State);

				ProgramConfiguration config = new ProgramConfiguration();
				Assert.True(config.Load(settings, settings, settings, true));
				Assert.Equal(SettingsImportState.Applied, config.ImportResult.State);
				Assert.True(config.BlackBrowsers);
			}
			finally { Directory.Delete(dir, true); }
		}

		[Fact]
		public void ExportRoundTripsOntoDefaultSettings()
		{
			string defaults = Path.Combine(Path.GetDirectoryName(typeof(Configuration).Assembly.Location), "UDBuilder.default.cfg");
			Configuration local = new Configuration(defaults, true);
			Assert.False(local.ErrorResult);

			Configuration fixture = Parse(FIXTURE);
			Configuration export = Export(fixture, ALL_GROUPS);
			HashSet<string> units = new HashSet<string>(UnitPaths(export));
			SettingsTransfer.Merge(local, SettingsTransfer.BuildPending(export, units, SettingsTransfer.ReadHeader(export), SettingsTransfer.PLATFORM_WINDOWS, @"C:\UDB"));

			foreach(string unit in units)
				Assert.Equal(Text(fixture, unit), Text(local, unit));
			Assert.Equal(Text(fixture, "shortcutsalt.builder_newmap"), Text(local, "shortcutsalt.builder_newmap"));
		}
	}
}
