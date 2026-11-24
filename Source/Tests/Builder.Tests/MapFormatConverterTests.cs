using CodeImp.DoomBuilder.Config;
using CodeImp.DoomBuilder.Map.Conversion;

namespace Builder.Tests
{
	public class MapFormatConverterTests
	{
		private static string ConfigFolder
		{
			get { return Path.Combine(Path.GetDirectoryName(typeof(XlatTable).Assembly.Location), "Configurations"); }
		}

		private static readonly Dictionary<string, FormatProfile> profiles = new Dictionary<string, FormatProfile>();

		private static FormatProfile Profile(string name)
		{
			lock(profiles)
			{
				FormatProfile p;
				if(!profiles.TryGetValue(name, out p))
					profiles[name] = p = FormatProfile.FromConfigurationFile(Path.Combine(ConfigFolder, name + ".cfg"), Path.Combine(ConfigFolder, "Xlat"));
				return p;
			}
		}

		private static LineRecord DoomRecord(int type, int tag, int bits = 0)
		{
			return new LineRecord { Action = type, Tags = new List<int> { tag }, Bits = bits };
		}

		private static MapRecords Convert(MapRecords map, string from, string to, out ConversionReport report)
		{
			report = new ConversionReport();
			return MapFormatConverter.Convert(map, Profile(from), Profile(to), report);
		}

		[Fact]
		public void Profiles_ReadFormatTablesAndWhatTheTargetOffers()
		{
			FormatProfile gz = Profile("GZDoom_DoomDoom"), vanilla = Profile("Doom_DoomDoom"), boom = Profile("Boom_DoomDoom");
			Assert.Equal(MapFormat.Doom, gz.Format);
			Assert.Equal(MapFormat.Hexen, Profile("GZDoom_DoomHexen").Format);
			Assert.Equal(MapFormat.Udmf, Profile("GZDoom_DoomUDMF").Format);
			Assert.NotEmpty(Profile("GZDoom_DoomHexen").LineFlagTable);
			Assert.NotEmpty(gz.ThingFlagTable);

			Assert.True(gz.OffersLineType(340) && gz.OffersLineType(0x3c01), "GZDoom reads every xlat type");
			Assert.True(boom.OffersLineType(260) && boom.OffersLineType(0x3c01), "Boom has its types and generalized ranges");
			Assert.False(boom.OffersLineType(340), "Boom has no ZDoom slope type");
			Assert.True(vanilla.OffersLineType(1) && !vanilla.OffersLineType(260) && !vanilla.OffersLineType(0x3c01), "vanilla has neither");

			Assert.Equal(0x3fff, gz.DoomLineFlagBits); // doom.txt's bits 0-13
			Assert.Equal(0x1ff, vanilla.DoomLineFlagBits);
			Assert.True(boom.OffersSectorType(9 | 0x80) && !vanilla.OffersSectorType(9 | 0x80), "generalized sector bits");
		}

		// Every Doom type through UDMF and Hexen and back: the same line, or a tie the engine reads the same, or a reported loss
		[Fact]
		public void DoomRoundTrip_EveryTypeKeepsItsLineOrIsReported()
		{
			FormatProfile doom = Profile("GZDoom_DoomDoom");
			LineXlat x = doom.Lines;
			List<int> types = new List<int>(x.Table.SimpleLines.Keys);
			foreach(XlatBoomRange b in x.Table.BoomRanges)
				for(int t = b.FirstType; t <= b.LastType; t += 5) types.Add(t);
			types.Sort();

			MapRecords map = new MapRecords();
			foreach(int type in types)
			{
				map.Lines.Add(DoomRecord(type, 23));
				map.Lines.Add(DoomRecord(type, 0, 0x200 | 0x20 | 0x4)); // passthrough, secret, two-sided
			}

			List<string> failures = new List<string>();
			foreach(string via in new[] { "GZDoom_DoomUDMF", "GZDoom_DoomHexen" })
			{
				ConversionReport there, back;
				MapRecords mid = Convert(map, "GZDoom_DoomDoom", via, out there);
				MapRecords result = Convert(mid, via, "GZDoom_DoomDoom", out back);
				// Every tag-23 line is ID 23, which the map's TranslucentLine types refer to, so Hexen reports IDs everywhere; only other reasons may skip a line
				string[] known = { "has no Hexen equivalent", "doesn't fit Hexen's one-byte action", "doesn't fit Hexen's one byte", "has no Doom type the target offers" };
				var lossy = there.Issues.Concat(back.Issues).Where(i => i.Element == ConversionElement.Linedef && !i.Message.StartsWith("line ID") && !i.Message.StartsWith("tag ")).ToList();
				HashSet<int> reported = new HashSet<int>(lossy.Select(i => i.Index));
				HashSet<int> idreported = new HashSet<int>(there.Issues.Concat(back.Issues).Where(i => i.Message.StartsWith("line ID")).Select(i => i.Index));
				foreach(ConversionIssue i in lossy) if(!known.Any(k => i.Message.Contains(k))) failures.Add(via + " unexpected: " + i);
				if(via.EndsWith("UDMF") && there.Issues.Count + back.Issues.Count > 0) failures.Add(via + ": UDMF reported " + string.Join("; ", there.Issues.Concat(back.Issues).Take(5)));

				for(int i = 0; i < map.Lines.Count; i++)
				{
					LineRecord a = map.Lines[i], r = result.Lines[i];
					if(reported.Contains(i)) continue;
					string what = via + " line " + i + " type " + a.Action + " bits " + a.Bits;
					// Hexen has no passthrough bit; the engine only reads it on use lines, which UseThrough carries
					bool idlepassthrough = via.EndsWith("Hexen") && (a.Bits ^ r.Bits) == 0x200 && x.Translate(a.Action, a.Bits, 0).Activation != LineActivation.UseThrough;
					if(r.Bits != a.Bits && !idlepassthrough) failures.Add(what + ": bits became " + r.Bits);
					if(r.Tags[0] != a.Tags[0] && !idreported.Contains(i) && x.Translate(a.Action, a.Bits, a.Tags[0]).Special != 0) failures.Add(what + ": tag became " + r.Tags[0]);
					if(r.Action == a.Action) continue;
					CanonicalLine ca = x.Translate(a.Action, a.Bits, a.Tags[0]), cr = x.Translate(r.Action, r.Bits, r.Tags[0]);
					HexenLineCodec.ClearIdArg(ca);
					HexenLineCodec.ClearIdArg(cr);
					if(!ca.SameSpecial(cr)) failures.Add(what + ": became type " + r.Action + ", " + ca + " vs " + cr);
				}
			}
			Assert.True(failures.Count == 0, failures.Count + " failures:\n" + string.Join("\n", failures.Take(40)));
		}

		[Fact]
		public void DoomLineFlags_UseTheEnginesBitsNotUdbsTable()
		{
			MapRecords map = new MapRecords();
			map.Lines.Add(DoomRecord(0, 0, (1 << 10) | (1 << 12) | (1 << 13) | 1));
			ConversionReport report;
			MapRecords udmf = Convert(map, "GZDoom_DoomDoom", "GZDoom_DoomUDMF", out report);
			Dictionary<string, bool> f = udmf.Lines[0].Flags;
			Assert.True(f.ContainsKey("midtex3d") && f.ContainsKey("blocklandmonsters") && f.ContainsKey("blockplayers") && f.ContainsKey("blocking"), string.Join(", ", f.Keys));

			Assert.Equal(map.Lines[0].Bits, Convert(udmf, "GZDoom_DoomUDMF", "GZDoom_DoomDoom", out report).Lines[0].Bits);
			Assert.Equal(1, Convert(udmf, "GZDoom_DoomUDMF", "Doom_DoomDoom", out report).Lines[0].Bits); // vanilla has none of the three
			Assert.True(report.HasDropped(ConversionElement.Linedef, "flag midtex3d") && report.HasDropped(ConversionElement.Linedef, "flag blockplayers"), report.ToString());

			MapRecords strife = new MapRecords();
			strife.Lines.Add(DoomRecord(0, 0, 0x200)); // Strife's bit 9 is the railing
			Assert.True(Convert(strife, "ZDoom_StrifeDoom", "ZDoom_StrifeUDMF", out report).Lines[0].Flags.ContainsKey("jumpover"));
		}

		// The engine reads Doom and Hexen tags as int16: a line and a sector tagged 40000 must still match
		[Fact]
		public void SixteenBitTags_LineAndSectorStayMatched()
		{
			MapRecords map = new MapRecords();
			map.Lines.Add(DoomRecord(2, 0x9C40)); // W1 Door_Open
			map.Sectors.Add(new SectorRecord { Tags = new List<int> { 0x9C40 } });
			ConversionReport report;
			MapRecords udmf = Convert(map, "GZDoom_DoomDoom", "GZDoom_DoomUDMF", out report);
			Assert.Equal(-25536, udmf.Lines[0].Args[0]);
			Assert.Equal(-25536, udmf.Sectors[0].Tags[0]);

			MapRecords back = Convert(udmf, "GZDoom_DoomUDMF", "GZDoom_DoomDoom", out report);
			Assert.Equal(0x9C40, back.Lines[0].Tags[0]);
			Assert.Equal(0x9C40, back.Sectors[0].Tags[0]);
			Assert.Empty(report.Issues);

			MapRecords hexen = Convert(map, "GZDoom_DoomDoom", "GZDoom_DoomHexen", out report);
			Assert.Equal(0x9C40, hexen.Sectors[0].Tags[0]);
			Assert.Equal(-25536, Convert(hexen, "GZDoom_DoomHexen", "GZDoom_DoomUDMF", out report).Sectors[0].Tags[0]);
		}

		// Hexen has one line ID slot: it goes to the lines some special refers to
		[Fact]
		public void HexenWriter_KeepsReferencedLineIdsAndReportsTheRest()
		{
			MapRecords map = new MapRecords();
			map.Lines.Add(new LineRecord { Tags = new List<int> { 7 } }); // referenced, no special
			map.Lines.Add(new LineRecord { Action = 208, Args = new[] { 7, 128, 0, 0, 0 }, Flags = { { "playercross", true } } }); // TranslucentLine(7)
			map.Lines.Add(new LineRecord { Tags = new List<int> { 9 } }); // nobody refers to 9
			map.Lines.Add(new LineRecord { Action = 12, Args = new[] { 1, 16, 0, 0, 0 }, Tags = new List<int> { 7 }, Flags = { { "playeruse", true } } }); // Door_Raise has no ID arg

			ConversionReport report;
			MapRecords hexen = Convert(map, "GZDoom_DoomUDMF", "GZDoom_DoomHexen", out report);
			Assert.Equal(121, hexen.Lines[0].Action);
			Assert.Equal(7, hexen.Lines[0].Args[0]);
			Assert.Equal(0, hexen.Lines[2].Action);
			Assert.True(report.HasDropped(ConversionElement.Linedef, "line ID no special refers to (scripts may)"), report.ToString());
			Assert.Contains(report.Issues, i => i.Index == 3 && i.Severity == ConversionSeverity.Lossy);
			Assert.Equal(1 << 10, hexen.Lines[3].Activate); // SPAC_Use
		}

		// The eight specials the old Hexen <-> UDMF code translated, through the converter both ways
		[Fact]
		public void HexenUdmfRoundTrip_KeepsTheOldTranslatedSpecials()
		{
			MapRecords map = new MapRecords();
			int[][] lines =
			{
				new[] { 121, 5, 1 | 8, 0, 0, 0 }, // Line_SetIdentification 5, its flags
				new[] { 208, 5, 128, 0, 0, 0 }, // TranslucentLine refers to 5
				new[] { 215, 0, 6, 0, 0, 0 }, // Teleport_Line to 6
				new[] { 215, 6, 0, 0, 0, 0 },
				new[] { 222, 8, 2, 0, 0, 0 }, // Scroll_Texture_Model
				new[] { 1, 1, 0, 0, 0, 0 }, // Polyobj_StartLine
				new[] { 181, 1, 0, 0, 0, 0 }, // Plane_Align
				new[] { 160, 10, 1, 0, 255, 0 }, // Sector_Set3DFloor
			};
			foreach(int[] l in lines) map.Lines.Add(new LineRecord { Action = l[0], Args = l.Skip(1).ToArray() });

			ConversionReport report;
			MapRecords udmf = Convert(map, "GZDoom_DoomHexen", "GZDoom_DoomUDMF", out report);
			Assert.Equal(0, udmf.Lines[0].Action);
			Assert.Equal(5, udmf.Lines[0].Tags[0]);
			Assert.True(udmf.Lines[0].Flags.ContainsKey("zoneboundary") && udmf.Lines[0].Flags.ContainsKey("clipmidtex"));
			Assert.Equal(6, udmf.Lines[3].Tags[0]);
			Assert.Equal(0, udmf.Lines[3].Args[0]);

			MapRecords back = Convert(udmf, "GZDoom_DoomUDMF", "GZDoom_DoomHexen", out report);
			for(int i = 0; i < map.Lines.Count; i++)
			{
				LineRecord a = map.Lines[i], b = back.Lines[i];
				Assert.True(a.Action == b.Action && a.Args.SequenceEqual(b.Args), i + ": " + b.Action + "(" + string.Join(", ", b.Args) + ")\n" + report);
			}
		}

		[Fact]
		public void DowngradeToDoom_NamesEverythingLostAndBlocksWhatCantFit()
		{
			MapRecords map = new MapRecords();
			map.Lines.Add(new LineRecord { Action = 80, Args = new[] { 1, 0, 0, 0, 0 }, Flags = { { "playeruse", true } }, Fields = { "alpha" }, Tags = new List<int> { 3, 4 } }); // ACS_Execute
			map.Sectors.Add(new SectorRecord { Fields = { "lightfloor" }, Flags = { { "silent", true } } });
			map.Things.Add(new ThingRecord { Type = 3004, Tag = 5, Z = 32, Action = 80, Args = new[] { 2, 0, 0, 0, 0 }, Flags = { { "skill1", true }, { "dormant", true } } });
			map.LongTextureNames.Add("TEXTURES/LONGNAME");
			map.FractionalVertices = 2;
			map.HasScripts = true;

			ConversionReport report;
			MapRecords doom = Convert(map, "GZDoom_DoomUDMF", "GZDoom_DoomDoom", out report);
			string text = report.ToString();
			Assert.Equal(0, doom.Lines[0].Action);
			Assert.Contains(report.Issues, i => i.Element == ConversionElement.Linedef && i.Index == 0);
			Assert.Contains(report.Issues, i => i.Element == ConversionElement.Thing && i.Message.Contains("TID 5") && i.Message.Contains("height 32") && i.Message.Contains("special 80"));
			foreach(string key in new[] { "Linedef field alpha", "Linedef extra line ID 4", "Sector field lightfloor", "Sector flag silent", "Thing flag dormant" })
				Assert.True(report.Dropped.ContainsKey(key), key + " missing from\n" + text);
			Assert.Contains("LONGNAME", text);
			Assert.Contains("SCRIPTS", text);
			Assert.Contains("rounded", text);
			Assert.False(report.Blocked);

			map.VertexCount = 70000;
			Convert(map, "GZDoom_DoomUDMF", "GZDoom_DoomHexen", out report);
			Assert.True(report.Blocked);
			Assert.DoesNotContain("SCRIPTS", report.ToString()); // Hexen keeps them
		}

		// Every combination of a binary format's thing flags survives UDMF, and Doom <-> Hexen keeps the shared ones
		[Fact]
		public void ThingFlags_RoundTripThroughUdmfAndBetweenBinaryFormats()
		{
			List<string> failures = new List<string>();
			foreach(string from in new[] { "GZDoom_DoomDoom", "GZDoom_DoomHexen" })
			{
				int all = 0;
				foreach(FlagTranslation f in Profile(from).ThingFlagTable) all |= f.Flag;
				List<int> bits = new List<int>();
				for(int b = 0; b < 16; b++) if((all & (1 << b)) != 0) bits.Add(1 << b);

				MapRecords map = new MapRecords();
				for(int combo = 0; combo < (1 << bits.Count); combo += combo < 64 ? 1 : 37)
				{
					int v = 0;
					for(int b = 0; b < bits.Count; b++) if((combo & (1 << b)) != 0) v |= bits[b];
					map.Things.Add(new ThingRecord { Type = 3004, Bits = v });
				}

				ConversionReport r1, r2;
				MapRecords back = Convert(Convert(map, from, "GZDoom_DoomUDMF", out r1), "GZDoom_DoomUDMF", from, out r2);
				for(int i = 0; i < map.Things.Count; i++)
					if(back.Things[i].Bits != map.Things[i].Bits) failures.Add(from + " via UDMF: " + map.Things[i].Bits + " became " + back.Things[i].Bits);
				if(r1.Dropped.Count + r2.Dropped.Count > 0) failures.Add(from + " via UDMF dropped " + string.Join(", ", r1.Dropped.Keys.Concat(r2.Dropped.Keys)));
			}

			// Skills, ambush and game modes mean the same in both binary formats
			MapRecords doom = new MapRecords();
			doom.Things.Add(new ThingRecord { Type = 3004, Bits = 1 | 8 }); // easy skills, ambush; in single player, coop and DM
			doom.Things.Add(new ThingRecord { Type = 3004, Bits = 4 | 16 }); // hard skills, not single player
			ConversionReport report;
			MapRecords there = Convert(doom, "GZDoom_DoomDoom", "GZDoom_DoomHexen", out report);
			MapRecords again = Convert(there, "GZDoom_DoomHexen", "GZDoom_DoomDoom", out report);
			for(int i = 0; i < doom.Things.Count; i++)
				if(again.Things[i].Bits != doom.Things[i].Bits) failures.Add("Doom via Hexen: " + doom.Things[i].Bits + " became " + again.Things[i].Bits + " (Hexen " + there.Things[i].Bits + ")");
			Assert.True(failures.Count == 0, string.Join("\n", failures.Take(30)));
		}

		[Fact]
		public void DoomWriter_PicksOnlyTypesTheTargetOffers()
		{
			// A Boom generalized door has no vanilla type; Door_Raise has
			MapRecords map = new MapRecords();
			map.Lines.Add(DoomRecord(0x3c06, 9));
			map.Lines.Add(DoomRecord(1, 0));
			ConversionReport report;
			MapRecords udmf = Convert(map, "Boom_DoomDoom", "GZDoom_DoomUDMF", out report);
			MapRecords vanilla = Convert(udmf, "GZDoom_DoomUDMF", "Doom_DoomDoom", out report);
			Assert.Equal(0, vanilla.Lines[0].Action);
			Assert.Contains(report.Issues, i => i.Index == 0 && i.Severity == ConversionSeverity.Lossy);
			Assert.Equal(1, vanilla.Lines[1].Action);
			Assert.Equal(0x3c06, Convert(udmf, "GZDoom_DoomUDMF", "Boom_DoomDoom", out report).Lines[0].Action);

			// Sectors likewise: a generalized secret bit needs generalized sectors
			MapRecords sectors = new MapRecords();
			sectors.Sectors.Add(new SectorRecord { Special = 1 | 0x80 });
			udmf = Convert(sectors, "Boom_DoomDoom", "GZDoom_DoomUDMF", out report);
			Assert.Equal(1 | 0x80, Convert(udmf, "GZDoom_DoomUDMF", "Boom_DoomDoom", out report).Sectors[0].Special);
			Assert.Equal(0, Convert(udmf, "GZDoom_DoomUDMF", "Doom_DoomDoom", out report).Sectors[0].Special);
			Assert.Single(report.Issues);

			// Vanilla has no passthrough bit in its config, so a use-through door (Door_Open, S1 type 103) says so
			MapRecords door = new MapRecords();
			door.Lines.Add(new LineRecord { Action = 11, Args = new[] { 1, 16, 0, 0, 0 }, Flags = { { "playeruse", true }, { "passuse", true } } });
			Assert.Equal(0, Convert(door, "GZDoom_DoomUDMF", "Doom_DoomDoom", out report).Lines[0].Bits & 0x200);
			Assert.Contains(report.Issues, i => i.Message.Contains("passthrough"));
		}

		// One line refers to ID 7 per case; a Hexen write must keep ID 7 on the plain line
		[Fact]
		public void ReferencedLineIds_EverySpecialThatLooksUpALine()
		{
			var cases = new List<Tuple<string, LineRecord, ThingRecord>>();
			foreach(int s in new[] { 16, 48, 52, 53, 55, 56, 150, 156, 183, 184, 208, 222, 281, 282 })
				cases.Add(Tuple.Create("special " + s, new LineRecord { Action = s, Args = new[] { 7, 0, 0, 0, 0 } }, (ThingRecord)null));
			cases.Add(Tuple.Create("Scroll_Texture_Both back side", new LineRecord { Action = 221, Args = new[] { -7, 0, 0, 0, 0 } }, (ThingRecord)null));
			cases.Add(Tuple.Create("Sector_SetPortal type 5", new LineRecord { Action = 57, Args = new[] { 7, 5, 0, 0, 0 } }, (ThingRecord)null));
			cases.Add(Tuple.Create("Line_SetPortalTarget arg1", new LineRecord { Action = 107, Args = new[] { 3, 7, 0, 0, 0 } }, (ThingRecord)null));
			cases.Add(Tuple.Create("Teleport_Line", new LineRecord { Action = 215, Args = new[] { 0, 7, 0, 0, 0 } }, (ThingRecord)null));
			cases.Add(Tuple.Create("Scroll_Texture_Offsets", new LineRecord { Action = 225, Args = new[] { 0, 7, 0, 0, 0 } }, (ThingRecord)null));
			cases.Add(Tuple.Create("Static_Init sector link", new LineRecord { Action = 190, Args = new[] { 7, 3, 0, 0, 0 } }, (ThingRecord)null));
			cases.Add(Tuple.Create("slope thing", (LineRecord)null, new ThingRecord { Type = 9500, Args = new[] { 7, 0, 0, 0, 0 } }));
			cases.Add(Tuple.Create("thing special", (LineRecord)null, new ThingRecord { Type = 3004, Action = 55, Args = new[] { 7, 0, 0, 0, 0 } }));

			List<string> failures = new List<string>();
			foreach(var c in cases)
			{
				MapRecords map = new MapRecords();
				map.Lines.Add(new LineRecord { Tags = new List<int> { 7 } });
				if(c.Item2 != null) { c.Item2.Flags["playercross"] = true; map.Lines.Add(c.Item2); }
				if(c.Item3 != null) map.Things.Add(c.Item3);
				ConversionReport report;
				LineRecord l = Convert(map, "GZDoom_DoomUDMF", "GZDoom_DoomHexen", out report).Lines[0];
				if(l.Action != 121 || l.Args[0] != 7) failures.Add(c.Item1 + ": " + l.Action + "(" + string.Join(", ", l.Args) + ")");
			}
			Assert.True(failures.Count == 0, string.Join("\n", failures));
		}

		// Line_SetIdentification's flags reach Doom bits, and Doom's bits reach Line_SetIdentification
		[Fact]
		public void IdentificationFlags_CrossBetweenHexenArgsAndDoomBits()
		{
			MapRecords hexen = new MapRecords();
			hexen.Lines.Add(new LineRecord { Action = 121, Args = new[] { 5, 32, 0, 0, 0 } }); // midtex3d
			ConversionReport report;
			MapRecords doom = Convert(hexen, "GZDoom_DoomHexen", "GZDoom_DoomDoom", out report);
			Assert.Equal(1 << 10, doom.Lines[0].Bits);

			MapRecords back = Convert(doom, "GZDoom_DoomDoom", "GZDoom_DoomHexen", out report);
			Assert.Equal(121, back.Lines[0].Action);
			Assert.Equal(32, back.Lines[0].Args[1]);
			Assert.True(report.Dropped.Count == 0, report.ToString());
		}

		// Doom-format things are in every game mode unless a bit clears it, which vanilla's table can't express
		[Fact]
		public void ThingModes_FollowTheDoomFormatsDefaults()
		{
			MapRecords map = new MapRecords();
			map.Things.Add(new ThingRecord { Type = 3004, Bits = 1 });
			ConversionReport report;
			Dictionary<string, bool> f = Convert(map, "Doom_Doom2Doom", "GZDoom_DoomUDMF", out report).Things[0].Flags;
			Assert.True(f["single"] && f["coop"] && f["dm"], string.Join(", ", f));

			MapRecords udmf = new MapRecords();
			udmf.Things.Add(new ThingRecord { Type = 3004, Flags = { { "skill1", true }, { "skill2", true }, { "single", true } } }); // single player only
			Convert(udmf, "GZDoom_DoomUDMF", "Doom_Doom2Doom", out report);
			Assert.True(report.HasDropped(ConversionElement.Thing, "flag coop (set by the format)"), report.ToString());
			Assert.Equal(1 | 32 | 64, Convert(udmf, "GZDoom_DoomUDMF", "GZDoom_DoomDoom", out report).Things[0].Bits); // Boom's not-DM, not-coop

			// Hexen TIDs are int16 in the engine
			MapRecords hx = new MapRecords();
			hx.Things.Add(new ThingRecord { Type = 3004, Tag = 0x9C40 });
			MapRecords u = Convert(hx, "GZDoom_DoomHexen", "GZDoom_DoomUDMF", out report);
			Assert.Equal(-25536, u.Things[0].Tag);
			Assert.Equal(0x9C40, Convert(u, "GZDoom_DoomUDMF", "GZDoom_DoomHexen", out report).Things[0].Tag);
		}
	}
}
