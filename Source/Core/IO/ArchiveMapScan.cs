#region ================== Namespaces

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

#endregion

namespace CodeImp.DoomBuilder.IO
{
	// A WAD inside an archive, extracted to the scan folder and open read-only
	internal sealed class ArchiveMapCandidate
	{
		public ArchiveMapSource Source { get; internal set; }
		public string ScanPath { get; internal set; }
		public WAD Wad { get; internal set; }
		public string NotWritableReason { get; internal set; } // Null when Save can write back into the archive
		public bool Writable { get { return NotWritableReason == null; } }
	}

	// Every WAD in a PK3/ZIP, at any depth, extracted for map discovery. Dispose deletes the extracted copies.
	internal sealed class ArchiveMapScan : IDisposable
	{
		#region ================== Variables

		private bool disposed;

		#endregion

		#region ================== Properties

		public string ArchivePath { get; private set; }
		public string ScanFolder { get; private set; }
		public List<ArchiveMapCandidate> Candidates { get; private set; }
		public List<string> Warnings { get; private set; }
		public string NotWritableReason { get; private set; } // Archive-wide; null when writable

		#endregion

		#region ================== Constructor / Disposer

		private ArchiveMapScan(string archivepath)
		{
			ArchivePath = Path.GetFullPath(archivepath);
			Candidates = new List<ArchiveMapCandidate>();
			Warnings = new List<string>();
		}

		// Throws InvalidDataException with a user-facing message when the file is not a usable zip
		public static ArchiveMapScan Open(string archivepath, string temproot)
		{
			ArchiveMapScan scan = new ArchiveMapScan(archivepath);
			try
			{
				scan.ScanFolder = Path.Combine(temproot, "udb-archive-scan-" + Path.GetRandomFileName().Replace(".", ""));
				Directory.CreateDirectory(scan.ScanFolder);
				General.WriteLogLine("Scanning archive \"" + scan.ArchivePath + "\" for maps in \"" + scan.ScanFolder + "\"");
				scan.Scan();
				return scan;
			}
			catch
			{
				scan.Dispose();
				throw;
			}
		}

		public void Dispose()
		{
			if(disposed) return;
			disposed = true;

			foreach(ArchiveMapCandidate c in Candidates)
			{
				if(c.Wad != null && !c.Wad.IsDisposed) c.Wad.Dispose();
			}

			if(ScanFolder != null && Directory.Exists(ScanFolder))
			{
				try { Directory.Delete(ScanFolder, true); }
				catch(Exception e) { General.WriteLogLine("WARNING: could not delete archive scan folder \"" + ScanFolder + "\": " + e.Message); }
			}
		}

		#endregion

		#region ================== Scanning

		private void Scan()
		{
			string archivename = Path.GetFileName(ArchivePath);
			using(FileStream s = File.Open(ArchivePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
			{
				ZipDirectory zip;
				try
				{
					zip = ZipDirectory.Read(s);
				}
				catch(InvalidDataException e)
				{
					throw new InvalidDataException("\"" + archivename + "\" is not a valid PK3/ZIP archive. " + e.Message, e);
				}
				catch(EndOfStreamException e)
				{
					throw new InvalidDataException("\"" + archivename + "\" is not a valid PK3/ZIP archive: it is truncated.", e);
				}

				if(zip.IsZip64) NotWritableReason = "The archive uses Zip64.";

				Dictionary<string, int> namecounts = new Dictionary<string, int>();
				foreach(ZipEntryRecord e in zip.Entries)
				{
					string key = Convert.ToBase64String(e.NameBytes);
					namecounts[key] = namecounts.TryGetValue(key, out int n) ? n + 1 : 1;
				}

				int number = 0;
				foreach(ZipEntryRecord e in zip.Entries)
				{
					if(e.IsDirectory || !e.Name.EndsWith(".wad", StringComparison.OrdinalIgnoreCase)) continue;

					if(!ValidPath(e.Name))
					{
						Warnings.Add("Skipped \"" + e.Name + "\": unsupported character in its path.");
						continue;
					}

					string scanpath = Path.Combine(ScanFolder, (number++) + ".wad");
					try
					{
						using(FileStream output = File.Create(scanpath))
							zip.Extract(s, e, output);
					}
					catch(Exception ex) when(ex is InvalidDataException || ex is NotSupportedException || ex is EndOfStreamException)
					{
						Warnings.Add("Skipped \"" + e.Name + "\": " + ex.Message);
						File.Delete(scanpath);
						continue;
					}

					string waderror = CheckWadHeader(scanpath);
					WAD wad = null;
					if(waderror == null)
					{
						try { wad = new WAD(scanpath, true); }
						catch(Exception ex) { waderror = ex.Message; }
					}

					if(wad == null)
					{
						Warnings.Add("Skipped \"" + e.Name + "\": not a valid WAD file. " + waderror);
						File.Delete(scanpath);
						continue;
					}

					ArchiveMapCandidate c = new ArchiveMapCandidate { Source = new ArchiveMapSource(ArchivePath, e), ScanPath = scanpath, Wad = wad };
					if(NotWritableReason != null) c.NotWritableReason = NotWritableReason;
					else if(namecounts[Convert.ToBase64String(e.NameBytes)] > 1) c.NotWritableReason = "The archive holds several entries named \"" + e.Name + "\".";
					else if(e.UsesZip64) c.NotWritableReason = "The entry uses Zip64.";
					Candidates.Add(c);
				}
			}

			foreach(string w in Warnings) General.WriteLogLine("WARNING: " + archivename + ": " + w);
		}

		// WAD's own reader trusts the header, so a garbage entry could ask for a huge lump table
		private static string CheckWadHeader(string path)
		{
			using(FileStream s = File.OpenRead(path))
			{
				if(s.Length < 12) return "It is too short.";
				byte[] header = new byte[12];
				int read = 0;
				while(read < 12) read += s.Read(header, read, 12 - read);
				string magic = Encoding.ASCII.GetString(header, 0, 4);
				if(magic != "IWAD" && magic != "PWAD") return "It has no IWAD/PWAD header.";
				long numlumps = BitConverter.ToInt32(header, 4);
				long offset = BitConverter.ToInt32(header, 8);
				if(numlumps < 0 || offset < 0 || offset + numlumps * 16 > s.Length) return "Its lump table is out of range.";
			}

			return null;
		}

		// Same rule as PK3Reader.CheckInvalidPathChars
		private static bool ValidPath(string path)
		{
			return !path.Any(c => c < 32 || c == '"' || c == '<' || c == '>' || c == '|');
		}

		#endregion
	}
}
