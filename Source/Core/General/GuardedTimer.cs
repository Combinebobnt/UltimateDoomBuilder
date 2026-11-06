#region ================== Namespaces

using System;
using System.ComponentModel;
using System.Windows.Forms;

#endregion

namespace CodeImp.DoomBuilder
{
	// Mono raises Tick outside any window procedure, so an exception there skips ThreadException and ends the process
	public class GuardedTimer : Timer
	{
		public GuardedTimer() { }

		public GuardedTimer(IContainer container) : base(container) { }

		protected override void OnTick(EventArgs e)
		{
#if DEBUG
			// No handler is registered in Debug: let it break in the debugger
			base.OnTick(e);
#else
			try
			{
				base.OnTick(e);
			}
			catch(Exception ex)
			{
				Application.OnThreadException(ex);
			}
#endif
		}
	}
}
