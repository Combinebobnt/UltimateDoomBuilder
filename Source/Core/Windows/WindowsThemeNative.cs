#region ================== Namespaces

using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Drawing;
using System.Runtime.CompilerServices;
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
		// The parts Windows draws itself: a TabControl's strip, a ListView's header. Called on every new handle.
		void SubclassForDark(Control c);
		// Process-wide; false when the call isn't available
		bool SetAppMode(DarkAppModeCall call, bool dark);
	}

	// Compiled into both builds; only Win32NT code paths call it
	internal sealed class Win32ThemeNative : INativeTheming
	{
		private const int ALLOW_DARK_MODE_FOR_WINDOW = 133;
		private const int SET_PREFERRED_APP_MODE = 135;
		private const int FLUSH_MENU_THEMES = 136;
		// PreferredAppMode values; AllowDarkModeForApp takes a BOOL in the same slot
		private const int APP_MODE_DEFAULT = 0;
		private const int APP_MODE_FORCE_DARK = 2;

		[UnmanagedFunctionPointer(CallingConvention.Winapi)]
		[return: MarshalAs(UnmanagedType.U1)]
		private delegate bool AllowDarkModeForWindowProc(IntPtr hwnd, [MarshalAs(UnmanagedType.U1)] bool allow);

		[UnmanagedFunctionPointer(CallingConvention.Winapi)]
		private delegate int AppModeProc(int mode);

		[UnmanagedFunctionPointer(CallingConvention.Winapi)]
		private delegate void FlushMenuThemesProc();

		private readonly AllowDarkModeForWindowProc allowdarkforwindow;
		private readonly ConditionalWeakTable<Control, NativeWindow> subclasses = new ConditionalWeakTable<Control, NativeWindow>();

		public Win32ThemeNative()
		{
			IntPtr proc = UxthemeOrdinal(ALLOW_DARK_MODE_FOR_WINDOW);
			if(proc != IntPtr.Zero) allowdarkforwindow = (AllowDarkModeForWindowProc)Marshal.GetDelegateForFunctionPointer(proc, typeof(AllowDarkModeForWindowProc));
		}

		public void SubclassForDark(Control c)
		{
			NativeWindow window;
			if(c is TabControl)
			{
				window = new DarkTabStrip((TabControl)c, this);
				window.AssignHandle(c.Handle);
			}
			else if(c is ListView)
			{
				DarkListHeader header = new DarkListHeader(this);
				header.AssignHandle(c.Handle);
				header.ThemeHeader();
				window = header;
			}
			else return;
			// The window table holds subclasses weakly; the control keeps the current one alive
			subclasses.Remove(c);
			subclasses.Add(c, window);
		}

		public bool SetAppMode(DarkAppModeCall call, bool dark)
		{
			if(call == DarkAppModeCall.None) return false;
			IntPtr proc = UxthemeOrdinal(SET_PREFERRED_APP_MODE);
			if(proc == IntPtr.Zero) return false;
			int mode = !dark ? APP_MODE_DEFAULT : call == DarkAppModeCall.SetPreferredAppMode ? APP_MODE_FORCE_DARK : 1;
			((AppModeProc)Marshal.GetDelegateForFunctionPointer(proc, typeof(AppModeProc)))(mode);
			// Context menus pick the mode up only after this
			IntPtr flush = UxthemeOrdinal(FLUSH_MENU_THEMES);
			if(flush != IntPtr.Zero) ((FlushMenuThemesProc)Marshal.GetDelegateForFunctionPointer(flush, typeof(FlushMenuThemesProc)))();
			return true;
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

	// Paints a whole TabControl dark: owner drawing reaches only the tabs, not the strip or the page frame
	internal sealed class DarkTabStrip : NativeWindow
	{
		private const int WM_PAINT = 0x000F;
		private const int WM_ERASEBKGND = 0x0014;
		private const int WM_PRINTCLIENT = 0x0318;

		private static bool loggedfailure;

		private readonly TabControl tabs;
		private readonly INativeTheming native;
		private IntPtr updown;
		private bool failed;

		public DarkTabStrip(TabControl tabs, INativeTheming native)
		{
			this.tabs = tabs;
			this.native = native;
		}

		protected override void WndProc(ref Message m)
		{
			if(!failed)
			{
				switch(m.Msg)
				{
					case WM_ERASEBKGND:
						m.Result = (IntPtr)1;
						return;

					case WM_PRINTCLIENT:
						if(Paint(m.WParam)) return;
						break;

					// A WM_PAINT with a DC in wParam paints into it, as the common controls do
					case WM_PAINT:
						if(m.WParam != IntPtr.Zero)
						{
							if(Paint(m.WParam)) return;
							break;
						}
						PAINTSTRUCT ps;
						IntPtr hdc = BeginPaint(m.HWnd, out ps);
						try { Paint(hdc); }
						finally { EndPaint(m.HWnd, ref ps); }
						return;
				}
			}
			base.WndProc(ref m);
		}

		private bool Paint(IntPtr hdc)
		{
			try
			{
				// The scroll arrows of a strip too narrow for its tabs, created when first needed
				IntPtr arrows = FindWindowEx(tabs.Handle, IntPtr.Zero, "msctls_updown32", null);
				if(arrows != IntPtr.Zero && arrows != updown)
				{
					updown = arrows;
					native.SetWindowTheme(arrows, DarkControlRules.EXPLORER_THEME);
				}

				Rectangle client = tabs.ClientRectangle;
				if(client.Width <= 0 || client.Height <= 0) return true;
				using(BufferedGraphics buffer = BufferedGraphicsManager.Current.Allocate(hdc, client))
				{
					DarkTabs.DrawControl(buffer.Graphics, tabs);
					buffer.Render(hdc);
				}
				return true;
			}
			catch(Exception e)
			{
				// Stock painting from here on
				failed = true;
				if(!loggedfailure)
				{
					loggedfailure = true;
					General.WriteLogLine("Interface theme: a tab strip was left light (" + e.GetType().Name + ": " + e.Message + ")");
				}
				tabs.Invalidate();
				return false;
			}
		}

		[StructLayout(LayoutKind.Sequential)]
		private struct PAINTSTRUCT
		{
			public IntPtr hdc;
			public int fErase;
			public int left, top, right, bottom;
			public int fRestore;
			public int fIncUpdate;
			[MarshalAs(UnmanagedType.ByValArray, SizeConst = 32)]
			public byte[] rgbReserved;
		}

		[DllImport("user32.dll")]
		private static extern IntPtr BeginPaint(IntPtr hwnd, out PAINTSTRUCT ps);

		[DllImport("user32.dll")]
		private static extern bool EndPaint(IntPtr hwnd, ref PAINTSTRUCT ps);

		[DllImport("user32.dll", CharSet = CharSet.Unicode)]
		private static extern IntPtr FindWindowEx(IntPtr parent, IntPtr after, string classname, string windowname);
	}

	// Darkens a ListView's header: the dark theme class for its background, custom draw for its text,
	// whose NM_CUSTOMDRAW goes to the header's parent, the ListView
	internal sealed class DarkListHeader : NativeWindow
	{
		private const int WM_NOTIFY = 0x004E;
		private const int NM_CUSTOMDRAW = -12;
		private const int CDDS_PREPAINT = 0x1;
		private const int CDDS_ITEMPREPAINT = 0x10001;
		private const int CDRF_DEFAULT = 0x0;
		private const int CDRF_NOTIFYITEMDRAW = 0x20;
		private const int LVM_GETHEADER = 0x101F;

		private static bool loggedfailure;

		private readonly INativeTheming native;
		private readonly int textcolor = ColorTranslator.ToWin32(UITheme.DarkPalette[KnownColor.ControlText]);
		private IntPtr header;
		private bool failed;

		public DarkListHeader(INativeTheming native)
		{
			this.native = native;
		}

		// The header a list view in another view creates later is caught at its first custom draw
		public void ThemeHeader()
		{
			IntPtr current = SendMessage(Handle, LVM_GETHEADER, IntPtr.Zero, IntPtr.Zero);
			if(current == IntPtr.Zero || current == header) return;
			header = current;
			native.AllowDarkForWindow(header);
			native.SetWindowTheme(header, DarkControlRules.HEADER_THEME);
		}

		protected override void WndProc(ref Message m)
		{
			if(m.Msg == WM_NOTIFY && !failed && m.LParam != IntPtr.Zero)
			{
				try
				{
					NMHDR hdr = (NMHDR)Marshal.PtrToStructure(m.LParam, typeof(NMHDR));
					if(hdr.code == NM_CUSTOMDRAW && hdr.hwndFrom != IntPtr.Zero)
					{
						if(hdr.hwndFrom != header) ThemeHeader();
						if(hdr.hwndFrom == header)
						{
							NMCUSTOMDRAW draw = (NMCUSTOMDRAW)Marshal.PtrToStructure(m.LParam, typeof(NMCUSTOMDRAW));
							if(draw.dwDrawStage == CDDS_PREPAINT)
							{
								m.Result = (IntPtr)CDRF_NOTIFYITEMDRAW;
								return;
							}
							if(draw.dwDrawStage == CDDS_ITEMPREPAINT)
							{
								SetTextColor(draw.hdc, textcolor);
								m.Result = (IntPtr)CDRF_DEFAULT;
								return;
							}
						}
					}
				}
				catch(Exception e)
				{
					failed = true;
					if(!loggedfailure)
					{
						loggedfailure = true;
						General.WriteLogLine("Interface theme: a list header was left light (" + e.GetType().Name + ": " + e.Message + ")");
					}
				}
			}
			base.WndProc(ref m);
		}

		[StructLayout(LayoutKind.Sequential)]
		private struct NMHDR
		{
			public IntPtr hwndFrom;
			public IntPtr idFrom;
			public int code;
		}

		[StructLayout(LayoutKind.Sequential)]
		private struct NMCUSTOMDRAW
		{
			public NMHDR hdr;
			public int dwDrawStage;
			public IntPtr hdc;
			public int left, top, right, bottom;
			public IntPtr dwItemSpec;
			public int uItemState;
			public IntPtr lItemlParam;
		}

		[DllImport("user32.dll")]
		private static extern IntPtr SendMessage(IntPtr hwnd, int msg, IntPtr wparam, IntPtr lparam);

		[DllImport("gdi32.dll")]
		private static extern int SetTextColor(IntPtr hdc, int color);
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
		private readonly INativeTheming native;
		private readonly DarkAppModeCall appmode;
		private readonly DarkControlWalker walker;
		private string tablefield = "not written";
		private bool loggedfailure;

		public WindowsThemeBackend(int build, INativeTheming native)
		{
			this.build = build;
			this.native = native;
			WindowsDarkFeatures features = WindowsDarkSupport.For(build);
			appmode = features.AppModeCall;
			walker = new DarkControlWalker(native, features.TitlebarAttribute);
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
			// Dark context menus and scrollbars; nothing depends on it
			string appmodestate = SetAppMode(true) ? appmode.ToString() : "unavailable";
			General.WriteLogLine("Interface theme: dark (Windows build " + build + ", color table " + tablefield + ", app mode " + appmodestate + ")");
		}

		public void UninstallFormTheming()
		{
			if(hook == IntPtr.Zero) return;
			UnhookWindowsHookEx(hook);
			hook = IntPtr.Zero;
			hookproc = null;
			SetAppMode(false);
		}

		// An undocumented call, so any failure only leaves menus and scrollbars light
		private bool SetAppMode(bool dark)
		{
			try { return native.SetAppMode(appmode, dark); }
			catch(Exception e)
			{
				General.WriteLogLine("Interface theme: app mode call failed (" + e.GetType().Name + ": " + e.Message + ")");
				return false;
			}
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
