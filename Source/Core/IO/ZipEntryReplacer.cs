#region ================== Namespaces

using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Security.Cryptography;

#endregion

namespace CodeImp.DoomBuilder.IO
{
	// The archive can't be written back safely; Message says why, for the user
	internal sealed class ZipNotWritableException : Exception
	{
		public ZipNotWritableException(string message) : base(message) { }
	}

	// Replaces one entry of a zip, copying every other entry's bytes unchanged, through a verified sibling file
	internal static class ZipEntryReplacer
	{
		#region ================== Constants

		private const int BUFFER_SIZE = 81920;
		private const long MAX_32 = 0xFFFFFFFFL;

		#endregion

		#region ================== Checks

		// Null when the target can be replaced, else the reason. target is the matching record.
		public static string CheckWritable(Stream s, ZipDirectory zip, ArchiveMapSource source, out ZipEntryRecord target)
		{
			target = null;
			if(zip.IsZip64) return "The archive uses Zip64.";
			if(zip.IsMultiDisk) return "The archive is split or spanned.";
			if(zip.Entries.Any(e => e.UsesZip64)) return "The archive uses Zip64.";

			int matches;
			target = source.FindByName(zip, out matches);
			if(matches == 0) return "The archive no longer holds \"" + source.EntryName + "\".";
			if(matches > 1) return "The archive holds several entries named \"" + source.EntryName + "\".";
			if(target.IsEncrypted) return "\"" + source.EntryName + "\" is encrypted.";

			List<ZipEntryRecord> byoffset = zip.Entries.OrderBy(e => e.LocalOffset).ToList();
			for(int i = 0; i < byoffset.Count; i++)
			{
				ZipEntryRecord e = byoffset[i];
				long next = i + 1 < byoffset.Count ? byoffset[i + 1].LocalOffset : zip.CentralOffset;
				long end;
				try
				{
					end = zip.DataOffset(s, e) + e.CompressedSize;
				}
				catch(InvalidDataException ex)
				{
					return ex.Message;
				}
				if(e.HasDataDescriptor) end += 12; // The descriptor's shortest form
				if(end > next) return "Entries \"" + e.Name + "\" and \"" + (i + 1 < byoffset.Count ? byoffset[i + 1].Name : "(central directory)") + "\" overlap.";
			}

			return null;
		}

		#endregion

		#region ================== Replace

		// Writes newdatapath's bytes as the source's entry and swaps the result in. Returns the entry's new record.
		// beforeswap runs once the new archive is verified, with the original still open (entry backups).
		// Throws ZipNotWritableException (refused, archive untouched), InvalidDataException (verification failed,
		// archive untouched) or IOException.
		public static ZipEntryRecord Replace(ArchiveMapSource source, string newdatapath, uint dostime,
			Action<Stream, ZipDirectory, ZipEntryRecord> beforeswap = null, Action<string> afterwrite = null)
		{
			using(SiblingFileSwap swap = new SiblingFileSwap(source.ArchivePath))
			{
				ZipEntryRecord written;
				using(FileStream original = File.Open(swap.TargetPath, FileMode.Open, FileAccess.Read, FileShare.Read))
				{
					ZipDirectory zip = ZipDirectory.Read(original);
					ZipEntryRecord target;
					string reason = CheckWritable(original, zip, source, out target);
					if(reason != null) throw new ZipNotWritableException(reason);
					if(!source.IsSameEntry(target)) throw new ZipNotWritableException("\"" + source.EntryName + "\" moved in the archive since it was opened.");

					Dictionary<ZipEntryRecord, byte[]> rangehashes;
					using(FileStream output = new FileStream(swap.SiblingPath, FileMode.CreateNew, FileAccess.ReadWrite, FileShare.None))
					{
						rangehashes = Write(original, zip, target, newdatapath, dostime, output);
						output.Flush(true);
					}

					afterwrite?.Invoke(swap.SiblingPath);
					written = Verify(swap.SiblingPath, original, zip, target, rangehashes, newdatapath, dostime);
					beforeswap?.Invoke(original, zip, target);
				}

				swap.CopyMetadataFromTarget();
				if(!string.IsNullOrEmpty(swap.MetadataWarning)) General.WriteLogLine("Warning: " + swap.MetadataWarning);
				swap.Swap();
				return written;
			}
		}

		// Returns the hash of every untouched entry's raw range, as copied
		private static Dictionary<ZipEntryRecord, byte[]> Write(Stream original, ZipDirectory zip, ZipEntryRecord target, string newdatapath, uint dostime, Stream output)
		{
			Dictionary<ZipEntryRecord, byte[]> hashes = new Dictionary<ZipEntryRecord, byte[]>();
			Dictionary<ZipEntryRecord, long> newoffsets = new Dictionary<ZipEntryRecord, long>();
			byte[] buffer = new byte[BUFFER_SIZE];

			// An SFX stub or other prefix stays as it is
			CopyRange(original, 0, zip.PrefixLength, output, buffer, null);

			uint newcrc = 0;
			long newcsize = 0, newusize = 0;
			List<ZipEntryRecord> byoffset = zip.Entries.OrderBy(e => e.LocalOffset).ToList();
			for(int i = 0; i < byoffset.Count; i++)
			{
				ZipEntryRecord e = byoffset[i];
				newoffsets[e] = output.Position;
				if(e == target)
				{
					WriteTarget(target, newdatapath, dostime, output, out newcrc, out newcsize, out newusize);
					continue;
				}

				long next = i + 1 < byoffset.Count ? byoffset[i + 1].LocalOffset : zip.CentralOffset;
				using(SHA1 sha = SHA1.Create())
				{
					CopyRange(original, e.LocalOffset, next - e.LocalOffset, output, buffer, sha);
					sha.TransformFinalBlock(buffer, 0, 0);
					hashes[e] = sha.Hash;
				}
			}

			// Central directory in its original order, offsets patched. Stored offsets keep the original bias.
			long cdstart = output.Position;
			BinaryWriter w = new BinaryWriter(output);
			foreach(ZipEntryRecord e in zip.Entries)
			{
				bool istarget = e == target;
				long storedoffset = newoffsets[e] - zip.OffsetBias;
				if(storedoffset > MAX_32) throw new ZipNotWritableException("The archive would need Zip64.");
				byte[] extra = istarget ? new byte[0] : e.Extra;

				w.Write(ZipDirectory.CENTRAL_SIGNATURE);
				w.Write(e.VersionMadeBy);
				w.Write(istarget ? (ushort)Math.Max((int)e.VersionNeeded, 20) : e.VersionNeeded);
				w.Write(istarget ? TargetFlags(e) : e.Flags);
				w.Write(istarget ? (ushort)8 : e.Method);
				w.Write(istarget ? (ushort)(dostime & 0xFFFF) : e.DosTime);
				w.Write(istarget ? (ushort)(dostime >> 16) : e.DosDate);
				w.Write(istarget ? newcrc : e.Crc);
				w.Write((uint)(istarget ? newcsize : e.CompressedSize));
				w.Write((uint)(istarget ? newusize : e.UncompressedSize));
				w.Write((ushort)e.NameBytes.Length);
				w.Write((ushort)extra.Length);
				w.Write((ushort)e.Comment.Length);
				w.Write(e.DiskStart);
				w.Write(e.InternalAttributes);
				w.Write(e.ExternalAttributes);
				w.Write((uint)storedoffset);
				w.Write(e.NameBytes);
				w.Write(extra);
				w.Write(e.Comment);
			}

			long cdsize = output.Position - cdstart;
			long storedcd = cdstart - zip.OffsetBias;
			if(storedcd > MAX_32 || cdsize > MAX_32) throw new ZipNotWritableException("The archive would need Zip64.");

			w.Write(ZipDirectory.EOCD_SIGNATURE);
			w.Write((ushort)0);
			w.Write((ushort)0);
			w.Write((ushort)zip.Entries.Count);
			w.Write((ushort)zip.Entries.Count);
			w.Write((uint)cdsize);
			w.Write((uint)storedcd);
			w.Write((ushort)zip.Comment.Length);
			w.Write(zip.Comment);
			w.Flush();

			return hashes;
		}

		// Local header, then raw deflate. Sizes are known, so no data descriptor.
		private static void WriteTarget(ZipEntryRecord target, string newdatapath, uint dostime, Stream output, out uint crc, out long csize, out long usize)
		{
			long header = output.Position;
			BinaryWriter w = new BinaryWriter(output);
			w.Write(ZipDirectory.LOCAL_SIGNATURE);
			w.Write((ushort)Math.Max((int)target.VersionNeeded, 20));
			w.Write(TargetFlags(target));
			w.Write((ushort)8);
			w.Write((ushort)(dostime & 0xFFFF));
			w.Write((ushort)(dostime >> 16));
			w.Write(0u); // CRC, patched below
			w.Write(0u); // Compressed size
			w.Write(0u); // Uncompressed size
			w.Write((ushort)target.NameBytes.Length);
			w.Write((ushort)0);
			w.Write(target.NameBytes);
			w.Flush();

			long datastart = output.Position;
			CRC c = new CRC();
			usize = 0;
			byte[] buffer = new byte[BUFFER_SIZE];
			using(FileStream input = File.OpenRead(newdatapath))
			using(DeflateStream deflate = new DeflateStream(output, CompressionMode.Compress, true))
			{
				int read;
				while((read = input.Read(buffer, 0, buffer.Length)) > 0)
				{
					c.Add(buffer, 0, read);
					deflate.Write(buffer, 0, read);
					usize += read;
				}
			}

			long dataend = output.Position;
			csize = dataend - datastart;
			crc = (uint)c.Value;
			if(csize > MAX_32 || usize > MAX_32) throw new ZipNotWritableException("The map's WAD would need Zip64.");

			output.Position = header + 14;
			w.Write(crc);
			w.Write((uint)csize);
			w.Write((uint)usize);
			w.Flush();
			output.Position = dataend;
		}

		// Data descriptor and encryption bits cleared, the UTF-8 name flag kept
		private static ushort TargetFlags(ZipEntryRecord e)
		{
			return (ushort)(e.Flags & 0x0800);
		}

		#endregion

		#region ================== Verification

		private static ZipEntryRecord Verify(string siblingpath, Stream original, ZipDirectory zip, ZipEntryRecord target,
			Dictionary<ZipEntryRecord, byte[]> rangehashes, string newdatapath, uint dostime)
		{
			using(FileStream s = File.Open(siblingpath, FileMode.Open, FileAccess.Read, FileShare.Read))
			{
				ZipDirectory result;
				try
				{
					result = ZipDirectory.Read(s);
				}
				catch(Exception e) when(e is InvalidDataException || e is EndOfStreamException)
				{
					throw new InvalidDataException("The rewritten archive does not parse: " + e.Message, e);
				}

				if(result.Entries.Count != zip.Entries.Count) Fail("the entry count changed");
				if(!result.Comment.SequenceEqual(zip.Comment)) Fail("the archive comment changed");
				if(result.PrefixLength != zip.PrefixLength || !HashRange(s, 0, result.PrefixLength).SequenceEqual(HashRange(original, 0, zip.PrefixLength)))
					Fail("the archive prefix changed");

				List<ZipEntryRecord> oldbyoffset = zip.Entries.OrderBy(e => e.LocalOffset).ToList();
				List<ZipEntryRecord> newbyoffset = result.Entries.OrderBy(e => e.LocalOffset).ToList();
				Dictionary<ZipEntryRecord, int> newposition = new Dictionary<ZipEntryRecord, int>();
				for(int i = 0; i < newbyoffset.Count; i++)
				{
					// The order of entries in the file is kept
					if(newbyoffset[i].Index != oldbyoffset[i].Index) Fail("the entry order changed");
					newposition[newbyoffset[i]] = i;
				}

				ZipEntryRecord written = null;
				for(int i = 0; i < zip.Entries.Count; i++)
				{
					ZipEntryRecord before = zip.Entries[i];
					ZipEntryRecord after = result.Entries[i];
					if(!after.NameBytes.SequenceEqual(before.NameBytes)) Fail("entry " + i + " changed its name");

					if(before == target)
					{
						written = after;
						continue;
					}

					if(after.Crc != before.Crc || after.CompressedSize != before.CompressedSize || after.UncompressedSize != before.UncompressedSize
						|| after.Method != before.Method || after.Flags != before.Flags || after.DosDateTime != before.DosDateTime
						|| after.ExternalAttributes != before.ExternalAttributes || !after.Extra.SequenceEqual(before.Extra) || !after.Comment.SequenceEqual(before.Comment))
						Fail("\"" + before.Name + "\" changed its central record");

					int pos = newposition[after];
					long next = pos + 1 < newbyoffset.Count ? newbyoffset[pos + 1].LocalOffset : result.CentralOffset;
					if(!HashRange(s, after.LocalOffset, next - after.LocalOffset).SequenceEqual(rangehashes[before]))
						Fail("\"" + before.Name + "\" changed its bytes");
				}

				if(written.DosDateTime != dostime) Fail("the map's time stamp did not stick");
				if(written.IsEncrypted || written.HasDataDescriptor) Fail("the map's entry has unexpected flags");

				byte[] expected;
				using(FileStream input = File.OpenRead(newdatapath))
					expected = HashStream(input);

				byte[] actual;
				using(HashingStream hs = new HashingStream())
				{
					try
					{
						result.Extract(s, written, hs);
					}
					catch(Exception e) when(e is InvalidDataException || e is NotSupportedException)
					{
						throw new InvalidDataException("The rewritten archive's map entry does not read back: " + e.Message, e);
					}
					actual = hs.Finish();
				}
				if(!actual.SequenceEqual(expected)) Fail("the map's entry does not read back as the saved WAD");

				return written;
			}
		}

		private static void Fail(string what)
		{
			throw new InvalidDataException("The rewritten archive failed its check: " + what + ".");
		}

		#endregion

		#region ================== Helpers

		private static void CopyRange(Stream input, long start, long count, Stream output, byte[] buffer, HashAlgorithm hash)
		{
			input.Position = start;
			long remaining = count;
			while(remaining > 0)
			{
				int read = input.Read(buffer, 0, (int)Math.Min(buffer.Length, remaining));
				if(read <= 0) throw new EndOfStreamException("The archive ended early.");
				output.Write(buffer, 0, read);
				if(hash != null) hash.TransformBlock(buffer, 0, read, null, 0);
				remaining -= read;
			}
		}

		private static byte[] HashRange(Stream s, long start, long count)
		{
			using(SHA1 sha = SHA1.Create())
			{
				byte[] buffer = new byte[BUFFER_SIZE];
				s.Position = start;
				long remaining = count;
				while(remaining > 0)
				{
					int read = s.Read(buffer, 0, (int)Math.Min(buffer.Length, remaining));
					if(read <= 0) throw new InvalidDataException("The rewritten archive ended early.");
					sha.TransformBlock(buffer, 0, read, null, 0);
					remaining -= read;
				}
				sha.TransformFinalBlock(buffer, 0, 0);
				return sha.Hash;
			}
		}

		private static byte[] HashStream(Stream s)
		{
			using(SHA1 sha = SHA1.Create()) return sha.ComputeHash(s);
		}

		// Write-only sink that hashes what it is given
		private sealed class HashingStream : Stream
		{
			private readonly SHA1 sha = SHA1.Create();
			public byte[] Finish() { sha.TransformFinalBlock(new byte[0], 0, 0); return sha.Hash; }
			public override void Write(byte[] buffer, int offset, int count) { sha.TransformBlock(buffer, offset, count, null, 0); }
			protected override void Dispose(bool disposing) { if(disposing) sha.Dispose(); base.Dispose(disposing); }
			public override bool CanRead { get { return false; } }
			public override bool CanSeek { get { return false; } }
			public override bool CanWrite { get { return true; } }
			public override long Length { get { throw new NotSupportedException(); } }
			public override long Position { get { throw new NotSupportedException(); } set { throw new NotSupportedException(); } }
			public override void Flush() { }
			public override int Read(byte[] buffer, int offset, int count) { throw new NotSupportedException(); }
			public override long Seek(long offset, SeekOrigin origin) { throw new NotSupportedException(); }
			public override void SetLength(long value) { throw new NotSupportedException(); }
		}

		#endregion
	}
}
