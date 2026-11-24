using CodeImp.DoomBuilder;
using CodeImp.DoomBuilder.Compilers;
using CodeImp.DoomBuilder.IO;
using CodeImp.DoomBuilder.Map;
using System.Text;

namespace Builder.Tests
{
	public class LightmapBakeTests : IDisposable
	{
		private readonly string directory = Path.Combine(Path.GetTempPath(), "lightmap-bake-" + Guid.NewGuid().ToString("N"));

		public LightmapBakeTests() { Directory.CreateDirectory(directory); }

		public void Dispose() { Directory.Delete(directory, true); }

		#region Arguments and progress

		[Fact]
		public void ArgumentsUseZdbspStyleNodesAndCapThreads()
		{
			var settings = new LightmapBakeSettings();
			Assert.Equal("-c -X -m MAP01 -j 4 -S 1024 -o \"/t/out.wad\" \"/t/in.wad\"",
				LightmapBaker.BuildArguments(settings, 16, "MAP01", "/t/in.wad", "/t/out.wad"));
			Assert.Contains("-j 2 ", LightmapBaker.BuildArguments(settings, 2, "MAP01", "in", "out"));

			settings.Threads = 12;
			settings.CpuOnly = true;
			settings.TextureSize = 256;
			Assert.Equal("-c -X -m MAP01 -C -j 12 -S 256 -o \"out\" \"in\"",
				LightmapBaker.BuildArguments(settings, 2, "MAP01", "in", "out"));
		}

		[Fact]
		public void MissingBakerIsReported()
		{
			var settings = new LightmapBakeSettings();
			Assert.NotNull(settings.BakerProblem());
			settings.BakerPath = Path.Combine(directory, "zdray");
			Assert.StartsWith("The ZDRay executable was not found", settings.BakerProblem());
			File.WriteAllText(settings.BakerPath, "");
			Assert.Null(settings.BakerProblem());
		}

		// ZDRay's stdout and stderr for a two-room bake, progress segments ending in '\r'
		private const string STDOUT =
			"----MAP01----\n   Removed 1 line with 0 length.\nThing lights: 3\n\n" +
			"------------- Building side surfaces -------------\n" +
			"Sides: 1 / 20\rSides: 5 / 20\rSides: 20 / 20\nSide surfaces: 10\n\n" +
			"------------- Building subsector surfaces -------------\n" +
			"Subsectors: 1 / 7\rSubsectors: 7 / 7\nLeaf surfaces: 17\nSurfaces total: 24\n\nBuilding level mesh...\n\n" +
			"Ray tracing in progress...\n0.0%\t0/10587\r50.0%\t5293/10587\r100.0%\t10587/10587\n\n" +
			"Ray tracing complete\n   1.686 seconds.\n\nTotal time: 1.686 seconds.\n";
		private const string STDERR = "   BSP:   0.0%\r   BSP: 100.0%\r";

		private static LightmapBakeProgress FeedUpTo(string marker)
		{
			var progress = new LightmapBakeProgress();
			progress.Feed(STDOUT.Substring(0, STDOUT.IndexOf(marker, StringComparison.Ordinal) + marker.Length), false);
			return progress;
		}

		[Fact]
		public void ProgressFollowsEachStage()
		{
			LightmapBakeProgress p = FeedUpTo("Sides: 5 / 20\r");
			Assert.Equal("Building side surfaces", p.Stage);
			Assert.Equal(0.25, p.Fraction);

			p = FeedUpTo("Subsectors: 7 / 7\n");
			Assert.Equal("Building subsector surfaces", p.Stage);
			Assert.Equal(1.0, p.Fraction);

			p = FeedUpTo("5293/10587\r");
			Assert.Equal("Ray tracing", p.Stage);
			Assert.True(Math.Abs(p.Fraction - 0.5) < 0.001, p.Fraction.ToString());

			p = FeedUpTo("Ray tracing complete\n");
			Assert.Equal("Writing output", p.Stage);
			Assert.Equal(-1.0, p.Fraction);

			p = new LightmapBakeProgress();
			p.Feed(STDERR, true);
			Assert.Equal("Building nodes", p.Stage);
			Assert.Equal(1.0, p.Fraction);
		}

		[Fact]
		public void ChunkBoundariesDoNotChangeTheResult()
		{
			var whole = new LightmapBakeProgress();
			whole.Feed(STDOUT, false);
			whole.End();

			for(int size = 1; size <= 13; size++)
			{
				var chunked = new LightmapBakeProgress();
				for(int i = 0; i < STDOUT.Length; i += size)
				{
					chunked.Feed(STDOUT.Substring(i, Math.Min(size, STDOUT.Length - i)), false);
					// Interleaved stderr must not split a stdout segment
					if(i == 40) chunked.Feed("   BSP:  5.0%\r", true);
				}
				chunked.End();
				Assert.Equal(string.Join("|", whole.LastLines), string.Join("|", chunked.LastLines));
			}

			// Only the last lines are kept, and progress segments never appear in them
			Assert.Equal(8, whole.LastLines.Length);
			Assert.Equal("Total time: 1.686 seconds.", whole.LastLines.Last());
			Assert.False(whole.LastLines.Any(l => l.StartsWith("Sides:") || l.Contains("%")));
		}

		[Fact]
		public void UnterminatedLastLineIsKeptAtEnd()
		{
			var p = new LightmapBakeProgress();
			p.Feed("Could not open input file", false);
			Assert.Empty(p.LastLines);
			p.End();
			Assert.Equal(new[] { "Could not open input file" }, p.LastLines);
		}

		#endregion

		#region Fingerprint and thing swap

		private const string GEOMETRY =
			"namespace = \"zdoom\";\n" +
			"vertex // 0\n{\nx = 0.0;\ny = 0.0;\n}\n" +
			"vertex // 1\n{\nx = 64.0;\ny = 0.0;\nuser_v = 3;\n}\n" +
			"linedef // 0\n{\nv1 = 0;\nv2 = 1;\nsidefront = 0;\n}\n" +
			"sidedef // 0\n{\nsector = 0;\n}\n" +
			"sector // 0\n{\ntexturefloor = \"FLAT\";\ntextureceiling = \"CEIL\";\nheightceiling = 128;\n}\n";

		private static string Thing(int type, double x, string extra = "")
		{
			return "thing\n{\nx = " + x.ToString("0.0", System.Globalization.CultureInfo.InvariantCulture) + ";\ny = 32.0;\ntype = " + type + ";\n" + extra + "}\n";
		}

		private static string[] Lines(string text) { return BakedTextmap.Lines(text); }

		private static readonly ICollection<int> EXCLUDED = BakeFingerprint.ExcludedTypes(32000);

		private static string Fingerprint(string textmap) { return BakeFingerprint.Compute(Lines(textmap), EXCLUDED); }

		[Fact]
		public void FingerprintIgnoresOnlyTestStartsAndTheVisualCamera()
		{
			string baseline = Fingerprint(GEOMETRY + Thing(1, 16) + Thing(9876, 32) + Thing(32000, 48));
			Assert.NotNull(baseline);

			// Player 1 start moved, added or removed, the 3D-mode camera moved, index comments changed
			Assert.Equal(baseline, Fingerprint(GEOMETRY + Thing(1, 40) + Thing(9876, 32) + Thing(32000, 48)));
			Assert.Equal(baseline, Fingerprint(GEOMETRY + Thing(9876, 32) + Thing(32000, 48)));
			Assert.Equal(baseline, Fingerprint(GEOMETRY + Thing(9876, 32) + Thing(32000, 48) + Thing(1, 8)));
			Assert.Equal(baseline, Fingerprint(GEOMETRY + Thing(1, 16) + Thing(9876, 32) + Thing(32000, 96)));
			Assert.Equal(baseline, Fingerprint(GEOMETRY.Replace("// 1", "// 7") + Thing(1, 16) + Thing(9876, 32) + Thing(32000, 48)));

			// Any other thing, a vertex, a custom field
			Assert.NotEqual(baseline, Fingerprint(GEOMETRY + Thing(1, 16) + Thing(9876, 33) + Thing(32000, 48)));
			Assert.NotEqual(baseline, Fingerprint(GEOMETRY + Thing(1, 16) + Thing(9876, 32) + Thing(3004, 0) + Thing(32000, 48)));
			Assert.NotEqual(baseline, Fingerprint(GEOMETRY.Replace("x = 64.0;", "x = 65.0;") + Thing(1, 16) + Thing(9876, 32) + Thing(32000, 48)));
			Assert.NotEqual(baseline, Fingerprint(GEOMETRY.Replace("user_v = 3;", "user_v = 4;") + Thing(1, 16) + Thing(9876, 32) + Thing(32000, 48)));

			// No 3D-mode camera type configured: only player 1 starts are left out
			Assert.Equal(new[] { 1 }, BakeFingerprint.ExcludedTypes(0).ToArray());
			Assert.Null(Fingerprint("vertex { x = bogus; }"));
		}

		private static UniversalParser Parse(string text)
		{
			var parser = new UniversalParser();
			Assert.True(parser.InputConfiguration(Lines(text)), parser.ErrorDescription);
			return parser;
		}

		private static string Entry(UniversalEntry e)
		{
			var single = new UniversalParser();
			single.Root.Add(e);
			return single.OutputConfiguration();
		}

		[Fact]
		public void ThingSwapKeepsTheBakedGeometryAndTakesTheCurrentThings()
		{
			// The baker renumbered: vertices swapped, a zero-length line and an unused sector gone, things after sectors
			string baked =
				"namespace = \"zdoom\";\n" +
				"vertex\n{\nx = 64.0;\ny = 0.0;\nuser_v = 3;\n}\nvertex\n{\nx = 0.0;\ny = 0.0;\n}\n" +
				"linedef\n{\nv1 = 1;\nv2 = 0;\nsidefront = 0;\n}\nsidedef\n{\nsector = 0;\n}\n" +
				"sector\n{\ntexturefloor = \"FLAT\";\ntextureceiling = \"CEIL\";\n}\n" +
				Thing(1, 16) + Thing(9876, 32) + Thing(3004, 0) +
				"customblock\n{\nfoo = 1;\n}\n";
			// Since the bake: the start moved, the monster removed, a thing with a custom field added, order changed
			string current = GEOMETRY + Thing(9876, 32) + Thing(2001, 8, "user_t = \"x\";\n") + Thing(1, 40);

			string swapped = BakedTextmap.ReplaceThings(Lines(baked), Lines(current));
			Assert.NotNull(swapped);

			List<UniversalEntry> result = Parse(swapped).Root;
			List<UniversalEntry> bakedentries = Parse(baked).Root;
			List<UniversalEntry> currentthings = Parse(current).Root.Where(BakeFingerprint.IsThing).ToList();

			string[] expected = bakedentries.Where(e => !BakeFingerprint.IsThing(e)).Select(Entry).ToArray();
			Assert.Equal(expected, result.Where(e => !BakeFingerprint.IsThing(e)).Select(Entry).ToArray());
			Assert.Equal(currentthings.Select(Entry).ToArray(), result.Where(BakeFingerprint.IsThing).Select(Entry).ToArray());

			// The things sit where the baked ones started: after the sector, before the unknown block
			string[] keys = result.Select(e => e.Key).ToArray();
			Assert.Equal(new[] { "namespace", "vertex", "vertex", "linedef", "sidedef", "sector", "thing", "thing", "thing", "customblock" }, keys);

			// A bake with no things gets the current ones at the end
			string nothings = BakedTextmap.ReplaceThings(Lines(GEOMETRY), Lines(current));
			Assert.Equal(3, Parse(nothings).Root.Count(BakeFingerprint.IsThing));
			Assert.True(BakeFingerprint.IsThing(Parse(nothings).Root.Last()));

			Assert.Null(BakedTextmap.ReplaceThings(Lines("vertex { x = bogus; }"), Lines(current)));
		}

		#endregion

		#region Output validation

		private WAD CreateWad(string[] names, Func<string, byte[]> data)
		{
			var wad = new WAD(Path.Combine(directory, Guid.NewGuid().ToString("N") + ".wad"));
			for(int i = 0; i < names.Length; i++)
			{
				byte[] bytes = data(names[i]);
				Lump lump = wad.Insert(names[i], i, bytes.Length);
				lump.Stream.Write(bytes, 0, bytes.Length);
			}
			return wad;
		}

		private static byte[] GoodLightmap()
		{
			return LightmapRetentionTests.Zlib(LightmapRetentionTests.Header(0, 16, 1, 1, 4, 0, 1), 20 + 4 * 8 + 16 * 16 * 6);
		}

		private string Validate(string nodesmagic, byte[] lightmap, params string[] names)
		{
			using(WAD wad = CreateWad(names, n => n == "ZNODES" ? Encoding.ASCII.GetBytes(nodesmagic + "rest")
				: n == "LIGHTMAP" ? lightmap : n == "TEXTMAP" ? new byte[] { 0x20 } : new byte[0]))
			{
				LightmapLump header;
				return MapManager.ValidateBakeOutput(wad, "MAP01", out header);
			}
		}

		[Fact]
		public void BakeOutputNeedsTextmapGlNodesAndALitLightmap()
		{
			string[] full = { "MAP01", "TEXTMAP", "ZNODES", "LIGHTMAP", "ENDMAP" };
			Assert.Null(Validate("XGLN", GoodLightmap(), full));
			Assert.Null(Validate("ZGL3", GoodLightmap(), full));

			Assert.Equal("the output ZNODES are not GL nodes", Validate("XNOD", GoodLightmap(), full));
			Assert.Equal("the output map has no LIGHTMAP", Validate("XGLN", GoodLightmap(), "MAP01", "TEXTMAP", "ZNODES", "ENDMAP"));
			Assert.Equal("the output map has no LIGHTMAP", Validate("XGLN", GoodLightmap(), "MAP01", "TEXTMAP", "ZNODES", "ENDMAP", "LIGHTMAP"));
			Assert.Equal("the output map has no ZNODES", Validate("XGLN", GoodLightmap(), "MAP01", "TEXTMAP", "LIGHTMAP", "ENDMAP"));
			Assert.Equal("the output map has no TEXTMAP", Validate("XGLN", GoodLightmap(), "MAP01", "ZNODES", "LIGHTMAP", "ENDMAP"));
			Assert.Equal("the output has no map MAP01", Validate("XGLN", GoodLightmap(), "MAP02", "TEXTMAP", "ZNODES", "LIGHTMAP", "ENDMAP"));

			byte[] unlit = LightmapRetentionTests.Zlib(LightmapRetentionTests.Header(0, 16, 1, 0, 0, 0, 1), 16 * 16 * 6);
			Assert.StartsWith("the bake lit no surfaces", Validate("XGLN", unlit, full));
			byte[] truncated = LightmapRetentionTests.Zlib(LightmapRetentionTests.Header(0, 16, 1, 1, 4, 0, 1), 10);
			Assert.StartsWith("the output LIGHTMAP is unreadable: truncated", Validate("XGLN", truncated, full));
		}

		#endregion

		#region Test launch

		[Theory]
		[InlineData("-file x.wad", true, "-enablelightmaps", "-file x.wad -enablelightmaps")]
		[InlineData("-file x.wad", false, "-enablelightmaps", "-file x.wad")]
		[InlineData("-file x.wad", true, "", "-file x.wad")]
		[InlineData("-file x.wad -enablelightmaps", true, "-enablelightmaps", "-file x.wad -enablelightmaps")]
		[InlineData("-file x-enablelightmaps.wad", true, "-enablelightmaps", "-file x-enablelightmaps.wad -enablelightmaps")]
		[InlineData("", true, "-enablelightmaps", "-enablelightmaps")]
		public void TestLaunchAddsTheLightmapParameterOnce(string args, bool haslightmap, string parameter, string expected)
		{
			Assert.Equal(expected, Launcher.AddLightmapParameter(args, haslightmap, parameter));
		}

		#endregion
	}
}
