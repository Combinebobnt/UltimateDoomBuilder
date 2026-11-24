#region ================== Namespaces

using System;
using System.IO;

#endregion

namespace CodeImp.DoomBuilder
{
	// Maps a reference manual page name to a file of the HTML manual
	internal static class HelpPaths
	{
		internal const string FALLBACK_PAGE = "introduction.html";
		internal const string INDEX_PAGE = "index.html";

		// Returns the page's full path inside helpdir, else the first fallback page that exists, else null
		internal static string Resolve(string helpdir, string page, out bool missing)
		{
			string root = Root(helpdir);
			string path = Inside(root, page);
			missing = (path == null || !File.Exists(path));
			if(!missing) return path;

			foreach(string fallback in new[] { FALLBACK_PAGE, INDEX_PAGE })
			{
				path = Path.Combine(root, fallback);
				if(File.Exists(path)) return path;
			}

			return null;
		}

		internal static string ToUri(string path)
		{
			return new Uri(path).AbsoluteUri;
		}

		// Returns the existing file an HTTP request path names inside helpdir, else null; no fallback pages
		internal static string ResolveUrlPath(string helpdir, string urlpath)
		{
			if(string.IsNullOrEmpty(urlpath) || urlpath[0] != '/') return null;
			int end = urlpath.IndexOfAny(new[] { '?', '#' });
			if(end >= 0) urlpath = urlpath.Substring(0, end);

			string path = Inside(Root(helpdir), Uri.UnescapeDataString(urlpath));
			return (path != null && File.Exists(path)) ? path : null;
		}

		// Returns the URL of a file inside helpdir under a help server's base URL, else null
		internal static string ToHttpUri(string baseurl, string helpdir, string path)
		{
			string root = Root(helpdir);
			string full = Path.GetFullPath(path);
			if(!full.StartsWith(root, StringComparison.Ordinal)) return null;

			string[] segments = full.Substring(root.Length).Split(Path.DirectorySeparatorChar);
			return baseurl.TrimEnd('/') + "/" + string.Join("/", Array.ConvertAll(segments, Uri.EscapeDataString));
		}

		private static string Root(string helpdir)
		{
			return Path.GetFullPath(helpdir).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
		}

		// Returns null for an empty page or one that resolves outside root
		private static string Inside(string root, string page)
		{
			if(string.IsNullOrEmpty(page)) return null;
			string relative = page.Replace('\\', '/').TrimStart('/').Replace('/', Path.DirectorySeparatorChar);
			if(relative.Length == 0) return null;

			string full;
			try { full = Path.GetFullPath(Path.Combine(root, relative)); }
			catch(Exception) { return null; }

			return full.StartsWith(root, StringComparison.Ordinal) ? full : null;
		}
	}
}
