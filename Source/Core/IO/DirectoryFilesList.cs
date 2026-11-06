
#region ================== Copyright (c) 2007 Pascal vd Heiden

/*
 * Copyright (c) 2007 Pascal vd Heiden, www.codeimp.com
 * This program is released under GNU General Public License
 * 
 * This program is distributed in the hope that it will be useful,
 * but WITHOUT ANY WARRANTY; without even the implied warranty of
 * MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
 * GNU General Public License for more details.
 * 
 */

#endregion

#region ================== Namespaces

using System;
using System.Collections.Generic;
using System.IO;
using CodeImp.DoomBuilder.Config;
using CodeImp.DoomBuilder.Data;

#endregion

namespace CodeImp.DoomBuilder.IO
{
	/// <summary>
	/// Compares two paths, ignoring case and converting the secondary string to the system's directory separator
	/// </summary>
	internal sealed class PathEqualityComparer : IEqualityComparer<string>
	{
		public bool Equals(string s1, string s2)
		{
			return s1.ToLowerInvariant() == ConvertPath(s2);
		}

		public int GetHashCode(string s)
		{
			return ConvertPath(s).GetHashCode();
		}

		private string ConvertPath(string s)
		{
			return s.Replace('\\', Path.DirectorySeparatorChar).Replace('/', Path.DirectorySeparatorChar).ToLowerInvariant();
		}
	}

	internal sealed class DirectoryFilesList
	{
		#region ================== Constants (mxd)

		#endregion

		#region ================== Variables

		private Dictionary<string, DirectoryFileEntry> entries; //mxd
		private List<string> wadentries; //mxd

		// Lump filters (ApplyFilters): the entries before any filter, and real <-> root-relative names
		private List<DirectoryFileEntry> unfiltered;
		private Dictionary<string, string> virtualpaths;
		private Dictionary<string, string> virtualtoreal;

		#endregion

		#region ================== Properties

		public int Count { get { return entries.Count; } }

		#endregion

		#region ================== Constructor / Disposer

		// Constructor to fill list from directory and optionally subdirectories
		public DirectoryFilesList(string path, GameConfiguration config, bool subdirectories, FileTitleStyle filetitlestyle)
		{
			path = Path.GetFullPath(path);
			string[] files = Directory.GetFiles(path, "*", subdirectories ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly);
			Array.Sort(files); //mxd
			entries = new Dictionary<string, DirectoryFileEntry>(files.Length, new PathEqualityComparer());
			wadentries = new List<string>();
			
			foreach(string file in files) //mxd
			{
				var e = new DirectoryFileEntry(file, path, filetitlestyle);
				if(string.Compare(e.extension, "wad", true) == 0 && e.path.Length == 0)
				{
					wadentries.Add(file);
					continue;
				}

				if (config != null)
				{
					if (config.IgnoredFileExtensions.Contains(e.extension)) continue;

					bool skipfolder = false;
					foreach (string ef in config.IgnoredDirectoryNames)
					{
						if (e.path.StartsWith(ef + Path.DirectorySeparatorChar))
						{
							skipfolder = true;
							break;
						}
					}
					if (skipfolder) continue;
				}

				AddOrReplaceEntry(e);
			}
		}

		// Constructor for custom list
		public DirectoryFilesList(string resourcename, GameConfiguration config, bool silent, ICollection<DirectoryFileEntry> sourceentries)
		{
			entries = new Dictionary<string, DirectoryFileEntry>(sourceentries.Count, new PathEqualityComparer());
			wadentries = new List<string>();
			foreach(DirectoryFileEntry e in sourceentries)
			{
				if(string.Compare(e.extension, "wad", true) == 0 && e.path.Length == 0)
				{
					wadentries.Add(e.filepathname);
					continue;
				}

				if (config != null)
				{
					if (config.IgnoredFileExtensions.Contains(e.extension)) continue;

					bool skipfolder = false;
					foreach (string ef in config.IgnoredDirectoryNames)
					{
						if (e.path.StartsWith(ef + Path.DirectorySeparatorChar))
						{
							skipfolder = true;
							break;
						}
					}
					if (skipfolder) continue;
				}

				if(entries.ContainsKey(e.filepathname))
				{
					if (!silent) General.ErrorLogger.Add(ErrorType.Warning, "Resource \"" + resourcename + "\" contains multiple files with the same filename. See: \"" + e.filepathname + "\"");
					continue;
				}

				AddOrReplaceEntry(e);
			}
		}

		#endregion

		#region ================== Methods

		// This checks whether a file is in the entry dictionary, adds it if it
		// isn't, and replaces the existing entry if the new entry is lowercase
		private void AddOrReplaceEntry(DirectoryFileEntry e)
		{
			// If the entry is already in the dictionary, add the one with
			// greater ordinal value (prefer lowercase)
			if(entries.ContainsKey(e.filepathname))
			{
				// Get the key for the existing entry. It may have a
				// different case, since the dictionary is set up to be
				// case insensitive.
				string existingEntryPath = entries[e.filepathname].filepathname;
				if(e.filepathname.CompareTo(existingEntryPath) == -1)
				{
					entries.Remove(e.filepathname);
					entries.Add(e.filepathname, e);
				}
			}
			else
			{
				// Just add the entry, since it's not in the dictionary yet.
				entries.Add(e.filepathname, e);
			}
		}

		// Makes filter/<name>/ groups for the given names root content, after the real root files and in name order,
		// and drops every other filter/ entry. Keys and filepathname stay real. Returns the matched WADs it skipped.
		public List<string> ApplyFilters(IList<string> names)
		{
			// Always rebuild from the unfiltered entries, so this can run again on the same list
			if(unfiltered == null) unfiltered = new List<DirectoryFileEntry>(entries.Values);

			string filterdir = "filter" + Path.DirectorySeparatorChar;
			var filtered = new Dictionary<string, DirectoryFileEntry>(unfiltered.Count, new PathEqualityComparer());
			virtualpaths = new Dictionary<string, string>(new PathEqualityComparer());
			virtualtoreal = new Dictionary<string, string>(new PathEqualityComparer());
			List<string> skippedwads = new List<string>();

			// Plain Add in this order: enumeration order is what makes root files come first
			foreach(DirectoryFileEntry e in unfiltered)
				if(!e.path.StartsWith(filterdir, StringComparison.Ordinal)) filtered.Add(e.filepathname, e);

			// An entry belongs to the first name that matches it, like the engine moving each group only once
			bool[] claimed = new bool[unfiltered.Count];
			foreach(string name in names)
			{
				string prefix = filterdir + name.ToLowerInvariant() + Path.DirectorySeparatorChar;
				for(int i = 0; i < unfiltered.Count; i++)
				{
					DirectoryFileEntry e = unfiltered[i];
					if(claimed[i] || !e.path.StartsWith(prefix, StringComparison.Ordinal)) continue;
					claimed[i] = true;

					DirectoryFileEntry v = e;
					v.path = e.path.Substring(prefix.Length);
					string virtualname = e.filepathname.Substring(prefix.Length);

					// WADs are opened at construction, from the real root only
					if(v.extension == "wad" && v.path.Length == 0)
					{
						skippedwads.Add(e.filepathname);
						continue;
					}

					filtered.Add(e.filepathname, v);
					virtualpaths[e.filepathname] = virtualname;
					virtualtoreal[virtualname] = e.filepathname;
				}
			}

			entries = filtered;
			return skippedwads;
		}

		// The root-relative name of a filtered entry, else the name itself
		public string GetVirtualPath(string filepathname)
		{
			string virtualname;
			if(virtualpaths != null && virtualpaths.TryGetValue(filepathname, out virtualname)) return virtualname;
			return filepathname;
		}

		// The real name for a real or root-relative name, root files first, else null
		public string Resolve(string name)
		{
			if(entries.ContainsKey(name)) return name;

			string realname;
			if(virtualtoreal != null && virtualtoreal.TryGetValue(name, out realname)) return realname;
			return null;
		}

		// This checks if a given file exists
		// The given file path must not be absolute
		public bool FileExists(string filepathname)
		{
			return Resolve(filepathname.ToLowerInvariant()) != null;
		}

		// This returns file information for the given file
		// The given file path must not be absolute
		public DirectoryFileEntry GetFileInfo(string filepathname)
		{
			return entries[Resolve(filepathname) ?? filepathname];
		}

		//mxd. This returns a list of all wad files (filepathname)
		public List<string> GetWadFiles()
		{
			return wadentries;
		}
		
		// This returns a list of all files (filepathname)
		public List<string> GetAllFiles()
		{
			List<string> files = new List<string>(entries.Count);
			foreach(DirectoryFileEntry e in entries.Values) files.Add(e.filepathname);
			return files;
		}

		// This returns a list of all files optionally with subdirectories included
		public List<string> GetAllFiles(bool subdirectories)
		{
			if(subdirectories) return GetAllFiles();

			List<string> files = new List<string>(entries.Count);
			foreach(DirectoryFileEntry e in entries.Values)
				if(e.path.Length == 0) files.Add(e.filepathname);
			return files;
		}

		// This returns a list of all files that are in the given path and optionally in subdirectories
		public List<string> GetAllFiles(string path, bool subdirectories)
		{
			path = CorrectPath(path);
			if(subdirectories)
			{
				List<string> files = new List<string>(entries.Count);
				foreach(DirectoryFileEntry e in entries.Values)
					if(e.path.StartsWith(path)) files.Add(e.filepathname);
				return files;
			}
			else
			{
				List<string> files = new List<string>(entries.Count);
				foreach(DirectoryFileEntry e in entries.Values)
					if(e.path == path) files.Add(e.filepathname);
				return files;
			}
		}

		// This returns a list of all files that are in the given path and subdirectories and have the given title
		public List<string> GetAllFilesWithTitle(string path, string title)
		{
			path = CorrectPath(path).ToLowerInvariant();
			title = title.ToLowerInvariant();
			List<string> files = new List<string>(entries.Count);
			foreach(DirectoryFileEntry e in entries.Values)
				if(e.path.StartsWith(path) && e.filetitle == title) files.Add(e.filepathname);
			return files;
		}

		// This returns a list of all files that are in the given path (optionally in subdirectories) and have the given title
		public List<string> GetAllFilesWithTitle(string path, string title, bool subdirectories)
		{
			if(subdirectories) return GetAllFilesWithTitle(path, title);

			path = CorrectPath(path).ToLowerInvariant();
			title = title.ToLowerInvariant();
			List<string> files = new List<string>(entries.Count);
			foreach(DirectoryFileEntry e in entries.Values)
				if(e.path == path && e.filetitle == title) files.Add(e.filepathname);
			return files;
		}

		//mxd. This returns a list of all files that are in the given path and which names starts with title
		public List<string> GetAllFilesWhichTitleStartsWith(string path, string title)
		{
			path = CorrectPath(path).ToLowerInvariant();
			title = title.ToLowerInvariant();
			List<string> files = new List<string>(entries.Count);
			foreach(DirectoryFileEntry e in entries.Values)
				if(e.path.StartsWith(path) && e.filetitle.StartsWith(title)) files.Add(e.filepathname);
			return files;
		}

		//mxd. This returns a list of all files that are in the given path and which names starts with title
		public List<string> GetAllFilesWhichTitleStartsWith(string path, string title, bool subdirectories) 
		{
			if(subdirectories) return GetAllFilesWhichTitleStartsWith(path, title);

			path = CorrectPath(path).ToLowerInvariant();
			title = title.ToLowerInvariant();
			List<string> files = new List<string>(entries.Count);
			foreach(DirectoryFileEntry e in entries.Values)
				if(e.path == path && e.filetitle.StartsWith(title)) files.Add(e.filepathname);
			return files;
		}

		// This returns a list of all files that are in the given path and subdirectories and have the given extension
		public List<string> GetAllFiles(string path, string extension)
		{
			path = CorrectPath(path).ToLowerInvariant();
			extension = extension.ToLowerInvariant();
			List<string> files = new List<string>(entries.Count);
			foreach(DirectoryFileEntry e in entries.Values)
				if(e.path.StartsWith(path) && e.extension == extension) files.Add(e.filepathname);
			return files;
		}

		// This returns a list of all files that are in the given path (optionally in subdirectories) and have the given extension
		public List<string> GetAllFiles(string path, string extension, bool subdirectories)
		{
			if(subdirectories) return GetAllFiles(path, extension);

			path = CorrectPath(path).ToLowerInvariant();
			extension = extension.ToLowerInvariant();
			List<string> files = new List<string>(entries.Count);
			foreach(DirectoryFileEntry e in entries.Values)
				if(e.path == path && e.extension == extension) files.Add(e.filepathname);
			return files;
		}

		// This finds the first file that has the specified name, regardless of file extension
		public string GetFirstFile(string title, bool subdirectories)
		{
			title = title.ToLowerInvariant();
			if(subdirectories)
			{
				foreach(DirectoryFileEntry e in entries.Values)
					if(e.filetitle == title) return e.filepathname;
			}
			else
			{
				foreach(DirectoryFileEntry e in entries.Values)
					if(e.filetitle == title && e.path.Length == 0) return e.filepathname;
			}

			return null;
		}

		// This finds the first file that has the specified name and extension
		public string GetFirstFile(string title, bool subdirectories, string extension)
		{
			title = title.ToLowerInvariant();
			extension = extension.ToLowerInvariant();
			if(subdirectories)
			{
				foreach(DirectoryFileEntry e in entries.Values)
					if(e.filetitle == title && e.extension == extension) return e.filepathname;
			}
			else
			{
				foreach(DirectoryFileEntry e in entries.Values)
					if(e.filetitle == title && e.path.Length == 0 && e.extension == extension) return e.filepathname;
			}

			return null;
		}

		// This finds the first file that has the specified name, regardless of file extension, and is in the given path
		public string GetFirstFile(string path, string title, bool subdirectories)
		{
			title = title.ToLowerInvariant();
			path = CorrectPath(path).ToLowerInvariant();
			if(subdirectories)
			{
				foreach(DirectoryFileEntry e in entries.Values)
					if((e.filetitle.Length > DataManager.CLASIC_IMAGE_NAME_LENGTH ? e.filetitle.StartsWith(title) : e.filetitle == title)
						&& e.path.StartsWith(path)) return e.filepathname;
			}
			else
			{
				foreach(DirectoryFileEntry e in entries.Values)
					if((e.filetitle.Length > DataManager.CLASIC_IMAGE_NAME_LENGTH ? e.filetitle.StartsWith(title) : e.filetitle == title) 
						&& e.path == path) return e.filepathname;
			}

			return null;
		}

		// This finds the first file that has the specified name, is in the given path and has the given extension
		public string GetFirstFile(string path, string title, bool subdirectories, string extension)
		{
			title = title.ToLowerInvariant();
			path = CorrectPath(path).ToLowerInvariant();
			extension = extension.ToLowerInvariant();
			if(subdirectories)
			{
				foreach(DirectoryFileEntry e in entries.Values)
					if(e.filetitle == title && e.path.StartsWith(path) && e.extension == extension) return e.filepathname;
			}
			else
			{
				foreach(DirectoryFileEntry e in entries.Values)
					if(e.filetitle == title && e.path == path && e.extension == extension) return e.filepathname;
			}

			return null;
		}
		
		// This fixes a path so that it ends with the proper directory separator (mxd)
		private static string CorrectPath(string path)
		{
			if(path.Length > 0)
			{
				if(path[path.Length - 1] == Path.DirectorySeparatorChar) return path;
				if(path[path.Length - 1] == Path.AltDirectorySeparatorChar)
					path = path.Substring(0, path.Length - 1);
				return path + Path.DirectorySeparatorChar;
			}

			return path;
		}

		#endregion
	}
}
