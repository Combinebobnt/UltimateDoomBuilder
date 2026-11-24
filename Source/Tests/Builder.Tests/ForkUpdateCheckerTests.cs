using System.Collections.Generic;
using CodeImp.DoomBuilder;

namespace Builder.Tests
{
	public class ForkUpdateCheckerTests
	{
		private static ForkVersion V(string text)
		{
			ForkVersion version;
			Assert.True(ForkVersion.TryParse(text, out version), text);
			return version;
		}

		private static string Entry(string tag, string html)
		{
			return "<entry><id>tag:github.com,2008:Repository/1/" + tag + "</id>"
				+ "<link rel=\"alternate\" type=\"text/html\" href=\"https://github.com/owner/repo/releases/tag/" + tag + "\"/>"
				+ "<title>Ultimate Doom Builder " + tag + "</title>"
				+ "<content type=\"html\">" + System.Security.SecurityElement.Escape(html) + "</content></entry>";
		}

		private static string Feed(params string[] entries)
		{
			return "<?xml version=\"1.0\" encoding=\"UTF-8\"?><feed xmlns=\"http://www.w3.org/2005/Atom\" xml:lang=\"en-US\">"
				+ "<id>tag:github.com,2008:https://github.com/owner/repo/releases</id><title>Release notes from repo</title>"
				+ string.Concat(entries) + "</feed>";
		}

		private static ForkRelease Release(string tag, string notes = "")
		{
			return new ForkRelease { Tag = tag, Version = V(tag), PageUrl = "https://example.invalid/" + tag, Notes = notes };
		}

		[Fact]
		public void VersionParseAcceptsOnlyTheExactLabelShape()
		{
			ForkVersion version = V("R3800-fork.12");
			Assert.Equal(3800, version.Upstream);
			Assert.Equal(12, version.Fork);
			Assert.Equal("R3800-fork.12", version.ToString());

			List<string> wrongly = new List<string>();
			foreach(string bad in new[] { "R3800", "R3800-fork.", "R3800-fork.x", "DEVBUILD", "R3800-fork.12 ", "R3800-fork.12\n", " R3800-fork.12", "", null, "R99999999999-fork.1" })
			{
				ForkVersion ignored;
				if(ForkVersion.TryParse(bad, out ignored)) wrongly.Add(bad ?? "<null>");
			}
			Assert.Empty(wrongly);
		}

		[Fact]
		public void VersionsOrderByUpstreamThenFork()
		{
			Assert.True(V("R3800-fork.12").CompareTo(V("R3800-fork.13")) < 0);
			Assert.True(V("R3800-fork.13").CompareTo(V("R3801-fork.3")) < 0);
			Assert.True(V("R3801-fork.3").CompareTo(V("R3801-fork.3")) == 0);
			Assert.True(V("R3800-fork.100").CompareTo(V("R3800-fork.99")) > 0);
		}

		[Fact]
		public void FeedParseKeepsForkEntriesAndSkipsOthers()
		{
			string xml = Feed(
				Entry("R3800-fork.12", "<p>Older</p>"),
				Entry("v2.0", "<p>Not ours</p>"),
				Entry("R3801-fork.3", "<p>Newer</p>"));
			List<ForkRelease> releases = ForkUpdateChecker.ParseFeed(xml);

			Assert.Equal(2, releases.Count);
			Assert.Equal("R3800-fork.12", releases[0].Tag);
			Assert.Equal("https://github.com/owner/repo/releases/tag/R3800-fork.12", releases[0].PageUrl);
			Assert.Equal("Older", releases[0].Notes);
			Assert.Equal("R3801-fork.3", releases[1].Tag);
			Assert.Empty(ForkUpdateChecker.ParseFeed(Feed()));
		}

		[Fact]
		public void FeedParseRejectsSomethingThatIsNotAnAtomFeed()
		{
			bool threw = false;
			try { ForkUpdateChecker.ParseFeed("<html><body>rate limited</body></html>"); }
			catch(System.FormatException) { threw = true; }
			Assert.True(threw, "non-Atom document");
		}

		[Fact]
		public void NotesHtmlBecomesPlainText()
		{
			string html = "<p>Based on upstream R3800.</p>\n<details>\n<summary>Changes since the previous fork release</summary>\n"
				+ "<ul>\n<li>Fix <code>A &amp; B</code> in <a href=\"https://x/\">the <em>browser</em></a></li>\n<li>Second &lt;item&gt;</li>\n</ul>\n</details>\n"
				+ "<p>Line one<br>line two</p>";
			Assert.Equal("Based on upstream R3800.\n\nChanges since the previous fork release\n\n- Fix A & B in the browser\n- Second <item>\n\nLine one\nline two",
				ForkUpdateChecker.HtmlToText(html));
			Assert.Equal("- loose one\n- loose two", ForkUpdateChecker.HtmlToText("<ul>\n<li>\n<p>loose one</p>\n</li>\n<li><p>loose two</p></li>\n</ul>"));
		}

		[Fact]
		public void SelectUpdateHonoursLocalAndIgnoredVersions()
		{
			List<ForkRelease> releases = new List<ForkRelease> { Release("R3800-fork.12"), Release("R3801-fork.3"), Release("R3800-fork.13") };
			ForkVersion? none = null;

			Assert.Equal("R3801-fork.3", ForkUpdateChecker.SelectUpdate(releases, V("R3800-fork.12"), none, false).Tag);
			Assert.Null(ForkUpdateChecker.SelectUpdate(releases, V("R3801-fork.3"), none, true));
			Assert.Null(ForkUpdateChecker.SelectUpdate(releases, V("R3802-fork.1"), none, true));
			Assert.Null(ForkUpdateChecker.SelectUpdate(new List<ForkRelease>(), V("R3800-fork.1"), none, true));

			Assert.Null(ForkUpdateChecker.SelectUpdate(releases, V("R3800-fork.12"), V("R3801-fork.3"), false));
			Assert.Equal("R3801-fork.3", ForkUpdateChecker.SelectUpdate(releases, V("R3800-fork.12"), V("R3801-fork.3"), true).Tag);
			Assert.Equal("R3801-fork.3", ForkUpdateChecker.SelectUpdate(releases, V("R3800-fork.12"), V("R3800-fork.13"), false).Tag);
		}

		[Fact]
		public void BuildNotesListsNewerReleasesNewestFirstWithoutBoilerplate()
		{
			const string footer = "The Linux AppImage bundles Mono and libgdiplus. Windows zips need .NET Framework 4.7.2 (included in Windows 10 and 11).";
			List<ForkRelease> releases = new List<ForkRelease>
			{
				Release("R3800-fork.12", "Based on upstream R3800.\n\nChanges since the previous fork release\n\n- old\n\n" + footer),
				Release("R3800-fork.13", "Based on upstream R3800.\n\nChanges since the previous fork release:\n\n- middle\n\n" + footer),
				Release("R3801-fork.3", "Based on upstream R3801.\n\nChanges since the previous fork release\n\n- new one\n- new two\n\n" + footer),
			};

			Assert.Equal("R3801-fork.3\n\nBased on upstream R3801.\n\n- new one\n- new two\n\nR3800-fork.13\n\nBased on upstream R3800.\n\n- middle",
				ForkUpdateChecker.BuildNotes(releases, V("R3800-fork.12")));
		}
	}
}
