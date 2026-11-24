using CodeImp.DoomBuilder.GZBuilder.Data;
using CodeImp.DoomBuilder.Rendering;

namespace Builder.Tests
{
	public class LightModelTests
	{
		private const MapInfo.GZDoomLightMode Unset = MapInfo.GZDoomLightMode.UNDEFINED;

		[Fact]
		public void UnsetMapUnderAKeyedConfigurationIsZDoomSoftware()
		{
			LightModelSettings s = LightModel.Resolve(Unset, LightModel.ZDOOM_SOFTWARE, true, 0, true);
			Assert.Equal(LightModelKind.ZDoomSoftware, s.Kind);
			Assert.Equal(40f, s.GlobVis);
			Assert.Equal(-1, s.HardwareMode);
			Assert.True(s.Distance);
		}

		[Fact]
		public void ExplicitModesZeroToFourAreHardwareUnderAKeyedConfiguration()
		{
			for(int mode = 0; mode <= 4; mode++)
			{
				LightModelSettings s = LightModel.Resolve((MapInfo.GZDoomLightMode)mode, LightModel.ZDOOM_SOFTWARE, true, 0, true);
				Assert.Equal(LightModelKind.Hardware, s.Kind);
				Assert.Equal(mode, s.HardwareMode);
			}

			// A configuration default in 0-4 applies to unset maps the same way
			Assert.Equal(3, LightModel.Resolve(Unset, 3, true, 0, true).HardwareMode);
		}

		[Fact]
		public void LegacyDensityIsHalfTheLevelFogDensityOrEighteen()
		{
			Assert.Equal(LightModel.LEGACY_DEFAULT_DENSITY, LightModel.Resolve(MapInfo.GZDoomLightMode.LEGACY, 8, true, 0, true).LegacyDensity);
			Assert.Equal(LightModel.LEGACY_DEFAULT_DENSITY, LightModel.Resolve(MapInfo.GZDoomLightMode.LEGACY, 8, true, 1, true).LegacyDensity);
			Assert.Equal(32, LightModel.Resolve(MapInfo.GZDoomLightMode.LEGACY, 8, true, 64, true).LegacyDensity);
			Assert.Equal(16, LightModel.Resolve(MapInfo.GZDoomLightMode.LEGACY, 8, true, 33, true).LegacyDensity);
		}

		[Fact]
		public void NoKeyKeepsTheEditorCurveForEveryMap()
		{
			foreach(MapInfo.GZDoomLightMode mode in new[] { Unset, MapInfo.GZDoomLightMode.STANDARD, MapInfo.GZDoomLightMode.BRIGHT,
				MapInfo.GZDoomLightMode.DOOM, MapInfo.GZDoomLightMode.DARK, MapInfo.GZDoomLightMode.LEGACY })
			{
				LightModelSettings s = LightModel.Resolve(mode, -1, true, 0, true);
				Assert.Equal(LightModelKind.Editor, s.Kind);
				Assert.Equal(mode != MapInfo.GZDoomLightMode.STANDARD, s.DoomCurve);
				Assert.Equal(mode == MapInfo.GZDoomLightMode.DOOM, LightModel.Resolve(mode, -1, false, 0, true).DoomCurve);
			}
		}

		[Fact]
		public void FogToggleOffTurnsDistanceOff()
		{
			Assert.False(LightModel.Resolve(Unset, LightModel.ZDOOM_SOFTWARE, true, 0, false).Distance);
			Assert.False(LightModel.Resolve(MapInfo.GZDoomLightMode.DOOM, LightModel.ZDOOM_SOFTWARE, true, 0, false).Distance);
			Assert.False(LightModel.Resolve(Unset, -1, true, 0, false).Distance);
		}

		[Fact]
		public void UnsupportedConfigurationDefaultsFallBackToTheEditorCurve()
		{
			Assert.Equal(LightModelKind.Editor, LightModel.Resolve(Unset, 16, true, 0, true).Kind);
			Assert.Equal(LightModelKind.Editor, LightModel.Resolve(Unset, 5, true, 0, true).Kind);
		}

		// The shader reads the shaded level from byte 1; a wrong pack shifts every wall
		[Fact]
		public void PackedLightCarriesTheShadedLevelAndTheBaseLevel()
		{
			var failures = new System.Collections.Generic.List<string>();
			for(int level = -300; level <= 600; level += 3)
			{
				for(int shade = -255; shade <= 255; shade += 15)
				{
					int shaded = CodeImp.DoomBuilder.General.Clamp(level + shade, 0, 255);
					int packed = LightModel.PackLight(level, shaded, level);
					if(((packed >> 8) & 0xFF) != shaded || (packed & 0xFF) != CodeImp.DoomBuilder.General.Clamp(level, 0, 255)
						|| ((packed >> 16) & 0xFF) != CodeImp.DoomBuilder.General.Clamp(level, 0, 255) || ((packed >> 24) & 0xFF) != 255)
						failures.Add(level + "/" + shade + ": " + packed.ToString("X8"));
				}
			}
			Assert.Empty(failures);
		}

		// GetFogDensity's order for black fog in the light, and which sectors still go through the fog programs
		[Fact]
		public void BlackFogFollowsTheFadeColourAndGetFogDensitysOrder()
		{
			LightModelSettings software = LightModel.Resolve(Unset, LightModel.ZDOOM_SOFTWARE, true, 0, true);
			LightModelSettings doom = LightModel.Resolve(MapInfo.GZDoomLightMode.DOOM, LightModel.ZDOOM_SOFTWARE, true, 0, true);
			LightModelSettings legacy = LightModel.Resolve(MapInfo.GZDoomLightMode.LEGACY, LightModel.ZDOOM_SOFTWARE, true, 64, true);
			LightModelSettings editor = LightModel.Resolve(MapInfo.GZDoomLightMode.DOOM, -1, true, 0, true);
			LightModelSettings doomflat = LightModel.Resolve(MapInfo.GZDoomLightMode.DOOM, LightModel.ZDOOM_SOFTWARE, true, 0, false);

			Assert.Equal(0f, LightModel.BlackFogDensity(doom, true, 0));
			Assert.Equal(40f, LightModel.BlackFogDensity(doom, true, 40));
			Assert.Equal(32f, LightModel.BlackFogDensity(legacy, true, 40));
			foreach(LightModelSettings s in new[] { doom, legacy, software, editor })
				Assert.Equal(-1f, LightModel.BlackFogDensity(s, false, 40));
			Assert.Equal(-1f, LightModel.BlackFogDensity(software, true, 40));
			Assert.Equal(-1f, LightModel.BlackFogDensity(editor, true, 0));
			Assert.Equal(-1f, LightModel.BlackFogDensity(doomflat, true, 0));

			Assert.True(LightModel.UsesFogPrograms(editor, true));
			Assert.False(LightModel.UsesFogPrograms(doom, true));
			Assert.False(LightModel.UsesFogPrograms(software, true));
			Assert.True(LightModel.UsesFogPrograms(software, false));
		}

		[Theory]
		[InlineData("UZDoom_DoomUDMF.cfg", 8)]
		[InlineData("Zandronum_DoomUDMF.cfg", -1)]
		public void OnlyAuditedConfigurationsCarryTheKey(string filename, int expected)
		{
			string dir = Path.Combine(Path.GetDirectoryName(typeof(CodeImp.DoomBuilder.General).Assembly.Location)!, "Configurations");
			var config = new CodeImp.DoomBuilder.IO.Configuration(Path.Combine(dir, filename), true);
			Assert.False(config.ErrorResult, config.ErrorDescription);
			Assert.Equal(expected, config.ReadSetting("defaultlightmode", -1));
		}
	}
}
