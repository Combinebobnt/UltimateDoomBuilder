using System.Drawing;
using System.Reflection;
using System.Windows.Forms;
using CodeImp.DoomBuilder;
using CodeImp.DoomBuilder.Config;
using CodeImp.DoomBuilder.IO;
using CodeImp.DoomBuilder.Windows;

namespace Builder.Tests
{
	// Never uses the Mono backend: remapping system colors and the toolstrip renderer is process-global
	[Collection("GeneralStatics")]
	public class UIThemeTests
	{
		private sealed class FakeBackend : IThemeBackend
		{
			public int PaletteCalls;
			public int RendererCalls;
			public IReadOnlyDictionary<KnownColor, Color> Palette;
			public IDictionary<KnownColor, Color> Previous;
			public IDictionary<KnownColor, Color> Restored;
			public bool Throw;
			public bool ThrowOnCreate;
			public bool ThrowOnInstall;
			public bool ThrowOnFormTheming;
			// What the table held before the palette was written
			public Color PreviousControlDark = Color.FromArgb(1, 2, 3);
			public readonly List<string> Calls = new List<string>();

			public IDictionary<KnownColor, Color> ApplyPalette(IReadOnlyDictionary<KnownColor, Color> palette)
			{
				PaletteCalls++;
				Calls.Add("palette");
				if(Throw) throw new MissingMemberException("no color table");
				Palette = palette;
				Previous = new Dictionary<KnownColor, Color> { { KnownColor.ControlDark, PreviousControlDark } };
				return Previous;
			}

			public void RestorePalette(IDictionary<KnownColor, Color> previous)
			{
				Calls.Add("restore");
				Restored = previous;
			}

			public ToolStripRenderer CreateToolStripRenderer()
			{
				Calls.Add("create");
				if(ThrowOnCreate) throw new InvalidOperationException("no renderer");
				return null;
			}

			public void InstallFormTheming()
			{
				Calls.Add("forms");
				if(ThrowOnFormTheming) throw new InvalidOperationException("no hook");
			}

			public void UninstallFormTheming() { Calls.Add("unforms"); }

			public void InstallToolStripRenderer(ToolStripRenderer renderer)
			{
				RendererCalls++;
				Calls.Add("renderer");
				if(ThrowOnInstall) throw new InvalidOperationException("no install");
			}
		}

		private static UIThemeEnvironment Windows(int build, int? appsuselighttheme = null, bool highcontrast = false)
		{
			return new UIThemeEnvironment { IsWindows = true, Build = build, AppsUseLightTheme = appsuselighttheme, HighContrast = highcontrast };
		}

		// Runs a failing Apply with a fresh error logger and returns what it logged
		private static List<ErrorItem> ApplyLoggingFailure(FakeBackend backend)
		{
			FieldInfo field = typeof(General).GetField("errorlogger", BindingFlags.NonPublic | BindingFlags.Static)!;
			object previous = field.GetValue(null);
			var logger = new ErrorLogger();
			field.SetValue(null, logger);
			try
			{
				UITheme.Apply(UIThemeKind.Dark, backend);
				return new List<ErrorItem>(logger.GetErrors(0));
			}
			finally { field.SetValue(null, previous); }
		}

		private static double Luminance(Color c)
		{
			double Channel(int v)
			{
				double s = v / 255.0;
				return s <= 0.03928 ? s / 12.92 : Math.Pow((s + 0.055) / 1.055, 2.4);
			}
			return 0.2126 * Channel(c.R) + 0.7152 * Channel(c.G) + 0.0722 * Channel(c.B);
		}

		private static double Contrast(Color a, Color b)
		{
			double la = Luminance(a), lb = Luminance(b);
			return (Math.Max(la, lb) + 0.05) / (Math.Min(la, lb) + 0.05);
		}

		[Fact]
		public void DarkPaletteKeepsTextReadable()
		{
			var p = UITheme.DarkPalette;
			var pairs = new[]
			{
				(KnownColor.WindowText, KnownColor.Window, 4.5),
				(KnownColor.ControlText, KnownColor.Control, 4.5),
				(KnownColor.MenuText, KnownColor.Menu, 4.5),
				(KnownColor.InfoText, KnownColor.Info, 4.5),
				(KnownColor.HighlightText, KnownColor.Highlight, 4.5),
				(KnownColor.GrayText, KnownColor.Control, 3.0),
				(KnownColor.HotTrack, KnownColor.Control, 3.0),
			};

			var failures = new List<string>();
			foreach(var (fore, back, minimum) in pairs)
			{
				double ratio = Contrast(p[fore], p[back]);
				if(ratio < minimum) failures.Add(fore + " on " + back + ": " + ratio.ToString("0.00") + " < " + minimum);
			}
			// Mono draws disabled text in the midpoint of ControlDark and ControlDarkDark
			Color dark = p[KnownColor.ControlDark], darkdark = p[KnownColor.ControlDarkDark];
			Color disabled = Color.FromArgb((dark.R + darkdark.R) / 2, (dark.G + darkdark.G) / 2, (dark.B + darkdark.B) / 2);
			double disabledratio = Contrast(disabled, p[KnownColor.Control]);
			if(disabledratio < 3.0) failures.Add("disabled text on Control: " + disabledratio.ToString("0.00") + " < 3");

			foreach(KnownColor back in new[] { KnownColor.Control, KnownColor.Window })
			{
				double ratio = Contrast(UITheme.DarkWarningText, p[back]);
				if(ratio < 4.5) failures.Add("DarkWarningText on " + back + ": " + ratio.ToString("0.00") + " < 4.5");
			}
			double accentratio = Contrast(p[KnownColor.HighlightText], UITheme.DarkAccent);
			if(accentratio < 4.5) failures.Add("HighlightText on DarkAccent: " + accentratio.ToString("0.00") + " < 4.5");
			Assert.Empty(failures);
		}

		[Fact]
		public void LightMakesNoBackendCalls()
		{
			var backend = new FakeBackend();
			try
			{
				UITheme.Apply(UIThemeKind.Light, backend);
				UITheme.Apply(UIThemeKind.Light, backend, Windows(22000, appsuselighttheme: 0));
				Assert.Empty(backend.Calls);
				Assert.False(UITheme.IsDark);
			}
			finally { UITheme.Reset(); }
		}

		[Fact]
		public void DarkAppliesTheWholePaletteAndTheRenderer()
		{
			var backend = new FakeBackend();
			try
			{
				UITheme.Apply(UIThemeKind.Dark, backend);
				Assert.Equal("create palette forms renderer", string.Join(" ", backend.Calls));
				Assert.Same(UITheme.DarkPalette, backend.Palette);
				Assert.True(UITheme.IsDark);
				Assert.Equal(Color.Red, UITheme.Pick(Color.Black, Color.Red));

				// Light values survive the remap for settings that must not pick up dark colors
				Assert.Equal(Color.FromArgb(1, 2, 3).ToArgb(), UITheme.LightSystemColor(KnownColor.ControlDark).ToArgb());
			}
			finally { UITheme.Reset(); }
		}

		// Windows refills the table from the OS on a color change: the palette goes back on, and the refilled values are the new light ones
		[Fact]
		public void ReapplyRewritesThePaletteOnlyWhenDark()
		{
			var backend = new FakeBackend();
			try
			{
				UITheme.ReapplyPalette(backend);
				Assert.Empty(backend.Calls);

				UITheme.Apply(UIThemeKind.Dark, backend);
				backend.Calls.Clear();
				backend.Palette = null;
				backend.PreviousControlDark = Color.FromArgb(4, 5, 6);
				UITheme.ReapplyPalette(backend);
				Assert.Equal("palette", string.Join(" ", backend.Calls));
				Assert.Same(UITheme.DarkPalette, backend.Palette);
				Assert.Equal(Color.FromArgb(4, 5, 6).ToArgb(), UITheme.LightSystemColor(KnownColor.ControlDark).ToArgb());
			}
			finally { UITheme.Reset(); }
		}

		[Fact]
		public void BackendFailureIsLoggedNotThrown()
		{
			try
			{
				var backend = new FakeBackend { Throw = true };
				ErrorItem item = Assert.Single(ApplyLoggingFailure(backend));
				Assert.Equal(ErrorType.Warning, item.Type);
				Assert.False(UITheme.IsDark);
				Assert.Equal(0, backend.RendererCalls);
				Assert.Null(backend.Restored);
			}
			finally { UITheme.Reset(); }
		}

		[Fact]
		public void RendererCreationFailureLeavesThePaletteAlone()
		{
			try
			{
				var backend = new FakeBackend { ThrowOnCreate = true };
				Assert.Single(ApplyLoggingFailure(backend));
				Assert.Equal(0, backend.PaletteCalls);
				Assert.False(UITheme.IsDark);
			}
			finally { UITheme.Reset(); }
		}

		[Fact]
		public void FailureAfterThePaletteRestoresIt()
		{
			try
			{
				var backend = new FakeBackend { ThrowOnInstall = true };
				Assert.Single(ApplyLoggingFailure(backend));
				Assert.Same(backend.Previous, backend.Restored);
				Assert.Equal("create palette forms renderer unforms restore", string.Join(" ", backend.Calls));
				Assert.False(UITheme.IsDark);
				Assert.Equal(KnownColor.ControlDark.ToString(), UITheme.LightSystemColor(KnownColor.ControlDark).Name);
			}
			finally { UITheme.Reset(); }
		}

		[Fact]
		public void FormThemingFailureRestoresThePaletteAndInstallsNoRenderer()
		{
			try
			{
				var backend = new FakeBackend { ThrowOnFormTheming = true };
				Assert.Single(ApplyLoggingFailure(backend));
				Assert.Equal("create palette forms restore", string.Join(" ", backend.Calls));
				Assert.Same(backend.Previous, backend.Restored);
				Assert.False(UITheme.IsDark);
			}
			finally { UITheme.Reset(); }
		}

		[Fact]
		public void WindowsBuildDecidesTheDarkModeCalls()
		{
			var cases = new (int build, bool supported, int titlebar, DarkAppModeCall appmode)[]
			{
				(0, false, 0, DarkAppModeCall.None),
				(17134, false, 0, DarkAppModeCall.None),
				(17763, true, 19, DarkAppModeCall.AllowDarkModeForApp),
				(18362, true, 19, DarkAppModeCall.SetPreferredAppMode),
				(18985, true, 20, DarkAppModeCall.SetPreferredAppMode),
				(22000, true, 20, DarkAppModeCall.SetPreferredAppMode),
			};
			var failures = new List<string>();
			foreach(var (build, supported, titlebar, appmode) in cases)
			{
				WindowsDarkFeatures f = WindowsDarkSupport.For(build);
				if(f.Supported != supported || f.TitlebarAttribute != titlebar || f.AppModeCall != appmode)
					failures.Add(build + ": " + f.Supported + " " + f.TitlebarAttribute + " " + f.AppModeCall);
			}
			Assert.Empty(failures);
		}

		[Fact]
		public void IsSystemColorMatchesTheDarkValueOnlyInTheDarkTheme()
		{
			Color darkwindowtext = UITheme.DarkPalette[KnownColor.WindowText];
			try
			{
				Assert.True(UITheme.IsSystemColor(SystemColors.WindowText, KnownColor.WindowText));
				Assert.False(UITheme.IsSystemColor(darkwindowtext, KnownColor.WindowText));
				Assert.False(UITheme.IsSystemColor(SystemColors.GrayText, KnownColor.WindowText));

				UITheme.Apply(UIThemeKind.Dark, new FakeBackend());
				Assert.True(UITheme.IsSystemColor(SystemColors.WindowText, KnownColor.WindowText));
				Assert.True(UITheme.IsSystemColor(darkwindowtext, KnownColor.WindowText));
				Assert.False(UITheme.IsSystemColor(UITheme.DarkPalette[KnownColor.GrayText], KnownColor.WindowText));
				Assert.False(UITheme.IsSystemColor(Color.Red, KnownColor.WindowText));
			}
			finally { UITheme.Reset(); }
		}

		[Fact]
		public void ColorTableProbeFindsMonosTable()
		{
			// .NET has no such table; Fork Build probes the .NET Framework one separately
			if(Type.GetType("Mono.Runtime") == null) return;
			Assert.Equal("s_colorTable", KnownColorTableWriter.Probe(false));
		}

		[Fact]
		public void UnsupportedWindowsBuildIgnoresDarkAndSystem()
		{
			try
			{
				var backend = new FakeBackend();
				UITheme.Apply(UIThemeKind.Dark, backend, Windows(17134, appsuselighttheme: 0));
				UITheme.Apply(UIThemeKind.System, backend, Windows(17134, appsuselighttheme: 0));
				Assert.Empty(backend.Calls);
				Assert.Equal(UIThemeKind.Light, UITheme.Current);
			}
			finally { UITheme.Reset(); }
		}

		[Fact]
		public void HighContrastTurnsDarkAndSystemLight()
		{
			try
			{
				var backend = new FakeBackend();
				UITheme.Apply(UIThemeKind.Dark, backend, Windows(22000, appsuselighttheme: 0, highcontrast: true));
				UITheme.Apply(UIThemeKind.System, backend, Windows(22000, appsuselighttheme: 0, highcontrast: true));
				Assert.Empty(backend.Calls);
				Assert.Equal(UIThemeKind.Light, UITheme.Current);
			}
			finally { UITheme.Reset(); }
		}

		[Fact]
		public void WindowsSystemFollowsAppsUseLightTheme()
		{
			var runner = new FakeRunner();
			runner.Replies[PORTAL] = "(<<uint32 1>>,)\n";
			var cases = new (int? value, UIThemeKind kind, string source)[]
			{
				(0, UIThemeKind.Dark, "Windows AppsUseLightTheme = 0"),
				(1, UIThemeKind.Light, "Windows AppsUseLightTheme = 1"),
				(null, UIThemeKind.Light, "no desktop preference found"),
			};
			foreach(var (value, kind, source) in cases)
			{
				UIThemeEnvironment env = Windows(22000, value);
				env.GetEnv = Env("GTK_THEME", "Adwaita:dark");
				env.Runner = runner;
				DesktopThemeResult r = DesktopTheme.Detect(env);
				Assert.Equal(kind, r.Kind);
				Assert.Equal(source, r.Source);
			}
			// The Linux sources are never asked on Windows
			Assert.Empty(runner.Calls);

			try
			{
				var backend = new FakeBackend();
				UITheme.Apply(UIThemeKind.System, backend, Windows(18362, appsuselighttheme: 0));
				Assert.Equal(UIThemeKind.Dark, UITheme.Current);
				Assert.Equal(1, backend.PaletteCalls);
			}
			finally { UITheme.Reset(); }
		}

		[Fact]
		public void SettingReadsMissingOrUnknownAsSystemAndWritesLowercase()
		{
			var cases = new (string value, UIThemeKind expected)[]
			{
				("dark", UIThemeKind.Dark),
				("light", UIThemeKind.Light),
				("system", UIThemeKind.System),
				(null, UIThemeKind.System),
				("bogus", UIThemeKind.System),
			};
			foreach(var (value, expected) in cases)
			{
				var cfg = new Configuration();
				if(value != null) cfg.WriteSetting("uitheme", value);
				Assert.Equal(expected, ProgramConfiguration.ReadUITheme(cfg));
			}

			var written = new Configuration();
			ProgramConfiguration.WriteUITheme(written, UIThemeKind.Dark);
			Assert.Equal("dark", written.ReadSetting("uitheme", ""));
			ProgramConfiguration.WriteUITheme(written, UIThemeKind.Light);
			Assert.Equal("light", written.ReadSetting("uitheme", ""));
			ProgramConfiguration.WriteUITheme(written, UIThemeKind.System);
			Assert.Equal("system", written.ReadSetting("uitheme", ""));
		}

		// Answers any command line containing a reply's key; every other command fails
		private sealed class FakeRunner : ICommandRunner
		{
			public readonly Dictionary<string, string> Replies = new Dictionary<string, string>();
			public readonly List<string> Calls = new List<string>();

			public string Run(string file, string args, int timeoutms)
			{
				Calls.Add(file + " " + args);
				foreach(KeyValuePair<string, string> reply in Replies)
					if((file + " " + args).Contains(reply.Key)) return reply.Value;
				return null;
			}
		}

		private const string MATE_THEME = "org.mate.interface gtk-theme";
		private const string PORTAL = "org.freedesktop.appearance color-scheme";
		private const string GNOME_SCHEME = "org.gnome.desktop.interface color-scheme";
		private const string GNOME_THEME = "org.gnome.desktop.interface gtk-theme";

		private static Func<string, string> Env(params string[] pairs)
		{
			var env = new Dictionary<string, string>();
			for(int i = 0; i + 1 < pairs.Length; i += 2) env[pairs[i]] = pairs[i + 1];
			return name => env.TryGetValue(name, out string value) ? value : null;
		}

		private static DesktopThemeResult Detect(Func<string, string> getenv, ICommandRunner runner)
		{
			return DesktopTheme.Detect(new UIThemeEnvironment { GetEnv = getenv, Runner = runner });
		}

		[Fact]
		public void DetectorReadsGtkThemeFirst()
		{
			var runner = new FakeRunner();
			runner.Replies[PORTAL] = "(<<uint32 2>>,)\n";
			DesktopThemeResult r = Detect(Env("GTK_THEME", "Adwaita:dark", "XDG_CURRENT_DESKTOP", "MATE"), runner);
			Assert.Equal(UIThemeKind.Dark, r.Kind);
			Assert.Contains("GTK_THEME", r.Source);
			Assert.Empty(runner.Calls);

			r = Detect(Env("GTK_THEME", "Adwaita"), runner);
			Assert.Equal(UIThemeKind.Light, r.Kind);
		}

		[Fact]
		public void DetectorReadsMateThemeNameBeforeThePortal()
		{
			var runner = new FakeRunner();
			runner.Replies[PORTAL] = "(<<uint32 1>>,)\n";
			runner.Replies[MATE_THEME] = "'Yaru-dark'\n";
			DesktopThemeResult r = Detect(Env("XDG_CURRENT_DESKTOP", "MATE"), runner);
			Assert.Equal(UIThemeKind.Dark, r.Kind);
			Assert.Equal("MATE gtk-theme 'Yaru-dark'", r.Source);

			// The portal answers dark from GNOME's key, which MATE's Appearance dialog doesn't change
			runner.Replies[MATE_THEME] = "'Yaru-red'\n";
			r = Detect(Env("XDG_CURRENT_DESKTOP", "MATE"), runner);
			Assert.Equal(UIThemeKind.Light, r.Kind);
			Assert.Equal("MATE gtk-theme 'Yaru-red'", r.Source);

			// Outside MATE the MATE key isn't asked
			r = Detect(Env("XDG_CURRENT_DESKTOP", "GNOME"), runner);
			Assert.Equal(UIThemeKind.Dark, r.Kind);
			Assert.Contains("portal", r.Source);
		}

		[Fact]
		public void DetectorReadsThePortalThenGnome()
		{
			var runner = new FakeRunner();
			runner.Replies[GNOME_SCHEME] = "'prefer-dark'\n";
			runner.Replies[PORTAL] = "(<<uint32 1>>,)\n";
			DesktopThemeResult r = Detect(Env(), runner);
			Assert.Equal(UIThemeKind.Dark, r.Kind);
			Assert.Contains("portal", r.Source);

			runner.Replies[PORTAL] = "(<<uint32 2>>,)\n";
			r = Detect(Env(), runner);
			Assert.Equal(UIThemeKind.Light, r.Kind);
			Assert.Contains("portal", r.Source);

			// 0 is "no preference": falls through to GNOME's key
			runner.Replies[PORTAL] = "(<<uint32 0>>,)\n";
			r = Detect(Env(), runner);
			Assert.Equal(UIThemeKind.Dark, r.Kind);
			Assert.Equal("GNOME color-scheme 'prefer-dark'", r.Source);

			// GNOME's 'default' scheme falls back to its theme name
			runner.Replies[GNOME_SCHEME] = "'default'\n";
			runner.Replies[GNOME_THEME] = "'Yaru-Red-light'\n";
			r = Detect(Env(), runner);
			Assert.Equal(UIThemeKind.Light, r.Kind);
			Assert.Equal("GNOME gtk-theme 'Yaru-Red-light'", r.Source);
		}

		[Fact]
		public void DetectorWithNoAnswerIsLight()
		{
			var runner = new FakeRunner();
			DesktopThemeResult r = Detect(Env("XDG_CURRENT_DESKTOP", "MATE"), runner);
			Assert.Equal(UIThemeKind.Light, r.Kind);
			Assert.Equal("no desktop preference found", r.Source);
			Assert.Equal(4, runner.Calls.Count);
		}

		[Fact]
		public void SystemAppliesTheDetectedTheme()
		{
			try
			{
				var runner = new FakeRunner();
				runner.Replies[PORTAL] = "(<<uint32 1>>,)\n";
				var backend = new FakeBackend();
				UITheme.Apply(UIThemeKind.System, backend, new UIThemeEnvironment { GetEnv = Env(), Runner = runner });
				Assert.Equal(1, backend.PaletteCalls);
				Assert.Equal(1, backend.RendererCalls);
				Assert.Equal(UIThemeKind.Dark, UITheme.Current);
				UITheme.Reset();

				runner.Replies[PORTAL] = "(<<uint32 2>>,)\n";
				backend = new FakeBackend();
				UITheme.Apply(UIThemeKind.System, backend, new UIThemeEnvironment { GetEnv = Env(), Runner = runner });
				Assert.Equal(0, backend.PaletteCalls);
				Assert.Equal(0, backend.RendererCalls);
				Assert.Equal(UIThemeKind.Light, UITheme.Current);
			}
			finally { UITheme.Reset(); }
		}
	}
}
