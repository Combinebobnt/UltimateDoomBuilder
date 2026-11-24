using CodeImp.DoomBuilder;
using CodeImp.DoomBuilder.Config;
using CodeImp.DoomBuilder.IO;
using CodeImp.DoomBuilder.Map;
using System.Collections;
using System.IO.Compression;

namespace Builder.Tests
{
	public class LightmapRetentionTests : IDisposable
	{
		private readonly string directory = Path.Combine(Path.GetTempPath(), "lightmap-retention-" + Guid.NewGuid().ToString("N"));

		public LightmapRetentionTests() { Directory.CreateDirectory(directory); }

		public void Dispose() { Directory.Delete(directory, true); }

		// version, texture size, texture count, surfaces, texcoords, probes, subsectors
		internal static byte[] Header(int version, ushort size, ushort textures, uint surfaces, uint texcoords, uint probes, uint subsectors)
		{
			var stream = new MemoryStream();
			var writer = new BinaryWriter(stream);
			writer.Write(version);
			writer.Write(size);
			writer.Write(textures);
			writer.Write(surfaces);
			writer.Write(texcoords);
			writer.Write(probes);
			writer.Write(subsectors);
			return stream.ToArray();
		}

		// zlib framing around raw deflate; the adler32 trailer is not checked by the reader
		internal static byte[] Zlib(byte[] header, long bodylength)
		{
			var output = new MemoryStream();
			output.WriteByte(0x78);
			output.WriteByte(0x9C);
			using(var deflate = new DeflateStream(output, CompressionMode.Compress, true))
			{
				deflate.Write(header, 0, header.Length);
				var zeros = new byte[65536];
				for(long left = bodylength; left > 0; left -= zeros.Length)
					deflate.Write(zeros, 0, (int)Math.Min(left, zeros.Length));
			}
			output.Write(new byte[4], 0, 4);
			return output.ToArray();
		}

		private static LightmapLump Read(byte[] data, bool checklength, out string error)
		{
			return LightmapLump.Read(new MemoryStream(data), checklength, out error);
		}

		[Fact]
		public void ValidLumpParsesAndMatchesItsLength()
		{
			// 2 textures of 16x16, 3 surfaces, 10 texcoords, 4 probes
			long body = 4 * 24 + 3 * 20 + 10 * 8 + 2 * 16 * 16 * 6;
			LightmapLump lump = Read(Zlib(Header(0, 16, 2, 3, 10, 4, 7), body), true, out string error);

			Assert.Null(error);
			Assert.NotNull(lump);
			Assert.Equal(16, lump.TextureSize);
			Assert.Equal(2, lump.TextureCount);
			Assert.Equal(3u, lump.SurfaceCount);
			Assert.Equal(7u, lump.SubsectorCount);
			Assert.Equal(LightmapLump.HEADER_SIZE + body, lump.ExpectedLength);
			Assert.False(lump.IsEmpty);
		}

		[Fact]
		public void TruncatedAndOverlongBodiesFailOnlyTheLengthCheck()
		{
			byte[] header = Header(0, 16, 1, 1, 4, 0, 1);
			long body = 20 + 4 * 8 + 16 * 16 * 6;

			Assert.Null(Read(Zlib(header, body - 1), true, out string error));
			Assert.StartsWith("truncated", error);
			Assert.Null(Read(Zlib(header, body + 1), true, out error));
			Assert.StartsWith("trailing data", error);
			Assert.NotNull(Read(Zlib(header, body - 1), false, out error));
		}

		[Fact]
		public void TruncatedHeaderWrongVersionAndNonZlibAreRejected()
		{
			byte[] header = Header(0, 16, 1, 1, 4, 0, 1);
			byte[] shortheader = new byte[10];
			Array.Copy(header, shortheader, 10);

			Assert.Null(Read(Zlib(shortheader, 0), false, out string error));
			Assert.Equal("truncated header", error);
			Assert.Null(Read(Zlib(Header(1, 16, 1, 1, 4, 0, 1), 0), false, out error));
			Assert.Equal("unsupported version 1", error);
			Assert.Null(Read(new byte[] { 1, 2, 3, 4 }, false, out error));
			Assert.Equal("not zlib data", error);
			Assert.Null(Read(new byte[0], false, out error));
		}

		[Fact]
		public void HostileCountsAreRejectedWithoutReadingTheBody()
		{
			// Claims over 4 GB of texels
			Assert.Null(Read(Zlib(Header(0, 65535, 65535, 1, 1, 0, 1), 0), true, out string error));
			Assert.StartsWith("header claims", error);

			// Claims ~2.4 GB of probes in a tiny stream: streamed, not allocated
			Assert.Null(Read(Zlib(Header(0, 1, 1, 1, 1, 100000000, 1), 0), true, out error));
			Assert.StartsWith("truncated", error);
		}

		[Fact]
		public void EmptyBakeIsReportedAsEmpty()
		{
			LightmapLump lump = Read(Zlib(Header(0, 1024, 0, 0, 0, 5, 3), 5 * 24), true, out string error);
			Assert.Null(error);
			Assert.True(lump.IsEmpty);
		}

		internal static Dictionary<string, MapLumpInfo> MapLumps(string configname)
		{
			string dir = Path.Combine(Path.GetDirectoryName(typeof(Configuration).Assembly.Location), "Configurations");
			var cfg = new Configuration(Path.Combine(dir, configname), true);
			Assert.False(cfg.ErrorResult, configname + ": " + cfg.ErrorDescription);
			var lumps = new Dictionary<string, MapLumpInfo>(StringComparer.Ordinal);
			foreach(DictionaryEntry entry in cfg.ReadSetting("maplumpnames", new Hashtable()))
			{
				string name = entry.Key.ToString();
				// A script lump looks up General.ScriptConfigs, which tests don't load
				if(cfg.ReadSetting("maplumpnames." + name + ".script", "") != "") continue;
				lumps.Add(name, new MapLumpInfo(name, cfg));
			}
			return lumps;
		}

		[Theory]
		[InlineData("UZDoom_DoomUDMF.cfg")]
		[InlineData("UZDoom_HereticUDMF.cfg")]
		[InlineData("UZDoom_HexenUDMF.cfg")]
		[InlineData("ZDoom_DoomUDMF.cfg")]
		public void UdmfConfigsKnowLightmapAsANodebuildLumpThatIsNeverBlindCopied(string configname)
		{
			Dictionary<string, MapLumpInfo> lumps = MapLumps(configname);
			Assert.True(lumps.ContainsKey(LightmapLump.LUMP_NAME), configname);
			MapLumpInfo info = lumps[LightmapLump.LUMP_NAME];
			Assert.True(info.NodeBuild);
			Assert.False(info.BlindCopy);
			Assert.False(info.Required);
		}

		private WAD CreateWad(params string[] names)
		{
			var wad = new WAD(Path.Combine(directory, Guid.NewGuid().ToString("N") + ".wad"));
			byte[] lightmap = Zlib(Header(0, 16, 1, 1, 4, 0, 1), 20 + 4 * 8 + 16 * 16 * 6);
			for(int i = 0; i < names.Length; i++)
			{
				byte[] data = (names[i] == LightmapLump.LUMP_NAME ? lightmap : names[i] == "TEXTMAP" ? new byte[] { 0x20 } : new byte[0]);
				Lump lump = wad.Insert(names[i], i, data.Length);
				lump.Stream.Write(data, 0, data.Length);
			}
			return wad;
		}

		private static string[] Names(WAD wad) { return wad.Lumps.Select(l => l.Name).ToArray(); }

		[Fact]
		public void RewriteDropsOnlyTheMapsOwnLightmapOnce()
		{
			// STRAY: a lump the current configuration doesn't list must not hide the LIGHTMAP
			using(WAD wad = CreateWad("TEMPMAP", "TEXTMAP", "ZNODES", "STRAY", "LIGHTMAP", "ENDMAP", "MAP02", "TEXTMAP", "LIGHTMAP", "ENDMAP"))
			{
				string summary = MapManager.RemoveStaleLightmap(wad, "TEMPMAP");
				Assert.Equal("version 0, 1 texture(s) of 16x16, 1 surfaces, 0 light probes, 1 subsectors", summary);
				Assert.Equal(new[] { "TEMPMAP", "TEXTMAP", "ZNODES", "STRAY", "ENDMAP", "MAP02", "TEXTMAP", "LIGHTMAP", "ENDMAP" }, Names(wad));

				Assert.Null(MapManager.RemoveStaleLightmap(wad, "TEMPMAP"));
				Assert.Equal(9, wad.Lumps.Count);
			}
		}

		[Fact]
		public void LightmapPastEndmapOrOutsideAUdmfMapIsLeftAlone()
		{
			using(WAD wad = CreateWad("TEMPMAP", "TEXTMAP", "ENDMAP", "LIGHTMAP"))
			{
				Assert.Null(MapManager.RemoveStaleLightmap(wad, "TEMPMAP"));
				Assert.Null(MapManager.RemoveStaleLightmap(wad, "NOMAP"));
				Assert.Equal(4, wad.Lumps.Count);
			}
			using(WAD wad = CreateWad("TEMPMAP", "THINGS", "LINEDEFS", "LIGHTMAP"))
			{
				Assert.Null(MapManager.RemoveStaleLightmap(wad, "TEMPMAP"));
				Assert.Equal(4, wad.Lumps.Count);
			}
		}

		[Theory]
		[InlineData(new byte[0], "unreadable: not zlib data")]
		[InlineData(new byte[] { 0x78, 0x20, 0, 0, 0, 0 }, "unreadable: not zlib data")]
		[InlineData(new byte[] { 0x78, 0x9C, 0xFF, 0xFF, 0xFF }, "unreadable: corrupt data")]
		public void UnreadableLightmapIsStillDropped(byte[] data, string expected)
		{
			using(WAD wad = CreateWad("TEMPMAP", "TEXTMAP", "ZNODES", "ENDMAP"))
			{
				Lump lump = wad.Insert("LIGHTMAP", 3, data.Length);
				lump.Stream.Write(data, 0, data.Length);
				Assert.StartsWith(expected, MapManager.RemoveStaleLightmap(wad, "TEMPMAP"));
				Assert.Equal(new[] { "TEMPMAP", "TEXTMAP", "ZNODES", "ENDMAP" }, Names(wad));
			}
		}

		[Fact]
		public void OnlySavesIntoAFileReportADroppedLightmap()
		{
			foreach(SavePurpose purpose in Enum.GetValues(typeof(SavePurpose)))
			{
				bool expected = purpose != SavePurpose.Autosave && purpose != SavePurpose.Testing;
				Assert.True(expected == MapManager.ReportsLightmapDrop(purpose), purpose.ToString());
			}
		}
	}
}
