using System;
using System.Windows.Forms;

namespace CodeImp.DoomBuilder.BuilderModes
{
	internal partial class DrawLineOptionsPanel : UserControl
	{
		public event EventHandler OnContinuousDrawingChanged;
		public event EventHandler OnAutoCloseDrawingChanged;
		public event EventHandler OnShowGuidelinesChanged;
		public event EventHandler OnMirrorXChanged;
		public event EventHandler OnMirrorYChanged;

		public bool ContinuousDrawing { get { return continuousdrawing.Checked; } set { continuousdrawing.Checked = value; } }
		public bool AutoCloseDrawing { get { return autoclosedrawing.Checked; } set { autoclosedrawing.Checked = value; } }
		public bool ShowGuidelines { get { return showguidelines.Checked; } set { showguidelines.Checked = value; } }
		public bool MirrorX { get { return mirrorx.Checked; } set { mirrorx.Checked = value; } }
		public bool MirrorY { get { return mirrory.Checked; } set { mirrory.Checked = value; } }
		
		private ToolStripMenuItem menu;

		public DrawLineOptionsPanel()
		{
			InitializeComponent();
		}

		public void Register()
		{
			General.Interface.BeginToolbarUpdate();

#if MONO_WINFORMS
			// Mono fix
			toolStrip1.Items.Clear();
#endif

			General.Interface.AddButton(continuousdrawing);
			General.Interface.AddButton(autoclosedrawing);
			General.Interface.AddButton(showguidelines);
			General.Interface.AddButton(mirrorx);
			General.Interface.AddButton(mirrory);
			General.Interface.EndToolbarUpdate();
			if(menu == null) menu = MenusForm.CreatePanelMenu("&Draw", continuousdrawing, autoclosedrawing, showguidelines, mirrorx, mirrory);
			MenusForm.ShowPanelMenu(menu);
		}

		public void Unregister()
		{
			if(menu != null) General.Interface.RemoveMenu(menu);
			General.Interface.BeginToolbarUpdate();
			General.Interface.RemoveButton(mirrory);
			General.Interface.RemoveButton(mirrorx);
			General.Interface.RemoveButton(showguidelines);
			General.Interface.RemoveButton(autoclosedrawing);
			General.Interface.RemoveButton(continuousdrawing);
			General.Interface.EndToolbarUpdate();
		}

		private void continuousdrawing_CheckedChanged(object sender, EventArgs e)
		{
			if(OnContinuousDrawingChanged != null) OnContinuousDrawingChanged(continuousdrawing.Checked, EventArgs.Empty);
		}

		private void autoclosedrawing_CheckedChanged(object sender, EventArgs e)
		{
			if(OnAutoCloseDrawingChanged != null) OnAutoCloseDrawingChanged(autoclosedrawing.Checked, EventArgs.Empty);
		}

		private void showguidelines_CheckedChanged(object sender, EventArgs e)
		{
			if(OnShowGuidelinesChanged != null) OnShowGuidelinesChanged(showguidelines.Checked, EventArgs.Empty);
		}

		private void mirrorx_CheckedChanged(object sender, EventArgs e)
		{
			if(OnMirrorXChanged != null) OnMirrorXChanged(mirrorx.Checked, EventArgs.Empty);
		}

		private void mirrory_CheckedChanged(object sender, EventArgs e)
		{
			if(OnMirrorYChanged != null) OnMirrorYChanged(mirrory.Checked, EventArgs.Empty);
		}
	}
}
