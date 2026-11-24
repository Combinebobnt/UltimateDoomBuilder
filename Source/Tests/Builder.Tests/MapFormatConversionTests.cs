using CodeImp.DoomBuilder.Config;
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
			return new LineXlat(XlatTable.LoadFromFolder(XlatFolder, game), SpecialNames());
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
				Assert.True(x.UnknownSpecials.Count == 0, game + ": unknown specials " + string.Join(", ", x.UnknownSpecials));
			}
		}

		[Fact]
		public void ParseError_NamesFileAndLine()
		{
			var files = new Dictionary<string, string>
			{
				{ "main.txt", "// comment\ninclude \"inc.txt\"\n" },
				{ "inc.txt", "define X (1)\n\n1 = 2, Door_Raise (tag, BOGUS)\n" },
			};
			var e = Assert.Throws<XlatParseException>(() => XlatTable.Load("main.txt", n => files.ContainsKey(n) ? files[n] : null));
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
		}

		// Every simple entry and every generalized type, for each game's table
		[Fact]
		public void Reverse_RoundTripsEveryType()
		{
			List<string> failures = new List<string>();
			foreach(string game in games)
			{
				LineXlat x = Lines(game);
				List<int> types = new List<int>(x.Table.SimpleLines.Keys);
				foreach(XlatBoomRange b in x.Table.BoomRanges)
					for(int t = b.FirstType; t <= b.LastType; t++) types.Add(t);

				foreach(int type in types)
				{
					foreach(int flags in new[] { 0, 0x200, 0x20 })
					{
						CanonicalLine f = x.Translate(type, flags, 23);
						if(f.Special == 0) continue;
						DoomLine? d = x.Find(f, (flags & 0x20) != 0);
						if(d == null)
						{
							if(!ExprOnly(x, type)) failures.Add(game + " " + type + "/" + flags + ": no match for " + f);
							continue;
						}
						CanonicalLine back = x.Translate(d.Value.Type, (flags & 0x20) | (d.Value.Passthrough ? 0x200 : 0), d.Value.Tag);
						if(!back.SameSpecial(f)) failures.Add(game + " " + type + "/" + flags + " -> " + d.Value.Type + ": " + f + " became " + back);
					}
				}
			}
			Assert.True(failures.Count == 0, failures.Count + " failures:\n" + string.Join("\n", failures.Take(40)));
		}

		private static bool ExprOnly(LineXlat x, int type)
		{
			XlatLineEntry e;
			return x.Table.SimpleLines.TryGetValue(type, out e) && e.Args.Any(a => a.Kind == XlatArgKind.Expr);
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
			Assert.Equal(0, doom.Translate(0xc000)); // cleared bits
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
