using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.ExceptionServices;

namespace CodeImp.DoomBuilder.Data
{
	internal sealed class ResourceFingerprint
	{
		internal static readonly StringComparer PathComparer = Path.DirectorySeparatorChar == '\\' ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;
		internal readonly string PathName;
		private readonly long length;
		private readonly DateTime modified;
		private readonly FileAttributes attributes;
		private readonly DataLocation location;

		private ResourceFingerprint(DataLocation location, FileInfo file)
		{
			this.location = location;
			PathName = file.FullName;
			length = file.Length;
			modified = file.LastWriteTimeUtc;
			attributes = file.Attributes;
		}

		internal static ResourceFingerprint Capture(DataLocation location)
		{
			if(location.type != DataLocation.RESOURCE_WAD && location.type != DataLocation.RESOURCE_PK3) return null;
			try
			{
				if(string.Equals(Path.GetExtension(location.location), ".pk7", StringComparison.OrdinalIgnoreCase)) return null;
				FileInfo file = new FileInfo(location.location);
				return file.Exists ? new ResourceFingerprint(location, file) : null;
			}
			catch(IOException) { return null; }
			catch(UnauthorizedAccessException) { return null; }
			catch(ArgumentException) { return null; }
			catch(NotSupportedException) { return null; }
		}

		internal bool Matches(ResourceFingerprint other)
		{
			return other != null && PathComparer.Equals(PathName, other.PathName)
				&& length == other.length && modified == other.modified && attributes == other.attributes
				&& location.type == other.location.type && location.option1 == other.location.option1
				&& location.option2 == other.location.option2 && location.notfortesting == other.location.notfortesting;
		}
	}

	internal sealed class ResourceReaderPool : IDisposable
	{
		private readonly Dictionary<string, DataReader> readers = new Dictionary<string, DataReader>(ResourceFingerprint.PathComparer);

		internal bool TryAdd(DataReader reader, string mapfile)
		{
			ResourceFingerprint fingerprint = reader.Fingerprint;
			if(reader.IsDisposed || reader.IsSuspended || !reader.IsReadOnly || fingerprint == null
				|| !fingerprint.Matches(ResourceFingerprint.Capture(reader.Location))) return false;
			if(!string.IsNullOrEmpty(mapfile) && ResourceFingerprint.PathComparer.Equals(fingerprint.PathName, Path.GetFullPath(mapfile))) return false;
			if(readers.ContainsKey(fingerprint.PathName)) return false;
			readers.Add(fingerprint.PathName, reader);
			return true;
		}

		internal bool TryTake(DataLocation location, out DataReader reader)
		{
			reader = null;
			ResourceFingerprint fingerprint = ResourceFingerprint.Capture(location);
			DataReader candidate;
			if(fingerprint == null || !readers.TryGetValue(fingerprint.PathName, out candidate)
				|| !candidate.Fingerprint.Matches(fingerprint)) return false;
			readers.Remove(fingerprint.PathName);
			try { candidate.PrepareForReload(); }
			catch { candidate.Dispose(); throw; }
			reader = candidate;
			return true;
		}

		public void Dispose()
		{
			Exception failure = null;
			foreach(DataReader reader in readers.Values)
			{
				try { reader.Dispose(); }
				catch(Exception e) { if(failure == null) failure = e; }
			}
			readers.Clear();
			if(failure != null) ExceptionDispatchInfo.Capture(failure).Throw();
		}
	}
}
