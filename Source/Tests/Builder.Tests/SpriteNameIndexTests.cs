using CodeImp.DoomBuilder.Data;

namespace Builder.Tests
{
	public class SpriteNameIndexTests
	{
		private static List<string> Oracle(IEnumerable<string> allspritenames, string sourcename)
		{
			// Copy of the selection loop SetupSpriteFrame ran before the index existed
			List<string> result = new List<string>();
			foreach(string s in allspritenames)
			{
				if(s.StartsWith(sourcename)) result.Add(s);
			}
			return result;
		}

		private static void AssertMatchesOracle(IEnumerable<string> names, params string[] sourcenames)
		{
			HashSet<string> set = new HashSet<string>(names);
			SpriteNameIndex index = new SpriteNameIndex(set);
			foreach(string sourcename in sourcenames)
			{
				List<string> expected = Oracle(set, sourcename);
				List<string> actual = index.Matching(sourcename);
				Assert.True(expected.SequenceEqual(actual), "source \"" + sourcename + "\": expected [" + string.Join(",", expected)
					+ "] actual [" + string.Join(",", actual) + "]");
			}
		}

		private static void AssertSelects(IEnumerable<string> names, string sourcename, params string[] mustselect)
		{
			List<string> selected = new SpriteNameIndex(new HashSet<string>(names)).Matching(sourcename);
			foreach(string name in mustselect) Assert.True(selected.Contains(name), sourcename + " selects " + name);
		}

		[Fact]
		public void MixedPrefixes_KeepEnumerationOrder()
		{
			string[] names = { "POSSA1", "TROOA1", "POSSA2A8", "SPOSA1", "POSSB1", "TROOB1", "SPOSB0", "POSSC0" };
			AssertMatchesOracle(names, "POSS", "TROO", "SPOS", "MISS");
			Assert.Equal(new List<string> { "POSSA1", "POSSA2A8", "POSSB1", "POSSC0" }, new SpriteNameIndex(names).Matching("POSS"));
		}

		[Fact]
		public void SamePrefixAcrossManyFrames_KeepsEnumerationOrder()
		{
			List<string> names = new List<string>();
			for(char frame = 'A'; frame <= 'Z'; frame++)
			{
				for(int angle = 0; angle <= 8; angle++)
				{
					names.Add("PLAY" + frame + angle);
					if(angle % 3 == 0) names.Add("SARG" + frame + angle);
				}
			}
			AssertMatchesOracle(names, "PLAY", "SARG");
			AssertSelects(names, "PLAY", "PLAYA1", "PLAYZ8");
		}

		[Fact]
		public void MirrorNames_AreSelected()
		{
			string[] names = { "POSSA2A8", "POSSA3A7", "TROOA1", "POSSB2B8", "POSSA4A6" };
			AssertMatchesOracle(names, "POSS", "TROO");
			AssertSelects(names, "POSS", "POSSA2A8", "POSSB2B8");
		}

		[Fact]
		public void NamesShorterThanFourCharacters_MatchAsTheLoopDid()
		{
			string[] names = { "POS", "PO", "P", "", "POSS", "POSSA1", "ABC" };
			AssertMatchesOracle(names, "POSS", "ABCD", "PO", "P", "");
			AssertSelects(names, "POSS", "POSS", "POSSA1");
		}

		[Fact]
		public void LowercaseNames_MatchAsTheLoopDid()
		{
			string[] names = { "possa1", "POSSA1", "PossA2A8", "possb0", "trooa1" };
			AssertMatchesOracle(names, "poss", "POSS", "Poss", "troo", "TROO");
			AssertSelects(names, "poss", "possa1", "possb0");
		}

		[Fact]
		public void BracketAndCaretFrameCharacters_MatchAsTheLoopDid()
		{
			string[] names = { "POSS[1", "POSSA1", "POSS\\1", "POSS]1", "POSS^1", "POSS[2]8", "PO[SA1", "\\POSA1", "^POSA1" };
			AssertMatchesOracle(names, "POSS", "PO[S", "\\POS", "^POS");
			AssertSelects(names, "POSS", "POSS[1", "POSS\\1", "POSS]1", "POSS^1", "POSSA1");
		}

		[Fact]
		public void HyphenatedNames_MatchAsTheLoopDid()
		{
			string[] names = { "PO-SSA1", "POSSA1", "POSS-A1", "-POSSA1", "POS-SA1", "POSSB1", "P-OSSA1" };
			AssertMatchesOracle(names, "POSS", "PO-S", "-POS");
			AssertSelects(names, "POSS", "POSSA1", "POSS-A1", "POSSB1");
		}

		[Fact]
		public void SoftHyphenNames_MatchAsTheLoopDid()
		{
			string[] names = { "PO\u00ADSSA1", "POSSA1", "POSS\u00ADA1", "\u00ADPOSSA1", "POSSB1", "POS\u00ADSA1" };
			AssertMatchesOracle(names, "POSS", "PO\u00ADS", "\u00ADPOS");
			AssertSelects(names, "POSS", "POSSA1", "POSSB1");
		}

		[Fact]
		public void CombiningMarkAtPositionFour_MatchesAsTheLoopDid()
		{
			string[] names = { "POSS\u0301A1", "POSSA1", "POS\u0301SA1", "POSSB1", "TROO\u0308A1" };
			AssertMatchesOracle(names, "POSS", "TROO", "POS\u0301");
		}

		[Fact]
		public void NonAsciiPrefix_MatchesAsTheLoopDid()
		{
			string[] names = { "P\u00D6SSA1", "POSSA1", "\u00C6POSA1", "POSS\u00D6A1", "POSSB1", "\u0420OSSA1", "PO\u00DFA1" };
			AssertMatchesOracle(names, "POSS", "P\u00D6SS", "\u00C6POS", "\u0420OSS", "PO\u00DF", "POSSA");
			AssertSelects(names, "POSS", "POSSA1", "POSS\u00D6A1", "POSSB1");
		}

		[Fact]
		public void SlowNameBeforeBucketNames_KeepsEnumerationOrder()
		{
			string[] names = { "POSS\u00D6A1", "POSSA1", "POSS\u00C6B1", "POSSB1" };
			AssertMatchesOracle(names, "POSS");
			Assert.Equal(new List<string> { "POSS\u00D6A1", "POSSA1", "POSS\u00C6B1", "POSSB1" }, new SpriteNameIndex(names).Matching("POSS"));
		}

		[Fact]
		public void NonAlphanumericSourcename_FallsBackToTheFullScan()
		{
			string[] names = { "PO-SA1", "POSSA1", "PO SA1", "PO.SA1", "PO_SA1", "PO\u00ADSA1", "POSA1" };
			AssertMatchesOracle(names, "PO-S", "PO S", "PO.S", "PO_S", "PO\u00ADS", "PO", "POSSA", "POSSA1", "");
			AssertSelects(names, "PO-S", "PO-SA1");
		}

		[Fact]
		public void GeneratedSets_MatchTheLoop()
		{
			string alphabet = "ABPOSTRZaps019[\\]^-_ \u00AD\u0301\u00D6\u00C6\u00DF";
			Random random = new Random(20231);
			for(int round = 0; round < 40; round++)
			{
				List<string> names = new List<string>();
				int count = random.Next(1, 300);
				for(int n = 0; n < count; n++)
				{
					int length = random.Next(0, 9);
					char[] chars = new char[length];
					for(int c = 0; c < length; c++)
					{
						// Mostly letters, so prefixes repeat and buckets fill
						chars[c] = random.Next(4) == 0 ? alphabet[random.Next(alphabet.Length)] : alphabet[random.Next(4)];
					}
					names.Add(new string(chars));
				}

				HashSet<string> sources = new HashSet<string>();
				foreach(string name in names)
					if(name.Length >= 4) sources.Add(name.Substring(0, 4));
				for(int n = 0; n < 20; n++)
				{
					char[] chars = new char[random.Next(3, 6)];
					for(int c = 0; c < chars.Length; c++) chars[c] = alphabet[random.Next(alphabet.Length)];
					sources.Add(new string(chars));
				}
				AssertMatchesOracle(names, sources.ToArray());
			}
		}
	}
}
