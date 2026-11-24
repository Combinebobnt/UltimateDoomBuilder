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

			public IDictionary<KnownColor, Color> ApplyPalette(IReadOnlyDictionary<KnownColor, Color> palette)
			{
				PaletteCalls++;
				if(Throw) throw new MissingMemberException("no color table");
				Palette = palette;
				Previous = new Dictionary<KnownColor, Color> { { KnownColor.ControlDark, Color.FromArgb(1, 2, 3) } };
				return Previous;
			}

			public void RestorePalette(IDictionary<KnownColor, Color> previous) { Restored = previous; }

			public ToolStripRenderer CreateToolStripRenderer()
			{
				if(ThrowOnCreate) throw new InvalidOperationException("no renderer");
				return null;
			}

			public void InstallToolStripRenderer(ToolStripRenderer renderer)
			{
				RendererCalls++;
				if(ThrowOnInstall) throw new InvalidOperationException("no install");
			}
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
				Assert.Equal(0, backend.PaletteCalls);
				Assert.Equal(0, backend.RendererCalls);
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
				Assert.Equal(1, backend.PaletteCalls);
				Assert.Equal(1, backend.RendererCalls);
				Assert.Same(UITheme.DarkPalette, backend.Palette);
				Assert.True(UITheme.IsDark);
				Assert.Equal(Color.Red, UITheme.Pick(Color.Black, Color.Red));

				// Light values survive the remap for settings that must not pick up dark colors
				Assert.Equal(Color.FromArgb(1, 2, 3).ToArgb(), UITheme.LightSystemColor(KnownColor.ControlDark).ToArgb());
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
				Assert.False(UITheme.IsDark);
				Assert.Equal(KnownColor.ControlDark.ToString(), UITheme.LightSystemColor(KnownColor.ControlDark).Name);
			}
			finally { UITheme.Reset(); }
		}

		[Fact]
		public void UnsupportedBuildIgnoresDark()
		{
			// Only meaningful where IsSupported is false (Windows): the Mono build would remap the real color table
			if(UITheme.IsSupported) return;
			try
			{
				UITheme.Apply(UIThemeKind.Dark);
				Assert.False(UITheme.IsDark);
			}
			finally { UITheme.Reset(); }
		}

		[Fact]
		public void SettingReadsOnlyDarkAsDarkAndWritesLowercase()
		{
			var cases = new (string value, UIThemeKind expected)[]
			{
				("dark", UIThemeKind.Dark),
				("light", UIThemeKind.Light),
				(null, UIThemeKind.Light),
				("bogus", UIThemeKind.Light),
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
		}
	}
}
