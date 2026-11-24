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
			Assert.Equal(new[] { "lm_gridsize", "lm_sampledistance", "lm_suncolor" }, Keys(cfg, "thingtypes.lightmaplights.9890.adduniversalfields"));

			// Merged into the standard UDMF fields, not replacing them
			Assert.Equal(new[] { "lm_sampledist", "lm_sampledist_bot", "lm_sampledist_mid", "lm_sampledist_top" }, Keys(cfg, "universalfields.sidedef").Where(k => k.StartsWith("lm_")).ToArray());
			Assert.Contains("scalex_top", Keys(cfg, "universalfields.sidedef"));
			Assert.Equal(new[] { "lm_sampledist_ceiling", "lm_sampledist_floor" }, Keys(cfg, "universalfields.sector").Where(k => k.StartsWith("lm_")).ToArray());
			Assert.Contains("lightfloor", Keys(cfg, "universalfields.sector"));
			Assert.Equal(new[] { "lm_gridsize", "lm_sampledistance", "lm_suncolor" }, Keys(cfg, "universalfields.thing").Where(k => k.StartsWith("lm_")).ToArray());
			Assert.Contains("health", Keys(cfg, "universalfields.thing"));
			Assert.Empty(Keys(cfg, "universalfields.linedef").Where(k => k.StartsWith("lm_")));

			// The baker parses the sun colour as hex text
			Assert.Equal(2, cfg.ReadSetting("universalfields.thing.lm_suncolor.type", -1));
		}

		[Theory]
		[InlineData("GZDoom_DoomUDMF.cfg")]
		[InlineData("ZDoom_DoomUDMF.cfg")]
		[InlineData("Zandronum_DoomUDMF.cfg")]
		public void OtherConfigs_HaveNoLightmapThingsOrFields(string name)
		{
			Configuration cfg = Load(name);
			Assert.False(cfg.SettingExists("thingtypes.lightmaplights"));
			foreach(string element in new[] { "linedef", "sidedef", "sector", "thing" })
				Assert.Empty(Keys(cfg, "universalfields." + element).Where(k => k.StartsWith("lm_")));
		}
	}
}
