using CodeImp.DoomBuilder.Config;
using CodeImp.DoomBuilder.IO;
using System.Collections;

namespace Builder.Tests
{
	public class SectorPortalConfigTests
	{
		[Theory]
		[InlineData("Zandronum_DoomUDMF.cfg", 3, 3)]
		[InlineData("Zandronum_HereticUDMF.cfg", 3, 3)]
		[InlineData("Zandronum_HexenUDMF.cfg", 3, 3)]
		[InlineData("Zandronum_StrifeUDMF.cfg", 3, 3)]
		[InlineData("Zandronum_DoomHexen.cfg", 3, 3)]
		[InlineData("Zandronum_HereticHexen.cfg", 3, 3)]
		[InlineData("Zandronum_HexenHexen.cfg", 3, 3)]
		[InlineData("Zandronum_StrifeHexen.cfg", 3, 3)]
		[InlineData("DnD_UDMF.cfg", 3, 3)]
		[InlineData("ZDoom_DoomUDMF.cfg", 7, 4)]
		[InlineData("ZDoom_HereticUDMF.cfg", 7, 4)]
		[InlineData("ZDoom_HexenUDMF.cfg", 7, 4)]
		[InlineData("ZDoom_StrifeUDMF.cfg", 7, 4)]
		[InlineData("ZDoom_DoomHexen.cfg", 7, 4)]
		[InlineData("ZDoom_HereticHexen.cfg", 7, 4)]
		[InlineData("ZDoom_HexenHexen.cfg", 7, 4)]
		[InlineData("ZDoom_StrifeHexen.cfg", 7, 4)]
		public void PortalArguments_OfferOnlyEngineSupportedValues(string name, int typecount, int planecount)
		{
			string dir = Path.Combine(Path.GetDirectoryName(typeof(Configuration).Assembly.Location), "Configurations");
			var cfg = new Configuration(Path.Combine(dir, name), true);
			Assert.False(cfg.ErrorResult, name + ": " + cfg.ErrorDescription);
			const string path = "linedeftypes.sector.57";
			Assert.Equal("Sector_SetPortal", cfg.ReadSetting(path + ".id", ""));
			Assert.False(cfg.ReadSetting(path + ".requiresactivation", true));
			Assert.Equal(13, cfg.ReadSetting(path + ".arg0.type", 0));
			Assert.Equal("Misc", cfg.ReadSetting(path + ".arg3.title", ""));
			Assert.Equal("Opacity", cfg.ReadSetting(path + ".arg4.title", ""));

			var enums = new Dictionary<string, EnumList>();
			foreach(DictionaryEntry entry in cfg.ReadSetting("enums", new Hashtable()))
				enums.Add(entry.Key.ToString(), new EnumList(entry.Key.ToString(), cfg));
			foreach(var pair in new[] { new[] { 1, typecount }, new[] { 2, planecount } })
			{
				var arg = new ArgumentInfo(cfg, path, pair[0], enums);
				Assert.Equal(11, arg.Type);
				Assert.Equal(Enumerable.Range(0, pair[1]).Select(i => i.ToString()), arg.Enum.Select(item => item.Value));
				Assert.True(arg.Enum.All(item => !string.IsNullOrEmpty(item.Title)));
			}
		}
	}
}
