using CodeImp.DoomBuilder.Config;
using CodeImp.DoomBuilder.IO;
using CodeImp.DoomBuilder.Map;
using CodeImp.DoomBuilder.Map.Conversion;

namespace Builder.Tests
{
	public class MapFormatConversionTests
	{
		private static readonly string[] games = { "doom.txt", "heretic.txt", "strife.txt" };

		private static string XlatFolder
		{
			get { return Path.Combine(Path.GetDirectoryName(typeof(XlatTable).Assembly.Location), "Configurations", "Xlat"); }
		}

		private static Dictionary<string, int> SpecialNames()
		{
			return XlatTable.ParseSpecialNames(File.ReadAllText(Path.Combine(XlatFolder, "actionspecials.h")));
		}

		private static LineXlat Lines(string game)
		{
			return new LineXlat(XlatTable.LoadFromFolder(XlatFolder, game));
		}

		private static XlatTable Parse(string text)
		{
			return XlatTable.Load("main.txt", n => n == "main.txt" ? text : null, SpecialNames());
		}

		// UDMF -> Hexen used to build this with &= from 0, so it always wrote 0
		[Fact]
		public void FlagsToArgBits_SetsEachFlagsBit()
		{
			Assert.Equal(0, Linedef.FlagsToArgBits(new Dictionary<string, bool>()));
			Assert.Equal(1 | 8 | 128, Linedef.FlagsToArgBits(new Dictionary<string, bool> { { "zoneboundary", true }, { "clipmidtex", true }, { "firstsideonly", true }, { "jumpover", false } }));
			for(int i = 0; i < Linedef.IdentificationArgFlags.Length; i++)
				Assert.Equal(1 << i, Linedef.FlagsToArgBits(new Dictionary<string, bool> { { Linedef.IdentificationArgFlags[i], true } }));
		}

		[Fact]
		public void BundledTables_ParseAndNameOnlyKnownSpecials()
		{
			foreach(string game in games)
			{
				LineXlat x = Lines(game);
				Assert.True(x.Table.SimpleLines.Count > 100, game + " has " + x.Table.SimpleLines.Count + " line types");
				if(game != "strife.txt") Assert.True(x.Table.BoomRanges.Count >= 7, game + " has " + x.Table.BoomRanges.Count + " Boom ranges"); // Strife has none
				Assert.True(x.Table.Warnings.Count == 0, game + ": " + string.Join("; ", x.Table.Warnings));
			}
			Assert.Equal(0x200, Lines("doom.txt").PassthroughFlag);
			Assert.Equal(0, Lines("strife.txt").PassthroughFlag); // bit 9 is ML_RAILING there
		}

		// Doom-format configs name the table the engine reads them with; Eternity and EDGE read Doom maps their own way
		[Fact]
		public void Configs_NameABundledTableForDoomFormat()
		{
			string dir = Path.GetDirectoryName(XlatFolder);
			List<string> failures = new List<string>();
			int withtable = 0;
			foreach(string path in Directory.GetFiles(dir, "*.cfg"))
			{
				string name = Path.GetFileName(path);
				var cfg = new Configuration(path, true);
				if(cfg.ErrorResult) { failures.Add(name + ": " + cfg.ErrorDescription); continue; }
				string table = cfg.ReadSetting("xlattranslator", "");
				bool doomformat = cfg.ReadSetting("formatinterface", "") == "DoomMapSetIO";
				bool ownxlat = name.StartsWith("Eternity_") || name.StartsWith("EdgeC_");
				if(doomformat && !ownxlat && table == "") failures.Add(name + ": no xlattranslator");
				if((!doomformat || ownxlat) && table != "") failures.Add(name + ": xlattranslator on a config that doesn't need one");
				if(table == "") continue;
				withtable++;
				string game = cfg.ReadSetting("basegame", "").ToLowerInvariant();
				if(table != game + ".txt") failures.Add(name + ": " + table + " for base game " + game);
				else if(XlatTable.LoadFromFolder(XlatFolder, table).Warnings.Count != 0) failures.Add(name + ": " + table + " has warnings");
			}
			Assert.True(failures.Count == 0, string.Join("\n", failures));
			Assert.True(withtable >= 16, withtable + " configs name a table");
		}

		// Where the engine's parser only prints a message, it keeps going the way these cases expect
		[Fact]
		public void Parser_FollowsTheEnginesLexerAndWarnings()
		{
			var names = SpecialNames();

			XlatTable t = Parse("1 = 0, Door_Raise + 1 (Door_Open)\n2 = 0, Door_Open (0)\n2 = 0, No_Such_Special (1)\n3 = 0, Door_Open (1, 2, 3, 4, 5, 6)\n");
			Assert.Equal(names["Door_Raise"] + 1, t.SimpleLines[1].Special); // special names are numbers in any expression
			Assert.Equal(names["Door_Open"], t.SimpleLines[1].Args[0].Value);
			Assert.Equal(names["Door_Open"], t.SimpleLines[2].Special); // an unknown name leaves the earlier entry
			Assert.Equal(new[] { 1, 2, 3, 4, 5 }, t.SimpleLines[3].Args.Select(a => a.Value).ToArray());
			Assert.Equal(2, t.Warnings.Count);
			Assert.Equal(3, t.Warnings[0].Line);
			Assert.Contains("No_Such_Special", t.Warnings[0].Message);

			string list = string.Join(", ", Enumerable.Range(1, 17).Select(i => i + ":" + i));
			string ranges = "";
			for(int i = 0; i < 17; i++) ranges += "[Generic_Floor] (" + (0x6000 + i * 8) + ", " + (0x6007 + i * 8) + ") { arg2 = 0x1ff [" + list + "] arg3 = 0x123 }\n";
			t = Parse(ranges);
			Assert.Equal(16, t.BoomRanges.Count);
			Assert.Equal(15, t.BoomRanges[0].Args[0].Filters.Length);
			Assert.Equal(0x1ff, t.BoomRanges[0].Args[0].AndValue);
			Assert.Equal(0x23, t.BoomRanges[0].Args[1].ConstantValue); // FBoomArg's ConstantValue is a byte
			Assert.Equal(17 + 1, t.Warnings.Count); // 17 lists over 15, one range over 16

			t = Parse("define BIG (99999999999999999999999)\ndefine HEX (0x1ffffffffffffffffff)\n4 = 0, Door_Open (BIG, HEX, 0x1ffff)\n[Generic_Floor] (0x16000, 0x6007) { arg2 = 0x10003 [0x10001 : 2] }\n");
			Assert.Equal(new[] { -1, -1, 0x1ffff }, t.SimpleLines[4].Args.Take(3).Select(a => a.Value).ToArray()); // strtoll clamps, the int cast keeps -1
			Assert.Equal(0x6000, t.BoomRanges[0].FirstType);
			Assert.Equal(3, t.BoomRanges[0].Args[0].AndValue);
			Assert.Equal(1, t.BoomRanges[0].Args[0].Filters[0]);
		}

		[Fact]
		public void ParseError_NamesFileAndLine()
		{
			var files = new Dictionary<string, string>
			{
				{ "main.txt", "// comment\ninclude \"inc.txt\"\n" },
				{ "inc.txt", "define X (1)\n\n1 = 2, Door_Raise (tag, BOGUS)\n" },
			};
			var e = Assert.Throws<XlatParseException>(() => XlatTable.Load("main.txt", n => files.ContainsKey(n) ? files[n] : null, SpecialNames()));
			Assert.Equal("inc.txt", e.File);
			Assert.Equal(3, e.Line);
			Assert.Contains("BOGUS", e.Message);
		}

		private sealed class LineCase
		{
			public int Type, Flags, Tag;
			public string Special;
			public int[] Args;
			public LineActivation Activation;
			public bool Repeat, Monsters;

			public LineCase(int type, int flags, int tag, string special, int[] args, LineActivation activation, bool repeat, bool monsters)
			{
				Type = type; Flags = flags; Tag = tag; Special = special; Args = args; Activation = activation; Repeat = repeat; Monsters = monsters;
			}
		}

		// Expected values come from base.txt and defines.i as the engine reads them
		[Fact]
		public void Forward_FollowsTranslateLineDef()
		{
			LineXlat x = Lines("doom.txt");
			var names = SpecialNames();
			LineActivation impactcross = LineActivation.Impact | LineActivation.PCross;

			var cases = new[]
			{
				new LineCase(1, 0, 7, "Door_Raise", new[] { 0, 16, 150, 7, 0 }, LineActivation.Use, true, true),
				new LineCase(1, 0x200, 7, "Door_Raise", new[] { 0, 16, 150, 7, 0 }, LineActivation.UseThrough, true, true), // Boom passthrough
				new LineCase(1, 0x20, 7, "Door_Raise", new[] { 0, 16, 150, 7, 0 }, LineActivation.Use, true, false), // secret drops monsters
				new LineCase(2, 0, 3, "Door_Open", new[] { 3, 16, 0, 0, 0 }, LineActivation.Cross, false, false),
				new LineCase(24, 0, 4, "Floor_RaiseToLowestCeiling", new[] { 4, 8, 0, 0, 0 }, impactcross, false, false),
				new LineCase(14, 0, 2, "Plat_UpByValueStayTx", new[] { 2, 4, 4, 0, 0 }, LineActivation.Use, false, false), // P_SLOW/2
				new LineCase(0x3c01, 0, 9, "Generic_Door", new[] { 9, 16, 64, 8, 0 }, LineActivation.Cross, true, false),
				new LineCase(0x3c06, 0, 9, "Generic_Door", new[] { 9, 16, 64 | 128, 8, 0 }, LineActivation.Use, false, false), // push keeps the tag
				new LineCase(0x3c80, 0, 9, "Generic_Door", new[] { 9, 16, 64, 8, 0 }, LineActivation.AnyCross, false, true),
				new LineCase(0x6006, 0, 9, "Generic_Floor", new[] { 0, 8, 0, 1, 0 }, LineActivation.Use, false, false), // push zeroes arg0
			};

			foreach(LineCase c in cases)
			{
				CanonicalLine l = x.Translate(c.Type, c.Flags, c.Tag);
				string what = "type " + c.Type + " flags " + c.Flags + ": " + l;
				Assert.True(names[c.Special] == l.Special, what);
				Assert.True(c.Args.SequenceEqual(l.Args), what);
				Assert.True(c.Activation == l.Activation, what);
				Assert.True(c.Repeat == l.Repeat, what);
				Assert.True(c.Monsters == l.Monsters, what);
				Assert.Equal(c.Tag, l.LineId);
			}

			CanonicalLine zero = x.Translate(0, 0, 12);
			Assert.Equal(0, zero.Special);
			Assert.Equal(new[] { 12, 0, 0, 0, 0 }, zero.Args);

			CanonicalLine unknown = x.Translate(999, 0, 12);
			Assert.Equal(0, unknown.Special);
			Assert.Equal(new[] { 0, 0, 0, 0, 0 }, unknown.Args);

			CanonicalLine negative = x.Translate(1, 0, 0xffff); // maplinedef_t's tag is signed
			Assert.Equal(-1, negative.LineId);
			Assert.Equal(-1, negative.Args[3]);
		}

		// Every simple entry and every generalized type, for each game's table, read the way the Doom reader and writer will
		[Fact]
		public void Reverse_RoundTripsEveryTypeAndLineId()
		{
			List<string> failures = new List<string>();
			foreach(string game in games)
			{
				LineXlat x = Lines(game);
				List<int> types = new List<int>(x.Table.SimpleLines.Keys);
				foreach(XlatBoomRange b in x.Table.BoomRanges)
					for(int t = b.FirstType; t <= b.LastType; t++) types.Add(t);

				foreach(int type in types)
				foreach(int tag in new[] { 0, 23 })
				foreach(int flags in new[] { 0, x.PassthroughFlag, 0x20 })
				{
					CanonicalLine f = x.Translate(type, flags, tag);
					if(f.Special == 0) continue;
					HexenLineCodec.ClearIdArg(f);
					DoomLine? d = x.Find(f, (flags & 0x20) != 0);
					string what = game + " " + type + " tag " + tag + " flags " + flags;
					if(d == null)
					{
						if(!ExprOnly(x, type)) failures.Add(what + ": no match for " + f);
						continue;
					}
					if(d.Value.LostPassthrough) failures.Add(what + ": lost passthrough");
					CanonicalLine back = x.Translate(d.Value.Type, (flags & 0x20) | d.Value.Flags, d.Value.Tag);
					HexenLineCodec.ClearIdArg(back);
					if(!back.SameSpecial(f) || back.LineId != f.LineId) failures.Add(what + " -> " + d.Value.Type + " tag " + d.Value.Tag + ": " + f + " became " + back);
				}
			}
			Assert.True(failures.Count == 0, failures.Count + " failures:\n" + string.Join("\n", failures.Take(40)));
		}

		[Fact]
		public void Reverse_IdArgsAndPassthroughEdges()
		{
			LineXlat doom = Lines("doom.txt");
			var names = SpecialNames();

			// A UDMF Teleport_Line keeps its ID in the id field; the Doom tag must carry the destination arg
			CanonicalLine tele = new CanonicalLine { Special = names["Teleport_Line"], Args = new[] { 0, 7, 0, 0, 0 }, Activation = LineActivation.Cross, Monsters = true, LineId = 3 };
			DoomLine? d = doom.Find(tele, false);
			Assert.True(d != null && d.Value.Type == 243 && d.Value.Tag == 7, "Teleport_Line: " + d);

			// A slope with any line ID keeps it as the tag
			CanonicalLine slope = new CanonicalLine { Special = names["Plane_Align"], Args = new[] { 1, 0, 0, 0, 0 }, Activation = LineActivation.Cross, LineId = 5 };
			d = doom.Find(slope, false);
			Assert.True(d != null && d.Value.Type == 340 && d.Value.Tag == 5, "Plane_Align: " + d);

			// Strife has no passthrough bit: a use-through line matches its use type and says so
			LineXlat strife = Lines("strife.txt");
			CanonicalLine door = strife.Translate(1, 0, 0);
			door.Activation = LineActivation.UseThrough;
			d = strife.Find(door, false);
			Assert.True(d != null && d.Value.Type == 1 && d.Value.Flags == 0 && d.Value.LostPassthrough, "Strife passthrough: " + d);
		}

		private static bool ExprOnly(LineXlat x, int type)
		{
			XlatLineEntry e;
			return x.Table.SimpleLines.TryGetValue(type, out e) && e.Args.Any(a => a.Kind == XlatArgKind.Expr);
		}

		private static HexenLine Hexen(int action, int spac, params int[] args)
		{
			HexenLine h = new HexenLine { Action = action, Spac = spac };
			for(int i = 0; i < args.Length; i++) h.Args[i] = args[i];
			return h;
		}

		// The ID-carrying specials the old Hexen <-> UDMF code handled, read the way SetLineID reads them
		[Fact]
		public void HexenCodec_LineIdSpecialsRoundTrip()
		{
			var cases = new[]
			{
				Tuple.Create(Hexen(121, 0, 5, 1 | 8, 0, 0, 1), 261, new[] { 0, 0, 0, 0, 0 }), // Line_SetIdentification
				Tuple.Create(Hexen(208, 0, 3, 128, 0, 2), 3, new[] { 3, 128, 0, 0, 0 }), // TranslucentLine
				Tuple.Create(Hexen(215, 0, 4, 9, 0), 4, new[] { 0, 9, 0, 0, 0 }), // Teleport_Line
				Tuple.Create(Hexen(222, 0, 6, 2), 6, new[] { 6, 2, 0, 0, 0 }), // Scroll_Texture_Model
				Tuple.Create(Hexen(1, 0, 1, 0, 0, 8), 8, new[] { 1, 0, 0, 0, 0 }), // Polyobj_StartLine
				Tuple.Create(Hexen(5, 0, 1, 1, 0, 0, 9), 9, new[] { 1, 1, 0, 0, 0 }), // Polyobj_ExplicitLine
				Tuple.Create(Hexen(181, 0, 1, 0, 10), 10, new[] { 1, 0, 0, 0, 0 }), // Plane_Align
				Tuple.Create(Hexen(160, 0, 10, 1 | 8, 0, 255, 7), 7, new[] { 10, 1, 0, 255, 0 }), // Sector_Set3DFloor, arg4 is a line ID
				Tuple.Create(Hexen(160, 0, 44, 1, 0, 255, 1), 0, new[] { 300, 1, 0, 255, 0 }), // arg4 is the tag's high byte
			};

			foreach(var c in cases)
			{
				CanonicalLine l = HexenLineCodec.Read(c.Item1);
				Assert.True(c.Item2 == l.LineId && c.Item3.SequenceEqual(l.Args), c.Item1.Action + ": " + l);
				List<string> issues = new List<string>();
				HexenLine back = HexenLineCodec.Write(l, true, issues);
				Assert.True(issues.Count == 0, c.Item1.Action + ": " + string.Join("; ", issues));
				Assert.True(back.Action == c.Item1.Action && back.Args.SequenceEqual(c.Item1.Args), c.Item1.Action + " wrote " + back.Action + "(" + string.Join(", ", back.Args) + ")");
			}

			CanonicalLine id = HexenLineCodec.Read(Hexen(121, 0, 5, 1 | 8 | 128, 0, 0, 1));
			Assert.Equal(new List<string> { "zoneboundary", "clipmidtex" }, id.IdFlags);
			Assert.True(id.FirstSideOnly);
		}

		[Fact]
		public void HexenCodec_ActivationsTheFormatCantHoldAreReported()
		{
			Assert.Equal(LineActivation.Impact | LineActivation.PCross, HexenLineCodec.FromSpac(7));
			Assert.Equal(LineActivation.Impact | LineActivation.PCross, HexenLineCodec.FromSpac(3));
			Assert.Null(HexenLineCodec.ToSpac(LineActivation.AnyCross));

			List<string> issues = new List<string>();
			CanonicalLine both = new CanonicalLine { Special = 12, Activation = LineActivation.Cross | LineActivation.Use };
			Assert.Equal(0, HexenLineCodec.Write(both, false, issues).Spac);
			Assert.Single(issues);

			issues.Clear();
			CanonicalLine taken = new CanonicalLine { Special = 12, Activation = LineActivation.Use, LineId = 5 };
			HexenLineCodec.Write(taken, true, issues);
			Assert.Single(issues); // Door_Raise has no ID arg
			issues.Clear();
			HexenLineCodec.Write(taken, false, issues);
			Assert.Empty(issues); // unreferenced IDs are not written
		}

		// Each case writes action 0, or Line_SetIdentification for a referenced ID, and one issue
		[Fact]
		public void HexenCodec_SpecialsHexenCantHoldAreDropped()
		{
			var cases = new[]
			{
				new CanonicalLine { Special = 273, Activation = LineActivation.Cross, LineId = 4 }, // Stairs_BuildUpDoomCrush: over one byte
				new CanonicalLine { Special = 80, Activation = LineActivation.Death, LineId = 4 }, // a UDMF death special
				new CanonicalLine { Special = 80, Activation = LineActivation.None, LineId = 4 }, // no activation at all
			};
			foreach(CanonicalLine c in cases)
			{
				List<string> issues = new List<string>();
				HexenLine h = HexenLineCodec.Write(c, false, issues);
				Assert.True(h.Action == 0 && issues.Count == 1, c + " wrote " + h.Action + ": " + string.Join("; ", issues));

				issues.Clear();
				h = HexenLineCodec.Write(c, true, issues);
				Assert.True(h.Action == 121 && h.Args[0] == 4 && issues.Count == 1, c + " with its ID wrote " + h.Action);
			}

			List<string> none = new List<string>();
			CanonicalLine slope = new CanonicalLine { Special = 181, Args = new[] { 1, 0, 0, 0, 0 } };
			Assert.Equal(181, HexenLineCodec.Write(slope, false, none, false).Action); // a load-time special needs no activation
			Assert.Empty(none);

			CanonicalLine walk = new CanonicalLine { Special = 80, Activation = LineActivation.Cross | LineActivation.Walking };
			Assert.Equal(0, HexenLineCodec.Write(walk, false, none).Spac);
			Assert.Single(none); // walking is dropped, the cross kept

			var fields = new Dictionary<string, bool> { { "deathspecial", true }, { "walking", true }, { "playercross", true } };
			Assert.Equal(LineActivation.Death | LineActivation.Walking | LineActivation.Cross, UdmfLineCodec.Read(80, new int[5], fields, 0).Activation);
		}

		// TranslucentLine arg0 0 means "this line", so it can't take the ID
		[Fact]
		public void HexenCodec_IdArgsWithAMeaningKeepIt()
		{
			List<string> issues = new List<string>();
			CanonicalLine self = new CanonicalLine { Special = 208, Args = new[] { 0, 128, 0, 0, 0 }, Activation = LineActivation.Cross, LineId = 5 };
			HexenLine h = HexenLineCodec.Write(self, true, issues);
			Assert.Equal(0, h.Args[0]);
			Assert.Single(issues);

			issues.Clear();
			CanonicalLine same = new CanonicalLine { Special = 208, Args = new[] { 5, 128, 0, 0, 0 }, Activation = LineActivation.Cross, LineId = 5 };
			Assert.Equal(5, HexenLineCodec.Write(same, true, issues).Args[0]);
			Assert.Empty(issues);
		}

		// Every Doom type: a UDMF write reads back the same, and so does a Hexen write that reports nothing
		[Fact]
		public void Codecs_RoundTripEveryDoomType()
		{
			List<string> failures = new List<string>();
			int hexenreported = 0;
			foreach(string game in games)
			{
				LineXlat x = Lines(game);
				List<int> types = new List<int>(x.Table.SimpleLines.Keys);
				foreach(XlatBoomRange b in x.Table.BoomRanges)
					for(int t = b.FirstType; t <= b.LastType; t += 7) types.Add(t);

				foreach(int type in types)
				{
					foreach(int flags in new[] { 0, 0x200 })
					{
						CanonicalLine f = x.Translate(type, flags, 23);
						if(f.Special == 0) continue;
						HexenLineCodec.ClearIdArg(f);

						Dictionary<string, bool> fields = new Dictionary<string, bool>();
						UdmfLineCodec.Write(f, fields);
						CanonicalLine u = UdmfLineCodec.Read(f.Special, f.Args, fields, f.LineId);
						if(!u.SameSpecial(f)) failures.Add(game + " " + type + " UDMF: " + f + " became " + u);

						List<string> issues = new List<string>();
						CanonicalLine h = HexenLineCodec.Read(HexenLineCodec.Write(f, false, issues));
						if(issues.Count > 0) hexenreported++;
						else if(!h.SameSpecial(f)) failures.Add(game + " " + type + " Hexen: " + f + " became " + h + " unreported");
					}
				}
			}
			Assert.True(failures.Count == 0, failures.Count + " failures:\n" + string.Join("\n", failures.Take(40)));
			Assert.True(hexenreported > 0, "no Boom monster-walk type was reported for Hexen");
		}

		[Fact]
		public void Sector_ForwardAndRoundTrip()
		{
			SectorXlat doom = new SectorXlat(XlatTable.LoadFromFolder(XlatFolder, "doom.txt"));
			Assert.Equal(65, doom.Translate(1)); // dLight_Flicker
			Assert.Equal(0x400, doom.Translate(9)); // secret
			Assert.Equal(65 | 0x400, doom.Translate(1 | 0x80)); // generalized secret bit
			Assert.Equal(0x100, doom.Translate(9 | 0x20)); // nobitmask with mask bits drops the type
			Assert.Equal(0, doom.Translate(6)); // unset below the table size
			Assert.Equal(26, doom.Translate(26)); // above it, kept
			Assert.Equal(0, doom.Translate(0x4000)); // cleared bits
			Assert.Equal(-65536, doom.Translate(0x8000)); // mapsector_t's special is signed: the clear leaves the sign bits
			Assert.Equal(9, doom.Find(0x400, true)); // the plain type wins over generalized bits

			List<string> failures = new List<string>();
			foreach(string game in games)
			{
				SectorXlat x = new SectorXlat(XlatTable.LoadFromFolder(XlatFolder, game));
				for(int d = 0; d <= 0xffff; d++)
				{
					int s = x.Translate(d);
					int? r = x.Find(s, true);
					if(r == null || x.Translate(r.Value) != s) failures.Add(game + " " + d + " -> " + s + " -> " + r);
					int? p = x.Find(s, false);
					if(p != null && ((p.Value & x.MaskBits) != 0 || x.Translate(p.Value) != s)) failures.Add(game + " plain " + d + " -> " + p);
				}
			}
			Assert.True(failures.Count == 0, failures.Count + " failures:\n" + string.Join("\n", failures.Take(40)));
		}
	}
}
