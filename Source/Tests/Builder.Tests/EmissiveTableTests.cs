using CodeImp.DoomBuilder.Compilers;
using CodeImp.DoomBuilder.Data;
using CodeImp.DoomBuilder.Map;
using CodeImp.DoomBuilder.ZDoom;

namespace Builder.Tests
{
	public class EmissiveTableTests
	{
		private sealed class Image
		{
			public readonly string Name;
			public Image(string name) { Name = name; }
			public override string ToString() { return Name; }
		}

		// Two namespaces with their own images; several names may resolve to one image
		private sealed class World
		{
			public readonly Dictionary<string, Image>[] Names = { new Dictionary<string, Image>(StringComparer.OrdinalIgnoreCase), new Dictionary<string, Image>(StringComparer.OrdinalIgnoreCase) };
			public readonly Dictionary<Image, Image[]> Sequences = new Dictionary<Image, Image[]>();
			public readonly Dictionary<Image, int?> Colors = new Dictionary<Image, int?>();
			public readonly List<Image> AutoAsked = new List<Image>();

			public Image Add(EmissiveSurface kind, params string[] names)
			{
				var image = new Image(kind + ":" + names[0]);
				foreach(string n in names) Names[(int)kind][n] = image;
				return image;
			}

			public EmissiveResolver Resolver()
			{
				return new EmissiveResolver
				{
					Resolve = (name, kind) => { Image i; return Names[(int)kind].TryGetValue(name, out i) ? i : null; },
					Frames = image => { Image[] f; return Sequences.TryGetValue((Image)image, out f) ? f : null; },
					AutoColor = image => { AutoAsked.Add((Image)image); int? c; return Colors.TryGetValue((Image)image, out c) ? c : null; },
					Describe = image => ((Image)image).Name,
				};
			}
		}

		private static List<EmitdefsEntry> Parse(string text, List<string> warnings = null, string source = "EMITDEFS")
		{
			return EmitdefsParser.Parse(text, source, w => { if(warnings != null) warnings.Add(w); });
		}

		private static EmissiveTable Build(World world, string text, List<string> warnings = null)
		{
			return EmissiveTable.Build(Parse(text), world.Resolver(), w => { if(warnings != null) warnings.Add(w); });
		}

		private static KeyValuePair<string, EmissiveSurface> Wall(string name) { return new KeyValuePair<string, EmissiveSurface>(name, EmissiveSurface.Walls); }
		private static KeyValuePair<string, EmissiveSurface> Flat(string name) { return new KeyValuePair<string, EmissiveSurface>(name, EmissiveSurface.Flats); }

		#region Parser

		[Fact]
		public void ParserReadsDefaultsKindsQuotesAndComments()
		{
			var warnings = new List<string>();
			List<EmitdefsEntry> e = Parse(
				"// a comment line\n" +
				"LAVA1 distance 192 // trailing comment\n" +
				"\"textures/my panel.png\" color 4080ff intensity 2.5 distance 64 on walls\n" +
				"NUKAGE1 color auto distance 10 on flats\n" +
				"\n", warnings);
			Assert.Empty(warnings);
			Assert.Equal(4, e.Count);

			// No "on": one entry per kind, both at the defaults
			Assert.Equal(new[] { EmissiveSurface.Walls, EmissiveSurface.Flats }, e.Take(2).Select(x => x.Kind).ToArray());
			foreach(EmitdefsEntry lava in e.Take(2))
			{
				Assert.Equal("LAVA1", lava.Name);
				Assert.Equal(0xFFFFFF, lava.Color);
				Assert.Equal(1.0, lava.Intensity);
				Assert.Equal(192.0, lava.Distance);
				Assert.Equal(2, lava.Line);
			}
			Assert.Equal("textures/my panel.png", e[2].Name);
			Assert.Equal(EmissiveSurface.Walls, e[2].Kind);
			Assert.Equal(0x4080FF, e[2].Color);
			Assert.Equal(2.5, e[2].Intensity);
			Assert.Equal(EmissiveSurface.Flats, e[3].Kind);
			Assert.Equal(EmitdefsEntry.AUTO_COLOR, e[3].Color);
		}

		[Fact]
		public void ParserSkipsEveryMalformedLineWithAWarningAndKeepsAnUnknownKeysEntry()
		{
			string[] bad =
			{
				"X color 12345 distance 8", "X color ggffff distance 8", "X intensity -1 distance 8", "X intensity abc distance 8",
				"X distance 0", "X distance -4", "X distance nan", "X", "X color ffffff", "X distance 8 on ceilings",
				"X distance", "\"X distance 8", "\"\" distance 8",
			};
			var failures = new List<string>();
			foreach(string line in bad)
			{
				var warnings = new List<string>();
				List<EmitdefsEntry> e = Parse("// first\n" + line, warnings, "res/emitdefs.txt");
				if(e.Count != 0 || warnings.Count != 1 || !warnings[0].StartsWith("res/emitdefs.txt, line 2: "))
					failures.Add(line + " -> " + e.Count + " entries, warnings: " + string.Join(" | ", warnings));
			}
			Assert.Empty(failures);

			var w = new List<string>();
			List<EmitdefsEntry> kept = Parse("X distance 8 falloff 2 on walls", w);
			Assert.Single(kept);
			Assert.Single(w);
			Assert.Contains("falloff", w[0]);

			// Intensity is clamped like the baker's keys
			Assert.Equal(EmitdefsParser.MAX_INTENSITY, Parse("X intensity 99 distance 8 on walls")[0].Intensity);
		}

		#endregion

		#region Merge, animation and auto colour

		[Fact]
		public void LaterEntryWinsPerImageAndKindAndIntensityZeroRemovesIt()
		{
			var world = new World();
			Image lava = world.Add(EmissiveSurface.Flats, "LAVA1", "flats/lava1.png");
			Image panel = world.Add(EmissiveSurface.Walls, "PANEL");
			world.Add(EmissiveSurface.Walls, "LAVA1");

			// First container, then a later one naming the same flat by its path, and removing the panel
			var entries = Parse("LAVA1 color ff0000 distance 100\nPANEL distance 50 on walls", null, "base");
			entries.AddRange(Parse("flats/lava1.png color 00ff00 distance 200 on flats\nPANEL intensity 0 distance 50 on walls", null, "mod"));
			var warnings = new List<string>();
			EmissiveTable table = EmissiveTable.Build(entries, world.Resolver(), warnings.Add);
			Assert.Empty(warnings);

			EmissiveItem flat = table.Find(lava, EmissiveSurface.Flats);
			Assert.Equal(0x00FF00, flat.Color);
			Assert.Equal(200.0, flat.Distance);
			Assert.Equal("mod", flat.Entry.Source);
			Assert.Null(table.Find(panel, EmissiveSurface.Walls));
			// base's LAVA1 for walls is a different image (the texture) and nothing replaced it
			Assert.Equal(0xFF0000, table.Find(world.Names[0]["LAVA1"], EmissiveSurface.Walls).Color);
			Assert.Equal(2, table.EntryCount);
		}

		[Fact]
		public void UnknownNamesWarnAndTheRestStillLoad()
		{
			var world = new World();
			world.Add(EmissiveSurface.Flats, "LAVA1");
			var warnings = new List<string>();
			EmissiveTable table = Build(world, "LAVA1 distance 8 on flats\nNOPE distance 8 on walls", warnings);
			Assert.Equal(1, table.EntryCount);
			Assert.Single(warnings);
			Assert.Contains("unknown texture \"NOPE\"", warnings[0]);
		}

		[Fact]
		public void AnimationFramesShareTheEntryUnlessAFrameHasItsOwn()
		{
			var world = new World();
			Image f1 = world.Add(EmissiveSurface.Flats, "NUKAGE1");
			Image f2 = world.Add(EmissiveSurface.Flats, "NUKAGE2");
			Image f3 = world.Add(EmissiveSurface.Flats, "NUKAGE3");
			Image f4 = world.Add(EmissiveSurface.Flats, "NUKAGE4");
			world.Sequences[f1] = new[] { f1, f2, f3, f4 };
			world.Colors[f1] = 0x20FF40;
			world.Colors[f2] = 0xFF0000;

			EmissiveTable table = Build(world,
				"NUKAGE1 color auto intensity 3 distance 64 on flats\n" +
				"NUKAGE3 color 0000ff distance 32 on flats\n" +
				"NUKAGE4 intensity 0 distance 32 on flats");

			// The expanded frame takes the named image's auto colour, not its own
			Assert.Equal(new[] { f1 }, world.AutoAsked.ToArray());
			EmissiveItem frame2 = table.Find(f2, EmissiveSurface.Flats);
			Assert.True(frame2.FromAnimation);
			Assert.Equal(0x20FF40, frame2.Color);
			Assert.Equal(3.0, frame2.Intensity);
			Assert.Equal(0x0000FF, table.Find(f3, EmissiveSurface.Flats).Color);
			Assert.Null(table.Find(f4, EmissiveSurface.Flats));
			// Frames are not entries; they don't change the namespace either
			Assert.Equal(2, table.EntryCount);
			Assert.Null(table.Find(f2, EmissiveSurface.Walls));
		}

		[Fact]
		public void AutoColourWithNoVisiblePixelsSkipsTheEntry()
		{
			var world = new World();
			world.Add(EmissiveSurface.Walls, "GLASS");
			var warnings = new List<string>();
			EmissiveTable table = Build(world, "GLASS color auto distance 8 on walls", warnings);
			Assert.Same(EmissiveTable.Empty, table);
			Assert.Single(warnings);
		}

		[Fact]
		public void AutoColourScalesTheBrightestChannelToFullAndKeepsTheHue()
		{
			Assert.Null(EmissiveTable.ScaleToFull(0, 0, 0));
			foreach(int[] rgb in new[] { new[] { 128, 64, 0 }, new[] { 10, 20, 40 }, new[] { 255, 255, 255 }, new[] { 1, 0, 0 } })
			{
				int c = EmissiveTable.ScaleToFull(rgb[0], rgb[1], rgb[2]).Value;
				int[] got = { (c >> 16) & 255, (c >> 8) & 255, c & 255 };
				Assert.Equal(255, got.Max());
				for(int i = 0; i < 3; i++)
					Assert.True(Math.Abs(got[i] - rgb[i] * 255.0 / rgb.Max()) <= 0.5, string.Join(",", rgb) + " -> " + string.Join(",", got));
			}
		}

		#endregion

		#region Bake file

		[Fact]
		public void BakeFileMatchesEveryMapStringOfTheImageInItsOwnNamespace()
		{
			var world = new World();
			world.Add(EmissiveSurface.Flats, "LAVA1", "flats/lava1.png");
			world.Add(EmissiveSurface.Walls, "LAVA1");
			Image longwall = world.Add(EmissiveSurface.Walls, "textures/" + new string('p', 70) + ".png", "PANELX");
			EmissiveTable table = Build(world, "LAVA1 color ff6000 intensity 12 distance 200 on flats\nPANELX color 4080ff intensity 8 distance 192 on walls");

			var mapnames = new[] { Flat("lava1"), Flat("flats/lava1.png"), Wall("LAVA1"), Flat("FLAT19"), Wall("-"), Wall(""), Wall(world.Names[0].First(p => p.Value == longwall).Key), Flat("LAVA1") };
			int used;
			string text = table.BuildBakeFile(mapnames, null, out used);
			Assert.Equal(2, used);

			// The baker reads it with the same grammar: every listed string, its own kind, the entry's values
			var warnings = new List<string>();
			List<EmitdefsEntry> lines = Parse(text, warnings);
			Assert.Empty(warnings);
			string[] got = lines.Select(l => l.Kind + " " + l.Name + " " + l.Color.ToString("x6") + " " + l.Intensity + " " + l.Distance).OrderBy(s => s, StringComparer.Ordinal).ToArray();
			string cut = ("textures/" + new string('p', 70)).Substring(0, EmissiveTable.ZDRAY_NAME_LENGTH);
			// The baker compares without case, so the later "LAVA1" flat folds into "lava1"
			Assert.Equal(new[] { "Flats flats/lava1.png ff6000 12 200", "Flats lava1 ff6000 12 200", "Walls " + cut + " 4080ff 8 192" }
				.OrderBy(s => s, StringComparer.Ordinal).ToArray(), got);
		}

		[Fact]
		public void BakeFileIsNullWhenNoMapStringEmits()
		{
			var world = new World();
			world.Add(EmissiveSurface.Flats, "LAVA1");
			world.Add(EmissiveSurface.Walls, "LAVA1");
			EmissiveTable table = Build(world, "LAVA1 distance 8 on flats");
			int used;
			Assert.Null(table.BuildBakeFile(new[] { Wall("LAVA1"), Flat("OTHER") }, null, out used));
			Assert.Equal(0, used);
			Assert.Null(EmissiveTable.Empty.BuildBakeFile(new[] { Flat("LAVA1") }, null, out used));
		}

		#endregion

		#region Fingerprint and arguments

		[Fact]
		public void DigestIsNullWithoutATableAndFollowsEveryTableEdit()
		{
			Assert.Null(EmissiveTable.Empty.Digest);
			var world = new World();
			world.Add(EmissiveSurface.Flats, "LAVA1");
			world.Add(EmissiveSurface.Walls, "LAVA1");
			string a = Build(world, "LAVA1 color ff6000 distance 200 on flats").Digest;
			Assert.NotNull(a);
			Assert.Equal(a, Build(world, "// same table\n\"LAVA1\" distance 200 color ff6000 on flats").Digest);
			foreach(string edit in new[] { "LAVA1 color ff6001 distance 200 on flats", "LAVA1 color ff6000 distance 201 on flats",
				"LAVA1 color ff6000 intensity 2 distance 200 on flats", "LAVA1 color ff6000 distance 200 on walls" })
				Assert.NotEqual(a, Build(world, edit).Digest);
		}

		[Fact]
		public void CombineKeepsTheTextmapFingerprintWithoutATable()
		{
			Assert.Equal("ABC", BakeFingerprint.Combine("ABC", null));
			Assert.Equal("ABC", BakeFingerprint.Combine("ABC", ""));
			Assert.Null(BakeFingerprint.Combine(null, "D1"));
			string one = BakeFingerprint.Combine("ABC", "D1");
			Assert.NotEqual("ABC", one);
			Assert.NotEqual(one, BakeFingerprint.Combine("ABC", "D2"));
			Assert.NotEqual(one, BakeFingerprint.Combine("ABD", "D1"));
			Assert.Equal(one, BakeFingerprint.Combine("ABC", "D1"));
		}

		[Fact]
		public void ArgumentsPassTheEmissiveFileOnlyWhenGiven()
		{
			var settings = new LightmapBakeSettings();
			string without = LightmapBaker.BuildArguments(settings, 4, "MAP01", "in", "out");
			Assert.DoesNotContain("--emissive", without);
			Assert.Equal(without, LightmapBaker.BuildArguments(settings, 4, "MAP01", "in", "out", null));
			Assert.Equal("-c -X -m MAP01 -j 4 -S 1024 --emissive \"/t/e.txt\" -o \"out\" \"in\"",
				LightmapBaker.BuildArguments(settings, 4, "MAP01", "in", "out", "/t/e.txt"));
		}

		[Fact]
		public void AnimatorReturnsTheSequenceHoldingAFrame()
		{
			var animator = new TextureAnimator();
			animator.Add(10, new AnimDef(new[] { 10, 20, 30 }, new[] { 5 }, new[] { 5 }, false, AnimationType.Forward));
			animator.Add(40, new AnimDef(new[] { 40, 50 }, new[] { 5, 5 }, new[] { 5, 5 }, true, AnimationType.Forward));
			Assert.Equal(new[] { 10, 20, 30 }, animator.SequenceOf(20));
			Assert.Equal(new[] { 40, 50 }, animator.SequenceOf(40));
			Assert.Null(animator.SequenceOf(60));
			animator.SequenceOf(10)[0] = 99;
			Assert.Equal(10, animator.SequenceOf(10)[0]);
		}

		#endregion
	}
}
