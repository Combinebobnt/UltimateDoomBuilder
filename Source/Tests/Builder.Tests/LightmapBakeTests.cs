using CodeImp.DoomBuilder;
using CodeImp.DoomBuilder.Compilers;
using CodeImp.DoomBuilder.Config;
using CodeImp.DoomBuilder.IO;
using CodeImp.DoomBuilder.Map;
using System.Text;
using System.Windows.Forms;

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

		private static string Fingerprint(string textmap) { return BakeFingerprint.Compute(Lines(textmap)); }

		[Fact]
		public void FingerprintIgnoresThingsTheBakerDoesNotRead()
		{
			string lights = Thing(9876, 32) + Thing(9881, 64, "angle = 90;\n");
			string baseline = Fingerprint(GEOMETRY + Thing(1, 16) + Thing(9876, 32) + Thing(3004, 40) + Thing(9881, 64, "angle = 90;\n") + Thing(2011, 80) + Thing(32000, 48));
			Assert.NotNull(baseline);

			// A monster, an item, player 1 start and the 3D-mode camera moved, added or removed
			Assert.Equal(baseline, Fingerprint(GEOMETRY + lights));
			Assert.Equal(baseline, Fingerprint(GEOMETRY + Thing(1, 24) + Thing(9876, 32) + Thing(3004, 8) + Thing(9881, 64, "angle = 90;\n") + Thing(2011, 0) + Thing(32000, 96)));
			Assert.Equal(baseline, Fingerprint(GEOMETRY + Thing(3004, 0) + Thing(2011, 0) + lights + Thing(1, 8) + Thing(32000, 8) + Thing(3004, 99)));
			// The monster between the two lights deleted
			Assert.Equal(baseline, Fingerprint(GEOMETRY + Thing(1, 16) + Thing(9876, 32) + Thing(9881, 64, "angle = 90;\n") + Thing(2011, 80) + Thing(32000, 48)));
			// Index comments changed
			Assert.Equal(baseline, Fingerprint(GEOMETRY.Replace("// 1", "// 7") + lights));

			// A vertex, a custom field, the lights' order
			Assert.NotEqual(baseline, Fingerprint(GEOMETRY.Replace("x = 64.0;", "x = 65.0;") + lights));
			Assert.NotEqual(baseline, Fingerprint(GEOMETRY.Replace("user_v = 3;", "user_v = 4;") + lights));
			Assert.NotEqual(baseline, Fingerprint(GEOMETRY + Thing(9881, 64, "angle = 90;\n") + Thing(9876, 32)));

			Assert.Null(Fingerprint("vertex { x = bogus; }"));
		}

		[Theory]
		[InlineData(9875)]
		[InlineData(9876)]
		[InlineData(9881)]
		[InlineData(9890)]
		[InlineData(9300)]
		[InlineData(3000)]
		[InlineData(1504)]
		[InlineData(9500)]
		public void FingerprintTracksEveryBakerThing(int type)
		{
			Assert.True(BakeFingerprint.BAKER_THING_TYPES.Contains(type));
			string others = Thing(3004, 40) + Thing(9876, 200);
			string baseline = Fingerprint(GEOMETRY + Thing(type, 16, "angle = 0;\narg0 = 1;\n") + others);
			Assert.NotNull(baseline);

			// Moved, a field changed, removed, added again
			Assert.NotEqual(baseline, Fingerprint(GEOMETRY + Thing(type, 17, "angle = 0;\narg0 = 1;\n") + others));
			Assert.NotEqual(baseline, Fingerprint(GEOMETRY + Thing(type, 16, "angle = 0;\narg0 = 2;\n") + others));
			Assert.NotEqual(baseline, Fingerprint(GEOMETRY + Thing(type, 16, "angle = 45;\narg0 = 1;\n") + others));
			Assert.NotEqual(baseline, Fingerprint(GEOMETRY + others));
			Assert.NotEqual(baseline, Fingerprint(GEOMETRY + Thing(type, 16, "angle = 0;\narg0 = 1;\n") + others + Thing(type, 96)));

			// Retyped from a monster, or to one
			Assert.NotEqual(Fingerprint(GEOMETRY + Thing(3004, 16) + others), Fingerprint(GEOMETRY + Thing(type, 16) + others));
			Assert.NotEqual(baseline, Fingerprint(GEOMETRY + Thing(3004, 16, "angle = 0;\narg0 = 1;\n") + others));
		}

		[Fact]
		public void FingerprintTracksHexenPolySpawnsOnlyWithAnAnchor()
		{
			foreach(int spawn in new[] { 3001, 3002 })
			{
				Assert.True(BakeFingerprint.HEXEN_POLY_SPAWN_TYPES.Contains(spawn));
				Assert.False(BakeFingerprint.BAKER_THING_TYPES.Contains(spawn));

				// No anchor: Doom's Imp and Demon, moved, removed or added
				string doom = Fingerprint(GEOMETRY + Thing(9876, 32) + Thing(spawn, 16, "angle = 1;\n"));
				Assert.Equal(doom, Fingerprint(GEOMETRY + Thing(9876, 32) + Thing(spawn, 80, "angle = 1;\n")));
				Assert.Equal(doom, Fingerprint(GEOMETRY + Thing(9876, 32)));

				// With an anchor: polyobject spawn spots
				string anchor = Thing(3000, 48, "angle = 1;\n");
				string hexen = Fingerprint(GEOMETRY + Thing(9876, 32) + Thing(spawn, 16, "angle = 1;\n") + anchor);
				Assert.NotEqual(hexen, Fingerprint(GEOMETRY + Thing(9876, 32) + Thing(spawn, 80, "angle = 1;\n") + anchor));
				Assert.NotEqual(hexen, Fingerprint(GEOMETRY + Thing(9876, 32) + Thing(spawn, 16, "angle = 2;\n") + anchor));
				Assert.NotEqual(hexen, Fingerprint(GEOMETRY + Thing(9876, 32) + anchor));

				// The first anchor added
				Assert.NotEqual(doom, hexen);
			}
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

		#region Applying a bake

		private static byte[] Bytes(Lump lump)
		{
			lump.Stream.Seek(0, SeekOrigin.Begin);
			return lump.Stream.ReadAllBytes();
		}

		[Fact]
		public void BakeReplacesEveryNodebuildLumpAndKeepsTheRest()
		{
			Dictionary<string, MapLumpInfo> lumps = LightmapRetentionTests.MapLumps("UZDoom_DoomUDMF.cfg");
			byte[] lightmap = GoodLightmap();
			using(WAD temp = CreateWad(new[] { "TEMPMAP", "TEXTMAP", "BEHAVIOR", "ZNODES", "BLOCKMAP", "REJECT", "LIGHTMAP", "ENDMAP" }, n => Encoding.ASCII.GetBytes("old " + n)))
			using(WAD baked = CreateWad(new[] { "MAP01", "TEXTMAP", "ZNODES", "LIGHTMAP", "ENDMAP" }, n => n == "LIGHTMAP" ? lightmap : Encoding.ASCII.GetBytes("baked " + n)))
			{
				byte[] textmap = Encoding.ASCII.GetBytes("swapped");
				Assert.True(MapManager.CopyBakeLumps(temp, "TEMPMAP", baked, "MAP01", lumps, textmap));

				Assert.Equal(new[] { "TEMPMAP", "TEXTMAP", "ZNODES", "LIGHTMAP", "BEHAVIOR", "ENDMAP" }, temp.Lumps.Select(l => l.Name).ToArray());
				Assert.Equal(textmap, Bytes(temp.Lumps[1]));
				Assert.Equal(Encoding.ASCII.GetBytes("baked ZNODES"), Bytes(temp.Lumps[2]));
				Assert.Equal(lightmap, Bytes(temp.Lumps[3]));
				Assert.Equal(Encoding.ASCII.GetBytes("old BEHAVIOR"), Bytes(temp.Lumps[4]));
				Assert.Equal(3, MapManager.FindLightmapIndex(temp, "TEMPMAP"));
			}
		}

		[Fact]
		public void BakeWithoutATextmapChangesNothing()
		{
			Dictionary<string, MapLumpInfo> lumps = LightmapRetentionTests.MapLumps("UZDoom_DoomUDMF.cfg");
			string[] names = { "TEMPMAP", "TEXTMAP", "ZNODES", "ENDMAP" };
			using(WAD temp = CreateWad(names, n => Encoding.ASCII.GetBytes("old " + n)))
			using(WAD baked = CreateWad(new[] { "MAP01", "ZNODES", "LIGHTMAP", "ENDMAP" }, n => Encoding.ASCII.GetBytes("baked " + n)))
			{
				Assert.False(MapManager.CopyBakeLumps(temp, "TEMPMAP", baked, "MAP01", lumps, new byte[] { 1 }));
				Assert.False(MapManager.CopyBakeLumps(temp, "TEMPMAP", baked, "MAP02", lumps, new byte[] { 1 }));
				Assert.Equal(names, temp.Lumps.Select(l => l.Name).ToArray());
				Assert.Equal(Encoding.ASCII.GetBytes("old TEXTMAP"), Bytes(temp.Lumps[1]));
			}
		}

		[Fact]
		public void UnchangedSaveIsReroutedOnlyToBringBackAMatchingBake()
		{
			foreach(SavePurpose purpose in Enum.GetValues(typeof(SavePurpose)))
			{
				bool expected = (purpose != SavePurpose.Autosave);
				Assert.Equal(expected, MapManager.ShouldRerouteToBake(purpose, true, false, false));
				Assert.False(MapManager.ShouldRerouteToBake(purpose, false, false, false));
				Assert.False(MapManager.ShouldRerouteToBake(purpose, true, true, false));
				Assert.False(MapManager.ShouldRerouteToBake(purpose, true, false, true));
			}
		}

		[Fact]
		public void MissLogShowsTheStartOfBothFingerprints()
		{
			Assert.Equal("0123456789AB", MapManager.ShortFingerprint("0123456789ABCDEF"));
			Assert.Equal("ABC", MapManager.ShortFingerprint("ABC"));
			Assert.Equal("unparsed", MapManager.ShortFingerprint(null));
		}

		#endregion

		#region Seeding from the opened map

		private static byte[] Ascii(string text) { return Encoding.ASCII.GetBytes(text); }

		private string SeedPath() { return Path.Combine(directory, Guid.NewGuid().ToString("N") + ".wad"); }

		[Theory]
		[InlineData(true, false, true, true)]
		[InlineData(true, true, true, false)]
		[InlineData(true, false, false, false)]
		[InlineData(true, true, false, false)]
		[InlineData(false, false, true, false)]
		[InlineData(false, true, true, false)]
		[InlineData(false, false, false, false)]
		[InlineData(false, true, false, false)]
		public void OnlyAnOpenedUdmfMapWithALightmapIsSeeded(bool udmf, bool maprestored, bool haslightmap, bool expected)
		{
			Assert.Equal(expected, MapManager.ShouldSeedLightmapBake(udmf, maprestored, haslightmap));
		}

		[Fact]
		public void SeedHoldsTheOpenedNodebuildLumpsAsABakeOutput()
		{
			Dictionary<string, MapLumpInfo> lumps = LightmapRetentionTests.MapLumps("UZDoom_DoomUDMF.cfg");
			byte[] lightmap = GoodLightmap();
			Func<string, byte[]> data = n => n == "ZNODES" ? Ascii("XGLNopened") : n == "LIGHTMAP" ? lightmap : Ascii("opened " + n);
			string[] names = { "TEMPMAP", "TEXTMAP", "BEHAVIOR", "ZNODES", "BLOCKMAP", "LIGHTMAP", "ENDMAP" };
			using(WAD temp = CreateWad(names, data))
			{
				string path = SeedPath();
				LightmapLump header;
				Assert.Null(MapManager.WriteSeedWad(temp, "TEMPMAP", path, lumps, out header));
				Assert.NotNull(header);
				Assert.Equal(1u, header.SurfaceCount);
				Assert.Equal(names, temp.Lumps.Select(l => l.Name).ToArray());

				// A stray BLOCKMAP goes too: a hit writes what the unchanged branch would
				using(WAD seed = new WAD(path, true))
				{
					Assert.Equal(new[] { "MAP01", "TEXTMAP", "ZNODES", "BLOCKMAP", "LIGHTMAP", "ENDMAP" }, seed.Lumps.Select(l => l.Name).ToArray());
					for(int i = 1; i < 5; i++) Assert.Equal(data(seed.Lumps[i].Name), Bytes(seed.Lumps[i]));
					Assert.Equal(0, seed.Lumps[0].Length);
					Assert.Equal(0, seed.Lumps[5].Length);
				}
			}
		}

		private string Seed(string nodesmagic, byte[] lightmap, string path, params string[] names)
		{
			Dictionary<string, MapLumpInfo> lumps = LightmapRetentionTests.MapLumps("UZDoom_DoomUDMF.cfg");
			using(WAD temp = CreateWad(names, n => n == "ZNODES" ? Ascii(nodesmagic + "rest") : n == "LIGHTMAP" ? lightmap : Ascii("opened " + n)))
			{
				LightmapLump header;
				string error = MapManager.WriteSeedWad(temp, "TEMPMAP", path, lumps, out header);
				if(error != null) Assert.Null(header);
				return error;
			}
		}

		[Fact]
		public void SeedIsRefusedAndRemovedWhenTheOpenedLumpsDoNotValidate()
		{
			string[] full = { "TEMPMAP", "TEXTMAP", "ZNODES", "LIGHTMAP", "ENDMAP" };
			byte[] unlit = LightmapRetentionTests.Zlib(LightmapRetentionTests.Header(0, 16, 1, 0, 0, 0, 1), 16 * 16 * 6);
			byte[] truncated = LightmapRetentionTests.Zlib(LightmapRetentionTests.Header(0, 16, 1, 1, 4, 0, 1), 10);
			var cases = new List<Tuple<string, Func<string, string>>>
			{
				Tuple.Create("the output map has no LIGHTMAP", (Func<string, string>)(p => Seed("XGLN", GoodLightmap(), p, "TEMPMAP", "TEXTMAP", "ZNODES", "ENDMAP"))),
				Tuple.Create("the output map has no ZNODES", (Func<string, string>)(p => Seed("XGLN", GoodLightmap(), p, "TEMPMAP", "TEXTMAP", "LIGHTMAP", "ENDMAP"))),
				Tuple.Create("the output ZNODES are not GL nodes", (Func<string, string>)(p => Seed("XNOD", GoodLightmap(), p, full))),
				Tuple.Create("the output LIGHTMAP is unreadable: truncated", (Func<string, string>)(p => Seed("XGLN", truncated, p, full))),
				Tuple.Create("the bake lit no surfaces", (Func<string, string>)(p => Seed("XGLN", unlit, p, full))),
				Tuple.Create("the map has no TEMPMAP", (Func<string, string>)(p => Seed("XGLN", GoodLightmap(), p, "MAP01", "TEXTMAP", "ZNODES", "LIGHTMAP", "ENDMAP"))),
			};
			foreach(Tuple<string, Func<string, string>> c in cases)
			{
				string path = SeedPath();
				string error = c.Item2(path);
				Assert.NotNull(error);
				Assert.StartsWith(c.Item1, error);
				Assert.False(File.Exists(path), c.Item1);
			}
			Assert.Null(Seed("XGLN", GoodLightmap(), SeedPath(), full));
		}

		[Fact]
		public void SeededBakeBringsBackTheOpenedLumpsAfterTheMapIsRewritten()
		{
			Dictionary<string, MapLumpInfo> lumps = LightmapRetentionTests.MapLumps("UZDoom_DoomUDMF.cfg");
			string opened = GEOMETRY + Thing(1, 16) + Thing(9876, 32);
			byte[] znodes = Ascii("XGLNopened nodes");
			byte[] lightmap = GoodLightmap();
			Func<string, byte[]> data = n => n == "TEXTMAP" ? UniversalStreamReader.ENCODING.GetBytes(opened)
				: n == "ZNODES" ? znodes : n == "LIGHTMAP" ? lightmap : Ascii("opened " + n);
			using(WAD temp = CreateWad(new[] { "TEMPMAP", "TEXTMAP", "BEHAVIOR", "ZNODES", "LIGHTMAP", "ENDMAP" }, data))
			{
				string path = SeedPath();
				LightmapLump header;
				Assert.Null(MapManager.WriteSeedWad(temp, "TEMPMAP", path, lumps, out header));

				// The save rewrites the TEXTMAP (same map, other formatting), dropping the LIGHTMAP; a nodes rebuild replaced ZNODES
				string rewritten = Parse(opened).OutputConfiguration();
				Assert.NotEqual(opened, rewritten);
				Assert.Equal(Fingerprint(opened), Fingerprint(rewritten));
				ReplaceLump(temp, "TEXTMAP", UniversalStreamReader.ENCODING.GetBytes(rewritten));
				ReplaceLump(temp, "ZNODES", Ascii("XGLNrebuilt"));
				Assert.NotNull(MapManager.RemoveStaleLightmap(temp, "TEMPMAP"));

				using(WAD seed = new WAD(path, true))
				{
					string swapped = BakedTextmap.ReplaceThings(BakedTextmap.ReadLines(seed.Lumps[seed.FindLumpIndex("TEXTMAP")]), Lines(rewritten));
					Assert.NotNull(swapped);
					Assert.True(MapManager.CopyBakeLumps(temp, "TEMPMAP", seed, "MAP01", lumps, UniversalStreamReader.ENCODING.GetBytes(swapped)));
				}

				Assert.Equal(new[] { "TEMPMAP", "TEXTMAP", "ZNODES", "LIGHTMAP", "BEHAVIOR", "ENDMAP" }, temp.Lumps.Select(l => l.Name).ToArray());
				Assert.Equal(znodes, Bytes(temp.Lumps[2]));
				Assert.Equal(lightmap, Bytes(temp.Lumps[3]));
				Assert.Equal(Ascii("opened BEHAVIOR"), Bytes(temp.Lumps[4]));
				string result = UniversalStreamReader.ENCODING.GetString(Bytes(temp.Lumps[1]));
				Assert.Equal(Parse(opened).Root.Select(Entry).ToArray(), Parse(result).Root.Select(Entry).ToArray());
			}
		}

		private static void ReplaceLump(WAD wad, string name, byte[] data)
		{
			int index = wad.FindLumpIndex(name);
			wad.RemoveAt(index);
			Lump lump = wad.Insert(name, index, data.Length);
			lump.Stream.Seek(0, SeekOrigin.Begin);
			lump.Stream.Write(data, 0, data.Length);
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

		[Theory]
		[InlineData(true, false, false, "None")]
		[InlineData(true, true, false, "None")]
		[InlineData(true, true, true, "None")]
		[InlineData(false, false, false, "None")]
		[InlineData(false, false, true, "None")]
		[InlineData(false, true, false, "Ask")]
		[InlineData(false, true, true, "Warn")]
		public void TestAsksOncePerStaleBakeThenWarns(bool savedwithlightmap, bool stale, bool asked, string expected)
		{
			Assert.Equal(expected, Launcher.StaleLightmapNotice(savedwithlightmap, stale, asked).ToString());
		}

		[Fact]
		public void StaleQuestionOffersARebakeOnlyWhenTheMapCanBeBaked()
		{
			MessageBoxButtons buttons;
			MessageBoxDefaultButton defaultbutton;

			string withbake = Launcher.StaleLightmapQuestion(true, out buttons, out defaultbutton);
			Assert.Equal(MessageBoxButtons.YesNoCancel, buttons);
			Assert.Equal(MessageBoxDefaultButton.Button2, defaultbutton); // No: test without

			string withoutbake = Launcher.StaleLightmapQuestion(false, out buttons, out defaultbutton);
			Assert.Equal(MessageBoxButtons.OKCancel, buttons);
			Assert.Equal(MessageBoxDefaultButton.Button1, defaultbutton); // OK: test without

			Assert.StartsWith(withoutbake.Substring(0, withoutbake.IndexOf('\n')), withbake);
			Assert.Contains("Yes", withbake);
			Assert.DoesNotContain("Yes", withoutbake);
		}

		#endregion
	}
}
