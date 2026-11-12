using CodeImp.DoomBuilder;
using CodeImp.DoomBuilder.Config;
using CodeImp.DoomBuilder.Data;
using CodeImp.DoomBuilder.IO;
using CodeImp.DoomBuilder.Windows;
using System.Collections;
using System.IO.Compression;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text;

namespace Builder.Tests
{
	[Collection("GeneralStatics")]
	public class PK3ReaderTempFileTests : IDisposable
	{
		private readonly string directory = Path.Combine(Path.GetTempPath(), "pk3-temp-tests-" + Guid.NewGuid().ToString("N"));
		private readonly object previousmap = typeof(General).GetField("map", BindingFlags.NonPublic | BindingFlags.Static)!.GetValue(null);
		private readonly object previoustemppath = typeof(General).GetField("temppath", BindingFlags.NonPublic | BindingFlags.Static)!.GetValue(null);
		private readonly GameConfiguration config = CreateConfiguration();
		private const string Decorate = "actor EmbeddedActor {}\n";

		public PK3ReaderTempFileTests()
		{
			Directory.CreateDirectory(Path.Combine(directory, "app"));
			Directory.CreateDirectory(Path.Combine(directory, "map"));
			typeof(General).GetField("map", BindingFlags.NonPublic | BindingFlags.Static)!.SetValue(null, null);
			typeof(General).GetField("temppath", BindingFlags.NonPublic | BindingFlags.Static)!.SetValue(null, Path.Combine(directory, "app"));
		}

		public void Dispose()
		{
			typeof(General).GetField("map", BindingFlags.NonPublic | BindingFlags.Static)!.SetValue(null, previousmap);
			typeof(General).GetField("temppath", BindingFlags.NonPublic | BindingFlags.Static)!.SetValue(null, previoustemppath);
			Directory.Delete(directory, true);
		}

		private static GameConfiguration CreateConfiguration()
		{
			// The full constructor needs the editor's format registry; readers only need these collections.
			var config = (GameConfiguration)RuntimeHelpers.GetUninitializedObject(typeof(GameConfiguration));
			GC.SuppressFinalize(config);
			foreach(string field in new[] { "patchranges", "spriteranges", "flatranges", "textureranges", "hiresranges", "colormapranges", "voxelranges" })
				typeof(GameConfiguration).GetField(field, BindingFlags.NonPublic | BindingFlags.Instance)!.SetValue(config, new Hashtable());
			foreach(string field in new[] { "ignoredextensions", "ignoreddirectories" })
				typeof(GameConfiguration).GetField(field, BindingFlags.NonPublic | BindingFlags.Instance)!.SetValue(config, new HashSet<string>());
			typeof(GameConfiguration).GetField("requiredarchives", BindingFlags.NonPublic | BindingFlags.Instance)!.SetValue(config, new List<RequiredArchive>());
			return config;
		}

		private DataLocation CreateArchive()
		{
			string wadpath = Path.Combine(directory, "source.wad");
			using(var wad = new WAD(wadpath))
			{
				byte[] data = Encoding.ASCII.GetBytes(Decorate);
				Lump lump = wad.Insert("DECORATE", 0, data.Length);
				lump.Stream.Write(data, 0, data.Length);
			}

			string path = Path.Combine(directory, "resource.pk3");
			using(var stream = File.Create(path))
			using(var zip = new ZipArchive(stream, ZipArchiveMode.Create))
			using(var entry = zip.CreateEntry("embedded.wad").Open())
			{
				byte[] data = File.ReadAllBytes(wadpath);
				entry.Write(data, 0, data.Length);
			}
			return new DataLocation(DataLocation.RESOURCE_PK3, path, false, false, false, new List<string>());
		}

		[Theory]
		[InlineData(false)]
		[InlineData(true)]
		public void EmbeddedWad_UsesAvailableTempDirectoryAndIsDeletedOnDispose(bool mapopen)
		{
			if(mapopen)
			{
				var map = (MapManager)RuntimeHelpers.GetUninitializedObject(typeof(MapManager));
				GC.SuppressFinalize(map);
				typeof(MapManager).GetField("temppath", BindingFlags.NonPublic | BindingFlags.Instance)!.SetValue(map, Path.Combine(directory, "map"));
				typeof(General).GetField("map", BindingFlags.NonPublic | BindingFlags.Static)!.SetValue(null, map);
			}

			string tempfile;
			using(var reader = new PK3Reader(CreateArchive(), config, true))
			{
				WADReader wad = Assert.Single(reader.Wads);
				tempfile = wad.Location.location;
				Assert.Equal(Path.Combine(directory, mapopen ? "map" : "app"), Path.GetDirectoryName(tempfile));
				Assert.Equal(File.ReadAllBytes(Path.Combine(directory, "source.wad")), File.ReadAllBytes(tempfile));
				using(MemoryStream data = wad.LoadFile("DECORATE"))
					Assert.Equal(Decorate, Encoding.ASCII.GetString(data.ToArray()));
			}
			Assert.False(File.Exists(tempfile), "Extracted WAD must be deleted after its reader is closed.");
		}

		[Fact]
		public void RequiredArchiveCheck_WithNoMap_FindsClassInsideEmbeddedWad()
		{
			Assert.Null(General.Map);
			config.RequiredArchives.Add(new RequiredArchive("embedded", "resource.pk3", false,
				new List<RequiredArchiveEntry> { new RequiredArchiveEntry("EmbeddedActor", null) }));
			Assert.Equal("embedded", Assert.Single(ResourceOptionsForm.CheckRequiredArchives(config, CreateArchive(), CancellationToken.None)));
			Assert.Empty(Directory.GetFiles(Path.Combine(directory, "app")));
		}
	}
}
