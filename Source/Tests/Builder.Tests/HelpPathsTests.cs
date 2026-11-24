using CodeImp.DoomBuilder;

namespace Builder.Tests
{
	// Mapping a reference manual page name to a file of the HTML manual
	public class HelpPathsTests : IDisposable
	{
		private readonly string dir = Path.Combine(Path.GetTempPath(), "udb-help " + Guid.NewGuid().ToString("N"));

		public HelpPathsTests()
		{
			Directory.CreateDirectory(Path.Combine(dir, "gzdb", "features"));
			File.WriteAllText(Path.Combine(dir, "e_visual.html"), "");
			File.WriteAllText(Path.Combine(dir, "gzdb", "features", "mode.html"), "");
		}

		public void Dispose()
		{
			Directory.Delete(dir, true);
		}

		private string Resolve(string page, out bool missing)
		{
			return HelpPaths.Resolve(dir, page, out missing);
		}

		[Fact]
		public void PageNamesResolveInsideTheHelpDirectory()
		{
			string mode = Path.Combine(dir, "gzdb", "features", "mode.html");
			foreach(string page in new[] { "gzdb/features/mode.html", "/gzdb/features/mode.html", "gzdb\\features\\mode.html", "\\gzdb/features/mode.html" })
			{
				bool missing;
				Assert.Equal(mode, Resolve(page, out missing));
				Assert.False(missing, page);
			}
		}

		[Fact]
		public void MissingPageFallsBackToIntroductionThenIndex()
		{
			bool missing;
			Assert.Null(Resolve("nope.html", out missing));
			Assert.True(missing);

			File.WriteAllText(Path.Combine(dir, HelpPaths.INDEX_PAGE), "");
			Assert.Equal(Path.Combine(dir, HelpPaths.INDEX_PAGE), Resolve("nope.html", out missing));
			Assert.True(missing);

			File.WriteAllText(Path.Combine(dir, HelpPaths.FALLBACK_PAGE), "");
			foreach(string page in new[] { "nope.html", "", null, "/", "gzdb" })
			{
				Assert.Equal(Path.Combine(dir, HelpPaths.FALLBACK_PAGE), Resolve(page, out missing));
				Assert.True(missing, page ?? "null");
			}
		}

		[Fact]
		public void PagesOutsideTheHelpDirectoryAreRefused()
		{
			string outside = Path.Combine(Path.GetDirectoryName(dir), Path.GetFileName(dir) + "-sibling.html");
			File.WriteAllText(outside, "");
			try
			{
				// The absolute path only escapes on Windows; on Unix its leading slash is trimmed like any page's
				foreach(string page in new[] { "../" + Path.GetFileName(outside), "gzdb/../../" + Path.GetFileName(outside), outside })
				{
					bool missing;
					Assert.Null(Resolve(page, out missing));
					Assert.True(missing, page);
				}
			}
			finally
			{
				File.Delete(outside);
			}
		}

		[Fact]
		public void UriEscapesSpacesAndRoundTrips()
		{
			bool missing;
			string path = Resolve("e_visual.html", out missing);
			string uri = HelpPaths.ToUri(path);

			Assert.StartsWith("file:///", uri);
			Assert.Contains("%20", uri);
			Assert.DoesNotContain(" ", uri);
			Assert.Equal(path, new Uri(uri).LocalPath);
		}

		[Fact]
		public void UrlPathsResolveExactlyWithoutFallback()
		{
			File.WriteAllText(Path.Combine(dir, HelpPaths.FALLBACK_PAGE), "");
			Directory.CreateDirectory(Path.Combine(dir, "with space"));
			File.WriteAllText(Path.Combine(dir, "with space", "p.html"), "");

			Assert.Equal(Path.Combine(dir, "gzdb", "features", "mode.html"), HelpPaths.ResolveUrlPath(dir, "/gzdb/features/mode.html?x=1#top"));
			Assert.Equal(Path.Combine(dir, "with space", "p.html"), HelpPaths.ResolveUrlPath(dir, "/with%20space/p.html"));
			foreach(string urlpath in new[] { "/nope.html", "/", "", null, "/gzdb", "e_visual.html" })
				Assert.Null(HelpPaths.ResolveUrlPath(dir, urlpath));
		}

		[Fact]
		public void UrlPathsOutsideTheHelpDirectoryAreRefused()
		{
			string name = Path.GetFileName(dir) + "-sibling.html";
			string outside = Path.Combine(Path.GetDirectoryName(dir), name);
			File.WriteAllText(outside, "");
			try
			{
				foreach(string urlpath in new[] { "/../" + name, "/%2e%2e/" + name, "/%2E%2E%2F" + name, "/gzdb%2f..%2f..%2f" + name, "/..\\" + name, "/gzdb/..%5c..%5c" + name })
					Assert.True(HelpPaths.ResolveUrlPath(dir, urlpath) == null, urlpath);
			}
			finally
			{
				File.Delete(outside);
			}
		}

		[Fact]
		public void HttpUrisEscapeEachSegmentUnderTheBaseUrl()
		{
			Directory.CreateDirectory(Path.Combine(dir, "with space"));
			string spaced = Path.Combine(dir, "with space", "p q.html");
			File.WriteAllText(spaced, "");

			Assert.Equal("http://127.0.0.1:5/e_visual.html", HelpPaths.ToHttpUri("http://127.0.0.1:5/", dir, Path.Combine(dir, "e_visual.html")));
			Assert.Equal("http://127.0.0.1:5/gzdb/features/mode.html", HelpPaths.ToHttpUri("http://127.0.0.1:5/", dir + Path.DirectorySeparatorChar, Path.Combine(dir, "gzdb", "features", "mode.html")));
			Assert.Equal("http://127.0.0.1:5/with%20space/p%20q.html", HelpPaths.ToHttpUri("http://127.0.0.1:5/", dir, spaced));
			Assert.Null(HelpPaths.ToHttpUri("http://127.0.0.1:5/", Path.Combine(dir, "gzdb"), Path.Combine(dir, "e_visual.html")));

			// What the server reads back from the URL is the same file
			Assert.Equal(spaced, HelpPaths.ResolveUrlPath(dir, new Uri(HelpPaths.ToHttpUri("http://127.0.0.1:5/", dir, spaced)).AbsolutePath));
		}
	}
}
