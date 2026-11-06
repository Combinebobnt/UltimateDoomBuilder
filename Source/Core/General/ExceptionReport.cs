
#region ================== Namespaces

using System;
using System.IO;
using System.Management;
using System.Runtime.CompilerServices;
using System.Text;

#endregion

namespace CodeImp.DoomBuilder
{
	// What the exception dialog reports, apart from the Form so the net8.0 tests can bind to it
	internal static class ExceptionReport
	{
		private const string CRASH_DUMP_FILE = "UDBCrash.txt";

		internal static string GetSystemInfo()
		{
			string result = "***********SYSTEM INFO***********" + Environment.NewLine;

			// WMI throws NotImplementedException on Mono
			try { result += GetWmiInfo(); }
			catch(Exception) { result += "OS: " + Environment.OSVersion + Environment.NewLine; }

            // Get GZDB version
            result += "UDB: R" + typeof(ExceptionReport).Assembly.GetName().Version.Revision + Environment.NewLine;
            result += "Platform: " + (Environment.Is64BitProcess ? "x64" : "x86") + Environment.NewLine + Environment.NewLine;

			return result;
		}

		// Its own method so the caller's try also sees System.Management failing to load at JIT time
		[MethodImpl(MethodImplOptions.NoInlining)]
		private static string GetWmiInfo()
		{
			string result = "";

			// Get OS name
			ManagementObjectSearcher searcher = new ManagementObjectSearcher("SELECT Caption FROM Win32_OperatingSystem");
			foreach(ManagementBaseObject mo in searcher.Get())
			{
				result += "OS: " + mo["Caption"] + Environment.NewLine;
				break;
			}

			// Get GPU name
			searcher = new ManagementObjectSearcher("SELECT * FROM Win32_VideoController");
			foreach(ManagementBaseObject mo in searcher.Get())
			{
				PropertyData bpp = mo.Properties["CurrentBitsPerPixel"];
				PropertyData description = mo.Properties["Description"];
				if(bpp != null && description != null && bpp.Value != null)
				{
					result += "GPU: " + description.Value + Environment.NewLine;
					break;
				}
			}

			return result;
		}

		// File reads only: the crash handler calls this on any thread, ErrorLogger included
		internal static string GetExceptionDescription(Exception ex)
		{
			StringBuilder message = new StringBuilder("********EXCEPTION DETAILS********");
			message.Append(Environment.NewLine).Append(ex.Source).Append(": ").Append(ex.Message).Append(Environment.NewLine).Append(ex.StackTrace);

			if(File.Exists(General.LogFile))
			{
				try
				{
					// StringBuilder: this runs before the crash backup, so a long log must not stall it
					string[] lines = File.ReadAllLines(General.LogFile);
					message.Append(Environment.NewLine).Append(Environment.NewLine).Append("***********ACTIONS LOG***********");
					for(int i = lines.Length - 1; i > -1; i--)
						message.Append(Environment.NewLine).Append(lines[i]);
				} catch(Exception) { }
			}

			return message.ToString();
		}

		// Null before startup sets the settings path
		internal static string CrashDumpPath
		{
			get { return General.SettingsPath == null ? null : Path.Combine(General.SettingsPath, CRASH_DUMP_FILE); }
		}

		// Returns null when it can't: no settings path yet, or an unwritable folder. The dialog still has to show
		internal static string WriteCrashDump(string report)
		{
			try
			{
				string path = Path.Combine(General.SettingsPath, CRASH_DUMP_FILE);
				File.WriteAllText(path, report);
				return path;
			}
			catch(Exception)
			{
				return null;
			}
		}
	}
}
