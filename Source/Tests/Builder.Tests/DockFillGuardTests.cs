using System.Drawing;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using CodeImp.DoomBuilder.Controls;

namespace Builder.Tests
{
	// Mono/X11 only: replays an old ConfigureNotify into a docked child, as a late-read event does.
	// Passes vacuously on Windows, where the guard is off.
	[Collection("WindowsUI")]
	public class DockFillGuardTests
	{
		[DllImport("libX11")] private static extern IntPtr XOpenDisplay(IntPtr name);
		[DllImport("libX11")] private static extern int XCloseDisplay(IntPtr display);
		[DllImport("libX11")] private static extern int XQueryTree(IntPtr display, IntPtr window, out IntPtr root, out IntPtr parent, out IntPtr children, out int count);
		[DllImport("libX11")] private static extern int XFree(IntPtr data);
		[DllImport("libX11")] private static extern int XGetGeometry(IntPtr display, IntPtr drawable, out IntPtr root, out int x, out int y, out uint width, out uint height, out uint border, out uint depth);
		[DllImport("libX11")] private static extern int XSendEvent(IntPtr display, IntPtr window, bool propagate, IntPtr mask, IntPtr ev);
		[DllImport("libX11")] private static extern int XSync(IntPtr display, bool discard);

		private const int CONFIGURE_NOTIFY = 22;
		private const int STRUCTURE_NOTIFY_MASK = 1 << 17;

		private static void Pump(int ms)
		{
			DateTime end = DateTime.Now.AddMilliseconds(ms);
			while(DateTime.Now < end) { Application.DoEvents(); Thread.Sleep(10); }
		}

		private static IntPtr XParent(IntPtr display, IntPtr window)
		{
			IntPtr root, parent, children; int count;
			XQueryTree(display, window, out root, out parent, out children, out count);
			if(children != IntPtr.Zero) XFree(children);
			return parent;
		}

		private static Size XSize(IntPtr display, IntPtr window)
		{
			IntPtr root; int x, y; uint w, h, border, depth;
			XGetGeometry(display, window, out root, out x, out y, out w, out h, out border, out depth);
			return new Size((int)w, (int)h);
		}

		// XConfigureEvent on LP64: window ids at 32/40, width/height at 56/60, inside a 192-byte XEvent
		private static void SendStaleConfigure(IntPtr display, IntPtr wholewindow, Size size)
		{
			IntPtr ev = Marshal.AllocHGlobal(192);
			try
			{
				for(int i = 0; i < 192; i += 8) Marshal.WriteInt64(ev, i, 0);
				Marshal.WriteInt32(ev, 0, CONFIGURE_NOTIFY);
				Marshal.WriteIntPtr(ev, 24, display);
				Marshal.WriteIntPtr(ev, 32, wholewindow);
				Marshal.WriteIntPtr(ev, 40, wholewindow);
				Marshal.WriteInt32(ev, 56, size.Width);
				Marshal.WriteInt32(ev, 60, size.Height);
				XSendEvent(display, wholewindow, false, new IntPtr(STRUCTURE_NOTIFY_MASK), ev);
				XSync(display, false);
			}
			finally { Marshal.FreeHGlobal(ev); }
		}

		[Fact]
		public void StaleConfigureOnDockedChildIsLaidOutAgain()
		{
			if(!DockFillGuard.Enabled || IntPtr.Size != 8) return;
			IntPtr display = XOpenDisplay(IntPtr.Zero);
			Assert.True(display != IntPtr.Zero, "no X display");
			try
			{
				using(Form form = new Form { ClientSize = new Size(800, 600), StartPosition = FormStartPosition.Manual })
				{
					Panel outer = new Panel { Dock = DockStyle.Fill };
					Panel inner = new Panel { Dock = DockStyle.Fill, Padding = new Padding(3, 3, 0, 3) };
					// Anchored, not docked, so it doesn't change the fill area
					Button button = new Button { Anchor = AnchorStyles.Bottom | AnchorStyles.Right };
					Panel leaf = new Panel();
					outer.Controls.Add(inner);
					form.Controls.Add(outer);
					DockFillGuard.AttachTree(form);
					// Added after the walk and docked after the add: guarded only through ControlAdded
					inner.Controls.Add(leaf);
					inner.Controls.Add(button);
					leaf.Dock = DockStyle.Fill;

					form.Show();
					Pump(300);
					Size full = inner.DisplayRectangle.Size;
					Assert.Equal(full, leaf.Size);
					int before = DockFillGuard.TotalRelayouts;

					SendStaleConfigure(display, XParent(display, leaf.Handle), new Size(300, 200));
					Pump(600);

					// One repair, and the repaired size reads as settled (no second pass)
					Assert.Equal(1, DockFillGuard.TotalRelayouts - before);
					Assert.Equal(full, leaf.Size);
					Assert.Equal(full, XSize(display, leaf.Handle));
				}
			}
			finally { XCloseDisplay(display); }
		}
	}
}
