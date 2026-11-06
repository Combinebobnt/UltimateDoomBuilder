using System.Diagnostics;
using CodeImp.DoomBuilder.Windows;

namespace Builder.Tests
{
	public class ZenityArgsTests
	{
		private static List<string> Filters(DialogSpec spec)
		{
			return ZenityArgs.Build(spec).Where(a => a.StartsWith("--file-filter=")).ToList();
		}

		[Fact]
		public void FilterTranslation()
		{
			Assert.Equal(new List<string> { "--file-filter=Doom WAD Files (*.wad) | *.wad" },
				ZenityArgs.FilterArgs("Doom WAD Files (*.wad)|*.wad", 1));
			Assert.Equal(new List<string> { "--file-filter=WAD | *.wad *.Wad *.WAD" },
				ZenityArgs.FilterArgs("WAD|*.wad;*.Wad; *.WAD", 1));
			Assert.Equal(new List<string> { "--file-filter=Prefabs | *.dbprefab", "--file-filter=All files | *" },
				ZenityArgs.FilterArgs("Prefabs|*.dbprefab|All files|*.*", 1));
		}

		[Fact]
		public void FilterIndexPairGoesFirstAndIsClamped()
		{
			string filter = "A|*.a|B|*.b|C|*.c";
			Assert.Equal(new List<string> { "--file-filter=B | *.b", "--file-filter=A | *.a", "--file-filter=C | *.c" }, ZenityArgs.FilterArgs(filter, 2));
			Assert.Equal(new List<string> { "--file-filter=C | *.c", "--file-filter=A | *.a", "--file-filter=B | *.b" }, ZenityArgs.FilterArgs(filter, 9));
			Assert.Equal(new List<string> { "--file-filter=A | *.a", "--file-filter=B | *.b", "--file-filter=C | *.c" }, ZenityArgs.FilterArgs(filter, 0));
		}

		[Fact]
		public void MalformedOrEmptyFilterYieldsNoFilterArgs()
		{
			Assert.Empty(ZenityArgs.FilterArgs("A|*.a|B", 1));
			Assert.Empty(ZenityArgs.FilterArgs(null, 1));
			Assert.Empty(ZenityArgs.FilterArgs("", 1));
			// FolderSelectDialog's placeholder filter has no patterns
			Assert.Empty(ZenityArgs.FilterArgs("Folders|\n", 1));
		}

		[Fact]
		public void StartLocation()
		{
			Assert.Equal("/maps/", ZenityArgs.StartLocation("/maps", null));
			Assert.Equal("/maps/", ZenityArgs.StartLocation("/maps/", ""));
			Assert.Equal("/maps/e1.wad", ZenityArgs.StartLocation("/maps", "e1.wad"));
			Assert.Equal("/home/u/maps/", ZenityArgs.StartLocation("\\home\\u\\maps", null));
			Assert.Equal("/other/x.wad", ZenityArgs.StartLocation("/maps", "/other/x.wad"));
			Assert.Equal("e1.wad", ZenityArgs.StartLocation(null, "e1.wad"));
			Assert.Null(ZenityArgs.StartLocation(null, null));
			Assert.Null(ZenityArgs.StartLocation("", ""));

			Assert.DoesNotContain("--filename=", string.Join(" ", ZenityArgs.Build(new DialogSpec())));
			Assert.Contains("--filename=/maps/e1.wad", ZenityArgs.Build(new DialogSpec { InitialDirectory = "/maps", FileName = "e1.wad" }));
		}

		[Fact]
		public void ModeFlags()
		{
			List<string> open = ZenityArgs.Build(new DialogSpec { Title = "Open Map", Filter = "W|*.wad" });
			Assert.Equal("--file-selection", open[0]);
			Assert.Contains("--title=Open Map", open);
			Assert.DoesNotContain("--save", open);
			Assert.DoesNotContain("--directory", open);

			List<string> save = ZenityArgs.Build(new DialogSpec { Save = true, OverwritePrompt = true });
			Assert.Contains("--save", save);
			// The wrapper prompts itself, after appending the extension
			Assert.DoesNotContain("--confirm-overwrite", save);

			DialogSpec folder = new DialogSpec { Folder = true, Filter = "Folders|*.x", InitialDirectory = "/data" };
			List<string> folderargs = ZenityArgs.Build(folder);
			Assert.Contains("--directory", folderargs);
			Assert.Contains("--filename=/data/", folderargs);
			Assert.Empty(Filters(folder));

			Assert.Contains("--attach=12345", ZenityArgs.Build(new DialogSpec { AttachWindow = 12345 }));
			Assert.DoesNotContain("--attach", string.Join(" ", ZenityArgs.Build(new DialogSpec())));
		}

		// --modal makes the WM keep focus on the chooser; without an owner it could be group-modal
		[Fact]
		public void ModalOnlyWithAnOwner()
		{
			Assert.Contains("--modal", ZenityArgs.Build(new DialogSpec { AttachWindow = 12345 }));
			Assert.Contains("--modal", ZenityArgs.Build(new DialogSpec { AttachWindow = 7, Folder = true }));
			Assert.DoesNotContain("--modal", ZenityArgs.Build(new DialogSpec()));
		}

		[Fact]
		public void PickChooserTakesFirstUntransientWindowOfThePid()
		{
			long[] windows = { 0x100, 0x200, 0x300, 0x400 };
			long[] pids = { 11, 42, 42, 0 };
			Assert.Equal(0x200L, ZenityArgs.PickChooser(windows, pids, new bool[4], 42));
			Assert.Equal(0L, ZenityArgs.PickChooser(windows, pids, new bool[4], 99));
			Assert.Equal(0L, ZenityArgs.PickChooser(new long[0], new long[0], new bool[0], 42));

			// A GTK sub-dialog of the chooser is already transient, so it is never re-parented
			Assert.Equal(0x300L, ZenityArgs.PickChooser(windows, pids, new[] { false, true, false, false }, 42));
			Assert.Equal(0L, ZenityArgs.PickChooser(windows, pids, new[] { false, true, true, false }, 42));
		}

		[Fact]
		public void DefaultExtension()
		{
			DialogSpec wad = new DialogSpec { Save = true, AddExtension = true, Filter = "Doom WAD Files (*.wad)|*.wad;*.Wad;*.WAD" };
			Assert.Equal("/m/map01.wad", ZenityArgs.ApplyDefaultExtension("/m/map01", wad));
			Assert.Equal("/m/map01.WAD", ZenityArgs.ApplyDefaultExtension("/m/map01.WAD", wad));
			Assert.Equal("/m.d/map01.wad", ZenityArgs.ApplyDefaultExtension("/m.d/map01", wad));

			DialogSpec defext = new DialogSpec { Save = true, AddExtension = true, DefaultExt = ".obj", Filter = "W|*.wad" };
			Assert.Equal("/m/model.obj", ZenityArgs.ApplyDefaultExtension("/m/model", defext));

			// The FilterIndex pair is listed first, so its extension wins; wildcard-only patterns are skipped
			DialogSpec indexed = new DialogSpec { Save = true, AddExtension = true, Filter = "All|*.*|PNG|*.png", FilterIndex = 2 };
			Assert.Equal("/i/shot.png", ZenityArgs.ApplyDefaultExtension("/i/shot", indexed));
			DialogSpec wildcard = new DialogSpec { Save = true, AddExtension = true, Filter = "All|*.*" };
			Assert.Equal("/i/shot", ZenityArgs.ApplyDefaultExtension("/i/shot", wildcard));

			wad.AddExtension = false;
			Assert.Equal("/m/map01", ZenityArgs.ApplyDefaultExtension("/m/map01", wad));

			// Open dialogs never rename the chosen file
			DialogSpec open = new DialogSpec { AddExtension = true, DefaultExt = "exe", Filter = "Programs|*" };
			Assert.Equal("/usr/bin/gzdoom", ZenityArgs.ApplyDefaultExtension("/usr/bin/gzdoom", open));
		}

		[Fact]
		public void Quoting()
		{
			Assert.Equal("\"plain\"", ZenityArgs.Quote("plain"));
			Assert.Equal("\"--title=Save Map As\"", ZenityArgs.Quote("--title=Save Map As"));
			Assert.Equal("\"a\\\"b\"", ZenityArgs.Quote("a\"b"));
			Assert.Equal("\"c:\\\\x\"", ZenityArgs.Quote("c:\\x"));
			Assert.Equal("\"\\$HOME \\`id\\`\"", ZenityArgs.Quote("$HOME `id`"));
			Assert.Equal("\"it's\"", ZenityArgs.Quote("it's"));
			Assert.Equal("\"a\" \"b c\"", ZenityArgs.Join(new[] { "a", "b c" }));
		}

		[Fact]
		public void QuotedArgumentsRoundTripThroughMonoProcessStart()
		{
			// The quoting targets Mono's Unix launcher; other runtimes split Arguments differently
			if(Type.GetType("Mono.Runtime") == null || !File.Exists("/usr/bin/printf")) return;

			string[] args = { "%s\\n", "plain", "two words", "quote\"d", "back\\slash", "trail\\", "it's", "$HOME", "`id`", "tab\tx", "--filename=/a b/c'd\"e\\f.wad" };
			ProcessStartInfo psi = new ProcessStartInfo("/usr/bin/printf", ZenityArgs.Join(args));
			psi.UseShellExecute = false;
			psi.RedirectStandardOutput = true;
			using(Process p = Process.Start(psi))
			{
				string output = p.StandardOutput.ReadToEnd();
				p.WaitForExit();
				Assert.Equal(args.Skip(1).ToList(), output.Split('\n').Take(args.Length - 1).ToList());
			}
		}

		[Fact]
		public void ResultClassification()
		{
			ZenityResult ok = ZenityArgs.Classify(0, "/maps/e1.wad\n", "");
			Assert.Equal(ZenityOutcome.Ok, ok.Outcome);
			Assert.Equal("/maps/e1.wad", ok.Path);
			Assert.Equal(ZenityOutcome.Cancel, ZenityArgs.Classify(0, "\n", "").Outcome);
			Assert.Equal(ZenityOutcome.Cancel, ZenityArgs.Classify(1, "", "").Outcome);
			Assert.Equal(ZenityOutcome.Cancel, ZenityArgs.Classify(1, "", "Gtk-Message: GtkDialog mapped without a transient parent").Outcome);
			Assert.Equal(ZenityOutcome.Unavailable, ZenityArgs.Classify(1, "", "(zenity:3): Gtk-WARNING **: 09:42:54.769: cannot open display: :0").Outcome);
			Assert.Equal(ZenityOutcome.Unavailable, ZenityArgs.Classify(255, "", "").Outcome);
		}

		// Blocking a modal dialog must drop its input but never paint, timers, activation or close
		[Fact]
		public void InputBlockerDropsOnlyInput()
		{
			int[] input = { 0x00A0, 0x00A1, 0x0100, 0x0101, 0x0102, 0x0104, 0x0200, 0x0201, 0x0204, 0x020A, 0x020E };
			int[] other = { 0x0006, 0x000F, 0x0010, 0x0014, 0x0112, 0x0113, 0x0007, 0x0008, 0x0400, 0x02A3 };
			foreach(int m in input) Assert.True(InputBlocker.IsInput(m));
			foreach(int m in other) Assert.False(InputBlocker.IsInput(m));
		}
	}
}
