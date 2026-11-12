using System;
using System.Collections.Generic;
using System.Text;
using System.Text.RegularExpressions;
using CodeImp.DoomBuilder.Config;
using CodeImp.DoomBuilder.Data;

namespace Builder.Tests
{
	public class MatchingTextureSetTests
	{
		// Real filters from Doom_texturesets.cfg and Heretic_texturesets.cfg
		private static readonly string[] RealFilters =
		{
			"ASHWALL*", "BSTONE*", "FLAT1_1", "FLAT10", "FLOOR6_1", "GRNROCK", "ROCK*", "SP_ROCK1",
			"SW1ROCK", "SW1STON6", "TANROCK*", "RROCK*", "RROCK10", "BIGBRIK*", "BRICK*", "GSTFONT1",
			"FLATHUH*", "FLTLAVA*", "WATRWAL*", "BRWNRCKS", "FLOOR00", "GATMETL*", "GATMETL", "SW1OFF",
			"LAVA1", "CTYSTCI*",
		};

		private static readonly string[] EdgeFilters =
		{
			"*", "?", "A?C", "*X", "A*B", "A+B", "A\\B", "a lower", "SW1*", "SW1**", "",
			"A.B", "A[B]", "A(B)", "A^B", "A$B", "A#B", "A|B", "A{2}", "A}B", "!~-_", "sw2*", "A+*",
			"TAB\t", (char)0xC9 + "TOILE*", "A?C*",
		};

		private static readonly string[] ExtraNames =
		{
			"", "A", "AB", "ABC", "AXC", "A+B", "A\\B", "A\\\\B", "AB+", "A LOWER", "a lower", "ALOWER",
			"ASHWALL", "ASHWALL2", "ASHWAL", "SP_ROCK1", "SP_ROCK12", "SW1", "SW1*", "SW1**", "X", "XX",
			"AAX", "AXB", "A.B", "AAB", "A{2}", "AA", "TAB\t", "tab\t", (char)0xC9 + "TOILE",
			(char)0xE9 + "toile1", "STARTAN3", "STARTAN", "STARTAN33", ((char)0xDF).ToString(), "i",
		};

		private sealed class NamedImage : ImageData
		{
			internal NamedImage(string shortname) { this.shortname = shortname; }
			protected override LocalLoadResult LocalLoadImage() { throw new NotSupportedException(); }
		}

		private static Regex ReferenceRegex(ICollection<string> filters)
		{
			// Copy of the regex construction Setup used to apply to every filter
			StringBuilder regexstr = new StringBuilder("");
			foreach(string s in filters)
			{
				string ss = s.ToUpperInvariant();
				ss = ss.Replace("+", "\\+");
				ss = ss.Replace("\\", "\\\\");
				ss = ss.Replace("|", "\\|");
				ss = ss.Replace("{", "\\{");
				ss = ss.Replace("[", "\\[");
				ss = ss.Replace("(", "\\(");
				ss = ss.Replace(")", "\\)");
				ss = ss.Replace("^", "\\^");
				ss = ss.Replace("$", "\\$");
				ss = ss.Replace(".", "\\.");
				ss = ss.Replace("#", "\\#");
				ss = ss.Replace(" ", "\\ ");
				ss = ss.Replace("?", ".");
				ss = ss.Replace("*", ".*?");
				if(regexstr.Length > 0) regexstr.Append("|");
				regexstr.Append("(?:\\A").Append(ss).Append("\\Z)");
			}
			if(filters.Count == 0) regexstr.Append("\\Z\\A");
			return new Regex(regexstr.ToString(), RegexOptions.Compiled | RegexOptions.CultureInvariant);
		}

		private static List<string> TestNames()
		{
			List<string> seeds = new List<string>();
			foreach(string f in RealFilters) seeds.Add(f);
			foreach(string f in EdgeFilters) seeds.Add(f);

			HashSet<string> names = new HashSet<string>(StringComparer.Ordinal);
			foreach(string f in seeds)
			{
				string upper = f.ToUpperInvariant();
				foreach(string s in new[] { upper, upper.Replace("*", "").Replace("?", ""), upper.Replace("*", "Q").Replace("?", "B") })
				{
					names.Add(s);
					names.Add(s + "X");
					names.Add("X" + s);
					names.Add(s.ToLowerInvariant());
					if(s.Length > 0)
					{
						names.Add(s.Substring(0, s.Length - 1));
						names.Add(s.Substring(1));
						char last = s[s.Length - 1];
						names.Add(s.Substring(0, s.Length - 1) + (last == 'Z' ? 'Y' : 'Z'));
					}
				}
			}
			foreach(string n in ExtraNames) names.Add(n);
			return new List<string>(names);
		}

		private static List<string[]> TestFilterLists()
		{
			List<string[]> lists = new List<string[]>();
			foreach(string f in RealFilters) lists.Add(new[] { f });
			foreach(string f in EdgeFilters) lists.Add(new[] { f });
			lists.Add(RealFilters);
			lists.Add(EdgeFilters);
			lists.Add(new string[0]);
			lists.Add(new[] { "STARTAN3", "SW1*", "A?C" });
			lists.Add(new[] { "A+B", "SW1*", "STARTAN3", "*X", "a lower" });
			return lists;
		}

		[Fact]
		public void IsMatchEqualsTheOldSingleRegexForEveryFilterAndName()
		{
			List<string> names = TestNames();
			List<string> failures = new List<string>();
			int pairs = 0, matched = 0;
			foreach(string[] filters in TestFilterLists())
			{
				Regex reference = ReferenceRegex(filters);
				MatchingTextureSet set = new MatchingTextureSet(filters);
				foreach(string name in names)
				{
					bool expected = reference.IsMatch(name.ToUpperInvariant());
					bool viaimage = set.AddTexture(new NamedImage(name));
					bool viaupper = set.AddFlat(new NamedImage(name), name.ToUpperInvariant());
					if(set.IsMatch(name) != expected || viaimage != expected || viaupper != expected)
						failures.Add("[" + string.Join(",", filters) + "] \"" + name + "\" expected " + expected);
					pairs++;
					if(expected) matched++;
				}
			}
			Assert.True(failures.Count == 0, failures.Count + " mismatches: " + string.Join("; ", failures));
			Assert.True(pairs > 10000, "only " + pairs + " pairs");
			Assert.True(matched > 500 && matched < pairs - 500, matched + " of " + pairs + " matched");
		}

		[Fact]
		public void PlusFiltersKeepTheBackslashRegexQuirk()
		{
			MatchingTextureSet set = new MatchingTextureSet(new[] { "A+B" });
			Assert.False(set.IsMatch("A+B"));
			Assert.True(set.IsMatch("A\\B"));
			Assert.True(set.IsMatch("A\\\\B"));
		}

		[Fact]
		public void EmptyFilterListMatchesOnlyTheEmptyName()
		{
			MatchingTextureSet set = new MatchingTextureSet(new string[0]);
			Assert.True(set.IsMatch(""));
			Assert.False(set.IsMatch("STARTAN3"));
		}

		[Fact]
		public void AddTextureAndAddFlatKeepInsertionOrder()
		{
			MatchingTextureSet set = new MatchingTextureSet(new[] { "SW1*", "STARTAN3", "A?C" });
			NamedImage[] images =
			{
				new NamedImage("SW1Z"), new NamedImage("NOPE"), new NamedImage("STARTAN3"),
				new NamedImage("ABC"), new NamedImage("SW1A"), new NamedImage("STARTAN"),
			};
			foreach(NamedImage image in images) set.AddTexture(image);
			for(int i = images.Length - 1; i >= 0; i--) set.AddFlat(images[i], images[i].ShortName.ToUpperInvariant());

			Assert.Equal(new ImageData[] { images[0], images[2], images[3], images[4] }, new List<ImageData>(set.Textures).ToArray());
			Assert.Equal(new ImageData[] { images[4], images[3], images[2], images[0] }, new List<ImageData>(set.Flats).ToArray());
		}

		[Fact]
		public void FiltersClassifyByTheLiteralCharRule()
		{
			object[][] cases =
			{
				new object[] { "STARTAN3", MatchingTextureSet.FilterKind.Exact, "STARTAN3" },
				new object[] { "startan3", MatchingTextureSet.FilterKind.Exact, "STARTAN3" },
				new object[] { "", MatchingTextureSet.FilterKind.Exact, "" },
				new object[] { "A.B[C](D)^$#|{", MatchingTextureSet.FilterKind.Exact, "A.B[C](D)^$#|{" },
				new object[] { "!~-_", MatchingTextureSet.FilterKind.Exact, "!~-_" },
				new object[] { "SW1*", MatchingTextureSet.FilterKind.Prefix, "SW1" },
				new object[] { "sw1*", MatchingTextureSet.FilterKind.Prefix, "SW1" },
				new object[] { "*", MatchingTextureSet.FilterKind.Prefix, "" },
				new object[] { "SW1**", MatchingTextureSet.FilterKind.Pattern, "SW1**" },
				new object[] { "*X", MatchingTextureSet.FilterKind.Pattern, "*X" },
				new object[] { "A*B", MatchingTextureSet.FilterKind.Pattern, "A*B" },
				new object[] { "?", MatchingTextureSet.FilterKind.Pattern, "?" },
				new object[] { "A?C", MatchingTextureSet.FilterKind.Pattern, "A?C" },
				new object[] { "A?C*", MatchingTextureSet.FilterKind.Pattern, "A?C*" },
				new object[] { "A+B", MatchingTextureSet.FilterKind.Pattern, "A+B" },
				new object[] { "A+*", MatchingTextureSet.FilterKind.Pattern, "A+*" },
				new object[] { "A\\B", MatchingTextureSet.FilterKind.Pattern, "A\\B" },
				new object[] { "a lower", MatchingTextureSet.FilterKind.Pattern, "A LOWER" },
				new object[] { "TAB\t", MatchingTextureSet.FilterKind.Pattern, "TAB\t" },
				new object[] { "DEL\u007F", MatchingTextureSet.FilterKind.Pattern, "DEL\u007F" },
				new object[] { (char)0xC9 + "TOILE*", MatchingTextureSet.FilterKind.Pattern, (char)0xC9 + "TOILE*" },
			};

			List<string> failures = new List<string>();
			foreach(object[] c in cases)
			{
				string literal;
				MatchingTextureSet.FilterKind kind = MatchingTextureSet.ClassifyFilter((string)c[0], out literal);
				if(kind != (MatchingTextureSet.FilterKind)c[1] || literal != (string)c[2])
					failures.Add("\"" + c[0] + "\" -> " + kind + " \"" + literal + "\"");
			}
			Assert.True(failures.Count == 0, string.Join("; ", failures));
		}

		[Fact]
		public void MembershipHashIsFnv1aOverLittleEndianLongNames()
		{
			// Reference values from an independent FNV-1a 32 implementation
			Assert.Equal(0x7a0b824eu, MatchingTextureSet.HashLongNames(new long[0], new long[0]));
			Assert.Equal(0x12203df8u, MatchingTextureSet.HashLongNames(new long[] { 1, 2 }, new long[] { 3 }));
			Assert.Equal(0x1c0cb506u, MatchingTextureSet.HashLongNames(
				new long[] { 0x0123456789ABCDEF, -1 }, new long[] { -0x7000000000000000 }));
		}

		[Fact]
		public void MembershipHashSeesOrderAndTheTextureFlatBoundary()
		{
			uint baseline = MatchingTextureSet.HashLongNames(new long[] { 1, 2 }, new long[] { 3 });
			Assert.NotEqual(baseline, MatchingTextureSet.HashLongNames(new long[] { 2, 1 }, new long[] { 3 }));
			Assert.NotEqual(baseline, MatchingTextureSet.HashLongNames(new long[] { 1 }, new long[] { 2, 3 }));
		}
	}
}
