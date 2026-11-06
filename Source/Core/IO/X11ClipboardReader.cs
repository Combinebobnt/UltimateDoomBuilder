#if MONO_WINFORMS

#region ================== Namespaces

using System;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Windows.Forms;

#endregion

namespace CodeImp.DoomBuilder.IO
{
	internal enum ClipboardReadResult
	{
		NoText,
		Text,
		SelfOwned,
		Busy,
		Unavailable
	}

	// Reads the X11 CLIPBOARD on a private connection, because Mono's Clipboard.GetText() can hang forever on INCR owners (upstream #1377)
	internal static class X11ClipboardReader
	{
		#region ================== Constants

		private const string LIBX11 = "libX11";
		private const int SELECTION_NOTIFY = 31;
		private const int PROPERTY_NOTIFY = 28;
		private const int PROPERTY_NEW_VALUE = 0;
		private const int PROPERTY_CHANGE_MASK = 1 << 22;
		private const int XA_STRING = 31;
		private const int EVENT_BUFFER_SIZE = 256;

		#endregion

		#region ================== Native

		[StructLayout(LayoutKind.Sequential)]
		private struct XSelectionEvent
		{
			public int type;
			public IntPtr serial;
			public int send_event;
			public IntPtr display;
			public IntPtr requestor;
			public IntPtr selection;
			public IntPtr target;
			public IntPtr property;
			public IntPtr time;
		}

		[StructLayout(LayoutKind.Sequential)]
		private struct XPropertyEvent
		{
			public int type;
			public IntPtr serial;
			public int send_event;
			public IntPtr display;
			public IntPtr window;
			public IntPtr atom;
			public IntPtr time;
			public int state;
		}

		[DllImport(LIBX11)] private static extern IntPtr XOpenDisplay(IntPtr name);
		[DllImport(LIBX11)] private static extern int XCloseDisplay(IntPtr display);
		[DllImport(LIBX11)] private static extern IntPtr XDefaultRootWindow(IntPtr display);
		[DllImport(LIBX11)] private static extern IntPtr XCreateSimpleWindow(IntPtr display, IntPtr parent, int x, int y, uint width, uint height, uint borderwidth, UIntPtr border, UIntPtr background);
		[DllImport(LIBX11)] private static extern int XSelectInput(IntPtr display, IntPtr window, IntPtr mask);
		[DllImport(LIBX11)] private static extern IntPtr XInternAtom(IntPtr display, string name, bool onlyifexists);
		[DllImport(LIBX11)] private static extern IntPtr XGetSelectionOwner(IntPtr display, IntPtr selection);
		[DllImport(LIBX11)] private static extern int XConvertSelection(IntPtr display, IntPtr selection, IntPtr target, IntPtr property, IntPtr requestor, IntPtr time);
		[DllImport(LIBX11)] private static extern bool XCheckTypedWindowEvent(IntPtr display, IntPtr window, int type, IntPtr ev);
		[DllImport(LIBX11)] private static extern int XGetWindowProperty(IntPtr display, IntPtr window, IntPtr property, IntPtr offset, IntPtr length, bool delete, IntPtr reqtype, out IntPtr actualtype, out int actualformat, out IntPtr nitems, out IntPtr bytesafter, out IntPtr data);
		[DllImport(LIBX11)] private static extern int XDeleteProperty(IntPtr display, IntPtr window, IntPtr property);
		[DllImport(LIBX11)] private static extern int XFree(IntPtr data);
		[DllImport(LIBX11)] private static extern int XFlush(IntPtr display);

		#endregion

		#region ================== Variables

		private static FieldInfo fosterparentfield;
		private static bool fosterparentlooked;

		#endregion

		#region ================== Methods

		internal static ClipboardReadResult Read(string prefix, int timeoutms, out string text)
		{
			text = null;
			if(prefix == null) prefix = string.Empty;

			// Mono's clipboard owner window. Our own clipboard must go through Mono, since this thread answers its requests
			IntPtr fosterparent = GetFosterParent();
			if(fosterparent == IntPtr.Zero) return ClipboardReadResult.Unavailable;

			IntPtr display = XOpenDisplay(IntPtr.Zero);
			if(display == IntPtr.Zero) return ClipboardReadResult.Unavailable;

			IntPtr ev = Marshal.AllocHGlobal(EVENT_BUFFER_SIZE);
			try
			{
				IntPtr clipboard = XInternAtom(display, "CLIPBOARD", false);
				IntPtr owner = XGetSelectionOwner(display, clipboard);
				if(owner == IntPtr.Zero) return ClipboardReadResult.NoText;
				if(owner == fosterparent) return ClipboardReadResult.SelfOwned;

				IntPtr window = XCreateSimpleWindow(display, XDefaultRootWindow(display), 0, 0, 1, 1, 0, UIntPtr.Zero, UIntPtr.Zero);
				XSelectInput(display, window, new IntPtr(PROPERTY_CHANGE_MASK));
				IntPtr targets = XInternAtom(display, "TARGETS", false);
				IntPtr utf8 = XInternAtom(display, "UTF8_STRING", false);
				IntPtr incr = XInternAtom(display, "INCR", false);
				IntPtr property = XInternAtom(display, "UDB_CLIPBOARD", false);
				Stopwatch timer = Stopwatch.StartNew();
				IntPtr type;
				int format;
				long count;
				bool timedout;

				// Ask for the target list first, so non-text data is never requested
				if(!Request(display, window, clipboard, targets, property, ev, timer, timeoutms, out timedout))
					return (timedout ? ClipboardReadResult.Busy : ClipboardReadResult.NoText);
				byte[] list = GetProperty(display, window, property, true, out type, out format, out count);
				if(type == incr || format != 32) return ClipboardReadResult.NoText;
				bool hasutf8 = false;
				bool hasstring = false;
				for(int i = 0; i < count; i++)
				{
					IntPtr atom = (IntPtr.Size == 8 ? new IntPtr(BitConverter.ToInt64(list, i * 8)) : new IntPtr(BitConverter.ToInt32(list, i * 4)));
					if(atom == utf8) hasutf8 = true;
					else if(atom == new IntPtr(XA_STRING)) hasstring = true;
				}
				if(!hasutf8 && !hasstring) return ClipboardReadResult.NoText;

				// Request the text itself
				if(!Request(display, window, clipboard, (hasutf8 ? utf8 : new IntPtr(XA_STRING)), property, ev, timer, timeoutms, out timedout))
					return (timedout ? ClipboardReadResult.Busy : ClipboardReadResult.NoText);
				byte[] data = GetProperty(display, window, property, false, out type, out format, out count);
				MemoryStream stream = new MemoryStream();
				bool keep = true;

				if(type == incr)
				{
					// The header's own notification is already queued, reading it as a chunk would end the transfer early
					while(XCheckTypedWindowEvent(display, window, PROPERTY_NOTIFY, ev)) { }
					XDeleteProperty(display, window, property);
					XFlush(display);

					while(true)
					{
						// Checked on every pass, so an owner that never ends the transfer can't hold us here
						if(timer.ElapsedMilliseconds > timeoutms) return ClipboardReadResult.Busy;
						if(!XCheckTypedWindowEvent(display, window, PROPERTY_NOTIFY, ev))
						{
							Thread.Sleep(1);
							continue;
						}

						XPropertyEvent pe = (XPropertyEvent)Marshal.PtrToStructure(ev, typeof(XPropertyEvent));
						if(pe.atom != property || pe.state != PROPERTY_NEW_VALUE) continue;

						byte[] chunk = GetProperty(display, window, property, true, out type, out format, out count);
						XFlush(display);
						if(chunk.Length == 0) break;

						// On a mismatch keep draining, a single-transfer owner would stay stuck if we stopped
						if(keep)
						{
							stream.Write(chunk, 0, chunk.Length);
							if(!PrefixMatches(stream, prefix))
							{
								keep = false;
								stream.SetLength(0);
							}
						}
					}
				}
				else
				{
					XDeleteProperty(display, window, property);
					XFlush(display);
					stream.Write(data, 0, data.Length);
				}

				if(!keep || stream.Length < prefix.Length || !PrefixMatches(stream, prefix)) return ClipboardReadResult.NoText;

				Encoding encoding = (hasutf8 ? Encoding.UTF8 : Encoding.GetEncoding(28591));
				text = encoding.GetString(stream.GetBuffer(), 0, (int)stream.Length);
				return ClipboardReadResult.Text;
			}
			finally
			{
				Marshal.FreeHGlobal(ev);
				XCloseDisplay(display);
			}
		}

		private static IntPtr GetFosterParent()
		{
			if(!fosterparentlooked)
			{
				fosterparentlooked = true;
				Type x11 = typeof(Form).Assembly.GetType("System.Windows.Forms.XplatUIX11");
				if(x11 != null) fosterparentfield = x11.GetField("FosterParent", BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public);
				if(fosterparentfield != null && fosterparentfield.FieldType != typeof(IntPtr)) fosterparentfield = null;
			}

			if(fosterparentfield == null) return IntPtr.Zero;
			try { return (IntPtr)fosterparentfield.GetValue(null); }
			catch(Exception) { return IntPtr.Zero; }
		}

		private static bool Request(IntPtr display, IntPtr window, IntPtr selection, IntPtr target, IntPtr property, IntPtr ev, Stopwatch timer, int timeoutms, out bool timedout)
		{
			timedout = false;
			XConvertSelection(display, selection, target, property, window, IntPtr.Zero);
			XFlush(display);

			while(!XCheckTypedWindowEvent(display, window, SELECTION_NOTIFY, ev))
			{
				if(timer.ElapsedMilliseconds > timeoutms)
				{
					timedout = true;
					return false;
				}
				Thread.Sleep(1);
			}

			// A refused conversion replies with property None
			XSelectionEvent se = (XSelectionEvent)Marshal.PtrToStructure(ev, typeof(XSelectionEvent));
			return (se.property != IntPtr.Zero);
		}

		private static byte[] GetProperty(IntPtr display, IntPtr window, IntPtr property, bool delete, out IntPtr type, out int format, out long count)
		{
			IntPtr nitems, after, data;
			XGetWindowProperty(display, window, property, IntPtr.Zero, new IntPtr(0x7fffffff), delete, IntPtr.Zero, out type, out format, out nitems, out after, out data);
			count = (long)nitems;

			// Format 32 items are longs on the client side
			int unit = (format == 32 ? IntPtr.Size : format / 8);
			byte[] result = new byte[count * unit];
			if(result.Length > 0) Marshal.Copy(data, result, 0, result.Length);
			if(data != IntPtr.Zero) XFree(data);
			return result;
		}

		private static bool PrefixMatches(MemoryStream stream, string prefix)
		{
			if(stream.Length < prefix.Length) return true;
			byte[] buffer = stream.GetBuffer();
			for(int i = 0; i < prefix.Length; i++)
				if(buffer[i] != (byte)prefix[i]) return false;
			return true;
		}

		#endregion
	}
}

#endif
