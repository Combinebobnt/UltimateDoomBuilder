#region ================== Namespaces

using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Reflection;
using System.Security.Authentication;
using System.Text;
using System.Text.RegularExpressions;
using System.Windows.Forms;
using System.Xml;

#endregion

namespace CodeImp.DoomBuilder
{
	// A fork release version, R<upstream revision>-fork.<fork commit count>
	internal struct ForkVersion : IComparable<ForkVersion>
	{
		private static readonly Regex pattern = new Regex(@"^R([0-9]+)-fork\.([0-9]+)\z", RegexOptions.CultureInvariant);

		public readonly int Upstream;
		public readonly int Fork;

		public ForkVersion(int upstream, int fork)
		{
			Upstream = upstream;
			Fork = fork;
		}

		public static bool TryParse(string text, out ForkVersion version)
		{
			version = default(ForkVersion);
			if(text == null) return false;
			Match match = pattern.Match(text);
			int upstream, fork;
			if(!match.Success
				|| !int.TryParse(match.Groups[1].Value, System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out upstream)
				|| !int.TryParse(match.Groups[2].Value, System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out fork))
				return false;
			version = new ForkVersion(upstream, fork);
			return true;
		}

		public int CompareTo(ForkVersion other)
		{
			int result = Upstream.CompareTo(other.Upstream);
			return result != 0 ? result : Fork.CompareTo(other.Fork);
		}

		public override string ToString() { return "R" + Upstream + "-fork." + Fork; }
	}

	internal sealed class ForkRelease
	{
		public string Tag;
		public ForkVersion Version;
		public string PageUrl;
		public string Notes;
	}

	// Update checks for fork release builds, against the fork's GitHub releases feed
	internal static class ForkUpdateChecker
	{
		#region ================== Constants

		private const string ATOM_NS = "http://www.w3.org/2005/Atom";
		private const string LOG_PREFIX = "Fork update check: ";
		private const int TIMEOUT_MS = 15000;
		private static readonly Regex repositorypattern = new Regex(@"^[A-Za-z0-9_.-]+/[A-Za-z0-9_.-]+\z", RegexOptions.CultureInvariant);
		private static readonly string[] appimageenv = { "LD_LIBRARY_PATH", "MONO_CONFIG", "MONO_CFG_DIR" };

		#endregion

		#region ================== Variables

		private static BackgroundWorker worker;
		private static string repository;
		private static bool repositoryread;

		#endregion

		#region ================== Properties

		internal static string Repository
		{
			get
			{
				if(!repositoryread)
				{
					foreach(AssemblyMetadataAttribute attr in typeof(ForkUpdateChecker).Assembly.GetCustomAttributes<AssemblyMetadataAttribute>())
					{
						if(attr.Key == "UpdateRepository" && attr.Value != null && repositorypattern.IsMatch(attr.Value))
							repository = attr.Value;
					}
					repositoryread = true;
				}
				return repository;
			}
		}

		internal static bool IsForkBuild
		{
			get
			{
				ForkVersion version;
				return !string.IsNullOrEmpty(Repository) && ForkVersion.TryParse(General.VersionLabel, out version);
			}
		}

		private static string ReleasesPageUrl { get { return "https://github.com/" + Repository + "/releases"; } }

		#endregion

		#region ================== Feed parsing

		internal static List<ForkRelease> ParseFeed(string xml)
		{
			XmlReaderSettings settings = new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null };
			XmlDocument doc = new XmlDocument { XmlResolver = null };
			using(XmlReader reader = XmlReader.Create(new StringReader(xml), settings)) doc.Load(reader);

			XmlNamespaceManager ns = new XmlNamespaceManager(doc.NameTable);
			ns.AddNamespace("a", ATOM_NS);
			if(doc.DocumentElement == null || doc.DocumentElement.LocalName != "feed" || doc.DocumentElement.NamespaceURI != ATOM_NS)
				throw new FormatException("not an Atom feed");

			List<ForkRelease> releases = new List<ForkRelease>();
			foreach(XmlNode entry in doc.DocumentElement.SelectNodes("a:entry", ns))
			{
				XmlNode id = entry.SelectSingleNode("a:id", ns);
				if(id == null) continue;
				string tag = id.InnerText.Trim();
				tag = tag.Substring(tag.LastIndexOf('/') + 1);

				ForkVersion version;
				if(!ForkVersion.TryParse(tag, out version)) continue;

				XmlNode link = entry.SelectSingleNode("a:link[@rel='alternate']/@href", ns);
				XmlNode content = entry.SelectSingleNode("a:content", ns);
				releases.Add(new ForkRelease
				{
					Tag = tag,
					Version = version,
					PageUrl = link != null ? link.Value : null,
					Notes = content != null ? HtmlToText(content.InnerText) : string.Empty,
				});
			}
			return releases;
		}

		private const string BLOCK_TAGS = "p|ul|ol|li|h[1-6]|div|details|summary|pre|blockquote|br";

		internal static string HtmlToText(string html)
		{
			if(string.IsNullOrEmpty(html)) return string.Empty;

			// HTML whitespace is one space, and none at all next to a block tag
			string text = Regex.Replace(html, @"\s+", " ");
			text = Regex.Replace(text, @"\s*(</?(?:" + BLOCK_TAGS + @")\b[^>]*>)\s*", "$1", RegexOptions.IgnoreCase);

			text = Regex.Replace(text, @"(<li\b[^>]*>)<p\b[^>]*>", "$1", RegexOptions.IgnoreCase);
			text = Regex.Replace(text, @"</p>(</li>)", "$1", RegexOptions.IgnoreCase);
			text = Regex.Replace(text, @"<li\b[^>]*>", "\n- ", RegexOptions.IgnoreCase);
			text = Regex.Replace(text, @"<br\b[^>]*>", "\n", RegexOptions.IgnoreCase);
			text = Regex.Replace(text, @"</(?:p|ul|ol|h[1-6]|div|details|summary|pre|blockquote)\s*>", "\n\n", RegexOptions.IgnoreCase);
			text = Regex.Replace(text, @"<(?:p|ul|ol|h[1-6]|div|details|summary|pre|blockquote)\b[^>]*>", "\n", RegexOptions.IgnoreCase);
			text = Regex.Replace(text, @"<[^>]*>", string.Empty);
			text = WebUtility.HtmlDecode(text);

			return CollapseBlankLines(text.Split('\n'));
		}

		private static string CollapseBlankLines(IEnumerable<string> lines)
		{
			StringBuilder sb = new StringBuilder();
			bool pendingblank = false;
			foreach(string raw in lines)
			{
				string line = raw.Trim();
				if(line.Length == 0)
				{
					pendingblank = sb.Length > 0;
					continue;
				}
				if(sb.Length > 0) sb.Append(pendingblank ? "\n\n" : "\n");
				sb.Append(line);
				pendingblank = false;
			}
			return sb.ToString();
		}

		#endregion

		#region ================== Update selection

		// The newest release newer than local, or null. The startup check also skips one the user ignored
		internal static ForkRelease SelectUpdate(IEnumerable<ForkRelease> releases, ForkVersion local, ForkVersion? ignored, bool verbose)
		{
			ForkRelease newest = null;
			foreach(ForkRelease release in releases)
				if(newest == null || release.Version.CompareTo(newest.Version) > 0) newest = release;

			if(newest == null || newest.Version.CompareTo(local) <= 0) return null;
			if(!verbose && ignored.HasValue && newest.Version.CompareTo(ignored.Value) <= 0) return null;
			return newest;
		}

		// Notes of every release newer than local, newest first, without the release body's fixed header and footer
		internal static string BuildNotes(IEnumerable<ForkRelease> releases, ForkVersion local)
		{
			List<ForkRelease> newer = new List<ForkRelease>();
			foreach(ForkRelease release in releases)
				if(release.Version.CompareTo(local) > 0) newer.Add(release);
			newer.Sort((a, b) => b.Version.CompareTo(a.Version));

			List<string> sections = new List<string>();
			foreach(ForkRelease release in newer)
			{
				List<string> lines = new List<string> { release.Tag, "" };
				bool footer = false;
				foreach(string line in (release.Notes ?? string.Empty).Split('\n'))
				{
					string trimmed = line.Trim();
					if(trimmed.TrimEnd(':') == "Changes since the previous fork release") continue;
					if(trimmed.StartsWith("The Linux AppImage bundles Mono", StringComparison.Ordinal)) footer = true;
					else if(trimmed.Length == 0) footer = false;
					if(!footer) lines.Add(line);
				}
				sections.Add(CollapseBlankLines(lines));
			}
			return string.Join("\n\n", sections);
		}

		#endregion

		#region ================== Check

		private sealed class CheckFailure : Exception
		{
			public readonly string Kind;
			public CheckFailure(string kind, string message) : base(message) { Kind = kind; }
		}

		private sealed class CheckArgs
		{
			public bool Verbose;
			public string Label;
			public string Ignored;
		}

		internal static void PerformCheck(bool verbose)
		{
			if(worker != null && worker.IsBusy)
			{
				if(verbose) General.ShowWarningMessage("Update check is already running!", MessageBoxButtons.OK);
				return;
			}

			worker = new BackgroundWorker();
			worker.DoWork += DoWork;
			worker.RunWorkerCompleted += delegate { worker = null; };
			worker.RunWorkerAsync(new CheckArgs { Verbose = verbose, Label = General.VersionLabel, Ignored = General.Settings.IgnoredForkRelease });
		}

		private static void DoWork(object sender, DoWorkEventArgs e)
		{
			CheckArgs args = (CheckArgs)e.Argument;
			Action show = null;
			try
			{
				ForkVersion local;
				if(!ForkVersion.TryParse(args.Label, out local)) return;
				ForkVersion ignoredversion;
				ForkVersion? ignored = ForkVersion.TryParse(args.Ignored, out ignoredversion) ? ignoredversion : (ForkVersion?)null;

				string via;
				string xml = Fetch(args.Label, out via);
				List<ForkRelease> releases;
				try { releases = ParseFeed(xml); }
				catch(Exception ex) { throw new CheckFailure("feed", ex.Message); }

				if(releases.Count == 0)
					General.WriteLogLine(LOG_PREFIX + "local " + local + ", no fork releases via " + via);
				else
					General.WriteLogLine(LOG_PREFIX + "local " + local + ", latest " + MaxVersion(releases) + " via " + via);

				ForkRelease update = SelectUpdate(releases, local, ignored, args.Verbose);
				if(update != null)
				{
					string notes = BuildNotes(releases, local);
					show = () => General.MainWindow.ForkUpdateAvailable(update, notes);
				}
				else if(args.Verbose)
				{
					string message = "You have the latest fork release (" + local + ").";
					show = () => MessageBox.Show(General.MainWindow, message, Application.ProductName, MessageBoxButtons.OK, MessageBoxIcon.Information);
				}
			}
			catch(Exception ex)
			{
				CheckFailure failure = ex as CheckFailure;
				string kind = failure != null ? failure.Kind : "feed";
				General.WriteLogLine(LOG_PREFIX + "failed: " + kind + ": " + ex.Message);
				if(args.Verbose)
				{
					string message = "Update check failed: " + ex.Message + "\n\nOpen the fork's releases page instead?";
					show = () =>
					{
						if(General.ShowWarningMessage(message, MessageBoxButtons.YesNo, MessageBoxDefaultButton.Button1, false) == DialogResult.Yes)
							OpenReleasePage(ReleasesPageUrl);
					};
				}
			}

			// The editor may be closing by now
			if(show == null) return;
			try { General.MainWindow.RunOnUIThread(show); }
			catch(Exception ex) { General.WriteLogLine("Fork update result not shown: " + ex.Message); }
		}

		private static ForkVersion MaxVersion(List<ForkRelease> releases)
		{
			ForkVersion max = releases[0].Version;
			foreach(ForkRelease release in releases)
				if(release.Version.CompareTo(max) > 0) max = release.Version;
			return max;
		}

		private static string Fetch(string label, out string via)
		{
			string feed = Environment.GetEnvironmentVariable("UDB_UPDATE_FEED");
			if(string.IsNullOrEmpty(feed)) feed = "https://github.com/" + Repository + "/releases.atom";

			if(!feed.StartsWith("https://", StringComparison.OrdinalIgnoreCase) && !feed.StartsWith("http://", StringComparison.OrdinalIgnoreCase))
			{
				via = "file";
				try { return File.ReadAllText(feed); }
				catch(Exception ex) { throw new CheckFailure("feed", ex.Message); }
			}

			string curlerror;
			if(Environment.GetEnvironmentVariable("UDB_UPDATE_FORCE_CURL") == "1")
			{
				via = "curl";
				string forced = CurlGet(feed, label, out curlerror);
				if(forced == null) throw new CheckFailure("tls", curlerror);
				return forced;
			}

			try
			{
				via = "http";
				return HttpGet(feed, label);
			}
			catch(Exception ex)
			{
				// Anything but a WebException may be the TLS provider failing to load, so it gets curl too
				WebException webex = ex as WebException;
				bool unix = Environment.OSVersion.Platform == PlatformID.Unix;
				if(webex != null ? !IsTlsFailure(webex) : !unix) throw new CheckFailure("network", ex.Message);
				if(unix)
				{
					via = "curl";
					string result = CurlGet(feed, label, out curlerror);
					if(result != null) return result;
					throw new CheckFailure("tls", ex.Message + "; " + curlerror);
				}
				throw new CheckFailure("tls", ex.Message);
			}
		}

		private static bool IsTlsFailure(WebException ex)
		{
			if(ex.Status == WebExceptionStatus.TrustFailure || ex.Status == WebExceptionStatus.SecureChannelFailure) return true;
			for(Exception inner = ex.InnerException; inner != null; inner = inner.InnerException)
				if(inner is AuthenticationException) return true;
			return false;
		}

		private static string HttpGet(string url, string label)
		{
			if(ServicePointManager.SecurityProtocol != SecurityProtocolType.SystemDefault)
				ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12;
			HttpWebRequest request = (HttpWebRequest)WebRequest.Create(url);
			request.UserAgent = "UltimateDoomBuilder/" + label;
			request.Timeout = TIMEOUT_MS;
			request.ReadWriteTimeout = TIMEOUT_MS;
			using(WebResponse response = request.GetResponse())
			using(StreamReader reader = new StreamReader(response.GetResponseStream(), Encoding.UTF8))
				return reader.ReadToEnd();
		}

		// Returns null and sets error when curl is missing or fails
		private static string CurlGet(string url, string label, out string error)
		{
			error = null;
			if(url.IndexOf('"') >= 0) { error = "unusable feed URL"; return null; }

			ProcessStartInfo info = new ProcessStartInfo("curl",
				"-fsSL --proto =https,http --max-time " + (TIMEOUT_MS / 1000) + " -A \"UltimateDoomBuilder/" + label + "\" \"" + url + "\"")
			{
				UseShellExecute = false,
				CreateNoWindow = true,
				RedirectStandardOutput = true,
				RedirectStandardError = true,
				StandardOutputEncoding = Encoding.UTF8,
			};
			CleanAppImageEnvironment(info);

			try
			{
				using(Process process = Process.Start(info))
				{
					var stderr = process.StandardError.ReadToEndAsync();
					string output = process.StandardOutput.ReadToEnd();
					process.WaitForExit();
					if(process.ExitCode == 0) return output;
					error = "curl exit " + process.ExitCode + ": " + stderr.Result.Trim();
					return null;
				}
			}
			catch(Exception ex)
			{
				error = "curl: " + ex.Message;
				return null;
			}
		}

		// A child of the AppImage must not inherit the image's library and Mono paths
		internal static void CleanAppImageEnvironment(ProcessStartInfo info)
		{
			if(string.IsNullOrEmpty(Environment.GetEnvironmentVariable("APPDIR"))) return;
			foreach(string name in appimageenv) info.EnvironmentVariables.Remove(name);
		}

		internal static void OpenReleasePage(string url)
		{
			if(url == null || !url.StartsWith(ReleasesPageUrl + "/", StringComparison.Ordinal)) url = ReleasesPageUrl;
			try
			{
				if(Environment.OSVersion.Platform == PlatformID.Unix)
				{
					ProcessStartInfo info = new ProcessStartInfo("xdg-open", "\"" + url.Replace("\"", "%22") + "\"") { UseShellExecute = false };
					CleanAppImageEnvironment(info);
					Process.Start(info);
				}
				else
				{
					Process.Start(url);
				}
			}
			catch(Exception ex)
			{
				General.ShowWarningMessage("Could not open a browser: " + ex.Message + "\n\n" + url, MessageBoxButtons.OK);
			}
		}

		#endregion
	}
}
