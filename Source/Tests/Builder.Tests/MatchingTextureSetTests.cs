using System.Collections.Generic;
using CodeImp.DoomBuilder.Config;

namespace Builder.Tests
{
	public class MatchingTextureSetTests
	{
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
