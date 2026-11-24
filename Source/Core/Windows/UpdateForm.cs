using System;
using System.Diagnostics;
using System.Windows.Forms;

namespace CodeImp.DoomBuilder.Windows
{
	public partial class UpdateForm : DelayedForm
	{
		public bool IgnoreThisUpdate { get { return ignorethisupdate.Checked; } }
		private int remoterev;
		private string releasepage;
		private bool forkrelease;

		public UpdateForm(int remoterev, string changelog)
		{
			InitializeComponent();
			Setup(remoterev, changelog);
		}

		// A fork release: the button opens its release page instead of running the updater
		public UpdateForm(string tag, string pageurl, string notes)
		{
			InitializeComponent();
			this.Text = this.Text.Replace("R[rev]", tag);
			this.label.Text = label.Text.Replace("R[rev]", tag).Replace("current revision", "your version");
			this.label2.Visible = false;
			this.hint.Visible = false;
			this.changelog.Text = notes.Replace("\n", Environment.NewLine);
			this.downloadupdate.Text = "Open release page";
			this.releasepage = pageurl;
			this.forkrelease = true;
		}

		private void Setup(int remoterev, string changelog)
		{
			this.Text = this.Text.Replace("[rev]", remoterev.ToString());
			this.label.Text = label.Text.Replace("[rev]", remoterev.ToString());
			this.changelog.SelectedRtf = changelog;
			this.remoterev = remoterev;
		}

		private void UpdateForm_Shown(object sender, EventArgs e)
		{
			this.changelog.Focus();
		}

		private void downloadupdate_Click(object sender, EventArgs e)
		{
			this.DialogResult = DialogResult.OK;

			if(forkrelease)
			{
				ForkUpdateChecker.OpenReleasePage(releasepage);
				this.Close();
				return;
			}

			// Working directory must be set
			Process.Start(new ProcessStartInfo { WorkingDirectory = General.AppPath, FileName = "Updater.exe", Arguments = "-rev " + remoterev });

			this.Close();
		}

		private void cancel_Click(object sender, EventArgs e)
		{
			this.DialogResult = DialogResult.Cancel;
			this.Close();
		}
	}
}
