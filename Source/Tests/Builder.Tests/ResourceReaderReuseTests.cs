using CodeImp.DoomBuilder;
using CodeImp.DoomBuilder.Config;
using CodeImp.DoomBuilder.Data;
using CodeImp.DoomBuilder.IO;
using System.Collections;
using System.IO.Compression;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text;

namespace Builder.Tests
{
	[Collection("GeneralStatics")]
	public class ResourceReaderReuseTests : IDisposable
	{
		private readonly string directory = Path.Combine(Path.GetTempPath(), "reader-reuse-" + Guid.NewGuid().ToString("N"));
		private readonly object previousmap = GetStatic("map");
		private readonly object previoustemp = GetStatic("temppath");
		private readonly object previousconfigs = GetStatic("configspath");
		private readonly GameConfiguration config = CreateConfiguration();
		private const BindingFlags Instance = BindingFlags.Instance | BindingFlags.NonPublic;
		private const BindingFlags Static = BindingFlags.Static | BindingFlags.NonPublic;

		public ResourceReaderReuseTests()
		{
			Directory.CreateDirectory(directory);
			typeof(General).GetField("map", Static)!.SetValue(null, null);
			typeof(General).GetField("temppath", Static)!.SetValue(null, directory);
		}

		public void Dispose()
		{
			typeof(General).GetField("map", Static)!.SetValue(null, previousmap);
			typeof(General).GetField("temppath", Static)!.SetValue(null, previoustemp);
			typeof(General).GetField("configspath", Static)!.SetValue(null, previousconfigs);
			Directory.Delete(directory, true);
		}

		private static object GetStatic(string field) => typeof(General).GetField(field, Static)!.GetValue(null);

		private static GameConfiguration CreateConfiguration()
		{
			var config = (GameConfiguration)RuntimeHelpers.GetUninitializedObject(typeof(GameConfiguration));
			GC.SuppressFinalize(config);
			foreach(string field in new[] { "patchranges", "spriteranges", "flatranges", "textureranges", "hiresranges", "colormapranges", "voxelranges" })
				typeof(GameConfiguration).GetField(field, Instance)!.SetValue(config, new Hashtable());
			foreach(string field in new[] { "ignoredextensions", "ignoreddirectories" })
				typeof(GameConfiguration).GetField(field, Instance)!.SetValue(config, new HashSet<string>());
			return config;
		}

		private DataLocation CreateArchive()
		{
			string wadpath = Path.Combine(directory, "embedded-source.wad");
			using(var wad = new WAD(wadpath))
			{
				byte[] text = Encoding.ASCII.GetBytes("actor EmbeddedActor {}\n");
				Lump lump = wad.Insert("DECORATE", 0, text.Length);
				lump.Stream.Write(text, 0, text.Length);
			}
			string path = Path.Combine(directory, "resource.pk3");
			using(var stream = File.Create(path))
			using(var zip = new ZipArchive(stream, ZipArchiveMode.Create))
			{
				using(var entry = zip.CreateEntry("embedded.wad").Open()) entry.Write(File.ReadAllBytes(wadpath));
				using(var writer = new StreamWriter(zip.CreateEntry("filter/doom/textures/TEST.png").Open())) writer.Write("test image");
			}
			return new DataLocation(DataLocation.RESOURCE_PK3, path, false, false, false, null);
		}

		[Theory]
		[InlineData("length")]
		[InlineData("time")]
		[InlineData("type")]
		[InlineData("option1")]
		[InlineData("option2")]
		[InlineData("notfortesting")]
		[InlineData("missing")]
		public void FingerprintRejectsChangedMetadataOrOptions(string change)
		{
			DataLocation location = CreateArchive();
			ResourceFingerprint before = ResourceFingerprint.Capture(location);
			Assert.True(before.Matches(ResourceFingerprint.Capture(location)));
			switch(change)
			{
				case "length": using(var file = File.OpenWrite(location.location)) file.SetLength(file.Length + 1); break;
				case "time": File.SetLastWriteTimeUtc(location.location, File.GetLastWriteTimeUtc(location.location).AddSeconds(5)); break;
				case "type": location.type = DataLocation.RESOURCE_WAD; break;
				case "option1": location.option1 = true; break;
				case "option2": location.option2 = true; break;
				case "notfortesting": location.notfortesting = true; break;
				case "missing": File.Delete(location.location); break;
			}
			Assert.False(before.Matches(ResourceFingerprint.Capture(location)));
		}

		[Fact]
		public void DirectoriesAndPK7HaveNoFingerprint()
		{
			Assert.Null(ResourceFingerprint.Capture(new DataLocation(DataLocation.RESOURCE_DIRECTORY, directory, false, false, false, null)));
			DataLocation location = CreateArchive();
			string path = Path.ChangeExtension(location.location, ".pk7");
			File.Copy(location.location, path);
			location.location = path;
			Assert.Null(ResourceFingerprint.Capture(location));
		}

		[Fact]
		public void ReuseResetsParentAndEmbeddedTextureSetsAndFilters()
		{
			DataLocation location = CreateArchive();
			using(var reader = new PK3Reader(location, config, true))
			{
				WADReader embedded = Assert.Single(reader.Wads);
				ResourceTextureSet oldparent = reader.TextureSet;
				ResourceTextureSet oldchild = embedded.TextureSet;
				reader.ApplyLumpFilters(new[] { "doom" });
				Assert.True(reader.FileExists("textures/TEST.png"));
				reader.BatchMode = false;
				using(var pool = new ResourceReaderPool())
				{
					Assert.True(pool.TryAdd(reader, null));
					Assert.True(pool.TryTake(location, out DataReader reused));
					Assert.True(ReferenceEquals(reader, reused));
					Assert.False(pool.TryTake(location, out _));
				}
				Assert.False(reader.IsDisposed);
				Assert.False(embedded.IsDisposed);
				Assert.False(ReferenceEquals(oldparent, reader.TextureSet));
				Assert.False(ReferenceEquals(oldchild, embedded.TextureSet));
				Assert.Empty(reader.TextureSet.Textures);
				Assert.Empty(embedded.TextureSet.Textures);
				Assert.True(reader.BatchMode);
				Assert.False(reader.FileExists("textures/TEST.png"));
				reader.ApplyLumpFilters(new[] { "doom" });
				Assert.True(reader.FileExists("textures/TEST.png"));
				using(var text = new StreamReader(embedded.LoadFile("DECORATE"))) Assert.Equal("actor EmbeddedActor {}\n", text.ReadToEnd());
			}
		}

		[Theory]
		[InlineData(false)]
		[InlineData(true)]
		public void UnusedOrChangedReadersAreDisposedWithEmbeddedTempFiles(bool changed)
		{
			DataLocation location = CreateArchive();
			var reader = new PK3Reader(location, config, true);
			string embedded = Assert.Single(reader.Wads).Location.location;
			Assert.True(File.Exists(embedded));
			using(var pool = new ResourceReaderPool())
			{
				Assert.True(pool.TryAdd(reader, null));
				if(changed)
				{
					File.SetLastWriteTimeUtc(location.location, File.GetLastWriteTimeUtc(location.location).AddSeconds(5));
					Assert.False(pool.TryTake(location, out _));
				}
			}
			Assert.True(reader.IsDisposed);
			Assert.False(File.Exists(embedded));
		}

		[Theory]
		[InlineData("map")]
		[InlineData("suspended")]
		[InlineData("writable")]
		[InlineData("diagnostic")]
		public void UnsafeReadersStayWithOriginalOwner(string reason)
		{
			DataLocation location = CreateArchive();
			using(var reader = new PK3Reader(location, config, reason != "writable"))
			using(var pool = new ResourceReaderPool())
			{
				if(reason == "suspended") reader.Suspend();
				if(reason == "diagnostic") reader.Fingerprint = null;
				Assert.False(pool.TryAdd(reader, reason == "map" ? location.location : null));
				Assert.False(reader.IsDisposed);
			}
		}

		[Fact]
		public void FailedHandoffDisposesUnusedReadersButNotTransferredReaders()
		{
			DataLocation first = CreateArchive();
			DataLocation second = first;
			second.location = Path.Combine(directory, "second.pk3");
			File.Copy(first.location, second.location);
			using(var transferred = new PK3Reader(first, config, true))
			using(var unused = new PK3Reader(second, config, true))
			{
				string unusedwad = Assert.Single(unused.Wads).Location.location;
				Assert.True(File.Exists(unusedwad));
				// The cast keeps xunit off its async Func<Task> overload for an always-throwing lambda.
				Assert.Throws<InvalidOperationException>((Action)(() =>
				{
					using(var pool = new ResourceReaderPool())
					{
						Assert.True(pool.TryAdd(transferred, null));
						Assert.True(pool.TryAdd(unused, null));
						Assert.True(pool.TryTake(first, out _));
						throw new InvalidOperationException("load failed after transfer");
					}
				}));
				Assert.False(transferred.IsDisposed);
				Assert.True(unused.IsDisposed);
				Assert.False(File.Exists(unusedwad));
			}
		}

		[Fact]
		public void FailedResetDisposesRemovedReaderAndItsEmbeddedWad()
		{
			DataLocation location = CreateArchive();
			using(var reader = new PK3Reader(location, config, true))
			using(var pool = new ResourceReaderPool())
			{
				string embedded = Assert.Single(reader.Wads).Location.location;
				Assert.True(File.Exists(embedded));
				Assert.True(pool.TryAdd(reader, null));
				typeof(PK3Reader).GetField("files", Instance)!.SetValue(reader, null);
				Assert.Throws<NullReferenceException>(() => pool.TryTake(location, out _));
				Assert.True(reader.IsDisposed);
				Assert.False(File.Exists(embedded));
			}
		}

		[Theory]
		[InlineData(false)]
		[InlineData(true)]
		public void DataManagerKeepsReadersOnFullPathWhenConfigurationChanges(bool changed)
		{
			DataLocation location = CreateArchive();
			using(var reader = new PK3Reader(location, config, true))
			{
				string configfile = Path.Combine(directory, "game.cfg");
				File.WriteAllText(configfile, "configuration");
				var map = (MapManager)RuntimeHelpers.GetUninitializedObject(typeof(MapManager));
				GC.SuppressFinalize(map);
				var info = (ConfigurationInfo)RuntimeHelpers.GetUninitializedObject(typeof(ConfigurationInfo));
				GC.SuppressFinalize(info);
				typeof(ConfigurationInfo).GetField("filename", Instance)!.SetValue(info, "game.cfg");
				typeof(MapManager).GetField("configinfo", Instance)!.SetValue(map, info);
				typeof(General).GetField("map", Static)!.SetValue(null, map);
				typeof(General).GetField("configspath", Static)!.SetValue(null, directory);
				var manager = (DataManager)RuntimeHelpers.GetUninitializedObject(typeof(DataManager));
				var containers = new List<DataReader> { reader };
				typeof(DataManager).GetField("containers", Instance)!.SetValue(manager, containers);
				typeof(DataManager).GetField("configurationfingerprint", Instance)!.SetValue(manager,
					ResourceFingerprint.Capture(new DataLocation(DataLocation.RESOURCE_WAD, configfile, false, false, false, null)));
				if(changed) File.SetLastWriteTimeUtc(configfile, File.GetLastWriteTimeUtc(configfile).AddSeconds(5));
				using(var pool = manager.DetachReadersForReload())
				{
					Assert.Equal(!changed, pool.TryTake(location, out _));
					Assert.Equal(changed ? 1 : 0, containers.Count);
				}
				Assert.False(reader.IsDisposed);
			}
		}

		[Fact]
		public void DisposedDataManagerDoesNotTryToDetachReaders()
		{
			var manager = (DataManager)RuntimeHelpers.GetUninitializedObject(typeof(DataManager));
			typeof(DataManager).GetField("isdisposed", Instance)!.SetValue(manager, true);
			using(var pool = manager.DetachReadersForReload())
				Assert.False(pool.TryTake(CreateArchive(), out _));
		}
	}
}
