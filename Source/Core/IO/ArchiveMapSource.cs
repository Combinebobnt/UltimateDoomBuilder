#region ================== Namespaces

using System;
using System.IO;
using System.Linq;

#endregion

namespace CodeImp.DoomBuilder.IO
{
	// What Save does with a map opened from an archive, decided before resources are suspended
	internal enum ArchiveCommitCheck { Proceed, SaveAsInstead, Cancel }

	// The archive entry a map was opened from, as stored, so Save replaces exactly that entry
	internal sealed class ArchiveMapSource
	{
		#region ================== Properties

		public string ArchivePath { get; private set; }
		public int EntryIndex { get; private set; }
		public long LocalOffset { get; private set; }
		public byte[] EntryNameBytes { get; private set; }
		public string EntryName { get; private set; }
		public uint EntryCrc { get; private set; }
		public long EntrySize { get; private set; }
		public long EntryCompressedSize { get; private set; }
		public uint EntryDosTime { get; private set; }

		// Last path segment of the entry, e.g. MAP01.wad
		public string EntryFileName
		{
			get
			{
				string name = EntryName.Replace('\\', '/');
				return name.Substring(name.LastIndexOf('/') + 1);
			}
		}

		public string DisplayName { get { return Path.GetFileName(ArchivePath) + ": " + EntryName; } }
		public DateTime EntryTime { get { return DosToDateTime(EntryDosTime); } }

		#endregion

		#region ================== Constructor

		public ArchiveMapSource(string archivepath, ZipEntryRecord record)
		{
			ArchivePath = Path.GetFullPath(archivepath);
			Update(record);
		}

		#endregion

		#region ================== Methods

		// After a write-back the entry moves and gets new contents
		public void Update(ZipEntryRecord record)
		{
			EntryIndex = record.Index;
			LocalOffset = record.LocalOffset;
			EntryNameBytes = record.NameBytes;
			EntryName = record.Name;
			EntryCrc = record.Crc;
			EntrySize = record.UncompressedSize;
			EntryCompressedSize = record.CompressedSize;
			EntryDosTime = record.DosDateTime;
		}

		// The record is still the entry this source was read from
		public bool IsSameEntry(ZipEntryRecord record)
		{
			return record.Index == EntryIndex && record.LocalOffset == LocalOffset && record.NameBytes.SequenceEqual(EntryNameBytes);
		}

		// Another tool changed the entry since it was read
		public bool ContentDiffers(ZipEntryRecord record)
		{
			return record.Crc != EntryCrc || record.UncompressedSize != EntrySize || record.CompressedSize != EntryCompressedSize;
		}

		// The record whose name bytes match, or null; several matches also give null
		public ZipEntryRecord FindByName(ZipDirectory zip, out int matches)
		{
			ZipEntryRecord found = null;
			matches = 0;
			foreach(ZipEntryRecord r in zip.Entries)
			{
				if(!r.NameBytes.SequenceEqual(EntryNameBytes)) continue;
				matches++;
				found = r;
			}

			return matches == 1 ? found : null;
		}

		public static DateTime DosToDateTime(uint dos)
		{
			int date = (int)(dos >> 16);
			int time = (int)(dos & 0xFFFF);
			try
			{
				return new DateTime(1980 + (date >> 9), Math.Max(1, (date >> 5) & 15), Math.Max(1, date & 31), time >> 11, (time >> 5) & 63, Math.Min(59, (time & 31) * 2), DateTimeKind.Local);
			}
			catch(ArgumentOutOfRangeException)
			{
				return new DateTime(1980, 1, 1, 0, 0, 0, DateTimeKind.Local);
			}
		}

		public static uint DateTimeToDos(DateTime t)
		{
			if(t.Year < 1980) t = new DateTime(1980, 1, 1);
			if(t.Year > 2107) t = new DateTime(2107, 12, 31, 23, 59, 58);
			uint date = (uint)(((t.Year - 1980) << 9) | (t.Month << 5) | t.Day);
			uint time = (uint)((t.Hour << 11) | (t.Minute << 5) | (t.Second / 2));
			return (date << 16) | time;
		}

		#endregion
	}
}
