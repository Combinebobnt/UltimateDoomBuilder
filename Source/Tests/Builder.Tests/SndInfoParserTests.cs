using CodeImp.DoomBuilder;
using CodeImp.DoomBuilder.Config;
using CodeImp.DoomBuilder.ZDoom;
using System.Reflection;
using System.Runtime.CompilerServices;

namespace Builder.Tests
{
	// Shares General.map with EngineArchiveFoldTests, so xunit must not run the two in parallel
	[Collection("GeneralStatics")]
	public class SndInfoParserTests
	{
		private const string GameBlocks = "$ifdoom\n d/x dsx\n$endif\n$ifheretic\n h/x hx\n$alias h/y h/x\n$endif\n e/x dsex\n";

		// The parser reads BaseGame and InternalSoundNames off General.Map.Config, and logs to General.ErrorLogger
		internal sealed class FakeMap : IDisposable
		{
			public readonly ErrorLogger Logger = new ErrorLogger();
			private readonly object previousmap;
			private readonly object previouslogger;

			public FakeMap(string basegame)
			{
				previousmap = typeof(General).GetField("map", BindingFlags.NonPublic | BindingFlags.Static)!.GetValue(null);
				previouslogger = typeof(General).GetField("errorlogger", BindingFlags.NonPublic | BindingFlags.Static)!.GetValue(null);

				var config = (GameConfiguration)RuntimeHelpers.GetUninitializedObject(typeof(GameConfiguration));
				typeof(GameConfiguration).GetField("basegame", BindingFlags.NonPublic | BindingFlags.Instance)!.SetValue(config, basegame);
				typeof(GameConfiguration).GetField("internalsoundnames", BindingFlags.NonPublic | BindingFlags.Instance)!.SetValue(config, new HashSet<string>(new[] { "*death", "*pain100" }, StringComparer.OrdinalIgnoreCase));

				var map = (MapManager)RuntimeHelpers.GetUninitializedObject(typeof(MapManager));
				typeof(MapManager).GetField("config", BindingFlags.NonPublic | BindingFlags.Instance)!.SetValue(map, config);

				// Their finalizers would dispose fields the real constructors set
				GC.SuppressFinalize(config);
				GC.SuppressFinalize(map);
				typeof(General).GetField("map", BindingFlags.NonPublic | BindingFlags.Static)!.SetValue(null, map);
				typeof(General).GetField("errorlogger", BindingFlags.NonPublic | BindingFlags.Static)!.SetValue(null, Logger);
			}

			public void Dispose()
			{
				typeof(General).GetField("map", BindingFlags.NonPublic | BindingFlags.Static)!.SetValue(null, previousmap);
				typeof(General).GetField("errorlogger", BindingFlags.NonPublic | BindingFlags.Static)!.SetValue(null, previouslogger);
			}
		}

		private static SndInfoParser Parse(string text, bool expectedresult = true)
		{
			var parser = new SndInfoParser();
			Assert.Equal(expectedresult, parser.Parse(CvarInfoParserTests.CreateTextResourceDataFromString(text), true));
			return parser;
		}

		private static void AssertFinishSetupLogsNothing(SndInfoParser parser, FakeMap map)
		{
			parser.FinishSetup();
			string logged = string.Join(" | ", map.Logger.GetErrors(0).Select(e => e.Description));
			Assert.True(map.Logger.ErrorsCount == 0, "logged: " + logged);
		}

		[Fact]
		public void AliasToUndefinedSound_LogsWarningWithoutAMainWindow()
		{
			using(var map = new FakeMap(GameType.DOOM))
			{
				Assert.Null(General.MainWindow);
				SndInfoParser parser = Parse("$alias alias missing\n");
				parser.FinishSetup();
				ErrorItem warning = Assert.Single(map.Logger.GetErrors(0));
				Assert.Equal(ErrorType.Warning, warning.Type);
				Assert.Equal("SNDINFO warning: sound \"missing\" is not defined.", warning.Description);
			}
		}

		[Fact]
		public void DoomBase_EndifClosesTheMatchingBlockAndTheOtherGameIsSkipped()
		{
			using(var map = new FakeMap(GameType.DOOM))
			{
				SndInfoParser parser = Parse(GameBlocks);
				Assert.True(parser.Sounds.ContainsKey("d/x"), "d/x");
				Assert.True(parser.Sounds.ContainsKey("e/x"), "e/x");
				Assert.False(parser.Sounds.ContainsKey("h/x"), "h/x");
				Assert.False(parser.Sounds.ContainsKey("h/y"), "h/y");
				AssertFinishSetupLogsNothing(parser, map);
			}
		}

		[Fact]
		public void HereticBase_ReadsOnlyTheHereticBlock()
		{
			using(new FakeMap(GameType.HERETIC))
			{
				SndInfoParser parser = Parse(GameBlocks);
				Assert.True(parser.Sounds.ContainsKey("h/x"), "h/x");
				Assert.True(parser.Sounds.ContainsKey("h/y"), "h/y");
				Assert.True(parser.Sounds.ContainsKey("e/x"), "e/x");
				Assert.False(parser.Sounds.ContainsKey("d/x"), "d/x");
			}
		}

		[Fact]
		public void ChexBase_ReadsIfdoomBlocks()
		{
			using(new FakeMap(GameType.CHEX))
			{
				SndInfoParser parser = Parse(GameBlocks);
				Assert.True(parser.Sounds.ContainsKey("d/x"), "d/x");
				Assert.False(parser.Sounds.ContainsKey("h/x"), "h/x");
			}
		}

		[Fact]
		public void EndifAtEndOfFileInsideAMatchingBlock_Parses()
		{
			using(new FakeMap(GameType.DOOM))
			{
				SndInfoParser parser = Parse("$ifdoom\n d/x dsx\n$endif\n");
				Assert.True(parser.Sounds.ContainsKey("d/x"), "d/x");
			}
		}

		// filter/game-doomchex/sndinfo.txt's menu/quit1 picks from names only $playercompat defines
		[Fact]
		public void PlayerCompatName_IsADefinedSoundForRandom()
		{
			using(var map = new FakeMap(GameType.DOOM))
			{
				SndInfoParser parser = Parse("$playercompat player male *death player/male/death1\n$random menu/quit1 { player/male/death1 }\n");
				AssertFinishSetupLogsNothing(parser, map);
				Assert.True(parser.Sounds.ContainsKey("menu/quit1"), "menu/quit1");
				Assert.True(parser.Sounds.ContainsKey("player/male/death1"), "player/male/death1");
			}
		}
	}
}
