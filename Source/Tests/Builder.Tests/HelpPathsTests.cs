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
	}
}
