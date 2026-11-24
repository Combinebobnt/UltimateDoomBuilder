using System.IO.Compression;
using System.Text;

namespace Builder.Tests
{
	// Small zip and WAD builders for the archive map tests
	internal static class ZipFixtures
	{
		// A PWAD whose lumps carry their own name as data
		public static byte[] Wad(params string[] lumps)
		{
			using (var ms = new MemoryStream())
			using (var w = new BinaryWriter(ms))
			{
				w.Write(Encoding.ASCII.GetBytes("PWAD"));
				w.Write(lumps.Length);
				w.Write(0);
				var offsets = new List<int>();
				foreach (string l in lumps)
				{
					offsets.Add((int)ms.Position);
					w.Write(Encoding.ASCII.GetBytes(l));
				}
				int dir = (int)ms.Position;
				for (int i = 0; i < lumps.Length; i++)
				{
					w.Write(offsets[i]);
					w.Write(lumps[i].Length);
					byte[] name = new byte[8];
					Encoding.ASCII.GetBytes(lumps[i]).CopyTo(name, 0);
					w.Write(name);
				}
				ms.Position = 8;
				w.Write(dir);
				return ms.ToArray();
			}
		}

		public static string[] DoomMap(string name)
		{
			return new[] { name, "THINGS", "LINEDEFS", "SIDEDEFS", "VERTEXES", "SECTORS" };
		}

		public static KeyValuePair<string, byte[]> E(string name, byte[] data) { return new KeyValuePair<string, byte[]>(name, data); }
		public static KeyValuePair<string, byte[]> E(string name, string text) { return E(name, Encoding.ASCII.GetBytes(text)); }

		// seekable: false writes every entry with a data descriptor
		public static byte[] Zip(IEnumerable<KeyValuePair<string, byte[]>> entries, bool seekable = true, CompressionLevel level = CompressionLevel.Optimal)
		{
			var ms = new MemoryStream();
			Stream target = seekable ? (Stream)ms : new NonSeekable(ms);
			using (var zip = new ZipArchive(target, ZipArchiveMode.Create, true))
			{
				foreach (var e in entries)
				{
					if (e.Key.EndsWith("/")) { zip.CreateEntry(e.Key); continue; }
					using (Stream s = zip.CreateEntry(e.Key, level).Open())
						s.Write(e.Value, 0, e.Value.Length);
				}
			}
			return ms.ToArray();
		}

		public static byte[] Zip(params KeyValuePair<string, byte[]>[] entries) { return Zip(entries, true); }

		public static Dictionary<string, byte[]> ReadAll(string path)
		{
			var result = new Dictionary<string, byte[]>();
			using (var zip = new ZipArchive(File.OpenRead(path), ZipArchiveMode.Read))
			{
				foreach (var e in zip.Entries)
				{
					using (var s = e.Open())
					using (var ms = new MemoryStream())
					{
						s.CopyTo(ms);
						result[e.FullName] = ms.ToArray();
					}
				}
			}
			return result;
		}

		// Inserts a zip64 end record and locator and saturates the classic end record's fields
		public static byte[] MakeZip64(byte[] zip)
		{
			int eocd = FindEocd(zip);
			int count = BitConverter.ToUInt16(zip, eocd + 10);
			uint cdsize = BitConverter.ToUInt32(zip, eocd + 12);
			uint cdoffset = BitConverter.ToUInt32(zip, eocd + 16);

			using (var ms = new MemoryStream())
			using (var w = new BinaryWriter(ms))
			{
				w.Write(zip, 0, eocd);
				long z64pos = ms.Position;
				w.Write(0x06064b50u); w.Write(44L); w.Write((ushort)45); w.Write((ushort)45);
				w.Write(0u); w.Write(0u); w.Write((long)count); w.Write((long)count); w.Write((long)cdsize); w.Write((long)cdoffset);
				w.Write(0x07064b50u); w.Write(0u); w.Write(z64pos); w.Write(1u);
				w.Write(0x06054b50u); w.Write((ushort)0); w.Write((ushort)0);
				w.Write((ushort)0xFFFF); w.Write((ushort)0xFFFF); w.Write(0xFFFFFFFFu); w.Write(0xFFFFFFFFu);
				w.Write((ushort)0);
				return ms.ToArray();
			}
		}

		public sealed class RawEntry
		{
			public string Name;
			public byte[] Data;
			public bool Deflate;
			public bool Utf8;
			public bool Descriptor; // Sizes and CRC after the data, zeros in the local header
			public byte[] Extra = new byte[0];
			public byte[] Comment = new byte[0];
			public ushort DosTime = 0x6000;
			public ushort DosDate = 0x5821;
		}

		public static RawEntry R(string name, byte[] data, bool deflate = false) { return new RawEntry { Name = name, Data = data, Deflate = deflate }; }

		// A zip written byte by byte, for layouts System.IO.Compression won't produce
		public static byte[] RawZip(IEnumerable<RawEntry> entries, string comment = "")
		{
			using (var ms = new MemoryStream())
			using (var w = new BinaryWriter(ms))
			{
				var central = new MemoryStream();
				var cw = new BinaryWriter(central);
				int count = 0;
				foreach (RawEntry e in entries)
				{
					byte[] name = e.Utf8 ? Encoding.UTF8.GetBytes(e.Name) : Encoding.ASCII.GetBytes(e.Name);
					byte[] data = e.Data;
					if (e.Deflate)
					{
						var dm = new MemoryStream();
						using (var ds = new System.IO.Compression.DeflateStream(dm, System.IO.Compression.CompressionMode.Compress, true)) ds.Write(e.Data, 0, e.Data.Length);
						data = dm.ToArray();
					}
					var crc = new CodeImp.DoomBuilder.CRC();
					crc.Add(e.Data);
					uint crcvalue = (uint)crc.Value;
					ushort flags = (ushort)((e.Utf8 ? 0x800 : 0) | (e.Descriptor ? 8 : 0));
					ushort method = (ushort)(e.Deflate ? 8 : 0);
					uint offset = (uint)ms.Position;

					w.Write(0x04034b50u); w.Write((ushort)20); w.Write(flags); w.Write(method); w.Write(e.DosTime); w.Write(e.DosDate);
					w.Write(e.Descriptor ? 0u : crcvalue); w.Write(e.Descriptor ? 0u : (uint)data.Length); w.Write(e.Descriptor ? 0u : (uint)e.Data.Length);
					w.Write((ushort)name.Length); w.Write((ushort)e.Extra.Length); w.Write(name); w.Write(e.Extra);
					w.Write(data);
					if (e.Descriptor) { w.Write(0x08074b50u); w.Write(crcvalue); w.Write((uint)data.Length); w.Write((uint)e.Data.Length); }

					cw.Write(0x02014b50u); cw.Write((ushort)0x031E); cw.Write((ushort)20); cw.Write(flags); cw.Write(method); cw.Write(e.DosTime); cw.Write(e.DosDate);
					cw.Write(crcvalue); cw.Write((uint)data.Length); cw.Write((uint)e.Data.Length);
					cw.Write((ushort)name.Length); cw.Write((ushort)e.Extra.Length); cw.Write((ushort)e.Comment.Length);
					cw.Write((ushort)0); cw.Write((ushort)1); cw.Write(0x81A40000u); cw.Write(offset);
					cw.Write(name); cw.Write(e.Extra); cw.Write(e.Comment);
					count++;
				}
				uint cdoffset = (uint)ms.Position;
				byte[] cd = central.ToArray();
				w.Write(cd);
				byte[] c = Encoding.ASCII.GetBytes(comment);
				w.Write(0x06054b50u); w.Write((ushort)0); w.Write((ushort)0); w.Write((ushort)count); w.Write((ushort)count);
				w.Write((uint)cd.Length); w.Write(cdoffset); w.Write((ushort)c.Length); w.Write(c);
				return ms.ToArray();
			}
		}

		public static int FindEocd(byte[] zip)
		{
			for (int i = zip.Length - 22; i >= 0; i--)
				if (BitConverter.ToUInt32(zip, i) == 0x06054b50) return i;
			throw new InvalidOperationException("no eocd");
		}

		public static byte[] Concat(params byte[][] parts)
		{
			return parts.SelectMany(p => p).ToArray();
		}

		private sealed class NonSeekable : Stream
		{
			private readonly Stream inner;
			public NonSeekable(Stream inner) { this.inner = inner; }
			public override bool CanRead { get { return false; } }
			public override bool CanSeek { get { return false; } }
			public override bool CanWrite { get { return true; } }
			public override long Length { get { throw new NotSupportedException(); } }
			public override long Position { get { return inner.Position; } set { throw new NotSupportedException(); } }
			public override void Flush() { inner.Flush(); }
			public override int Read(byte[] buffer, int offset, int count) { throw new NotSupportedException(); }
			public override long Seek(long offset, SeekOrigin origin) { throw new NotSupportedException(); }
			public override void SetLength(long value) { throw new NotSupportedException(); }
			public override void Write(byte[] buffer, int offset, int count) { inner.Write(buffer, offset, count); }
		}
	}
}
