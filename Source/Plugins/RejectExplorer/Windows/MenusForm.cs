using System;
using System.Windows.Forms;

namespace CodeImp.DoomBuilder.RejectExplorer
{
	public partial class MenusForm : Form
	{
		public ToolStripButton ColorConfiguration { get { return colorconfiguration; } }
		public ToolStripMenuItem ColorConfigurationItem { get { return colorconfigurationitem; } }

		private ToolStripMenuItem colorconfigurationitem;

		public MenusForm()
		{
			InitializeComponent();
			colorconfigurationitem = new ToolStripMenuItem("Configure &Colors...", colorconfiguration.Image) { Tag = colorconfiguration.Tag };
			colorconfigurationitem.Click += InvokeTaggedAction;
		}

		// This invokes an action from control event
		private void InvokeTaggedAction(object sender, EventArgs e)
		{
			General.Interface.InvokeTaggedAction(sender, e);
		}
	}
}
