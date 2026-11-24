#region ================== Namespaces

using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;

#endregion

namespace CodeImp.DoomBuilder.IO
{
	// Map settings, autosaves and entry backups for maps opened from an archive, kept under the settings folder so the mod folder gets no new files
	internal sealed class ArchiveMapStore
	{
		#region ================== Properties

		public string ArchivePath { get; private set; }
		public string Folder { get; private set; }
		public string IndexFile { get { return Path.Combine(Folder, "index.cfg"); } }

		#endregion

		#region ================== Constructor

		// root is normally <settings>/ArchiveMaps
		public ArchiveMapStore(string root, string archivepath)
		{
			ArchivePath = NormalizePath(archivepath);
			Folder = Path.Combine(root, Hash(ArchivePath));
		}

		public static ArchiveMapStore ForArchive(string archivepath)
		{
			return new ArchiveMapStore(Path.Combine(General.SettingsPath, "ArchiveMaps"), archivepath);
		}

		#endregion

		#region ================== Paths

		// Entry paths contain dots, so they can't be config keys; their hash names the entry's files
		public static string EntryKey(byte[] entrynamebytes)
		{
			return Hash(entrynamebytes);
		}

		// The entry's map settings, the same content a .dbs beside a WAD would have
		public string SettingsFile(ArchiveMapSource source)
		{
			return Path.Combine(Folder, EntryKey(source.EntryNameBytes) + ".dbs");
		}

		// Autosaves and backups go here, named after the entry's file
		public string EntryFolder(ArchiveMapSource source)
		{
			return Path.Combine(Folder, EntryKey(source.EntryNameBytes));
		}

		// <entry folder>/<entry file name>, the base that .backupN and .<MAP>.autosaveN are appended to
		public string EntryFileBase(ArchiveMapSource source)
		{
			return Path.Combine(EntryFolder(source), source.EntryFileName);
		}

		public void EnsureEntryFolder(ArchiveMapSource source)
		{
			Directory.CreateDirectory(EntryFolder(source));
		}

		// Crash-restore key: any change to the archive entry gives a new key, like a WAD's write time does
		public static string RestoreKey(ArchiveMapSource source, string levelname)
		{
			return Hash(NormalizePath(source.ArchivePath) + "\n" + EntryKey(source.EntryNameBytes) + "\n" + levelname + "\n" + source.EntryDosTime.ToString("X8"));
		}

		#endregion

		#region ================== Index

		// Last entry (its key) and map opened from this archive, or nulls
		public void ReadIndex(out string entrykey, out string mapname)
		{
			entrykey = null;
			mapname = null;
			if(!File.Exists(IndexFile)) return;

			Configuration cfg = new Configuration(true);
			if(!cfg.LoadConfiguration(IndexFile)) return;
			entrykey = cfg.ReadSetting("lastentry", "");
			mapname = cfg.ReadSetting("lastmap", "");
			if(entrykey.Length == 0) entrykey = null;
			if(mapname.Length == 0) mapname = null;
		}

		public void WriteIndex(ArchiveMapSource source, string mapname)
		{
			Directory.CreateDirectory(Folder);
			Configuration cfg = new Configuration(true);
			cfg.WriteSetting("archive", ArchivePath);
			cfg.WriteSetting("lastentry", EntryKey(source.EntryNameBytes));
			cfg.WriteSetting("lastentryname", source.EntryName);
			cfg.WriteSetting("lastmap", mapname);
			cfg.SaveConfiguration(IndexFile);
		}

		#endregion

		#region ================== Helpers

		public static string NormalizePath(string path)
		{
			string full = Path.GetFullPath(path);
			return Environment.OSVersion.Platform == PlatformID.Unix || Environment.OSVersion.Platform == PlatformID.MacOSX ? full : full.ToLowerInvariant();
		}

		private static string Hash(string text)
		{
			return Hash(Encoding.UTF8.GetBytes(text));
		}

		private static string Hash(byte[] data)
		{
			using(SHA1 sha = SHA1.Create())
			{
				byte[] digest = sha.ComputeHash(data);
				StringBuilder sb = new StringBuilder(16);
				for(int i = 0; i < 8; i++) sb.Append(digest[i].ToString("x2"));
				return sb.ToString();
			}
		}

		#endregion
	}
}
