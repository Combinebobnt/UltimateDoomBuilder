using System.Reflection;
using System.Runtime.CompilerServices;
using CodeImp.DoomBuilder;
using CodeImp.DoomBuilder.Config;
using CodeImp.DoomBuilder.Data;
using CodeImp.DoomBuilder.GZBuilder.Data;
using CodeImp.DoomBuilder.Map;
using CodeImp.DoomBuilder.Types;
using CodeImp.DoomBuilder.VisualModes;
using CodeImp.DoomBuilder.ZDoom;

namespace Builder.Tests
{
	[Collection("GeneralStatics")]
	public class MapinfoFogTests
	{
		private static MapInfo Parse(string properties)
		{
			var parser = new MapinfoParser();
			Assert.True(parser.Parse(CvarInfoParserTests.CreateTextResourceDataFromString("map MAP01 \"Fog\" { " + properties + " }"), "MAP01", true));
			Assert.False(parser.HasError);
			return parser.MapInfo;
		}

		private static void Set(object target, string field, object value)
		{
			target.GetType().GetField(field, BindingFlags.NonPublic | BindingFlags.Instance)!.SetValue(target, value);
		}

		private static Sector CreateSector(MapInfo info, int brightness = 192, int? color = null, int? density = null, bool sky = false, bool udmf = true, int effect = 0)
		{
			Set(General.Map.Config, "universalmapformat", udmf);
			General.Map.Config.SkyFlatName = "F_SKY1";
			Set(General.Map.Config, "sectoreffects", new Dictionary<int, SectorEffectInfo> { { 87, null } });
			var data = (DataManager)RuntimeHelpers.GetUninitializedObject(typeof(DataManager));
			GC.SuppressFinalize(data);
			Set(data, "mapinfo", info);
			Set(General.Map, "data", data);
			var sector = new Sector(new MapSet(), 0, 0) { Brightness = brightness, Effect = effect };
			Set(sector, "ceiltexname", sky ? "F_SKY1" : "CEIL");
			if(color.HasValue) sector.Fields["fadecolor"] = new UniValue(UniversalType.Integer, color.Value);
			if(density.HasValue) sector.Fields["fogdensity"] = new UniValue(UniversalType.Integer, density.Value);
			sector.UpdateFogColor();
			return sector;
		}

		private static void AssertDensity(Sector sector, int engineDensity)
		{
			float expected = engineDensity * (-1.442692f / 64000f);
			float actual = VisualGeometry.CalculateFogFactor(sector, sector.Brightness);
			Assert.True(Math.Abs(expected - actual) < 0.00000001f, "Expected " + expected + ", got " + actual);
		}

		[Fact]
		public void DefaultsHaveNoDensityOrColour()
		{
			var info = new MapInfo();
			Assert.Equal(0, info.FogDensity);
			Assert.Equal(0, info.OutsideFogDensity);
			Assert.False(info.HasFogDensity);
			Assert.False(info.HasOutsideFogDensity);
			Assert.False(info.HasFadeColor);
			Assert.False(info.HasOutsideFogColor);
			Assert.False(info.IsDefined);
		}

		[Theory]
		[InlineData("")]
		[InlineData("fogdensity = 0 outsidefogdensity = 0")]
		public void ColourRemainsEnabledWithMissingOrZeroDensity(string densities)
		{
			MapInfo info = Parse("fade = \"800000\" outsidefog = \"00ff00\" " + densities);
			Assert.True(info.HasFadeColor);
			Assert.True(info.HasOutsideFogColor);
			Assert.Equal(0, info.FogDensity);
			Assert.Equal(0, info.OutsideFogDensity);
			Assert.Equal(densities.Length > 0, info.HasFogDensity);
			Assert.Equal(densities.Length > 0, info.HasOutsideFogDensity);
		}

		[Theory]
		[InlineData(-1, 0)]
		[InlineData(0, 0)]
		[InlineData(1, 1)]
		[InlineData(32, 32)]
		[InlineData(255, 255)]
		[InlineData(512, 512)]
		[InlineData(1024, 512)]
		public void ExplicitDensitiesAreRecordedAndClamped(int value, int expected)
		{
			MapInfo info = Parse("fogdensity = " + value + " outsidefogdensity = " + value);
			Assert.Equal(expected, info.FogDensity);
			Assert.Equal(expected, info.OutsideFogDensity);
			Assert.True(info.HasFogDensity);
			Assert.True(info.HasOutsideFogDensity);
		}

		[Fact]
		public void BlackColourOverridesDoNotLeaveStaleColourFlags()
		{
			MapInfo info = Parse("fade = \"800000\" outsidefog = \"00ff00\" fade = \"000000\" outsidefog = \"000000\"");
			Assert.False(info.HasFadeColor);
			Assert.False(info.HasOutsideFogColor);
		}

		[Fact]
		public void UnrelatedMapDoesNotBecomeDefinedDuringColourValidation()
		{
			var parser = new MapinfoParser();
			Assert.True(parser.Parse(CvarInfoParserTests.CreateTextResourceDataFromString("map MAP02 \"Other\" { fade = \"800000\" }"), "MAP01", true));
			Assert.False(parser.MapInfo.IsDefined);
		}

		[Fact]
		public void DefaultMapInheritanceAndResetTrackDensityPresence()
		{
			var parser = new MapinfoParser();
			Assert.True(parser.Parse(CvarInfoParserTests.CreateTextResourceDataFromString("defaultmap { fogdensity = 0 fade = \"800000\" } adddefaultmap { outsidefog = \"00ff00\" } map MAP01 \"Fog\" { }"), "MAP01", true));
			Assert.True(parser.MapInfo.HasFogDensity);
			Assert.False(parser.MapInfo.HasOutsideFogDensity);
			Assert.True(parser.MapInfo.HasFadeColor);
			Assert.True(parser.MapInfo.HasOutsideFogColor);
			Assert.True(parser.Parse(CvarInfoParserTests.CreateTextResourceDataFromString("defaultmap { } map MAP01 \"Reset\" { }"), "MAP01", true));
			Assert.False(parser.MapInfo.HasFogDensity);
			Assert.False(parser.MapInfo.HasFadeColor);
			Assert.False(parser.MapInfo.HasOutsideFogColor);
		}

		[Theory]
		[InlineData(0, 255)]
		[InlineData(192, 63)]
		[InlineData(247, 30)]
		[InlineData(248, 0)]
		[InlineData(255, 0)]
		public void SectorColourUsesEngineLightLevelFallback(int brightness, int expected)
		{
			using(new SndInfoParserTests.FakeMap("doom"))
			{
				Sector sector = CreateSector(new MapInfo(), brightness, color: 0x0000ff);
				Assert.Equal(SectorFogMode.FADE, sector.FogMode);
				AssertDensity(sector, expected);
			}
		}

		[Theory]
		[InlineData(false, false)]
		[InlineData(true, false)]
		[InlineData(false, true)]
		[InlineData(true, true)]
		public void MapinfoColoursUseFallbackWithMissingOrZeroDensities(bool sky, bool zero)
		{
			using(new SndInfoParserTests.FakeMap("doom"))
			{
				MapInfo info = Parse("fade = \"800000\" outsidefog = \"00ff00\" " + (zero ? "fogdensity = 0 outsidefogdensity = 0" : ""));
				Sector sector = CreateSector(info, sky: sky);
				Assert.Equal(sky ? 0x00ff00 : 0x800000, sector.FogColor.ToArgb() & 0xffffff);
				AssertDensity(sector, 63);
			}
		}

		[Theory]
		[InlineData(-1, 16)]
		[InlineData(0, 16)]
		[InlineData(1, 16)]
		[InlineData(2, 1)]
		[InlineData(64, 32)]
		[InlineData(255, 127)]
		[InlineData(512, 256)]
		[InlineData(600, 256)]
		public void SectorDensityOverridesOutsideAndLevelDensity(int density, int expected)
		{
			using(new SndInfoParserTests.FakeMap("doom"))
			{
				MapInfo info = Parse("outsidefog = \"00ff00\" outsidefogdensity = 32 fogdensity = 80");
				AssertDensity(CreateSector(info, color: 0x00ff00, density: density, sky: true), expected);
			}
		}

		[Theory]
		[InlineData(0x00ff00, 16)]
		[InlineData(0x0000ff, 40)]
		public void OutsideDensityMatchesResolvedColourEvenIndoors(int color, int expected)
		{
			using(new SndInfoParserTests.FakeMap("doom"))
			{
				MapInfo info = Parse("outsidefog = \"00ff00\" outsidefogdensity = 32 fogdensity = 80");
				AssertDensity(CreateSector(info, color: color), expected);
			}
		}

		[Theory]
		[InlineData(null)]
		[InlineData(0x0000ff)]
		public void LevelDensityPrecedesLightFallbackForMapinfoAndSectorColour(int? color)
		{
			using(new SndInfoParserTests.FakeMap("doom"))
			{
				AssertDensity(CreateSector(Parse("fade = \"800000\" fogdensity = 32"), color: color), 16);
			}
		}

		[Theory]
		[InlineData(0)]
		[InlineData(1)]
		public void ZeroOutsideDensityFallsThroughToLevelDensity(int outsideDensity)
		{
			using(new SndInfoParserTests.FakeMap("doom"))
			{
				AssertDensity(CreateSector(Parse("outsidefog = \"00ff00\" outsidefogdensity = " + outsideDensity + " fogdensity = 32"), sky: true), 16);
			}
		}

		[Theory]
		[InlineData(false, 0)]
		[InlineData(true, 0)]
		[InlineData(false, 87)]
		public void OutsideColourAppliesToSkyAndOutsideEffectsInBothFormats(bool udmf, int effect)
		{
			using(new SndInfoParserTests.FakeMap("doom"))
			{
				Sector sector = CreateSector(Parse("outsidefog = \"00ff00\" outsidefogdensity = 32"), sky: effect == 0, udmf: udmf, effect: effect);
				Assert.Equal(0x00ff00, sector.FogColor.ToArgb() & 0xffffff);
				AssertDensity(sector, 16);
			}
		}

		[Theory]
		[InlineData(192, null)]
		[InlineData(247, null)]
		[InlineData(248, null)]
		[InlineData(192, 0x000000)]
		[InlineData(248, 0x000000)]
		public void BlackFogKeepsClassicCurveAndIgnoresLevelDensity(int brightness, int? color)
		{
			using(new SndInfoParserTests.FakeMap("doom"))
			{
				Sector sector = CreateSector(Parse("fogdensity = 256 outsidefogdensity = 128"), brightness, color: color);
				Assert.Equal(brightness < 248 ? SectorFogMode.CLASSIC : SectorFogMode.NONE, sector.FogMode);
				float expected = (brightness < 248 ? Math.Max(30, 255 - brightness) : 0) * (-1.442692f / 512000f);
				Assert.Equal(expected, VisualGeometry.CalculateFogFactor(sector, brightness));
			}
		}

		[Fact]
		public void BlackSectorColourOverridesMapinfoFogWithoutUsingItsDensity()
		{
			using(new SndInfoParserTests.FakeMap("doom"))
			{
				Sector sector = CreateSector(Parse("fade = \"800000\" outsidefog = \"00ff00\" fogdensity = 32 outsidefogdensity = 64"), color: 0, sky: true);
				Assert.Equal(SectorFogMode.CLASSIC, sector.FogMode);
				Assert.Equal(63 * (-1.442692f / 512000f), VisualGeometry.CalculateFogFactor(sector, 192));
			}
		}

		[Fact]
		public void ExplicitSectorDensityPrecedesBlackFogAtFullBrightness()
		{
			using(new SndInfoParserTests.FakeMap("doom"))
			{
				Sector sector = CreateSector(new MapInfo(), 255, density: 64);
				Assert.Equal(SectorFogMode.FADE, sector.FogMode);
				AssertDensity(sector, 32);
			}
		}
	}
}
