using CodeImp.DoomBuilder.Data;
using CodeImp.DoomBuilder.IO;
using CodeImp.DoomBuilder.ZDoom;
using System.IO.Compression;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text;

namespace Builder.Tests
{
	public class LumpFilterTests : IDisposable
	{
		private readonly List<string> paths = new List<string>();

		public void Dispose()
		{
			foreach (string path in paths) File.Delete(path);
		}

		private static string P(string slashed)
		{
			return slashed.Replace('/', Path.DirectorySeparatorChar);
		}

		// Engine order: d_main.cpp builds the list, resourcefile.cpp tries each dotted prefix
		[Fact]
		public void BuildFilterNames_FollowsTheEngineOrder()
		{
			Assert.Equal(new List<string> { "game-DoomStrifeChex", "game-DoomChex", "game-Doom", "doom", "doom.id", "doom.id.doom2", "doom.id.doom2.commercial", "doom.doom2", "doom.doom2.commercial" },
				LumpFilters.BuildFilterNames("Doom", "doom.id.doom2.commercial"));

			Assert.Equal(new List<string> { "game-Raven", "game-Heretic", "heretic", "heretic.heretic" },
				LumpFilters.BuildFilterNames("Heretic", "heretic.heretic"));

			Assert.Equal(new List<string> { "game-DoomStrifeChex", "game-DoomChex", "game-Doom", "doom", "doom.id", "doom.id.doom1", "doom.id.doom1.ultimate", "doom.doom1", "doom.doom1.ultimate" },
				LumpFilters.BuildFilterNames("Doom", "doom.id.doom1.ultimate"));

			Assert.Equal(new List<string> { "game-DoomStrifeChex", "game-Strife" }, LumpFilters.BuildFilterNames("Strife", null));
			Assert.Equal(new List<string> { "game-DoomStrifeChex", "game-DoomChex", "game-Chex" }, LumpFilters.BuildFilterNames(LumpFilters.GameFromBaseGame("chex"), ""));
		}

		// The gate: GZDoom and UZDoom configs only. DnD says engine "gzdoom" but targets Zandronum, which has no filters
		[Theory]
		[InlineData("GZDoom_DoomUDMF.cfg", true)]
		[InlineData("GZDoom_HexenHexen.cfg", true)]
		[InlineData("UZDoom_DoomUDMF.cfg", true)]
		[InlineData("Zandronum_DoomUDMF.cfg", false)]
		[InlineData("DnD_UDMF.cfg", false)]
		[InlineData("ZDoom_DoomUDMF.cfg", false)]
		public void LumpFiltersKey_IsOnForGZDoomFamilyConfigsOnly(string name, bool expected)
		{
			string dir = Path.Combine(Path.GetDirectoryName(typeof(Configuration).Assembly.Location), "Configurations");
			var cfg = new Configuration(Path.Combine(dir, name), true);
			Assert.False(cfg.ErrorResult, name + ": " + cfg.ErrorDescription);
			Assert.Equal(expected, cfg.ReadSetting("lumpfilters", false));
		}

		[Fact]
		public void IdentifyIWad_TakesTheFirstFullMatchOfTheBundledTable()
		{
			List<IWadInfo> table = LumpFilters.GetBundledIWadInfos();
			Assert.True(table.Count > 40, "bundled table has " + table.Count + " entries");

			IWadInfo doom2 = LumpFilters.IdentifyIWad(new[] { "MAP01" }, table);
			Assert.Equal("DOOM 2: Hell on Earth", doom2?.Name);
			Assert.Equal("doom.id.doom2.commercial", doom2.AutoName);
			Assert.Equal("Doom", doom2.Game);

			Assert.Equal("Freedoom: Phase 2", LumpFilters.IdentifyIWad(new[] { "map01", "FREEDOOM" }, table)?.Name);
			Assert.Equal("Heretic", LumpFilters.IdentifyIWad(new[] { "E1M1", "E2M1", "TITLE", "MUS_E1M1" }, table)?.Name);
			Assert.Null(LumpFilters.IdentifyIWad(new string[0], table));
		}

		[Fact]
		public void IWadInfoParser_ReadsNameGameMustContainAndAutonameWithoutQuotes()
		{
			var parser = new IWadInfoParser();
			parser.Parse(CvarInfoParserTests.CreateTextResourceDataFromString(
				"IWad\n{\n\tName = \"Test IWAD\"\n\tAutoname = \"doom.test\"\n\tNoKeyboardCheats\n\tGame = \"Doom\"\n\tMustContain = \"MAP01\", \"TESTLUMP\"\n}\n"), false);

			Assert.False(parser.HasError);
			IWadInfo info = Assert.Single(parser.IWads);
			Assert.Equal("Test IWAD", info.Name);
			Assert.Equal("doom.test", info.AutoName);
			Assert.Equal("Doom", info.Game);
			Assert.Equal(new List<string> { "MAP01", "TESTLUMP" }, info.MustContain);
		}

		private static DirectoryFilesList NewList(params string[] names)
		{
			var entries = names.Select(n => new DirectoryFileEntry(P(n), FileTitleStyle.DEFAULT)).ToList();
			return new DirectoryFilesList("test", null, true, entries);
		}

		[Fact]
		public void ApplyFilters_MakesMatchingGroupsRootContentAfterTheRealRoot()
		{
			// Alphabetical, as a directory lists them: filter/ sorts before the root files, doom.id before doom
			DirectoryFilesList files = NewList("filter/doom.id/gldefs.txt", "filter/doom.id/textures/x.png", "filter/doom/gldefs.txt", "filter/heretic/gldefs.txt", "gldefs.txt");
			files.ApplyFilters(new List<string> { "doom", "doom.id" });

			Assert.Equal(new List<string> { "gldefs.txt", P("filter/doom/gldefs.txt"), P("filter/doom.id/gldefs.txt") },
				files.GetAllFilesWhichTitleStartsWith("", "gldefs", false));

			Assert.Equal(P("textures/x.png"), files.GetVirtualPath(P("filter/doom.id/textures/x.png")));
			Assert.Equal("gldefs.txt", files.GetVirtualPath("gldefs.txt"));
			Assert.True(files.FileExists("textures/x.png"));
			Assert.Equal(P("filter/doom.id/textures/x.png"), files.Resolve("textures/x.png"));
			Assert.Equal(P("filter/doom.id/textures/x.png"), files.GetFileInfo("textures/x.png").filepathname);
			Assert.Equal(new List<string> { P("filter/doom.id/textures/x.png") }, files.GetAllFiles("textures", true));

			// A root file wins a same-name lookup over the filtered copy
			Assert.Equal("gldefs.txt", files.Resolve("gldefs.txt"));
		}

		[Fact]
		public void ApplyFilters_DropsEveryUnmatchedFilterEntry()
		{
			DirectoryFilesList files = NewList("filter/doom.id/gldefs.txt", "filter/heretic/gldefs.txt", "filter/readme.txt", "gldefs.txt");
			files.ApplyFilters(new List<string> { "doom", "doom.id" });

			string heretic = P("filter/heretic/gldefs.txt");
			Assert.DoesNotContain(heretic, files.GetAllFiles());
			Assert.DoesNotContain(heretic, files.GetAllFiles("", true));
			Assert.DoesNotContain(heretic, files.GetAllFilesWithTitle("", "gldefs"));
			Assert.DoesNotContain(P("filter/readme.txt"), files.GetAllFiles("filter", true));
			Assert.False(files.FileExists(heretic));
			Assert.Equal(2, files.Count);
		}

		[Fact]
		public void ApplyFilters_StartsOverOnEveryCall()
		{
			DirectoryFilesList files = NewList("filter/doom.id/gldefs.txt", "filter/doom.id/textures/x.png", "filter/heretic/gldefs.txt", "gldefs.txt");
			files.ApplyFilters(new List<string> { "doom", "doom.id" });
			files.ApplyFilters(new List<string> { "heretic" });

			Assert.Equal(new List<string> { "gldefs.txt", P("filter/heretic/gldefs.txt") }, files.GetAllFilesWhichTitleStartsWith("", "gldefs", false));
			Assert.False(files.FileExists("textures/x.png"));
			Assert.Equal(P("filter/doom.id/textures/x.png"), files.GetVirtualPath(P("filter/doom.id/textures/x.png")));
		}

		[Fact]
		public void ApplyFilters_SkipsWadsInFilterFolders()
		{
			DirectoryFilesList files = NewList("filter/doom.id/extra.wad", "filter/doom.id/maps/map01.wad", "gldefs.txt");
			List<string> skipped = files.ApplyFilters(new List<string> { "doom.id" });

			Assert.Equal(new List<string> { P("filter/doom.id/extra.wad") }, skipped);
			Assert.False(files.FileExists("extra.wad"));
			Assert.True(files.FileExists(P("maps/map01.wad")));
		}

		private static void WriteZip(string path, params string[] namesandcontents)
		{
			using (var stream = new FileStream(path, FileMode.Create))
			using (var zip = new ZipArchive(stream, ZipArchiveMode.Create))
			{
				for (int i = 0; i < namesandcontents.Length; i += 2)
				{
					using (var writer = new StreamWriter(zip.CreateEntry(namesandcontents[i], CompressionLevel.NoCompression).Open()))
						writer.Write(namesandcontents[i + 1]);
				}
			}
		}

		// PK3ReaderLoadFileTests.Open plus the file list and empty WAD list the constructor would build
		private PK3Reader OpenFiltered(string path, IList<string> filters)
		{
			var reader = (PK3Reader)RuntimeHelpers.GetUninitializedObject(typeof(PK3Reader));
			object location = new DataLocation(DataLocation.RESOURCE_PK3, path, false, false, false, new List<string>());
			typeof(DataReader).GetField("location", BindingFlags.NonPublic | BindingFlags.Instance)!.SetValue(reader, location);

			List<DirectoryFileEntry> entries;
			using (var stream = File.OpenRead(path))
			using (var zip = new ZipArchive(stream, ZipArchiveMode.Read))
				entries = zip.Entries.Select(e => new DirectoryFileEntry(e.FullName, FileTitleStyle.DEFAULT)).ToList();
			typeof(PK3Reader).GetField("files", BindingFlags.NonPublic | BindingFlags.Instance)!.SetValue(reader, new DirectoryFilesList("test", null, true, entries));
			typeof(PK3StructuredReader).GetField("wads", BindingFlags.NonPublic | BindingFlags.Instance)!.SetValue(reader, new List<WADReader>());

			reader.Silent = true;
			reader.BatchMode = true;
			reader.ApplyLumpFilters(filters);
			return reader;
		}

		private static string Text(Stream stream)
		{
			stream.Position = 0;
			return new StreamReader(stream).ReadToEnd();
		}

		[Fact]
		public void PK3Reader_LoadsFilteredGldefsAndFilesByTheirRootName()
		{
			string path = Path.GetTempFileName();
			paths.Add(path);
			WriteZip(path, "filter/doom.id/gldefs.txt", "doom.id lights", "filter/heretic/gldefs.txt", "heretic lights", "gldefs.txt", "root lights", "filter/doom.id/textures/x.png", "png");

			PK3Reader reader = OpenFiltered(path, new List<string> { "doom", "doom.id" });
			try
			{
				List<TextResourceData> gldefs = reader.GetGldefsData("doom").ToList();
				Assert.Equal(new List<string> { "root lights", "doom.id lights" }, gldefs.Select(d => Text(d.Stream)).ToList());
				Assert.Equal("filter/doom.id/gldefs.txt", gldefs[1].Filename);

				using (MemoryStream data = reader.LoadFile("textures/x.png"))
					Assert.Equal("png", Encoding.UTF8.GetString(data.ToArray()));
			}
			finally
			{
				reader.BatchMode = false;
			}
		}
	}
}
