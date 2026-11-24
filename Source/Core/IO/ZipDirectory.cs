#region ================== Namespaces

using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Text;

#endregion

namespace CodeImp.DoomBuilder.IO
{
	// One central directory record, raw bytes kept so the entry can be written back unchanged
	internal sealed class ZipEntryRecord
	{
		public int Index;
		public ushort VersionMadeBy;
		public ushort VersionNeeded;
		public ushort Flags;
		public ushort Method;
		public ushort DosTime;
		public ushort DosDate;
		public uint Crc;
		public long CompressedSize;
		public long UncompressedSize;
		public ushort DiskStart;
		public ushort InternalAttributes;
		public uint ExternalAttributes;
		public long LocalOffset; // Absolute position in the file, bias applied
		public byte[] NameBytes;
		public byte[] Extra;
		public byte[] Comment;
		public string Name;
		public bool UsesZip64;

		public bool IsDirectory { get { return Name.EndsWith("/") || Name.EndsWith("\\"); } }
		public bool IsEncrypted { get { return (Flags & 0x0001) != 0; } }
		public bool HasDataDescriptor { get { return (Flags & 0x0008) != 0; } }
		public bool IsUtf8 { get { return (Flags & 0x0800) != 0; } }
		public uint DosDateTime { get { return ((uint)DosDate << 16) | DosTime; } }
	}

	// Reads a zip's end record and central directory (APPNOTE 6.3). Stdlib only; entries are inflated with DeflateStream.
	internal sealed class ZipDirectory
	{
		#region ================== Constants

		public const uint LOCAL_SIGNATURE = 0x04034b50;
		public const uint CENTRAL_SIGNATURE = 0x02014b50;
		public const uint EOCD_SIGNATURE = 0x06054b50;
		public const uint ZIP64_EOCD_SIGNATURE = 0x06064b50;
		public const uint ZIP64_LOCATOR_SIGNATURE = 0x07064b50;
		public const int EOCD_SIZE = 22;
		public const int LOCAL_HEADER_SIZE = 30;
		public const int CENTRAL_HEADER_SIZE = 46;
		private const int MAX_COMMENT = 0xFFFF;

		#endregion

		#region ================== Properties

		public long FileLength { get; private set; }
		public long EocdOffset { get; private set; }
		public long CentralOffset { get; private set; } // Absolute
		public long CentralSize { get; private set; }
		public long OffsetBias { get; private set; } // Stored offsets + bias = file positions (an SFX stub prepended without fixing offsets)
		public long PrefixLength { get; private set; } // Bytes before the first local header
		public ushort DiskNumber { get; private set; }
		public ushort CentralDisk { get; private set; }
		public byte[] Comment { get; private set; }
		public bool IsZip64 { get; private set; }
		public bool IsMultiDisk { get { return DiskNumber != 0 || CentralDisk != 0; } }
		public List<ZipEntryRecord> Entries { get; private set; }

		#endregion

		#region ================== Reading

		// Not a WAD, and has an end of central directory record
		public static bool IsZip(string path)
		{
			try
			{
				using(FileStream s = File.Open(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
				{
					if(IsWad(s)) return false;
					return FindEocd(s) >= 0;
				}
			}
			catch(IOException) { return false; }
			catch(UnauthorizedAccessException) { return false; }
		}

		public static bool IsWad(Stream s)
		{
			if(s.Length < 4) return false;
			byte[] magic = new byte[4];
			s.Position = 0;
			ReadFully(s, magic, 4);
			string m = Encoding.ASCII.GetString(magic);
			return m == "IWAD" || m == "PWAD";
		}

		// Position of the end record, or -1. The record nearest the end wins.
		public static long FindEocd(Stream s)
		{
			long length = s.Length;
			if(length < EOCD_SIZE) return -1;

			int window = (int)Math.Min(length, EOCD_SIZE + MAX_COMMENT);
			byte[] tail = new byte[window];
			s.Position = length - window;
			ReadFully(s, tail, window);

			for(int i = window - EOCD_SIZE; i >= 0; i--)
			{
				if(BitConverter.ToUInt32(tail, i) != EOCD_SIGNATURE) continue;
				int commentlength = BitConverter.ToUInt16(tail, i + 20);
				if(i + EOCD_SIZE + commentlength <= window) return length - window + i;
			}

			return -1;
		}

		public static ZipDirectory Read(string path)
		{
			using(FileStream s = File.Open(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
				return Read(s);
		}

		// Throws InvalidDataException when the stream is not a readable zip
		public static ZipDirectory Read(Stream s)
		{
			ZipDirectory zip = new ZipDirectory();
			zip.FileLength = s.Length;

			if(IsWad(s)) throw new InvalidDataException("The file is a WAD, not a zip archive.");
			long eocd = FindEocd(s);
			if(eocd < 0) throw new InvalidDataException("No zip end of central directory record found.");
			zip.EocdOffset = eocd;

			byte[] rec = ReadAt(s, eocd, EOCD_SIZE);
			zip.DiskNumber = BitConverter.ToUInt16(rec, 4);
			zip.CentralDisk = BitConverter.ToUInt16(rec, 6);
			long count = BitConverter.ToUInt16(rec, 10);
			long cdsize = BitConverter.ToUInt32(rec, 12);
			long cdoffset = BitConverter.ToUInt32(rec, 16);
			zip.Comment = ReadAt(s, eocd + EOCD_SIZE, BitConverter.ToUInt16(rec, 20));

			// The central directory ends where the zip64 end record starts, if there is one
			long cdend = eocd;
			if(eocd >= 20 && BitConverter.ToUInt32(ReadAt(s, eocd - 20, 4), 0) == ZIP64_LOCATOR_SIGNATURE)
			{
				zip.IsZip64 = true;
				byte[] locator = ReadAt(s, eocd - 20, 20);
				long z64stored = (long)BitConverter.ToUInt64(locator, 8);
				long z64pos = FindZip64Record(s, eocd - 20, z64stored);
				byte[] z64 = ReadAt(s, z64pos, 56);
				zip.DiskNumber = (ushort)Math.Min(BitConverter.ToUInt32(z64, 16), ushort.MaxValue);
				zip.CentralDisk = (ushort)Math.Min(BitConverter.ToUInt32(z64, 20), ushort.MaxValue);
				count = (long)BitConverter.ToUInt64(z64, 32);
				cdsize = (long)BitConverter.ToUInt64(z64, 40);
				cdoffset = (long)BitConverter.ToUInt64(z64, 48);
				cdend = z64pos;
			}
			else if(count == 0xFFFF || cdsize == 0xFFFFFFFF || cdoffset == 0xFFFFFFFF)
			{
				throw new InvalidDataException("Zip64 end record is missing.");
			}

			if(zip.IsMultiDisk) throw new InvalidDataException("Split or spanned zip archives are not supported.");

			zip.OffsetBias = cdend - cdsize - cdoffset;
			if(zip.OffsetBias < 0 || cdsize < 0 || count < 0 || count > cdsize / CENTRAL_HEADER_SIZE + 1)
				throw new InvalidDataException("The central directory is out of range.");
			zip.CentralOffset = cdoffset + zip.OffsetBias;
			zip.CentralSize = cdsize;

			byte[] cd = ReadAt(s, zip.CentralOffset, (int)Math.Min(cdsize, int.MaxValue));
			zip.Entries = ParseCentral(cd, (int)count, zip.OffsetBias, zip.CentralOffset);

			long prefix = zip.CentralOffset;
			foreach(ZipEntryRecord e in zip.Entries) prefix = Math.Min(prefix, e.LocalOffset);
			zip.PrefixLength = prefix;

			return zip;
		}

		private static long FindZip64Record(Stream s, long locatorpos, long stored)
		{
			// Usual layout: the record sits right before the locator. Otherwise trust the stored offset.
			long[] guesses = { locatorpos - 56, stored };
			foreach(long pos in guesses)
			{
				if(pos >= 0 && pos + 56 <= s.Length && BitConverter.ToUInt32(ReadAt(s, pos, 4), 0) == ZIP64_EOCD_SIGNATURE)
					return pos;
			}

			throw new InvalidDataException("Zip64 end record not found.");
		}

		private static List<ZipEntryRecord> ParseCentral(byte[] cd, int count, long bias, long cdpos)
		{
			List<ZipEntryRecord> entries = new List<ZipEntryRecord>(Math.Min(count, 65536));
			int p = 0;
			for(int i = 0; i < count; i++)
			{
				if(p + CENTRAL_HEADER_SIZE > cd.Length || BitConverter.ToUInt32(cd, p) != CENTRAL_SIGNATURE)
					throw new InvalidDataException("Central directory record " + i + " is corrupt.");

				ZipEntryRecord e = new ZipEntryRecord();
				e.Index = i;
				e.VersionMadeBy = BitConverter.ToUInt16(cd, p + 4);
				e.VersionNeeded = BitConverter.ToUInt16(cd, p + 6);
				e.Flags = BitConverter.ToUInt16(cd, p + 8);
				e.Method = BitConverter.ToUInt16(cd, p + 10);
				e.DosTime = BitConverter.ToUInt16(cd, p + 12);
				e.DosDate = BitConverter.ToUInt16(cd, p + 14);
				e.Crc = BitConverter.ToUInt32(cd, p + 16);
				e.CompressedSize = BitConverter.ToUInt32(cd, p + 20);
				e.UncompressedSize = BitConverter.ToUInt32(cd, p + 24);
				int namelen = BitConverter.ToUInt16(cd, p + 28);
				int extralen = BitConverter.ToUInt16(cd, p + 30);
				int commentlen = BitConverter.ToUInt16(cd, p + 32);
				e.DiskStart = BitConverter.ToUInt16(cd, p + 34);
				e.InternalAttributes = BitConverter.ToUInt16(cd, p + 36);
				e.ExternalAttributes = BitConverter.ToUInt32(cd, p + 38);
				long localoffset = BitConverter.ToUInt32(cd, p + 42);

				int varstart = p + CENTRAL_HEADER_SIZE;
				if(varstart + namelen + extralen + commentlen > cd.Length)
					throw new InvalidDataException("Central directory record " + i + " is truncated.");

				e.NameBytes = Slice(cd, varstart, namelen);
				e.Extra = Slice(cd, varstart + namelen, extralen);
				e.Comment = Slice(cd, varstart + namelen + extralen, commentlen);
				e.Name = DecodeName(e.NameBytes, e.IsUtf8);

				ApplyZip64Extra(e, ref localoffset);
				e.LocalOffset = localoffset + bias;
				if(e.LocalOffset < 0 || e.LocalOffset + LOCAL_HEADER_SIZE > cdpos)
					throw new InvalidDataException("Entry \"" + e.Name + "\" has a local header outside the archive.");

				entries.Add(e);
				p = varstart + namelen + extralen + commentlen;
			}

			return entries;
		}

		// Fields saturated at their 32-bit maximum come from the zip64 extra field, in this order
		private static void ApplyZip64Extra(ZipEntryRecord e, ref long localoffset)
		{
			bool needusize = e.UncompressedSize == 0xFFFFFFFF;
			bool needcsize = e.CompressedSize == 0xFFFFFFFF;
			bool needoffset = localoffset == 0xFFFFFFFF;
			if(!needusize && !needcsize && !needoffset) return;

			for(int p = 0; p + 4 <= e.Extra.Length;)
			{
				int id = BitConverter.ToUInt16(e.Extra, p);
				int size = BitConverter.ToUInt16(e.Extra, p + 2);
				if(p + 4 + size > e.Extra.Length) break;
				if(id == 0x0001)
				{
					int q = p + 4;
					int end = q + size;
					if(needusize && q + 8 <= end) { e.UncompressedSize = (long)BitConverter.ToUInt64(e.Extra, q); q += 8; }
					if(needcsize && q + 8 <= end) { e.CompressedSize = (long)BitConverter.ToUInt64(e.Extra, q); q += 8; }
					if(needoffset && q + 8 <= end) { localoffset = (long)BitConverter.ToUInt64(e.Extra, q); }
					e.UsesZip64 = true;
					return;
				}
				p += 4 + size;
			}

			throw new InvalidDataException("Entry \"" + e.Name + "\" lacks its zip64 extra field.");
		}

		// Flag bit 11 means UTF-8; otherwise valid UTF-8 is taken as such, then code page 437, then Latin-1
		public static string DecodeName(byte[] name, bool utf8)
		{
			if(utf8) return Encoding.UTF8.GetString(name);
			try
			{
				return new UTF8Encoding(false, true).GetString(name);
			}
			catch(DecoderFallbackException) { }

			try
			{
				return Encoding.GetEncoding(437).GetString(name);
			}
			catch(Exception)
			{
				return Encoding.GetEncoding(28591).GetString(name);
			}
		}

		#endregion

		#region ================== Entry data

		// Position of the entry's compressed data, after its local header
		public long DataOffset(Stream s, ZipEntryRecord e)
		{
			byte[] local = ReadAt(s, e.LocalOffset, LOCAL_HEADER_SIZE);
			if(BitConverter.ToUInt32(local, 0) != LOCAL_SIGNATURE)
				throw new InvalidDataException("Entry \"" + e.Name + "\" has no local header at its offset.");
			long start = e.LocalOffset + LOCAL_HEADER_SIZE + BitConverter.ToUInt16(local, 26) + BitConverter.ToUInt16(local, 28);
			if(start + e.CompressedSize > CentralOffset)
				throw new InvalidDataException("Entry \"" + e.Name + "\" runs past the central directory.");
			return start;
		}

		// Stored and deflated entries only. Checks the CRC and size.
		public void Extract(Stream s, ZipEntryRecord e, Stream output)
		{
			if(e.IsEncrypted) throw new NotSupportedException("Entry \"" + e.Name + "\" is encrypted.");
			if(e.Method != 0 && e.Method != 8) throw new NotSupportedException("Entry \"" + e.Name + "\" uses compression method " + e.Method + ".");

			s.Position = DataOffset(s, e);
			Stream raw = new BoundedStream(s, e.CompressedSize);
			Stream data = e.Method == 8 ? new DeflateStream(raw, CompressionMode.Decompress, true) : raw;

			CRC crc = new CRC();
			long total = 0;
			byte[] buffer = new byte[81920];
			int read;
			while((read = data.Read(buffer, 0, buffer.Length)) > 0)
			{
				crc.Add(buffer, 0, read);
				output.Write(buffer, 0, read);
				total += read;
				if(total > e.UncompressedSize) break;
			}
			if(data != raw) data.Dispose();

			if(total != e.UncompressedSize || (uint)crc.Value != e.Crc)
				throw new InvalidDataException("Entry \"" + e.Name + "\" fails its CRC or size check.");
		}

		#endregion

		#region ================== Helpers

		public static byte[] ReadAt(Stream s, long position, int count)
		{
			if(position < 0 || position + count > s.Length) throw new InvalidDataException("Read past the end of the archive.");
			byte[] data = new byte[count];
			s.Position = position;
			ReadFully(s, data, count);
			return data;
		}

		private static void ReadFully(Stream s, byte[] buffer, int count)
		{
			int done = 0;
			while(done < count)
			{
				int read = s.Read(buffer, done, count - done);
				if(read <= 0) throw new EndOfStreamException();
				done += read;
			}
		}

		private static byte[] Slice(byte[] data, int start, int length)
		{
			byte[] result = new byte[length];
			Buffer.BlockCopy(data, start, result, 0, length);
			return result;
		}

		// Read-only view of the next count bytes of a stream
		private sealed class BoundedStream : Stream
		{
			private readonly Stream inner;
			private long remaining;

			public BoundedStream(Stream inner, long count) { this.inner = inner; remaining = count; }

			public override int Read(byte[] buffer, int offset, int count)
			{
				if(remaining <= 0) return 0;
				int read = inner.Read(buffer, offset, (int)Math.Min(count, remaining));
				remaining -= read;
				return read;
			}

			public override bool CanRead { get { return true; } }
			public override bool CanSeek { get { return false; } }
			public override bool CanWrite { get { return false; } }
			public override long Length { get { throw new NotSupportedException(); } }
			public override long Position { get { throw new NotSupportedException(); } set { throw new NotSupportedException(); } }
			public override void Flush() { }
			public override long Seek(long offset, SeekOrigin origin) { throw new NotSupportedException(); }
			public override void SetLength(long value) { throw new NotSupportedException(); }
			public override void Write(byte[] buffer, int offset, int count) { throw new NotSupportedException(); }
		}

		#endregion
	}
}
