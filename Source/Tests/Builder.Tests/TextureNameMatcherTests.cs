using System;
using System.Globalization;
using System.Threading;
using CodeImp.DoomBuilder.Data;
using Xunit;

namespace Builder.Tests
{
	public class TextureNameMatcherTests
	{
		[Theory]
		[InlineData("")]
		[InlineData("brick")]
		[InlineData("WALL")]
		[InlineData(" ")]
		[InlineData("textures/")]
		[InlineData("i")]
		public void NameModeEqualsInvariantUpperContains(string query)
		{
			var matcher = new TextureNameMatcher(TextureNameMatchMode.Name, query);
			foreach(string name in new[] { "", "BRICK1", "brickwall", "textures/Wall 1", "BR1CK", "FILE", "\u0131", "\u0130", "a b" })
			{
				int score;
				Assert.Equal(name.ToUpperInvariant().Contains(query.ToUpperInvariant()), matcher.Match(name, out score));
				Assert.Equal(0, score);
			}
			Assert.Null(matcher.Error);
		}

		[Fact]
		public void NameDoesNotInterpretWhitespaceOrWildcards()
		{
			int score;
			Assert.False(new TextureNameMatcher(TextureNameMatchMode.Name, "b k").Match("BRICK", out score));
			Assert.True(new TextureNameMatcher(TextureNameMatchMode.Name, "*").Match("WALL*", out score));
			Assert.False(new TextureNameMatcher(TextureNameMatchMode.Name, "*").Match("WALL", out score));
		}

		[Theory]
		[InlineData("")]
		[InlineData(null)]
		[InlineData(" \t\r\n\u2003 ")]
		public void EmptyFuzzyQueryMatchesEveryNonNullName(string query)
		{
			var matcher = new TextureNameMatcher(TextureNameMatchMode.Fuzzy, query);
			int score;
			Assert.True(matcher.Match("", out score));
			Assert.Equal(0, score);
			Assert.True(matcher.Match("BRICK", out score));
			Assert.Equal(0, score);
		}

		[Fact]
		public void FuzzyTokensAreCaseInsensitiveAndRequireEverySubsequence()
		{
			var matcher = new TextureNameMatcher(TextureNameMatchMode.Fuzzy, " brk\tWL\n12 ");
			int score;
			Assert.True(matcher.Match("textures/BrickWall12", out score));
			Assert.False(matcher.Match("BrickFloor12", out score));
			Assert.Equal(0, score);
			Assert.False(matcher.Match("BrickWall21", out score));
			Assert.False(matcher.Match("BK", out score));
			Assert.True(new TextureNameMatcher(TextureNameMatchMode.Fuzzy, "wall brick").Match("BrickWall", out score));
		}

		[Fact]
		public void FuzzyRepeatedCharactersNeedDistinctPositionsWithinEachToken()
		{
			var matcher = new TextureNameMatcher(TextureNameMatchMode.Fuzzy, "aaa");
			int score;
			Assert.False(matcher.Match("AA", out score));
			Assert.False(matcher.Match("ABC", out score));
			Assert.True(matcher.Match("A-A-A", out score));
			Assert.True(new TextureNameMatcher(TextureNameMatchMode.Fuzzy, "aaa aaa").Match("AAA", out score));
		}

		[Theory]
		[InlineData("/")]
		[InlineData("\\")]
		[InlineData("_")]
		[InlineData("-")]
		[InlineData(".")]
		[InlineData(" ")]
		public void FuzzyBoundaryHitsOutrankEquallySpacedInteriorHits(string separator)
		{
			AssertRanksAbove("bc", "A" + separator + "B" + separator + "C", "AXBXC");
		}

		[Fact]
		public void FuzzyLetterDigitTransitionsAndStartReceiveBonuses()
		{
			AssertRanksAbove("bc", "A1B2C", "AXBXC");
			AssertRanksAbove("12", "A1B2C", "1132C");
			AssertRanksAbove("bc", "BXC", "ABXC");
		}

		[Fact]
		public void FuzzyConsecutiveHitsAndFewerSkipsRankHigher()
		{
			AssertRanksAbove("bcd", "ABCXXD", "ABXCXD");
			AssertRanksAbove("bcd", "ABCDX", "ABXCXD");
			AssertRanksAbove("bc", "ABXC", "ABXXXC");
		}

		[Fact]
		public void FuzzyChoosesBestAlignmentInsteadOfFirstOccurrence()
		{
			AssertRanksAbove("bcd", "BXXXBCXD", "BXXXXCXD");
		}

		[Fact]
		public void EverySubstringRanksAboveEveryNonSubstringIncludingLongNames()
		{
			var matcher = new TextureNameMatcher(TextureNameMatchMode.Fuzzy, "bc");
			string[] substrings = { "BC", "ABC", "___BC", new string('X', 10000) + "BC" };
			string[] subsequences = { "B-C", "B_C", "B C", "BXC", "ABCXBC_XC".Replace("BC", "B_C") };
			foreach(string substring in substrings)
			foreach(string subsequence in subsequences)
			{
				int high, low;
				Assert.True(matcher.Match(substring, out high));
				Assert.True(matcher.Match(subsequence, out low));
				Assert.True(high > low, substring.Length + ": substring must rank above " + subsequence);
			}
		}

		[Fact]
		public void FuzzyScoresAreStableAcrossCallsAndCase()
		{
			var matcher = new TextureNameMatcher(TextureNameMatchMode.Fuzzy, "brk");
			int first, second, third;
			Assert.True(matcher.Match("BRICK", out first));
			Assert.True(matcher.Match("bRiCk", out second));
			Assert.True(matcher.Match("BRICK", out third));
			Assert.Equal(first, second);
			Assert.Equal(first, third);
		}

		[Fact]
		public void RegexPreservesEscapesAndMatchesCaseInsensitively()
		{
			var matcher = new TextureNameMatcher(TextureNameMatchMode.Regex, @"^textures/(brick|wall)\d+$");
			int score;
			Assert.True(matcher.Match("TEXTURES/Brick12", out score));
			Assert.True(matcher.Match("textures/wALL3", out score));
			Assert.False(matcher.Match("textures/wallX", out score));
			Assert.False(matcher.Match("textures/brick1/extra", out score));
			Assert.Null(matcher.Error);
			Assert.False(matcher.TimedOut);
		}

		[Fact]
		public void AllModesIgnoreCurrentCulture()
		{
			CultureInfo before = Thread.CurrentThread.CurrentCulture;
			try
			{
				Thread.CurrentThread.CurrentCulture = new CultureInfo("tr-TR");
				foreach(TextureNameMatchMode mode in Enum.GetValues(typeof(TextureNameMatchMode)))
				{
					int score;
					Assert.True(new TextureNameMatcher(mode, "file").Match("FILE", out score));
				}
			}
			finally { Thread.CurrentThread.CurrentCulture = before; }
		}

		[Theory]
		[InlineData("[")]
		[InlineData("(")]
		[InlineData(@"\")]
		public void InvalidRegexExposesErrorWithoutThrowingOrFallingBack(string pattern)
		{
			var matcher = new TextureNameMatcher(TextureNameMatchMode.Regex, pattern);
			Assert.False(string.IsNullOrEmpty(matcher.Error));
			Assert.False(matcher.TimedOut);
			int score;
			Assert.False(matcher.Match(pattern, out score));
			Assert.False(matcher.Match("anything", out score));
			Assert.Equal(0, score);
		}

		[Theory]
		[InlineData("(a+)+$")]
		[InlineData("(a|aa)+$")]
		public void CatastrophicRegexFinishesWithTimeoutWithinBoundedWorkerJoin(string pattern)
		{
			var matcher = new TextureNameMatcher(TextureNameMatchMode.Regex, pattern);
			Exception error = null;
			bool matched = true;
			int score = -1;
			var worker = new Thread(() =>
			{
				try { matched = matcher.Match(new string('a', 40) + "b", out score); }
				catch(Exception e) { error = e; }
			}) { IsBackground = true };
			worker.Start();
			Assert.True(worker.Join(2000), "Regex exceeded the 2 s safety bound: " + pattern);
			Assert.Null(error);
			Assert.False(matched);
			Assert.Equal(0, score);
			Assert.True(matcher.TimedOut);
			Assert.False(string.IsNullOrEmpty(matcher.Error));
			Assert.False(matcher.Match("a", out score));
			Assert.True(new TextureNameMatcher(TextureNameMatchMode.Regex, "a").Match("a", out score));
		}

		[Fact]
		public void NullNamesNeverMatchAndNullQueriesActAsEmpty()
		{
			foreach(TextureNameMatchMode mode in Enum.GetValues(typeof(TextureNameMatchMode)))
			{
				var matcher = new TextureNameMatcher(mode, null);
				int score;
				Assert.False(matcher.Match(null, out score));
				Assert.Equal(0, score);
				Assert.True(matcher.Match("", out score));
			}
			Assert.Throws<ArgumentOutOfRangeException>(() => new TextureNameMatcher((TextureNameMatchMode)99, ""));
		}

		private static void AssertRanksAbove(string query, string better, string worse)
		{
			var matcher = new TextureNameMatcher(TextureNameMatchMode.Fuzzy, query);
			int high, low;
			Assert.True(matcher.Match(better, out high), better);
			Assert.True(matcher.Match(worse, out low), worse);
			Assert.True(high > low, better + " (" + high + ") must outrank " + worse + " (" + low + ")");
		}
	}
}
