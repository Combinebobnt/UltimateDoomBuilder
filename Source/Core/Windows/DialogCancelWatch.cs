#region ================== Namespaces

using System;
using System.Windows.Forms;

#endregion

namespace CodeImp.DoomBuilder.Windows
{
	// Reports a modal dialog closed through its Cancel button or Escape. DialogResult can't tell:
	// a dialog whose OK path only calls Close() ends with Cancel on .NET and None on Mono.
	internal static class DialogCancelWatch
	{
		internal static event EventHandler Cancelled;

		// Call once the form is shown, so Modal is known
		internal static void Attach(Form form)
		{
			if(!form.Modal) return;
			Control cancel = form.CancelButton as Control;
			if(cancel == null) return;

			bool clicked = false;
			cancel.Click += (s, e) => clicked = true;
			form.FormClosed += (s, e) =>
			{
				if(clicked && (Cancelled != null)) Cancelled(form, EventArgs.Empty);
			};
		}
	}
}
