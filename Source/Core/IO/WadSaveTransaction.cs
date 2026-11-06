#region ================== Namespaces

using System;
using System.IO;
using System.Runtime.CompilerServices;

#endregion

namespace CodeImp.DoomBuilder.IO
{
	internal sealed class WadSaveTransaction : IDisposable
	{
		#region ================== Constants

		internal const string SIBLING_SUFFIX = ".saving";

		#endregion

		#region ================== Variables

		private readonly string targetpath;
		private readonly string siblingpath;
		private bool targetexisted;
		private bool swapped;
		private bool isdisposed;

		#endregion

		#region ================== Properties

		internal WAD Wad { get; private set; }
		internal string MetadataWarning { get; private set; }
		internal string SiblingPath { get { return siblingpath; } }

		#endregion

		#region ================== Constructor / Disposer

		internal WadSaveTransaction(string targetpath, string basepath)
		{
			this.targetpath = ResolveRealPath(targetpath);
			this.siblingpath = this.targetpath + SIBLING_SUFFIX;

			try
			{
				// A leftover sibling is a save that never took effect. WAD would parse it, so it goes first
				if(File.Exists(siblingpath)) File.Delete(siblingpath);

				Wad = new WAD(siblingpath);

				// Rebuild from the base, since WADs whose directory is not at the end break otherwise (#531)
				if(!string.IsNullOrEmpty(basepath))
				{
					WAD basewad = new WAD(basepath, true);
					try
					{
						Wad.IsIWAD = basewad.IsIWAD;
						CopyAllLumps(basewad, Wad);
					}
					finally
					{
						basewad.Dispose();
					}
				}
			}
			catch
			{
				Dispose();
				throw;
			}
		}

		public void Dispose()
		{
			if(isdisposed) return;
			isdisposed = true;

			if(Wad != null)
			{
				// A failed flush has already been reported by the call that hit it
				try { Wad.Dispose(); }
				catch(Exception) { }
				Wad = null;
			}

			// The sibling may be the only copy left when a replace lost the target
			if(swapped || (targetexisted && !File.Exists(targetpath))) return;

			try
			{
				if(File.Exists(siblingpath)) File.Delete(siblingpath);
			}
			catch(IOException) { }
			catch(UnauthorizedAccessException) { }
		}

		#endregion

		#region ================== Methods

		internal void Seal()
		{
			if(Wad == null) throw new InvalidOperationException("The save transaction is already sealed.");

			Wad.FlushToDisk();
			Wad.Dispose();
			Wad = null;

			if(File.Exists(targetpath)) MetadataWarning = CopyMetadata(targetpath, siblingpath);
		}

		internal void Swap()
		{
			if(Wad != null || isdisposed) throw new InvalidOperationException("The save transaction must be sealed and open to swap.");

			// Checked here, not earlier: Autosave's rotation moves the target away after Seal
			targetexisted = File.Exists(targetpath);
			try
			{
				if(targetexisted) File.Replace(siblingpath, targetpath, null, true);
				else File.Move(siblingpath, targetpath);
			}
			catch(Exception e)
			{
				if(!targetexisted || File.Exists(targetpath) || !File.Exists(siblingpath)) throw;

				// The replace lost the target, so the sibling must take its name now: a retry would delete it
				try
				{
					File.Move(siblingpath, targetpath);
				}
				catch(Exception)
				{
					throw new IOException("\"" + targetpath + "\" could not be replaced and is missing. The saved map is in \"" + siblingpath + "\".", e);
				}
			}

			swapped = true;
		}

		private static void CopyAllLumps(WAD source, WAD target)
		{
			for(int i = 0; i < source.Lumps.Count; i++)
			{
				Lump srclump = source.Lumps[i];
				Lump tgtlump = target.Insert(srclump.Name, target.Lumps.Count, srclump.Length, false);
				srclump.CopyTo(tgtlump);
			}

			target.WriteHeaders();
		}

		private static string ResolveRealPath(string path)
		{
#if MONO_WINFORMS
			// The sibling must sit beside the real file, or the rename would replace a symlink
			if(File.Exists(path))
			{
				try
				{
					return PosixRealPath(Path.GetFullPath(path));
				}
				catch(DllNotFoundException) { }
				catch(EntryPointNotFoundException) { }
				catch(TypeLoadException) { }
				catch(FileNotFoundException) { }
			}
#endif
			return path;
		}

		private static string CopyMetadata(string from, string to)
		{
#if MONO_WINFORMS
			try
			{
				return PosixCopyMetadata(from, to);
			}
			catch(DllNotFoundException e) { return MetadataFailure(from, e); }
			catch(EntryPointNotFoundException e) { return MetadataFailure(from, e); }
			catch(TypeLoadException e) { return MetadataFailure(from, e); }
			catch(FileNotFoundException e) { return MetadataFailure(from, e); }
#else
			// File.Replace keeps ACLs and attributes on Windows
			return null;
#endif
		}

#if MONO_WINFORMS
		private static string MetadataFailure(string path, Exception e)
		{
			return "file mode and owner of \"" + path + "\" could not be kept: " + e.Message;
		}

		// Mono.Posix calls stay in these non-inlined helpers, so a missing library fails inside the catches above
		[MethodImpl(MethodImplOptions.NoInlining)]
		private static string PosixRealPath(string fullpath)
		{
			return Mono.Unix.UnixPath.GetCompleteRealPath(fullpath);
		}

		[MethodImpl(MethodImplOptions.NoInlining)]
		private static string PosixCopyMetadata(string from, string to)
		{
			Mono.Unix.Native.Stat st;
			if(Mono.Unix.Native.Syscall.stat(from, out st) != 0)
				return "could not read the file mode of \"" + from + "\": " + Mono.Unix.Native.Stdlib.GetLastError();

			string warning = null;

			// Owner first: chown may clear the setuid and setgid bits that chmod then restores
			if(Mono.Unix.Native.Syscall.chown(to, st.st_uid, st.st_gid) != 0)
			{
				Mono.Unix.Native.Errno error = Mono.Unix.Native.Stdlib.GetLastError();
				if(Mono.Unix.Native.Syscall.chown(to, unchecked((uint)-1), st.st_gid) == 0)
					warning = "the owner of \"" + from + "\" could not be kept (" + error + "), the group was";
				else
					warning = "the owner and group of \"" + from + "\" could not be kept: " + error;
			}

			Mono.Unix.Native.FilePermissions mode = (Mono.Unix.Native.FilePermissions)((uint)st.st_mode & 0xFFF);
			if(Mono.Unix.Native.Syscall.chmod(to, mode) != 0)
			{
				string modewarning = "the file mode of \"" + from + "\" could not be kept: " + Mono.Unix.Native.Stdlib.GetLastError();
				warning = (warning == null) ? modewarning : warning + "; " + modewarning;
			}

			return warning;
		}
#endif

		#endregion
	}
}
