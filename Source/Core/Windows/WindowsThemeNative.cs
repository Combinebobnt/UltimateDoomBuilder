#region ================== Namespaces

using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Windows.Forms;

#endregion

namespace CodeImp.DoomBuilder.Windows
{
	// The Win32 theming calls the dark walker makes, so tests can record them
	internal interface INativeTheming
	{
		void SetWindowTheme(IntPtr hwnd, string subappname);
		// attribute: WindowsDarkFeatures.TitlebarAttribute
		void SetTitlebarDark(IntPtr hwnd, int attribute);
		void AllowDarkForWindow(IntPtr hwnd);
	}

	// Compiled into both builds; only Win32NT code paths call it
	internal sealed class Win32ThemeNative : INativeTheming
	{
		private const int ALLOW_DARK_MODE_FOR_WINDOW = 133;

		[UnmanagedFunctionPointer(CallingConvention.Winapi)]
		[return: MarshalAs(UnmanagedType.U1)]
		private delegate bool AllowDarkModeForWindowProc(IntPtr hwnd, [MarshalAs(UnmanagedType.U1)] bool allow);

		private readonly AllowDarkModeForWindowProc allowdarkforwindow;

		public Win32ThemeNative()
		{
			IntPtr proc = UxthemeOrdinal(ALLOW_DARK_MODE_FOR_WINDOW);
			if(proc != IntPtr.Zero) allowdarkforwindow = (AllowDarkModeForWindowProc)Marshal.GetDelegateForFunctionPointer(proc, typeof(AllowDarkModeForWindowProc));
		}

		public void SetWindowTheme(IntPtr hwnd, string subappname)
		{
			SetWindowTheme(hwnd, subappname, null);
		}

		public void SetTitlebarDark(IntPtr hwnd, int attribute)
		{
			if(attribute == 0) return;
			int on = 1;
			DwmSetWindowAttribute(hwnd, attribute, ref on, sizeof(int));
		}

		public void AllowDarkForWindow(IntPtr hwnd)
		{
			if(allowdarkforwindow != null) allowdarkforwindow(hwnd, true);
		}

		// An undocumented uxtheme export by ordinal, or zero
		internal static IntPtr UxthemeOrdinal(int ordinal)
		{
			IntPtr module = LoadLibrary("uxtheme.dll");
			return module != IntPtr.Zero ? GetProcAddress(module, (IntPtr)ordinal) : IntPtr.Zero;
		}

		[DllImport("uxtheme.dll", CharSet = CharSet.Unicode)]
		private static extern int SetWindowTheme(IntPtr hwnd, string pszSubAppName, string pszSubIdList);

		[DllImport("dwmapi.dll")]
		private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);

		[DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
		private static extern IntPtr LoadLibrary(string name);

		[DllImport("kernel32.dll", SetLastError = true)]
		private static extern IntPtr GetProcAddress(IntPtr module, IntPtr ordinal);
	}

	// Windows (.NET Framework): native controls paint backgrounds with the real OS brushes, so besides the color table
	// every form is walked as it is created, through a CBT hook on the UI thread
	internal sealed class WindowsThemeBackend : IThemeBackend
	{
		private const int WH_CBT = 5;
		private const int HCBT_CREATEWND = 3;
		private const int HCBT_ACTIVATE = 5;

		private delegate IntPtr HookProc(int code, IntPtr wparam, IntPtr lparam);

		// Rooted here: a collected hook delegate crashes the process at the next window creation
		private static HookProc hookproc;
		private static IntPtr hook;

		private readonly int build;
		private readonly DarkControlWalker walker;
		private string tablefield = "not written";
		private bool loggedfailure;

		public WindowsThemeBackend(int build, INativeTheming native)
		{
			this.build = build;
			walker = new DarkControlWalker(native, WindowsDarkSupport.For(build).TitlebarAttribute);
		}

		internal DarkControlWalker Walker { get { return walker; } }

		public IDictionary<KnownColor, Color> ApplyPalette(IReadOnlyDictionary<KnownColor, Color> palette)
		{
			return KnownColorTableWriter.Write(palette, out tablefield);
		}

		public void RestorePalette(IDictionary<KnownColor, Color> previous)
		{
			KnownColorTableWriter.Restore(previous);
		}

		public ToolStripRenderer CreateToolStripRenderer()
		{
			return new DarkToolStripRenderer();
		}

		public void InstallToolStripRenderer(ToolStripRenderer renderer)
		{
			ToolStripManager.Renderer = renderer;
		}

		public void InstallFormTheming()
		{
			if(hook != IntPtr.Zero) throw new InvalidOperationException("Form theming is already installed");
			hookproc = CbtProc;
			hook = SetWindowsHookEx(WH_CBT, hookproc, IntPtr.Zero, GetCurrentThreadId());
			if(hook == IntPtr.Zero)
			{
				hookproc = null;
				throw new Win32Exception(Marshal.GetLastWin32Error(), "SetWindowsHookEx failed");
			}
			General.WriteLogLine("Interface theme: dark (Windows build " + build + ", color table " + tablefield + ")");
		}

		public void UninstallFormTheming()
		{
			if(hook == IntPtr.Zero) return;
			UnhookWindowsHookEx(hook);
			hook = IntPtr.Zero;
			hookproc = null;
		}

		// An exception escaping a hook callback would crash, so everything is caught and the hook always chains
		private IntPtr CbtProc(int code, IntPtr wparam, IntPtr lparam)
		{
			try
			{
				if(code == HCBT_CREATEWND)
				{
					// CBT_CREATEWND.lpcs, then CREATESTRUCT.hwndParent after lpCreateParams, hInstance and hMenu
					IntPtr createstruct = Marshal.ReadIntPtr(lparam);
					IntPtr parent = Marshal.ReadIntPtr(createstruct, 3 * IntPtr.Size);
					Control c = parent != IntPtr.Zero ? Control.FromHandle(parent) : null;
					Form form = c as Form ?? (c != null ? c.FindForm() : null);
					if(form != null && !walker.IsWalked(form)) walker.Theme(form, wparam);
				}
				else if(code == HCBT_ACTIVATE)
				{
					// A form with no child windows
					Form form = Control.FromHandle(wparam) as Form;
					if(form != null && !walker.IsWalked(form)) walker.Theme(form, IntPtr.Zero);
				}
			}
			catch(Exception e)
			{
				if(!loggedfailure)
				{
					loggedfailure = true;
					General.WriteLogLine("Interface theme: a form was left partly light (" + e.GetType().Name + ": " + e.Message + ")");
				}
			}
			return CallNextHookEx(IntPtr.Zero, code, wparam, lparam);
		}

		[DllImport("user32.dll", SetLastError = true)]
		private static extern IntPtr SetWindowsHookEx(int idHook, HookProc lpfn, IntPtr hMod, uint dwThreadId);

		[DllImport("user32.dll")]
		private static extern bool UnhookWindowsHookEx(IntPtr hhk);

		[DllImport("user32.dll")]
		private static extern IntPtr CallNextHookEx(IntPtr hhk, int nCode, IntPtr wParam, IntPtr lParam);

		[DllImport("kernel32.dll")]
		private static extern uint GetCurrentThreadId();
	}
}
