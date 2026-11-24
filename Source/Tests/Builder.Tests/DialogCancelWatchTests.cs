using System;
using System.Drawing;
using System.Windows.Forms;
using CodeImp.DoomBuilder.Windows;
using Xunit;

namespace Builder.Tests
{
	// A macro run stops on a cancelled dialog: missing a Cancel lets the run go on, a false one rolls back an OK
	public class DialogCancelWatchTests
	{
		private enum Ending { CancelButton, OkThenClose, OkWithResult }

		private static int CancelsReported(Ending how)
		{
			int reported = 0;
			EventHandler counter = (s, e) => reported++;
			DialogCancelWatch.Cancelled += counter;
			try
			{
				using(Form form = new Form { ClientSize = new Size(200, 100), StartPosition = FormStartPosition.Manual, ShowInTaskbar = false })
				using(System.Windows.Forms.Timer failsafe = new System.Windows.Forms.Timer { Interval = 5000 })
				{
					Button ok = new Button { Text = "OK" };
					Button cancel = new Button { Text = "Cancel", DialogResult = DialogResult.Cancel, Left = 100 };
					form.Controls.Add(ok);
					form.Controls.Add(cancel);
					form.CancelButton = cancel;
					// The two OK paths upstream dialogs use: Close() alone, or a set result
					if(how == Ending.OkWithResult) ok.DialogResult = DialogResult.OK;
					else ok.Click += (s, e) => form.Close();

					failsafe.Tick += (s, e) => { failsafe.Stop(); form.Close(); };
					form.Shown += (s, e) =>
					{
						DialogCancelWatch.Attach(form);
						failsafe.Start();
						form.BeginInvoke(new MethodInvoker(() =>
						{
							// Escape reaches the same PerformClick through ProcessDialogKey
							if(how == Ending.CancelButton) cancel.PerformClick();
							else ok.PerformClick();
						}));
					};
					form.ShowDialog();
					Assert.True(failsafe.Enabled, "the dialog was closed by the failsafe, not by " + how);
				}
			}
			finally { DialogCancelWatch.Cancelled -= counter; }
			return reported;
		}

		[Fact]
		public void OnlyTheCancelButtonCounts()
		{
			Assert.Equal(1, CancelsReported(Ending.CancelButton));
			Assert.Equal(0, CancelsReported(Ending.OkThenClose));
			Assert.Equal(0, CancelsReported(Ending.OkWithResult));
		}
	}
}
