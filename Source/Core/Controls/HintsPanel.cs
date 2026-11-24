using System;
using System.Windows.Forms;

namespace CodeImp.DoomBuilder.Controls
{
	internal partial class HintsPanel : UserControl
	{
		private string lasthints;
		private bool reapplypending;

		public HintsPanel() 
		{
			InitializeComponent();
			hints.Clear();
			hints.HandleCreated += hints_HandleCreated;
		}

		//hints should be in rtf markup!
		internal void SetHints(string hintsText) 
		{
			lasthints = hintsText;
			reapplypending = !hints.IsHandleCreated;
			hints.Clear();
			hints.SelectedRtf = hintsText;
		}

		internal void ClearHints() 
		{
			lasthints = null;
			reapplypending = false;
			hints.Clear();
		}

		// Mono's RichTextBox drops character formatting set before its handle exists, just after creating it
		private void hints_HandleCreated(object sender, EventArgs e)
		{
			if(reapplypending) hints.BeginInvoke(new MethodInvoker(ReapplyHints));
		}

		private void ReapplyHints()
		{
			if(!reapplypending) return;
			reapplypending = false;
			if(lasthints != null) SetHints(lasthints);
		}

		// Fight TextBoxes habit of not releasing the focus by using a carefully placed label
		private void hints_Enter(object sender, EventArgs e) 
		{
			label1.Focus();
		}
	}
}
