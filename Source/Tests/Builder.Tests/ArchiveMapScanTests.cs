using CodeImp.DoomBuilder.IO;
using System.Text;
using static Builder.Tests.ZipFixtures;

namespace Builder.Tests
{
	public class ArchiveMapScanTests : IDisposable
	{
		private readonly string dir = Path.Combine(Path.GetTempPath(), "udb-archscan-" + Guid.NewGuid().ToString("N"));

		public ArchiveMapScanTests()
		{
			Directory.CreateDirectory(dir);
		}

		public void Dispose()
		{
			Directory.Delete(dir, true);
		}

		private string Write(string name, byte[] data)
		{
			string path = Path.Combine(dir, name);
			File.WriteAllBytes(path, data);
			return path;
		}

		private string ScanRoot()
		{
			string root = Path.Combine(dir, "temp");
			Directory.CreateDirectory(root);
			return root;
		}

		private static string[] Entries(ArchiveMapScan scan)
		{
			return scan.Candidates.Select(c => c.Source.EntryName).ToArray();
		}

		[Fact]
		public void FindsWadsAtAnyDepthAndAnyCase()
		{
			byte[] map = Wad(DoomMap("MAP01"));
			string path = Write("mod.pk3", Zip(
				E("maps/", new byte[0]),
				E("maps/MAP01.wad", map),
				E("levels/ep1/e1m1.wad", Wad(DoomMap("E1M1"))),
				E("root.wad", Wad(DoomMap("MAP02").Concat(DoomMap("MAP03")).ToArray())),
				E("maps/UPPER.WAD", map),
				E("textures/wall.png", "png"),
				E("maps/MAP01.wad.txt", "notes")));

			using (ArchiveMapScan scan = ArchiveMapScan.Open(path, ScanRoot()))
			{
				Assert.Equal<IEnumerable<string>>(new[] { "maps/MAP01.wad", "levels/ep1/e1m1.wad", "root.wad", "maps/UPPER.WAD" }, Entries(scan));
				Assert.Empty(scan.Warnings);
				Assert.Null(scan.NotWritableReason);
				Assert.True(scan.Candidates.All(c => c.Writable));

				ArchiveMapCandidate root = scan.Candidates[2];
				Assert.Equal(12, root.Wad.Lumps.Count);
				Assert.Equal("MAP03", root.Wad.Lumps[6].Name);
				Assert.Equal("root.wad", root.Source.EntryFileName);
				Assert.Equal("e1m1.wad", scan.Candidates[1].Source.EntryFileName);
				Assert.Equal("mod.pk3: levels/ep1/e1m1.wad", scan.Candidates[1].Source.DisplayName);
				Assert.Equal<IEnumerable<byte>>(map, File.ReadAllBytes(scan.Candidates[0].ScanPath));
			}
		}

		[Fact]
		public void ACorruptWadEntryIsAWarningAndTheRestStillList()
		{
			string path = Write("mod.pk3", Zip(
				E("maps/bad.wad", "this is not a wad file at all"),
				E("maps/huge.wad", Concat(Encoding.ASCII.GetBytes("PWAD"), BitConverter.GetBytes(int.MaxValue), BitConverter.GetBytes(12))),
				E("maps/good.wad", Wad(DoomMap("MAP01")))));

			using (ArchiveMapScan scan = ArchiveMapScan.Open(path, ScanRoot()))
			{
				Assert.Equal<IEnumerable<string>>(new[] { "maps/good.wad" }, Entries(scan));
				Assert.Equal(2, scan.Warnings.Count);
				Assert.Contains("maps/bad.wad", scan.Warnings[0]);
				Assert.Contains("maps/huge.wad", scan.Warnings[1]);
			}
		}

		[Fact]
		public void DataDescriptorEntriesAndStoredEntriesExtract()
		{
			byte[] map = Wad(DoomMap("MAP01"));
			string streamed = Write("streamed.pk3", Zip(new[] { E("maps/MAP01.wad", map) }, false));
			string stored = Write("stored.pk3", RawZip(new[] { R("maps/MAP01.wad", map) }));
			Assert.True(ZipDirectory.Read(streamed).Entries[0].HasDataDescriptor);
			Assert.Equal(0, ZipDirectory.Read(stored).Entries[0].Method);

			foreach (string path in new[] { streamed, stored })
			{
				using (ArchiveMapScan scan = ArchiveMapScan.Open(path, ScanRoot()))
				{
					Assert.Single(scan.Candidates);
					Assert.Equal<IEnumerable<byte>>(map, File.ReadAllBytes(scan.Candidates[0].ScanPath));
				}
			}
		}

		[Fact]
		public void AnEmptyZipListsNothing()
		{
			string path = Write("empty.zip", Zip());
			using (ArchiveMapScan scan = ArchiveMapScan.Open(path, ScanRoot()))
			{
				Assert.Empty(scan.Candidates);
				Assert.Empty(scan.Warnings);
			}
		}

		[Fact]
		public void NonZipFilesAreRefusedWithAMessageAndLeaveNoScanFolder()
		{
			string root = ScanRoot();
			string[] bad =
			{
				Write("text.pk3", Encoding.ASCII.GetBytes("hello, this is plain text and certainly not an archive")),
				Write("seven.pk7", Concat(new byte[] { 0x37, 0x7A, 0xBC, 0xAF, 0x27, 0x1C, 0, 4 }, new byte[64])),
				Write("map.pk3", Wad(DoomMap("MAP01"))),
				Write("zero.zip", new byte[0]),
			};

			foreach (string path in bad)
			{
				var e = Assert.Throws<InvalidDataException>(() => ArchiveMapScan.Open(path, root));
				Assert.Contains("is not a valid PK3/ZIP archive", e.Message);
				Assert.False(ZipDirectory.IsZip(path));
			}

			Assert.Empty(Directory.GetFileSystemEntries(root));
		}

		[Fact]
		public void ACorruptCentralDirectoryIsRefused()
		{
			byte[] zip = Zip(E("maps/MAP01.wad", Wad(DoomMap("MAP01"))));
			int eocd = FindEocd(zip);
			int cd = (int)BitConverter.ToUInt32(zip, eocd + 16);
			zip[cd] = 0;
			string path = Write("corrupt.pk3", zip);

			Assert.True(ZipDirectory.IsZip(path));
			var e = Assert.Throws<InvalidDataException>(() => ArchiveMapScan.Open(path, ScanRoot()));
			Assert.Contains("corrupt", e.Message);
		}

		[Fact]
		public void AnSfxPrefixedZipLists()
		{
			byte[] map = Wad(DoomMap("MAP01"));
			byte[] stub = Encoding.ASCII.GetBytes(new string('M', 300));
			string path = Write("sfx.zip", Concat(stub, Zip(E("maps/MAP01.wad", map))));

			ZipDirectory zip = ZipDirectory.Read(path);
			Assert.Equal(300, zip.OffsetBias);
			Assert.Equal(300, zip.PrefixLength);

			using (ArchiveMapScan scan = ArchiveMapScan.Open(path, ScanRoot()))
			{
				Assert.Single(scan.Candidates);
				Assert.Equal<IEnumerable<byte>>(map, File.ReadAllBytes(scan.Candidates[0].ScanPath));
			}
		}

		[Fact]
		public void Zip64ArchivesListButAreNotWritable()
		{
			byte[] map = Wad(DoomMap("MAP01"));
			string path = Write("z64.pk3", MakeZip64(Zip(E("a.txt", "x"), E("maps/MAP01.wad", map))));

			ZipDirectory zip = ZipDirectory.Read(path);
			Assert.True(zip.IsZip64);
			Assert.Equal(2, zip.Entries.Count);

			using (ArchiveMapScan scan = ArchiveMapScan.Open(path, ScanRoot()))
			{
				Assert.Single(scan.Candidates);
				Assert.False(scan.Candidates[0].Writable);
				Assert.NotNull(scan.NotWritableReason);
			}
		}

		[Fact]
		public void DuplicateEntryNamesAreNotWritable()
		{
			string path = Write("dup.pk3", Zip(E("maps/MAP01.wad", Wad(DoomMap("MAP01"))), E("maps/MAP01.wad", Wad(DoomMap("MAP02"))), E("maps/MAP02.wad", Wad(DoomMap("MAP02")))));

			using (ArchiveMapScan scan = ArchiveMapScan.Open(path, ScanRoot()))
			{
				Assert.Equal(3, scan.Candidates.Count);
				Assert.False(scan.Candidates[0].Writable);
				Assert.False(scan.Candidates[1].Writable);
				Assert.True(scan.Candidates[2].Writable);
			}
		}

		[Fact]
		public void DisposeDeletesTheScanFolderAndReleasesTheWads()
		{
			string root = ScanRoot();
			string path = Write("mod.pk3", Zip(E("maps/MAP01.wad", Wad(DoomMap("MAP01")))));

			ArchiveMapScan scan = ArchiveMapScan.Open(path, root);
			Assert.True(Directory.Exists(scan.ScanFolder));
			Assert.StartsWith("udb-archive-scan-", Path.GetFileName(scan.ScanFolder));
			WAD wad = scan.Candidates[0].Wad;

			scan.Dispose();
			scan.Dispose();

			Assert.True(wad.IsDisposed);
			Assert.Empty(Directory.GetFileSystemEntries(root));
		}

		[Fact]
		public void ADosTimeRoundTrips()
		{
			var t = new DateTime(2024, 5, 17, 13, 45, 58);
			Assert.Equal(t, ArchiveMapSource.DosToDateTime(ArchiveMapSource.DateTimeToDos(t)));
		}

		[Fact]
		public void TheStoreKeepsStablePathsPerArchiveAndEntry()
		{
			string root = Path.Combine(dir, "store");
			string path = Write("mod.pk3", Zip(E("maps/a/MAP01.wad", Wad(DoomMap("MAP01"))), E("maps/b/MAP01.wad", Wad(DoomMap("MAP01")))));
			ZipDirectory zip = ZipDirectory.Read(path);
			var a = new ArchiveMapSource(path, zip.Entries[0]);
			var b = new ArchiveMapSource(path, zip.Entries[1]);

			var store = new ArchiveMapStore(root, path);
			var again = new ArchiveMapStore(root, Path.Combine(dir, ".", "mod.pk3"));
			var other = new ArchiveMapStore(root, Path.Combine(dir, "other.pk3"));

			Assert.Equal(store.Folder, again.Folder);
			Assert.NotEqual(store.Folder, other.Folder);
			Assert.Equal(store.SettingsFile(a), again.SettingsFile(a));
			Assert.NotEqual(store.SettingsFile(a), store.SettingsFile(b));
			Assert.NotEqual(store.EntryFileBase(a), store.EntryFileBase(b));
			Assert.Equal("MAP01.wad", Path.GetFileName(store.EntryFileBase(a)));
			Assert.NotEqual(ArchiveMapStore.RestoreKey(a, "MAP01"), ArchiveMapStore.RestoreKey(b, "MAP01"));
			Assert.NotEqual(ArchiveMapStore.RestoreKey(a, "MAP01"), ArchiveMapStore.RestoreKey(a, "MAP02"));

			store.WriteIndex(b, "MAP01");
			again.ReadIndex(out string entrykey, out string mapname);
			Assert.Equal(ArchiveMapStore.EntryKey(b.EntryNameBytes), entrykey);
			Assert.Equal("MAP01", mapname);
		}
	}
}
