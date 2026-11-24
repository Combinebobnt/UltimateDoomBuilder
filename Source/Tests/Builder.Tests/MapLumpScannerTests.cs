using CodeImp.DoomBuilder.IO;

namespace Builder.Tests
{
	public class MapLumpScannerTests
	{
		private const string DoomLumps = @"
			maplumpnames
			{
				~MAP { required = true; }
				THINGS { required = true; }
				LINEDEFS { required = true; }
				SIDEDEFS { required = true; }
				VERTEXES { required = true; }
				SEGS { required = false; }
				SECTORS { required = true; }
				BEHAVIOR { forbidden = true; }
			}";

		private const string UdmfLumps = @"
			maplumpnames
			{
				~MAP { required = true; }
				TEXTMAP { required = true; }
				BEHAVIOR { required = false; }
				ZNODES { required = false; }
				ENDMAP { required = true; }
			}";

		private static Configuration Config(string lumps, string mapnameformat = null)
		{
			var cfg = new Configuration(true);
			string text = lumps + (mapnameformat == null ? "" : "\nmapnameformat = \"" + mapnameformat + "\";\n");
			Assert.True(cfg.InputConfiguration(text, true), cfg.ErrorDescription);
			return cfg;
		}

		private static string[] Doom(string name)
		{
			return new[] { name, "THINGS", "LINEDEFS", "SIDEDEFS", "VERTEXES", "SEGS", "SECTORS" };
		}

		private static string[] Join(params string[][] parts)
		{
			return parts.SelectMany(p => p).ToArray();
		}

		[Fact]
		public void FindsDoomAndUdmfMapsOnlyWithTheirOwnConfig()
		{
			string[] doom = Doom("MAP01");
			string[] udmf = { "MAP02", "TEXTMAP", "ZNODES", "ENDMAP" };

			Assert.Equal<IEnumerable<string>>(new[] { "MAP01" }, MapLumpScanner.FindMaps(Config(DoomLumps), doom, true));
			Assert.Empty(MapLumpScanner.FindMaps(Config(DoomLumps), udmf, true));
			Assert.Equal<IEnumerable<string>>(new[] { "MAP02" }, MapLumpScanner.FindMaps(Config(UdmfLumps), udmf, true));
			Assert.Empty(MapLumpScanner.FindMaps(Config(UdmfLumps), doom, true));

			Assert.True(MapLumpScanner.Matches(Config(DoomLumps), doom));
			Assert.False(MapLumpScanner.Matches(Config(DoomLumps), udmf));
			Assert.True(MapLumpScanner.Matches(Config(UdmfLumps), udmf));
		}

		[Fact]
		public void AForbiddenLumpRejectsTheMap()
		{
			string[] hexen = Join(Doom("MAP01"), new[] { "BEHAVIOR" });

			Assert.Empty(MapLumpScanner.FindMaps(Config(DoomLumps), hexen, true));
			Assert.False(MapLumpScanner.Matches(Config(DoomLumps), hexen));
		}

		[Fact]
		public void MapNameFormatGatesMatchingButNotListing()
		{
			string[] episode = Doom("E1M1");
			string[] noepisode = Doom("MAP01");

			Assert.False(MapLumpScanner.Matches(Config(DoomLumps, "MAPxy"), episode));
			Assert.True(MapLumpScanner.Matches(Config(DoomLumps, "MAPxy"), noepisode));
			Assert.False(MapLumpScanner.Matches(Config(DoomLumps, "ExMy"), noepisode));
			Assert.True(MapLumpScanner.Matches(Config(DoomLumps, "ExMy"), episode));
			Assert.True(MapLumpScanner.Matches(Config(DoomLumps), episode));

			// The listing never applied the format check
			Assert.Equal<IEnumerable<string>>(new[] { "E1M1" }, MapLumpScanner.FindMaps(Config(DoomLumps, "MAPxy"), episode, true));
		}

		[Fact]
		public void ListsEveryMapInLumpOrderAndDeduplicatesOnlyWhenAsked()
		{
			string[] wad = Join(Doom("MAP03"), new[] { "DEHACKED" }, Doom("MAP01"), Doom("MAP03"));

			Assert.Equal<IEnumerable<string>>(new[] { "MAP03", "MAP01" }, MapLumpScanner.FindMaps(Config(DoomLumps), wad, true));
			Assert.Equal<IEnumerable<string>>(new[] { "MAP03", "MAP01", "MAP03" }, MapLumpScanner.FindMaps(Config(DoomLumps), wad, false));
		}

		[Fact]
		public void ATextureOnlyWadHasNoMaps()
		{
			string[] wad = { "PNAMES", "TEXTURE1", "P_START", "WALL1", "P_END", "F_START", "FLOOR1", "F_END" };

			Assert.Empty(MapLumpScanner.FindMaps(Config(DoomLumps), wad, true));
			Assert.False(MapLumpScanner.Matches(Config(DoomLumps), wad));
			Assert.Empty(MapLumpScanner.FindMaps(Config(DoomLumps), new string[0], true));
		}
	}
}
