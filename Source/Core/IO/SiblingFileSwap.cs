#region ================== Namespaces

using System;
using System.IO;
using System.Runtime.CompilerServices;

#endregion

namespace CodeImp.DoomBuilder.IO
{
	// Writes a file's new contents to "<file>.saving" beside it, then swaps it in with File.Replace.
	// Dispose deletes an unswapped sibling, unless a failed replace left the sibling as the only copy.
	internal sealed class SiblingFileSwap : IDisposable
	{
		#region ================== Constants

		internal const string SIBLING_SUFFIX = ".saving";

		#endregion

		#region ================== Variables

		private bool targetexisted;
		private bool swapped;
		private bool isdisposed;

		#endregion

		#region ================== Properties

		internal string TargetPath { get; private set; }
		internal string SiblingPath { get; private set; }
		internal string MetadataWarning { get; private set; }
		internal bool IsDisposed { get { return isdisposed; } }

		#endregion

		#region ================== Constructor / Disposer

		// Deletes a leftover sibling: it is a save that never took effect
		internal SiblingFileSwap(string targetpath)
		{
			TargetPath = ResolveRealPath(targetpath);
			SiblingPath = TargetPath + SIBLING_SUFFIX;
			if(File.Exists(SiblingPath)) File.Delete(SiblingPath);
		}

		public void Dispose()
		{
			if(isdisposed) return;
			isdisposed = true;

			// The sibling may be the only copy left when a replace lost the target
			if(swapped || (targetexisted && !File.Exists(TargetPath))) return;

			try
			{
				if(File.Exists(SiblingPath)) File.Delete(SiblingPath);
			}
			catch(IOException) { }
			catch(UnauthorizedAccessException) { }
		}

		#endregion

		#region ================== Methods

		// Gives the sibling the target's mode and owner; call once the sibling is written and closed
		internal void CopyMetadataFromTarget()
		{
			if(File.Exists(TargetPath)) MetadataWarning = CopyMetadata(TargetPath, SiblingPath);
		}

		internal void Swap()
		{
			if(isdisposed) throw new InvalidOperationException("The file swap is already disposed.");

			// Checked here, not earlier: Autosave's rotation moves the target away after the sibling is written
			targetexisted = File.Exists(TargetPath);
			try
			{
				if(targetexisted) File.Replace(SiblingPath, TargetPath, null, true);
				else File.Move(SiblingPath, TargetPath);
			}
			catch(Exception e)
			{
				if(!targetexisted || File.Exists(TargetPath) || !File.Exists(SiblingPath)) throw;

				// The replace lost the target, so the sibling must take its name now: a retry would delete it
				try
				{
					File.Move(SiblingPath, TargetPath);
				}
				catch(Exception)
				{
					throw new IOException("\"" + TargetPath + "\" could not be replaced and is missing. The saved file is \"" + SiblingPath + "\".", e);
				}
			}

			swapped = true;
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
