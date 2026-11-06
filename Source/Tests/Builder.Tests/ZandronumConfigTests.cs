using CodeImp.DoomBuilder.IO;
using System.Collections;

namespace Builder.Tests
{
	public class ZandronumConfigTests
	{
		// The configs the build copied next to Builder.exe, includes and all
		private static Configuration Load(string name)
		{
			string dir = Path.Combine(Path.GetDirectoryName(typeof(Configuration).Assembly.Location), "Configurations");
			var cfg = new Configuration(Path.Combine(dir, name), true);
			Assert.False(cfg.ErrorResult, name + ": " + cfg.ErrorDescription + " (" + cfg.ErrorFile + ":" + cfg.ErrorLine + ")");
			return cfg;
		}

		[Theory]
		[InlineData("Zandronum_DoomUDMF.cfg")]
		[InlineData("Zandronum_HereticUDMF.cfg")]
		[InlineData("Zandronum_HexenUDMF.cfg")]
		[InlineData("Zandronum_StrifeUDMF.cfg")]
		[InlineData("DnD_UDMF.cfg")]
		public void Udmf_KeepsZDoomMultiTagAndPlaneEquations(string name)
		{
			Configuration cfg = Load(name);

			// Zandronum 3.3 reads moreids and floorplane_* (p_udmf.cpp)
			Assert.True(cfg.ReadSetting("sectormultitag", false), name + " sectormultitag");
			Assert.True(cfg.ReadSetting("planeequationsupport", false), name + " planeequationsupport");
		}

		[Theory]
		[InlineData("Zandronum_DoomUDMF.cfg")]
		[InlineData("Zandronum_HereticUDMF.cfg")]
		[InlineData("Zandronum_HexenUDMF.cfg")]
		[InlineData("Zandronum_StrifeUDMF.cfg")]
		[InlineData("Zandronum_DoomHexen.cfg")]
		[InlineData("DnD_UDMF.cfg")]
		public void Linedefs_DropNoOpSpecialsAndKeepZandronumOnes(string name)
		{
			Configuration cfg = Load(name);

			// LS_NOP or past LineSpecials[] in p_lnspec.cpp. LoadLinedefActions takes a structure from any category
			var dead = new[] { "104", "107", "156", "158", "301" };
			foreach(DictionaryEntry cat in cfg.ReadSetting("linedeftypes", new Hashtable()))
				if(cat.Value is IDictionary actions)
					foreach(string number in dead)
						Assert.False(actions[number] is IDictionary, name + " offers " + cat.Key + "." + number);

			// Guards against the scan passing because the key or the include went away
			foreach(string key in new[] { "ceiling.104", "line.107", "line.156", "script.158" })
				Assert.True(cfg.SettingExists("linedeftypes." + key), name + " " + key + " missing");

			foreach(string key in new[] { "player.145", "team.152", "team.153" })
				Assert.True(cfg.ReadSettingObject("linedeftypes." + key, null) is IDictionary, name + " " + key);

			Assert.True(cfg.ReadSetting("zandronumtextureblend", false), name + " zandronumtextureblend");
		}

		[Theory]
		[InlineData("Zandronum_DoomDoom.cfg")]
		[InlineData("Zandronum_DoomHexen.cfg")]
		[InlineData("Zandronum_DoomUDMF.cfg")]
		[InlineData("Zandronum_HereticDoom.cfg")]
		[InlineData("Zandronum_HereticHexen.cfg")]
		[InlineData("Zandronum_HereticUDMF.cfg")]
		[InlineData("Zandronum_HexenHexen.cfg")]
		[InlineData("Zandronum_HexenUDMF.cfg")]
		[InlineData("Zandronum_StrifeDoom.cfg")]
		[InlineData("Zandronum_StrifeHexen.cfg")]
		[InlineData("Zandronum_StrifeUDMF.cfg")]
		[InlineData("DnD_UDMF.cfg")]
		public void Lights_OfferZandronum33LightsOnly(string name)
		{
			Configuration cfg = Load(name);

			// Zandronum 3.3 decorate.z has 9800-9824 and 1502-1503, no attenuated, spot or lightmap lights
			foreach(string number in new[] { "9800", "9824", "1502", "1503" })
				Assert.True(cfg.ReadSettingObject("thingtypes.dynlights." + number, null) is IDictionary, name + " dynlights." + number + " missing");
			for(int number = 9830; number <= 9834; number++)
				Assert.False(cfg.ReadSettingObject("thingtypes.dynlights." + number, null) is IDictionary, name + " offers dynlights." + number);
			// LoadThingCategories skips a category that isn't a structure, so "= null" removes one
			Assert.False(cfg.ReadSettingObject("thingtypes.spotlights", null) is IDictionary, name + " offers spotlights");
		}

		[Fact]
		public void DnD_OffersZandronumThingsAndLightsOnly()
		{
			Configuration cfg = Load("DnD_UDMF.cfg");

			// mapformat_udmf overrides the top-level engine; it is only the UDMF namespace
			Assert.Equal("zdoom", cfg.ReadSetting("engine", ""));
			Assert.Equal("zandronum_acs.cfg", cfg.ReadSetting("defaultscriptcompiler", ""));

			// Lights_OfferZandronum33LightsOnly covers the dynamic and spot lights
			Assert.False(cfg.ReadSettingObject("thingtypes.zdoom.5004", null) is IDictionary, "offers GZDoom's FS_MapSpot");

			// DnD_things.cfg's "DnD" structure is merged into thingtypes, so check one of its categories
			Assert.True(cfg.ReadSettingObject("thingtypes.DnD_Monsters.Zombiemen.21018", null) is IDictionary, "DnD things missing");

			// Skulltag's firesticks would take 5150-5153 first and log "defined more than once" warnings
			for(int number = 5150; number <= 5153; number++)
			{
				Assert.False(cfg.ReadSettingObject("thingtypes.lights." + number, null) is IDictionary, "offers Skulltag's " + number);
				Assert.True(cfg.ReadSettingObject("thingtypes.lights.Torches." + number, null) is IDictionary, "DnD torch " + number + " missing");
			}
		}

		// Local fixes an upstream DnD Db_Config resync drops
		[Fact]
		public void DnD_ThingsKeepLocalSpriteLocksAndForcedSizes()
		{
			Configuration cfg = Load(Path.Combine("Includes", "DnD_things.cfg"));
			int sprites = 0, elites = 0;
			var pending = new Stack<DictionaryEntry>();
			foreach(DictionaryEntry e in cfg.Root) pending.Push(e);
			while(pending.Count > 0)
			{
				DictionaryEntry e = pending.Pop();
				if(!(e.Value is IDictionary thing)) continue;
				foreach(DictionaryEntry child in thing) pending.Push(child);

				// Without locksprite the DECORATE spawn sprite replaces the config icon (ThingTypeInfo.ModifyByDecorateActor)
				if(thing.Contains("sprite"))
				{
					sprites++;
					Assert.True(thing["locksprite"] is bool locked && locked, e.Key + " sprite is not locked");
				}

				if(int.TryParse(e.Key.ToString(), out int number) && number >= 27000 && number <= 27016)
				{
					elites++;
					Assert.True(thing.Contains("forcewidth") && thing.Contains("forceheight") && !thing.Contains("width") && !thing.Contains("height"), number + " elite spawner size is not forced");
				}
			}
			Assert.True(sprites > 200, "only " + sprites + " DnD sprites found");
			Assert.Equal(17, elites);
		}

		[Theory]
		[InlineData("Zandronum_DoomDoom.cfg", "zandronum")]
		[InlineData("Zandronum_DoomHexen.cfg", "zandronum")]
		[InlineData("Zandronum_DoomUDMF.cfg", "zandronum")]
		[InlineData("Zandronum_HereticDoom.cfg", "zandronum")]
		[InlineData("Zandronum_HereticHexen.cfg", "zandronum")]
		[InlineData("Zandronum_HereticUDMF.cfg", "zandronum")]
		[InlineData("Zandronum_HexenHexen.cfg", "zandronum")]
		[InlineData("Zandronum_HexenUDMF.cfg", "zandronum")]
		[InlineData("Zandronum_StrifeDoom.cfg", "zandronum")]
		[InlineData("Zandronum_StrifeHexen.cfg", "zandronum")]
		[InlineData("Zandronum_StrifeUDMF.cfg", "zandronum")]
		[InlineData("DnD_UDMF.cfg", "zandronum")]
		[InlineData("ZDoom_DoomDoom.cfg", "zdoom")]
		[InlineData("ZDoom_DoomHexen.cfg", "zdoom")]
		[InlineData("ZDoom_DoomUDMF.cfg", "zdoom")]
		[InlineData("ZDoom_HereticDoom.cfg", "zdoom")]
		[InlineData("ZDoom_HereticHexen.cfg", "zdoom")]
		[InlineData("ZDoom_HereticUDMF.cfg", "zdoom")]
		[InlineData("ZDoom_HexenHexen.cfg", "zdoom")]
		[InlineData("ZDoom_HexenUDMF.cfg", "zdoom")]
		[InlineData("ZDoom_StrifeDoom.cfg", "zdoom")]
		[InlineData("ZDoom_StrifeHexen.cfg", "zdoom")]
		[InlineData("ZDoom_StrifeUDMF.cfg", "zdoom")]
		public void RequiredArchives_DeclareTheEngineArchive(string name, string id)
		{
			Configuration cfg = Load(name);

			// The resource load warns when no loaded resource provides this archive
			Assert.Equal(id + ".pk3", cfg.ReadSetting("requiredarchives." + id + ".filename", ""));
			Assert.True(cfg.ReadSettingObject("requiredarchives." + id + ".0", null) is IDictionary, name + " has no sentinel entry");
			Assert.Equal("actor", cfg.ReadSetting("requiredarchives." + id + ".0.class", ""));
		}
	}
}
