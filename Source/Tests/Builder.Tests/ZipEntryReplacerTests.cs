using CodeImp.DoomBuilder.IO;
using System.Text;
using static Builder.Tests.ZipFixtures;

namespace Builder.Tests
{
	public class ZipEntryReplacerTests : IDisposable
	{
		private const uint DOSTIME = 0x5A2B6C3D;
		private readonly string dir = Path.Combine(Path.GetTempPath(), "udb-zipreplace-" + Guid.NewGuid().ToString("N"));

		public ZipEntryReplacerTests()
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

		private static ArchiveMapSource SourceOf(string archive, string entry)
		{
			ZipDirectory zip = ZipDirectory.Read(archive);
			return new ArchiveMapSource(archive, zip.Entries.Single(e => e.Name == entry));
		}

		private ZipEntryRecord Replace(string archive, string entry, byte[] data, Action<string> afterwrite = null)
		{
			return ZipEntryReplacer.Replace(SourceOf(archive, entry), Write("new.wad", data), DOSTIME, null, afterwrite);
		}

		// Every entry's raw bytes, from its local header to the next one, in file order
		private static List<KeyValuePair<string, byte[]>> RawRanges(string archive)
		{
			byte[] bytes = File.ReadAllBytes(archive);
			ZipDirectory zip = ZipDirectory.Read(archive);
			List<ZipEntryRecord> byoffset = zip.Entries.OrderBy(e => e.LocalOffset).ToList();
			var result = new List<KeyValuePair<string, byte[]>>();
			for (int i = 0; i < byoffset.Count; i++)
			{
				long end = i + 1 < byoffset.Count ? byoffset[i + 1].LocalOffset : zip.CentralOffset;
				result.Add(new KeyValuePair<string, byte[]>(byoffset[i].Name, bytes.Skip((int)byoffset[i].LocalOffset).Take((int)(end - byoffset[i].LocalOffset)).ToArray()));
			}
			return result;
		}

		private static byte[] ExtractEntry(string archive, string entry)
		{
			ZipDirectory zip = ZipDirectory.Read(archive);
			using (FileStream s = File.OpenRead(archive))
			using (var ms = new MemoryStream())
			{
				zip.Extract(s, zip.Entries.Single(e => e.Name == entry), ms);
				return ms.ToArray();
			}
		}

		private void AssertUntouched(string archive, byte[] before)
		{
			Assert.Equal<IEnumerable<byte>>(before, File.ReadAllBytes(archive));
			Assert.Empty(Directory.GetFiles(dir, "*" + SiblingFileSwap.SIBLING_SUFFIX));
		}

		private static void AssertOthersIdentical(List<KeyValuePair<string, byte[]>> before, List<KeyValuePair<string, byte[]>> after, string target)
		{
			Assert.Equal<IEnumerable<string>>(before.Select(r => r.Key), after.Select(r => r.Key));
			for (int i = 0; i < before.Count; i++)
			{
				if (before[i].Key == target) continue;
				Assert.Equal<IEnumerable<byte>>(before[i].Value, after[i].Value);
			}
		}

		[Fact]
		public void OnlyTheTargetChangesAndTheResultOpensElsewhere()
		{
			byte[] newmap = Wad(DoomMap("MAP07"));
			string path = Write("mod.pk3", RawZip(new[]
			{
				R("readme.txt", Encoding.ASCII.GetBytes("hello")),
				new RawEntry { Name = "textures/wall.lmp", Data = new byte[3000], Deflate = true, Descriptor = true, Extra = new byte[] { 0xFE, 0xCA, 1, 0, 7 }, Comment = Encoding.ASCII.GetBytes("c") },
				new RawEntry { Name = "maps/M\u00C4P01.wad", Data = Wad(DoomMap("MAP01")), Deflate = true, Utf8 = true, Descriptor = true },
				R("zz.txt", Encoding.ASCII.GetBytes("last"), true),
			}, "archive comment"));
			var before = RawRanges(path);
			ZipDirectory oldzip = ZipDirectory.Read(path);

			ZipEntryRecord written = Replace(path, "maps/M\u00C4P01.wad", newmap);

			AssertOthersIdentical(before, RawRanges(path), "maps/M\u00C4P01.wad");
			ZipDirectory zip = ZipDirectory.Read(path);
			Assert.Equal<IEnumerable<byte>>(oldzip.Comment, zip.Comment);
			Assert.Equal<IEnumerable<string>>(oldzip.Entries.Select(e => e.Name), zip.Entries.Select(e => e.Name));

			ZipEntryRecord target = zip.Entries[2];
			Assert.True(target.IsUtf8);
			Assert.False(target.HasDataDescriptor);
			Assert.Equal(DOSTIME, target.DosDateTime);
			Assert.Equal(target.LocalOffset, written.LocalOffset);
			Assert.Equal(target.Crc, written.Crc);

			Dictionary<string, byte[]> all = ReadAll(path);
			Assert.Equal<IEnumerable<byte>>(newmap, all["maps/M\u00C4P01.wad"]);
			Assert.Equal(new byte[3000], all["textures/wall.lmp"]);
			Assert.Equal("last", Encoding.ASCII.GetString(all["zz.txt"]));
		}

		[Fact]
		public void DataDescriptorArchivesFromAStreamingWriter()
		{
			byte[] newmap = Wad(DoomMap("MAP02"));
			string path = Write("stream.pk3", Zip(new[] { E("a.txt", "a"), E("maps/MAP01.wad", Wad(DoomMap("MAP01"))), E("b.txt", "b") }, false));
			Assert.True(ZipDirectory.Read(path).Entries.All(e => e.HasDataDescriptor));
			var before = RawRanges(path);

			Replace(path, "maps/MAP01.wad", newmap);

			AssertOthersIdentical(before, RawRanges(path), "maps/MAP01.wad");
			Assert.Equal<IEnumerable<byte>>(newmap, ReadAll(path)["maps/MAP01.wad"]);
		}

		[Fact]
		public void ASfxPrefixAndUnfixedOffsetsAreKept()
		{
			byte[] stub = Encoding.ASCII.GetBytes(new string('M', 300));
			byte[] newmap = Wad(DoomMap("MAP03"));
			string path = Write("sfx.zip", Concat(stub, Zip(E("maps/MAP01.wad", Wad(DoomMap("MAP01"))), E("x.txt", "x"))));
			var before = RawRanges(path);

			Replace(path, "maps/MAP01.wad", newmap);

			byte[] after = File.ReadAllBytes(path);
			Assert.Equal<IEnumerable<byte>>(stub, after.Take(300));
			Assert.Equal(300, ZipDirectory.Read(path).OffsetBias);
			AssertOthersIdentical(before, RawRanges(path), "maps/MAP01.wad");
			Assert.Equal<IEnumerable<byte>>(newmap, ExtractEntry(path, "maps/MAP01.wad"));
		}

		[Fact]
		public void RefusalsLeaveTheArchiveUntouched()
		{
			byte[] map = Wad(DoomMap("MAP01"));

			string z64 = Write("z64.pk3", MakeZip64(Zip(E("a.txt", "x"), E("maps/MAP01.wad", map))));
			byte[] z64bytes = File.ReadAllBytes(z64);
			Assert.Throws<ZipNotWritableException>(() => Replace(z64, "maps/MAP01.wad", map));
			AssertUntouched(z64, z64bytes);

			string dup = Write("dup.pk3", Zip(E("maps/MAP01.wad", map), E("maps/MAP01.wad", map)));
			byte[] dupbytes = File.ReadAllBytes(dup);
			ArchiveMapSource dupsource = new ArchiveMapSource(dup, ZipDirectory.Read(dup).Entries[0]);
			Assert.Throws<ZipNotWritableException>(() => ZipEntryReplacer.Replace(dupsource, Write("new.wad", map), DOSTIME));
			AssertUntouched(dup, dupbytes);

			// Another tool rewrote the archive since it was opened, moving the entry
			string stale = Write("stale.pk3", Zip(E("maps/MAP01.wad", map)));
			ArchiveMapSource stalesource = SourceOf(stale, "maps/MAP01.wad");
			File.WriteAllBytes(stale, Zip(E("first.txt", "moved"), E("maps/MAP01.wad", map)));
			byte[] stalebytes = File.ReadAllBytes(stale);
			Assert.Throws<ZipNotWritableException>(() => ZipEntryReplacer.Replace(stalesource, Write("new.wad", map), DOSTIME));
			AssertUntouched(stale, stalebytes);
		}

		[Fact]
		public void AFailedVerificationLeavesTheArchiveUntouched()
		{
			string path = Write("mod.pk3", Zip(E("a.txt", "untouched entry"), E("maps/MAP01.wad", Wad(DoomMap("MAP01")))));
			byte[] before = File.ReadAllBytes(path);

			// Flip a byte inside the first entry's copied range
			Action<string> corrupt = sibling =>
			{
				byte[] b = File.ReadAllBytes(sibling);
				b[ZipDirectory.LOCAL_HEADER_SIZE + "a.txt".Length + 2] ^= 0xFF;
				File.WriteAllBytes(sibling, b);
			};

			Assert.ThrowsAny<InvalidDataException>(() => Replace(path, "maps/MAP01.wad", Wad(DoomMap("MAP02")), corrupt));
			AssertUntouched(path, before);
		}

		[Fact]
		public void TheOldEntryIsReadableBeforeTheSwap()
		{
			byte[] oldmap = Wad(DoomMap("MAP01"));
			string path = Write("mod.pk3", Zip(E("maps/MAP01.wad", oldmap)));
			byte[] seen = null;

			ZipEntryReplacer.Replace(SourceOf(path, "maps/MAP01.wad"), Write("new.wad", Wad(DoomMap("MAP09"))), DOSTIME, (s, zip, target) =>
			{
				using (var ms = new MemoryStream())
				{
					zip.Extract(s, target, ms);
					seen = ms.ToArray();
				}
			});

			Assert.Equal<IEnumerable<byte>>(oldmap, seen);
		}

		[Fact]
		public void AnExternalChangeShowsAsDifferentContent()
		{
			string path = Write("mod.pk3", Zip(E("maps/MAP01.wad", Wad(DoomMap("MAP01")))));
			ArchiveMapSource source = SourceOf(path, "maps/MAP01.wad");
			Assert.False(source.ContentDiffers(ZipDirectory.Read(path).Entries[0]));

			File.WriteAllBytes(path, Zip(E("maps/MAP01.wad", Wad(DoomMap("MAP05")))));
			int matches;
			ZipEntryRecord now = source.FindByName(ZipDirectory.Read(path), out matches);
			Assert.True(source.ContentDiffers(now));
		}
	}
}
