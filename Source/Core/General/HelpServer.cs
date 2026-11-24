#region ================== Namespaces

using System;
using System.ComponentModel;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;

#endregion

namespace CodeImp.DoomBuilder
{
	// Serves the HTML manual over loopback HTTP, for browsers that can't read the AppImage's mount (snap, flatpak)
	internal static class HelpServer
	{
		internal const string ENABLE_VARIABLE = "UDB_HELP_SERVER";
		private const int START_ATTEMPTS = 3;

		private static readonly object lockobj = new object();
		private static HttpListener listener;
		private static Thread thread;
		private static string servedir;
		private static string baseurl;

		// UDB_HELP_SERVER=1 or 0 forces the choice, else the AppImage (APPDIR set) serves help over HTTP
		internal static bool Wanted(string appdir, string setting)
		{
			if(setting == "1") return true;
			if(setting == "0") return false;
			return !string.IsNullOrEmpty(appdir);
		}

		// Returns the base URL of the server for helpdir, starting it on a free loopback port if needed
		internal static string Start(string helpdir)
		{
			lock(lockobj)
			{
				if(listener != null && servedir == helpdir) return baseurl;
				StopLocked();

				Exception last = null;
				for(int attempt = 0; attempt < START_ATTEMPTS; attempt++)
				{
					string url = "http://127.0.0.1:" + FreePort() + "/";
					HttpListener candidate = new HttpListener();
					candidate.Prefixes.Add(url);
					try
					{
						candidate.Start();
					}
					catch(Win32Exception ex)
					{
						// Another process took the port between probe and bind
						last = ex;
						candidate.Close();
						continue;
					}

					listener = candidate;
					servedir = helpdir;
					baseurl = url;
					thread = new Thread(() => Serve(candidate, helpdir)) { IsBackground = true, Name = "Help server" };
					thread.Start();
					General.WriteLogLine("Help server listening on " + url);
					return url;
				}

				throw last;
			}
		}

		internal static void Stop()
		{
			Thread stopping;
			lock(lockobj)
			{
				stopping = thread;
				StopLocked();
			}
			if(stopping != null) stopping.Join(1000);
		}

		private static void StopLocked()
		{
			if(listener == null) return;
			try { listener.Close(); }
			catch(Exception) { }
			listener = null;
			thread = null;
			servedir = null;
			baseurl = null;
		}

		private static int FreePort()
		{
			TcpListener probe = new TcpListener(IPAddress.Loopback, 0);
			probe.Start();
			int port = ((IPEndPoint)probe.LocalEndpoint).Port;
			probe.Stop();
			return port;
		}

		private static void Serve(HttpListener server, string helpdir)
		{
			while(true)
			{
				HttpListenerContext context;
				try
				{
					context = server.GetContext();
				}
				catch(Exception ex)
				{
					if(server.IsListening) General.WriteLogLine("Help server stopped: " + ex.Message);
					return;
				}

				try
				{
					Respond(context, helpdir);
				}
				catch(Exception ex)
				{
					General.WriteLogLine("Help server request " + context.Request.RawUrl + " failed: " + ex.Message);
					try { Fail(context.Response, 500, false); }
					catch(Exception) { }
				}
			}
		}

		private static void Respond(HttpListenerContext context, string helpdir)
		{
			HttpListenerResponse response = context.Response;
			string method = context.Request.HttpMethod;
			bool head = (method == "HEAD");
			if(method != "GET" && !head)
			{
				response.AddHeader("Allow", "GET, HEAD");
				Fail(response, 405, false);
				return;
			}

			string path = HelpPaths.ResolveUrlPath(helpdir, context.Request.RawUrl);
			if(path == null)
			{
				Fail(response, 404, head);
				return;
			}

			Send(response, 200, ContentType(path), File.ReadAllBytes(path), head);
		}

		private static void Fail(HttpListenerResponse response, int status, bool head)
		{
			Send(response, status, "text/plain", Encoding.ASCII.GetBytes(status + "\n"), head);
		}

		private static void Send(HttpListenerResponse response, int status, string contenttype, byte[] body, bool head)
		{
			response.StatusCode = status;
			response.ContentType = contenttype;
			response.AddHeader("X-Content-Type-Options", "nosniff");
			response.ContentLength64 = body.Length;
			if(!head) response.OutputStream.Write(body, 0, body.Length);
			response.Close();
		}

		// No charset: the pages declare their own, as they do over file://
		internal static string ContentType(string path)
		{
			switch(Path.GetExtension(path).ToLowerInvariant())
			{
				case ".html":
				case ".htm": return "text/html";
				case ".css": return "text/css";
				case ".js": return "text/javascript";
				case ".png": return "image/png";
				case ".jpg":
				case ".jpeg": return "image/jpeg";
				case ".gif": return "image/gif";
				case ".svg": return "image/svg+xml";
				default: return "application/octet-stream";
			}
		}
	}
}
