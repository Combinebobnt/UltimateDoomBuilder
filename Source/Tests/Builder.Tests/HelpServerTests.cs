using System.Net;
using System.Net.Sockets;
using System.Text;
using CodeImp.DoomBuilder;

namespace Builder.Tests
{
	// The loopback HTTP server for the HTML manual; UDB only starts it on Unix, so the tests skip elsewhere
	public class HelpServerTests : IDisposable
	{
		private const string SECRET = "outside the help root";
		private readonly string parent = Path.Combine(Path.GetTempPath(), "udb-helpserver " + Guid.NewGuid().ToString("N"));
		private readonly string root;

		private static bool Unix { get { return Environment.OSVersion.Platform == PlatformID.Unix; } }

		public HelpServerTests()
		{
			root = Path.Combine(parent, "Help");
			Directory.CreateDirectory(Path.Combine(root, "sub"));
			File.WriteAllText(Path.Combine(root, "a.html"), "<p>a</p>");
			File.WriteAllText(Path.Combine(root, "sub", "b.css"), "p { }");
			File.WriteAllBytes(Path.Combine(root, "sub", "c d.png"), new byte[] { 0x89, 0x50, 0x4E, 0x47, 0, 1, 2 });
			File.WriteAllText(Path.Combine(parent, "outside.txt"), SECRET);
		}

		public void Dispose()
		{
			HelpServer.Stop();
			Directory.Delete(parent, true);
		}

		// http_proxy may be set in the environment and must not see loopback requests
		private static WebClient Client()
		{
			return new WebClient { Proxy = null };
		}

		// Sends a raw request line, since WebClient and Uri collapse dot segments before sending
		private static string Raw(string baseurl, string requestline)
		{
			int port = new Uri(baseurl).Port;
			using(TcpClient client = new TcpClient())
			{
				client.ReceiveTimeout = 5000;
				client.SendTimeout = 5000;
				client.Connect(IPAddress.Loopback, port);
				NetworkStream stream = client.GetStream();
				byte[] request = Encoding.ASCII.GetBytes(requestline + "\r\nHost: 127.0.0.1:" + port + "\r\nContent-Length: 0\r\nConnection: close\r\n\r\n");
				stream.Write(request, 0, request.Length);

				MemoryStream response = new MemoryStream();
				stream.CopyTo(response);
				return Encoding.ASCII.GetString(response.ToArray());
			}
		}

		private static string Body(string response)
		{
			int start = response.IndexOf("\r\n\r\n", StringComparison.Ordinal);
			return start < 0 ? null : response.Substring(start + 4);
		}

		[Fact]
		public void ServesFilesWithTheirTypeAndNosniff()
		{
			if(!Unix) return;
			string baseurl = HelpServer.Start(root);
			Assert.StartsWith("http://127.0.0.1:", baseurl);
			Assert.Equal(baseurl, HelpServer.Start(root));

			string[][] cases =
			{
				new[] { "a.html", "<p>a</p>", "text/html" },
				new[] { "sub/b.css", "p { }", "text/css" },
			};
			foreach(string[] c in cases)
			{
				using(WebClient client = Client())
				{
					Assert.Equal(c[1], client.DownloadString(baseurl + c[0]));
					Assert.Equal(c[2], client.ResponseHeaders["Content-Type"]);
					Assert.Equal("nosniff", client.ResponseHeaders["X-Content-Type-Options"]);
				}
			}

			using(WebClient client = Client())
			{
				byte[] png = client.DownloadData(HelpPaths.ToHttpUri(baseurl, root, Path.Combine(root, "sub", "c d.png")));
				Assert.Equal(Convert.ToBase64String(File.ReadAllBytes(Path.Combine(root, "sub", "c d.png"))), Convert.ToBase64String(png));
				Assert.Equal("image/png", client.ResponseHeaders["Content-Type"]);
			}
		}

		[Fact]
		public void MissingFilesAndDirectoriesGive404()
		{
			if(!Unix) return;
			string baseurl = HelpServer.Start(root);
			foreach(string page in new[] { "nope.html", "sub", "", "sub/" })
			{
				using(WebClient client = Client())
				{
					WebException ex = Assert.Throws<WebException>(() => client.DownloadString(baseurl + page));
					Assert.Equal(HttpStatusCode.NotFound, ((HttpWebResponse)ex.Response).StatusCode);
				}
			}
		}

		[Fact]
		public void OnlyGetAndHeadAreAllowed()
		{
			if(!Unix) return;
			string baseurl = HelpServer.Start(root);

			foreach(string method in new[] { "POST", "PUT", "DELETE" })
			{
				string response = Raw(baseurl, method + " /a.html HTTP/1.1");
				Assert.StartsWith("HTTP/1.1 405", response);
				Assert.DoesNotContain("<p>a</p>", response);
			}

			string head = Raw(baseurl, "HEAD /a.html HTTP/1.1");
			Assert.StartsWith("HTTP/1.1 200", head);
			Assert.Contains("Content-Length: 8", head);
			Assert.Equal("", Body(head));
		}

		[Fact]
		public void EscapesFromTheHelpRootGive404()
		{
			if(!Unix) return;
			string baseurl = HelpServer.Start(root);
			Assert.StartsWith("HTTP/1.1 200", Raw(baseurl, "GET /a.html HTTP/1.1"));

			foreach(string target in new[] { "/../outside.txt", "/%2e%2e/outside.txt", "/sub%2f..%2f..%2foutside.txt", "/sub/..%5c..%5coutside.txt" })
			{
				string response = Raw(baseurl, "GET " + target + " HTTP/1.1");
				Assert.True(response.StartsWith("HTTP/1.1 404", StringComparison.Ordinal), target + ": " + response);
				Assert.DoesNotContain(SECRET, response);
			}
		}

		[Fact]
		public void StopClosesThePort()
		{
			if(!Unix) return;
			string baseurl = HelpServer.Start(root);
			using(WebClient client = Client()) Assert.Equal("<p>a</p>", client.DownloadString(baseurl + "a.html"));

			HelpServer.Stop();
			using(WebClient client = Client())
			{
				WebException ex = Assert.Throws<WebException>(() => client.DownloadString(baseurl + "a.html"));
				Assert.Equal(WebExceptionStatus.ConnectFailure, ex.Status);
			}

			string restarted = HelpServer.Start(root);
			using(WebClient client = Client()) Assert.Equal("<p>a</p>", client.DownloadString(restarted + "a.html"));
		}

		[Fact]
		public void AnotherHelpFolderRestartsTheServer()
		{
			if(!Unix) return;
			string other = Path.Combine(parent, "Other");
			Directory.CreateDirectory(other);
			File.WriteAllText(Path.Combine(other, "x.html"), "<p>x</p>");

			string first = HelpServer.Start(root);
			string second = HelpServer.Start(other);
			Assert.NotEqual(first, second);
			using(WebClient client = Client()) Assert.Equal("<p>x</p>", client.DownloadString(second + "x.html"));
			using(WebClient client = Client())
			{
				WebException ex = Assert.Throws<WebException>(() => client.DownloadString(first + "a.html"));
				Assert.Equal(WebExceptionStatus.ConnectFailure, ex.Status);
			}
		}

		[Fact]
		public void ServerIsWantedInTheAppImageUnlessOverridden()
		{
			Assert.False(HelpServer.Wanted(null, null));
			Assert.False(HelpServer.Wanted("", ""));
			Assert.True(HelpServer.Wanted("/tmp/.mount_x", null));
			Assert.True(HelpServer.Wanted(null, "1"));
			Assert.True(HelpServer.Wanted("/tmp/.mount_x", "1"));
			Assert.False(HelpServer.Wanted("/tmp/.mount_x", "0"));
			Assert.False(HelpServer.Wanted(null, "0"));
			Assert.True(HelpServer.Wanted("/tmp/.mount_x", "yes"));
		}

		[Fact]
		public void ContentTypesFollowTheExtension()
		{
			string[][] cases =
			{
				new[] { "x.html", "text/html" }, new[] { "x.HTM", "text/html" }, new[] { "x.css", "text/css" },
				new[] { "x.js", "text/javascript" }, new[] { "x.png", "image/png" }, new[] { "x.jpg", "image/jpeg" },
				new[] { "x.gif", "image/gif" }, new[] { "x.svg", "image/svg+xml" }, new[] { "x.hhc", "application/octet-stream" },
				new[] { "noext", "application/octet-stream" },
			};
			foreach(string[] c in cases) Assert.Equal(c[1], HelpServer.ContentType(c[0]));
		}
	}
}
