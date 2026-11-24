#region ================== Namespaces

using System;
using System.Diagnostics;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

#endregion

namespace CodeImp.DoomBuilder.Windows
{
	// Runs a desktop query tool. Returns its stdout, or null on a missing binary, a non-zero exit or a timeout.
	internal interface ICommandRunner
	{
		string Run(string file, string args, int timeoutms);
	}

	internal struct DesktopThemeResult
	{
		public readonly UIThemeKind Kind;
		public readonly string Source;

		public DesktopThemeResult(UIThemeKind kind, string source)
		{
			Kind = kind;
			Source = source;
		}
	}

	// Reads the desktop's light or dark preference, for the "system" interface theme
	internal static class DesktopTheme
	{
		#region ================== Constants

		private const int CALL_TIMEOUT = 500;
		private const int TOTAL_BUDGET = 1500;
		private const string FALLBACK_SOURCE = "no desktop preference found";

		private const string PORTAL_ARGS = "call --session --dest org.freedesktop.portal.Desktop --object-path /org/freedesktop/portal/desktop "
			+ "--method org.freedesktop.portal.Settings.Read org.freedesktop.appearance color-scheme";

		private static readonly Regex portalvalue = new Regex(@"uint32\s+(\d+)", RegexOptions.CultureInvariant);

		#endregion

		#region ================== Methods

		// First answer wins: GTK_THEME, MATE's theme name, the settings portal, GNOME's keys. Never throws.
		public static DesktopThemeResult Detect(Func<string, string> getenv, ICommandRunner runner)
		{
			try
			{
				Stopwatch elapsed = Stopwatch.StartNew();
				Func<string, string, string> run = (file, args) =>
				{
					int left = TOTAL_BUDGET - (int)elapsed.ElapsedMilliseconds;
					if(left <= 0) return null;
					try { return runner.Run(file, args, Math.Min(CALL_TIMEOUT, left)); }
					catch(Exception) { return null; }
				};

				string gtktheme = getenv("GTK_THEME");
				if(!string.IsNullOrWhiteSpace(gtktheme))
					return new DesktopThemeResult(ByName(gtktheme), "GTK_THEME '" + gtktheme.Trim() + "'");

				// MATE's Appearance dialog doesn't touch the GNOME keys the portal answers from
				string desktop = getenv("XDG_CURRENT_DESKTOP");
				if(desktop != null && desktop.IndexOf("MATE", StringComparison.OrdinalIgnoreCase) >= 0)
				{
					string name = Unquote(run("gsettings", "get org.mate.interface gtk-theme"));
					if(!string.IsNullOrEmpty(name))
						return new DesktopThemeResult(ByName(name), "MATE gtk-theme '" + name + "'");
				}

				string reply = run("gdbus", PORTAL_ARGS);
				if(reply != null)
				{
					Match m = portalvalue.Match(reply);
					if(m.Success && m.Groups[1].Value == "1") return new DesktopThemeResult(UIThemeKind.Dark, "portal color-scheme 1");
					if(m.Success && m.Groups[1].Value == "2") return new DesktopThemeResult(UIThemeKind.Light, "portal color-scheme 2");
				}

				string scheme = Unquote(run("gsettings", "get org.gnome.desktop.interface color-scheme"));
				if(scheme == "prefer-dark") return new DesktopThemeResult(UIThemeKind.Dark, "GNOME color-scheme '" + scheme + "'");
				if(scheme == "prefer-light") return new DesktopThemeResult(UIThemeKind.Light, "GNOME color-scheme '" + scheme + "'");

				string gnometheme = Unquote(run("gsettings", "get org.gnome.desktop.interface gtk-theme"));
				if(!string.IsNullOrEmpty(gnometheme))
					return new DesktopThemeResult(ByName(gnometheme), "GNOME gtk-theme '" + gnometheme + "'");
			}
			catch(Exception) { }

			return new DesktopThemeResult(UIThemeKind.Light, FALLBACK_SOURCE);
		}

		// Themes ship their dark variants as *-dark or *-Dark, and GTK_THEME's variant follows a colon
		private static UIThemeKind ByName(string name)
		{
			return name.IndexOf("dark", StringComparison.OrdinalIgnoreCase) >= 0 ? UIThemeKind.Dark : UIThemeKind.Light;
		}

		// gsettings prints strings quoted: 'Yaru-red'
		private static string Unquote(string output)
		{
			if(output == null) return null;
			string s = output.Trim();
			if(s.Length >= 2 && s[0] == '\'' && s[s.Length - 1] == '\'') s = s.Substring(1, s.Length - 2);
			return s;
		}

		#endregion
	}

	internal sealed class ProcessCommandRunner : ICommandRunner
	{
		public string Run(string file, string args, int timeoutms)
		{
			Process p = null;
			try
			{
				ProcessStartInfo info = new ProcessStartInfo(file, args)
				{
					UseShellExecute = false,
					CreateNoWindow = true,
					RedirectStandardInput = true,
					RedirectStandardOutput = true,
					// dconf prints warnings there in restricted environments
					RedirectStandardError = true,
				};
				ForkUpdateChecker.CleanAppImageEnvironment(info);
				Stopwatch elapsed = Stopwatch.StartNew();
				p = Process.Start(info);
				if(p == null) return null;
				p.StandardInput.Close();
				p.ErrorDataReceived += (s, e) => { };
				p.BeginErrorReadLine();
				Task<string> output = p.StandardOutput.ReadToEndAsync();

				if(!p.WaitForExit(timeoutms) || !output.Wait(Math.Max(100, timeoutms - (int)elapsed.ElapsedMilliseconds)))
				{
					try { p.Kill(); }
					catch(Exception) { }
					return null;
				}
				return p.ExitCode == 0 ? output.Result : null;
			}
			catch(Exception) { return null; }
			finally
			{
				if(p != null) p.Dispose();
			}
		}
	}
}
