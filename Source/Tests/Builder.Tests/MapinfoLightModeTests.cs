using CodeImp.DoomBuilder;
using CodeImp.DoomBuilder.GZBuilder.Data;
using CodeImp.DoomBuilder.ZDoom;

namespace Builder.Tests
{
	[Collection("GeneralStatics")]
	public class MapinfoLightModeTests
	{
		private static MapinfoParser Parse(string properties)
		{
			var parser = new MapinfoParser();
			Assert.True(parser.Parse(CvarInfoParserTests.CreateTextResourceDataFromString("map MAP01 \"Lighting\" { " + properties + " }"), "MAP01", true));
			Assert.False(parser.HasError);
			return parser;
		}

		[Theory]
		[InlineData(0, MapInfo.GZDoomLightMode.STANDARD)]
		[InlineData(1, MapInfo.GZDoomLightMode.BRIGHT)]
		[InlineData(2, MapInfo.GZDoomLightMode.DOOM)]
		[InlineData(3, MapInfo.GZDoomLightMode.DARK)]
		[InlineData(4, MapInfo.GZDoomLightMode.LEGACY)]
		[InlineData(8, MapInfo.GZDoomLightMode.UNDEFINED)]
		[InlineData(16, MapInfo.GZDoomLightMode.UNDEFINED)]
		public void SupportedMapinfoValuesMatchEngineSemantics(int value, MapInfo.GZDoomLightMode expected)
		{
			using(var map = new SndInfoParserTests.FakeMap("doom"))
			{
				Assert.Equal(expected, Parse("lightmode = " + value).MapInfo.LightMode);
				Assert.Equal(0, map.Logger.ErrorsCount);
			}
		}

		[Theory]
		[InlineData(8)]
		[InlineData(16)]
		public void SoftwareValuesResetAnInheritedLightModeToUnset(int value)
		{
			using(var map = new SndInfoParserTests.FakeMap("doom"))
			{
				var parser = new MapinfoParser();
				Assert.True(parser.Parse(CvarInfoParserTests.CreateTextResourceDataFromString("defaultmap { lightmode = 3 } map MAP01 \"Lighting\" { lightmode = " + value + " }"), "MAP01", true));
				Assert.Equal(MapInfo.GZDoomLightMode.UNDEFINED, parser.MapInfo.LightMode);
				Assert.Equal(0, map.Logger.ErrorsCount);
			}
		}

		[Theory]
		[InlineData(-2147483648)]
		[InlineData(-1)]
		[InlineData(5)]
		[InlineData(6)]
		[InlineData(7)]
		[InlineData(9)]
		[InlineData(15)]
		[InlineData(17)]
		[InlineData(32)]
		[InlineData(2147483647)]
		public void InvalidIntegersWarnAndContinueWithTheExistingMode(int value)
		{
			using(var map = new SndInfoParserTests.FakeMap("doom"))
			{
				var parser = new MapinfoParser();
				string source = "defaultmap { lightmode = 4 } map MAP01 \"Lighting\" { lightmode = " + value + " fogdensity = 32 }";
				Assert.True(parser.Parse(CvarInfoParserTests.CreateTextResourceDataFromString(source), "MAP01", true));
				Assert.False(parser.HasError);
				Assert.Equal(MapInfo.GZDoomLightMode.LEGACY, parser.MapInfo.LightMode);
				Assert.Equal(32, parser.MapInfo.FogDensity);
				ErrorItem warning = Assert.Single(map.Logger.GetErrors(0));
				Assert.Equal(ErrorType.Warning, warning.Type);
				Assert.Contains("MAPINFO warning", warning.Description);
				Assert.Contains("Invalid light mode " + value, warning.Description);
			}
		}

		[Fact]
		public void InvalidModeWithoutAnInheritedValueStaysUnset()
		{
			using(var map = new SndInfoParserTests.FakeMap("doom"))
			{
				Assert.Equal(MapInfo.GZDoomLightMode.UNDEFINED, Parse("lightmode = 5").MapInfo.LightMode);
				Assert.Equal(1, map.Logger.ErrorsCount);
			}
		}

		[Fact]
		public void ValidModeAfterAWarningIsStillParsed()
		{
			using(var map = new SndInfoParserTests.FakeMap("doom"))
			{
				MapinfoParser parser = Parse("lightmode = 5 lightmode = 2 pixelratio = 1.5");
				Assert.Equal(MapInfo.GZDoomLightMode.DOOM, parser.MapInfo.LightMode);
				Assert.Equal(1.5f, parser.MapInfo.PixelRatio);
				Assert.Equal(1, map.Logger.ErrorsCount);
			}
		}

		[Fact]
		public void SoftwareEnumNamesHaveTheirEngineValues()
		{
			Assert.Equal(8, (int)MapInfo.GZDoomLightMode.ZDoomSoftware);
			Assert.Equal(16, (int)MapInfo.GZDoomLightMode.DoomSoftware);
			Assert.Equal(MapInfo.GZDoomLightMode.UNDEFINED, new MapInfo().LightMode);
		}
	}
}
