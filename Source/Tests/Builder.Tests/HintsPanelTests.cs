using System.Drawing;
using System.Windows.Forms;
using CodeImp.DoomBuilder.Controls;

namespace Builder.Tests
{
	// Mono drops RichTextBox character formatting set before its handle exists
	[Collection("WindowsUI")]
	public class HintsPanelTests
	{
		private static void Pump(int ms)
		{
			DateTime end = DateTime.Now.AddMilliseconds(ms);
			while(DateTime.Now < end) { Application.DoEvents(); Thread.Sleep(10); }
		}

		[Fact]
		public void HintsSetBeforeHandleKeepBoldOnceShown()
		{
			using(Form form = new Form { ClientSize = new Size(400, 300), StartPosition = FormStartPosition.Manual })
			{
				TabControl tabs = new TabControl { Dock = DockStyle.Fill };
				TabPage other = new TabPage("Undo");
				TabPage help = new TabPage("Help");
				tabs.TabPages.Add(other);
				tabs.TabPages.Add(help);
				form.Controls.Add(tabs);
				form.Show();
				Pump(100);

				// The state the editor's first render arrives in: visible, no handle yet
				HintsPanel panel = new HintsPanel { Dock = DockStyle.Fill };
				RichTextBox box = panel.Controls.OfType<RichTextBox>().Single();
				Assert.True(box.Visible && !box.IsHandleCreated, "precondition: visible box with no handle");
				panel.SetHints("{\\rtf1 Press {\\b KEYNAME} to go}");
				help.Controls.Add(panel);
				tabs.SelectedTab = help;
				Pump(100);

				int at = box.Text.IndexOf("KEYNAME", StringComparison.Ordinal);
				Assert.True(at >= 0, "hint text missing: " + box.Text);
				box.Select(at, "KEYNAME".Length);
				Assert.True(box.SelectionFont.Bold, "key name lost its bold");
				box.Select(0, "Press".Length);
				Assert.False(box.SelectionFont.Bold, "plain text came out bold");
			}
		}
	}
}
