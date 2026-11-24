#region ================== Namespaces

using System;
using System.Windows.Forms;

#endregion

namespace CodeImp.DoomBuilder.Windows
{
	// Reports a dialog closed through its Cancel button or Escape. DialogResult can't tell: a
	// dialog whose OK path only calls Close() ends with Cancel on .NET and None on Mono.
	// Listeners only care while an action runs, which only a modal dialog can interrupt.
	internal static class DialogCancelWatch
	{
		internal static event EventHandler Cancelled;

		// Call once per form, when it loads
		internal static void Attach(Form form)
		{
			Control cancel = form.CancelButton as Control;
			if(cancel == null) return;

			// Either order: a Click handler calling Close() fires FormClosed before this Click handler on Mono
			bool clicked = false, closed = false;
			cancel.Click += (s, e) => { clicked = true; if(closed) Report(form); };
			form.FormClosed += (s, e) => { closed = true; if(clicked) Report(form); };
		}

		private static void Report(Form form)
		{
			if(Cancelled != null) Cancelled(form, EventArgs.Empty);
		}
	}
}
