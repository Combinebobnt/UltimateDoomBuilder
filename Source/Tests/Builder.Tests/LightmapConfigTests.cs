using CodeImp.DoomBuilder.IO;
using System.Collections;

namespace Builder.Tests
{
	public class LightmapConfigTests
	{
		private static Configuration Load(string name)
		{
			string dir = Path.Combine(Path.GetDirectoryName(typeof(Configuration).Assembly.Location), "Configurations");
			var cfg = new Configuration(Path.Combine(dir, name), true);
			Assert.False(cfg.ErrorResult, name + ": " + cfg.ErrorDescription);
			return cfg;
		}

		private static string[] Keys(Configuration cfg, string path)
		{
			return cfg.ReadSetting(path, new Hashtable()).Keys.Cast<object>().Select(k => k.ToString()).OrderBy(k => k).ToArray();
		}

		[Theory]
		[InlineData("UZDoom_DoomUDMF.cfg")]
		[InlineData("UZDoom_HereticUDMF.cfg")]
		[InlineData("UZDoom_HexenUDMF.cfg")]
		public void BakingConfigs_OfferOnlyWhatTheBakerReads(string name)
		{
			Configuration cfg = Load(name);
			string[] types = Keys(cfg, "thingtypes.lightmaplights").Where(k => k.All(char.IsDigit)).ToArray();
			Assert.Equal(new[] { "9876", "9881", "9890" }, types);
			Assert.Equal(new[] { "lm_ao", "lm_bounce", "lm_gridsize", "lm_sampledistance", "lm_suncolor", "lm_sunintensity" }, Keys(cfg, "thingtypes.lightmaplights.9890.adduniversalfields"));

			// Merged into the standard UDMF fields, not replacing them
			Assert.Equal(new[] { "lm_lightcolor", "lm_lightdistance", "lm_lightintensity", "lm_sampledist", "lm_sampledist_bot", "lm_sampledist_mid", "lm_sampledist_top" },
				Keys(cfg, "universalfields.sidedef").Where(k => k.StartsWith("lm_")).ToArray());
			Assert.Contains("scalex_top", Keys(cfg, "universalfields.sidedef"));
			Assert.Equal(new[] { "lm_lightcolorceiling", "lm_lightcolorfloor", "lm_lightdistanceceiling", "lm_lightdistancefloor",
				"lm_lightintensityceiling", "lm_lightintensityfloor", "lm_sampledist_ceiling", "lm_sampledist_floor" },
				Keys(cfg, "universalfields.sector").Where(k => k.StartsWith("lm_")).ToArray());

			// Surface light colours use the colour picker, which writes the decimal int the baker reads;
			// a distance default of 0 matches the baker's absent = off, since the grid drops default values
			Assert.Equal(10, cfg.ReadSetting("universalfields.sidedef.lm_lightcolor.type", -1));
			Assert.Equal(10, cfg.ReadSetting("universalfields.sector.lm_lightcolorfloor.type", -1));
			Assert.Equal(10, cfg.ReadSetting("universalfields.sector.lm_lightcolorceiling.type", -1));
			foreach(string path in new[] { "sidedef.lm_lightdistance", "sector.lm_lightdistancefloor", "sector.lm_lightdistanceceiling" })
				Assert.Equal(0.0, cfg.ReadSetting("universalfields." + path + ".default", -1.0));
			Assert.Contains("lightfloor", Keys(cfg, "universalfields.sector"));
			Assert.Equal(new[] { "lm_ao", "lm_bounce", "lm_gridsize", "lm_sampledistance", "lm_suncolor", "lm_sunintensity" }, Keys(cfg, "universalfields.thing").Where(k => k.StartsWith("lm_")).ToArray());
			Assert.Contains("health", Keys(cfg, "universalfields.thing"));
			Assert.Empty(Keys(cfg, "universalfields.linedef").Where(k => k.StartsWith("lm_")));

			// The baker parses the sun colour as hex text
			Assert.Equal(2, cfg.ReadSetting("universalfields.thing.lm_suncolor.type", -1));

			// The baker reads a bounce count; UDB's old VkDoom-era field was a bool
			Assert.Equal(0, cfg.ReadSetting("universalfields.thing.lm_bounce.type", -1));

			// The baker reads an absent lm_ao as on, so only an explicit false may reach the map
			Assert.Equal(3, cfg.ReadSetting("universalfields.thing.lm_ao.type", -1));
			Assert.True(cfg.ReadSetting("universalfields.thing.lm_ao.default", false));

			// Soft shadow source radius on both baked lights; the default must match the baker's absent = 0,
			// since the fields grid drops a value equal to its default
			Assert.Equal(new[] { "light_softshadowradius" }, Keys(cfg, "thingtypes.lightmaplights.9876.adduniversalfields"));
			Assert.Equal(new[] { "light_softshadowradius" }, Keys(cfg, "thingtypes.lightmaplights.9881.adduniversalfields"));
			Assert.Equal(0.0, cfg.ReadSetting("universalfields.thing.light_softshadowradius.default", -1.0));
		}

		private static string[] ThingsWithField(Configuration cfg, string field)
		{
			var result = new List<string>();
			foreach(DictionaryEntry cat in cfg.ReadSetting("thingtypes", new Hashtable()))
			{
				if(!(cat.Value is IDictionary types)) continue;
				foreach(DictionaryEntry t in types)
					if(t.Value is IDictionary thing && thing["adduniversalfields"] is IDictionary fields && fields.Contains(field))
						result.Add(t.Key.ToString());
			}
			return result.ToArray();
		}

		[Theory]
		[InlineData("GZDoom_DoomUDMF.cfg")]
		[InlineData("ZDoom_DoomUDMF.cfg")]
		[InlineData("Zandronum_DoomUDMF.cfg")]
		public void OtherConfigs_HaveNoLightmapThingsOrFields(string name)
		{
			Configuration cfg = Load(name);
			Assert.False(cfg.SettingExists("thingtypes.lightmaplights"));
			Assert.Contains("scalex_top", Keys(cfg, "universalfields.sidedef"));
			Assert.Contains("health", Keys(cfg, "universalfields.thing"));
			foreach(string element in new[] { "linedef", "sidedef", "sector", "thing" })
				Assert.Empty(Keys(cfg, "universalfields." + element).Where(k => k.StartsWith("lm_")));
			Assert.Empty(ThingsWithField(cfg, "light_softshadowradius"));
			Assert.DoesNotContain("light_softshadowradius", Keys(cfg, "universalfields.thing"));
		}
	}
}
