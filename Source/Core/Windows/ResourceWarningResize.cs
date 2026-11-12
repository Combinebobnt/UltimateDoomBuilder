using System;
using System.Drawing;
using System.Windows.Forms;
using CodeImp.DoomBuilder.Controls;

namespace CodeImp.DoomBuilder.Windows
{
	internal sealed class ResourceWarningResize
	{
		private readonly Form form;
		private readonly Control container;
		private readonly ResourceListEditor editor;
		private readonly Control apply;
		private readonly int initialclientheight;
		private readonly int bottomdistance;
		private int warningsheight;

		internal ResourceWarningResize(Form form, Control container, ResourceListEditor editor, Control apply)
		{
			this.form = form;
			this.container = container;
			this.editor = editor;
			this.apply = apply;
			initialclientheight = form.ClientSize.Height;
			bottomdistance = initialclientheight - container.Bottom;

			if(DelayedForm.GeomDebugLogging)
			{
				form.Resize += delegate { Trace("Resize", warningsheight); };
				form.Layout += delegate { Trace("Layout", warningsheight); };
			}
		}

		internal void Apply(int size)
		{
			if(form.IsDisposed || form.Disposing || container.IsDisposed || container.Disposing) return;
			warningsheight = size;
			Trace("entry", size);
			int height = initialclientheight + size;
			form.SuspendLayout();
			try
			{
				form.ClientSize = new Size(form.ClientSize.Width, height);
				// Pin the bottom anchor against the requested client size, not a stale WM reply.
				container.Height = height - bottomdistance - container.Top;
			}
			finally
			{
				form.ResumeLayout(true);
			}
			Trace("exit", size);
			TraceSettled();
		}

		private void Trace(string stage, int size)
		{
			if(!DelayedForm.GeomDebugLogging || form.IsDisposed || form.Disposing || editor.IsDisposed || editor.Disposing) return;
			General.WriteLogLine(string.Format(
				"WARNRESIZE {0}#{1} {2} size={3} Height={4} ClientSize={5} Bounds={6} IsHandleCreated={7} Visible={8} Opacity={9} container={10} editor={11} apply={12} {13} initialClientHeight={14} bottomDistance={15}",
				form.Name, form.GetHashCode(), stage, size, form.Height, form.ClientSize, form.Bounds,
				form.IsHandleCreated, form.Visible, form.Opacity, container.Bounds, editor.Bounds, apply.Bounds,
				editor.WarningLayout, initialclientheight, bottomdistance));
		}

		private void TraceSettled()
		{
			if(!DelayedForm.GeomDebugLogging || !form.IsHandleCreated || form.IsDisposed || form.Disposing) return;
			try
			{
				form.BeginInvoke(new MethodInvoker(delegate { Trace("settled", warningsheight); }));
			}
			catch(InvalidOperationException)
			{
				if(!form.IsDisposed && !form.Disposing && form.IsHandleCreated) throw;
			}
		}
	}
}
