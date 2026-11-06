using CodeImp.DoomBuilder.Config;
using CodeImp.DoomBuilder.ZDoom;

namespace Builder.Tests
{
	// Swaps General.map like SndInfoParserTests, so it joins the same non-parallel collection
	[Collection("GeneralStatics")]
	public class TerrainParserTests
	{
		private const string GameBlocks = "ifdoom\nterrain DoomOnly { splash x }\nendif\nifheretic\nterrain HereticOnly { }\nendif\nterrain Shared { }\n";

		private static TerrainParser Parse(string text)
		{
			var parser = new TerrainParser();
			Assert.True(parser.Parse(CvarInfoParserTests.CreateTextResourceDataFromString(text), true));
			return parser;
		}

		// The engine's CheckGame(name, true) counts Chex as Doom for ifdoom
		[Fact]
		public void ChexBase_ReadsIfdoomBlocksAndSkipsTheOtherGame()
		{
			using(new SndInfoParserTests.FakeMap(GameType.CHEX))
			{
				TerrainParser parser = Parse(GameBlocks);
				Assert.True(parser.TerrainNames.Contains("DoomOnly"), "DoomOnly");
				Assert.True(parser.TerrainNames.Contains("Shared"), "Shared");
				Assert.False(parser.TerrainNames.Contains("HereticOnly"), "HereticOnly");
			}
		}
	}
}
