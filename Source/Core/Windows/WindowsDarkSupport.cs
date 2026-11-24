#region ================== Namespaces

using System;
using System.Runtime.InteropServices;

#endregion

namespace CodeImp.DoomBuilder.Windows
{
	// How Windows lets an app opt into dark mode, by build
	internal enum DarkAppModeCall
	{
		None,
		// uxtheme ordinal 135 on builds 17763 to 18361: AllowDarkModeForApp(BOOL)
		AllowDarkModeForApp,
		// uxtheme ordinal 135 from build 18362: SetPreferredAppMode(PreferredAppMode)
		SetPreferredAppMode,
	}

	internal struct WindowsDarkFeatures
	{
		public readonly bool Supported;
		// DWMWA_USE_IMMERSIVE_DARK_MODE: 20 from build 18985, 19 before that, 0 when unsupported
		public readonly int TitlebarAttribute;
		public readonly DarkAppModeCall AppModeCall;

		public WindowsDarkFeatures(bool supported, int titlebarattribute, DarkAppModeCall appmodecall)
		{
			Supported = supported;
			TitlebarAttribute = titlebarattribute;
			AppModeCall = appmodecall;
		}
	}

	internal static class WindowsDarkSupport
	{
		// Windows 10 1809, the first build with the DarkMode_Explorer theme class
		public const int MINIMUM_BUILD = 17763;

		public static WindowsDarkFeatures For(int build)
		{
			if(build < MINIMUM_BUILD) return new WindowsDarkFeatures(false, 0, DarkAppModeCall.None);
			return new WindowsDarkFeatures(true, build >= 18985 ? 20 : 19,
				build >= 18362 ? DarkAppModeCall.SetPreferredAppMode : DarkAppModeCall.AllowDarkModeForApp);
		}

		// The real build number: without supportedOS manifest entries Environment.OSVersion stops at 6.2. 0 on failure or off Windows.
		public static int CurrentBuild()
		{
			if(Environment.OSVersion.Platform != PlatformID.Win32NT) return 0;
			try
			{
				OSVERSIONINFO info = new OSVERSIONINFO();
				info.dwOSVersionInfoSize = Marshal.SizeOf(typeof(OSVERSIONINFO));
				return RtlGetVersion(ref info) == 0 ? info.dwBuildNumber : 0;
			}
			catch(Exception) { return 0; }
		}

		[StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
		private struct OSVERSIONINFO
		{
			public int dwOSVersionInfoSize;
			public int dwMajorVersion;
			public int dwMinorVersion;
			public int dwBuildNumber;
			public int dwPlatformId;
			[MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
			public string szCSDVersion;
		}

		[DllImport("ntdll.dll")]
		private static extern int RtlGetVersion(ref OSVERSIONINFO info);
	}
}
