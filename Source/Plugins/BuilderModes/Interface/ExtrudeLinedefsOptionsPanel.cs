using System;
using System.Windows.Forms;

namespace CodeImp.DoomBuilder.BuilderModes.Interface
{
	public partial class ExtrudeLinedefsOptionsPanel : UserControl
	{
		#region ================== Event Handlers

		public event EventHandler OnValueChanged;

		#endregion

		#region ================== Variables

		private bool blockevents;

		#endregion

		#region ================== Properties

		public int Distance { get { return (int)distance.Value; } set { distance.Value = General.Clamp(value, (int)distance.Minimum, (int)distance.Maximum); } }
		public int FloorDelta { get { return (int)floordelta.Value; } }
		public int CeilingDelta { get { return (int)ceildelta.Value; } }

		#endregion

		#region ================== Constructor

		public ExtrudeLinedefsOptionsPanel()
		{
			InitializeComponent();
		}

		#endregion

		#region ================== Methods

		public void SetValues(int distance, int floordelta, int ceildelta)
		{
			blockevents = true;

			this.distance.Value = General.Clamp(distance, (int)this.distance.Minimum, (int)this.distance.Maximum);
			this.floordelta.Value = General.Clamp(floordelta, (int)this.floordelta.Minimum, (int)this.floordelta.Maximum);
			this.ceildelta.Value = General.Clamp(ceildelta, (int)this.ceildelta.Minimum, (int)this.ceildelta.Maximum);

			blockevents = false;
		}

		public void Register()
		{
			General.Interface.BeginToolbarUpdate();

#if MONO_WINFORMS
			// Mono fix
			toolstrip.Items.Clear();
#endif

			General.Interface.AddButton(distancelabel);
			General.Interface.AddButton(distance);
			General.Interface.AddButton(flip);
			General.Interface.AddButton(separator1);
			General.Interface.AddButton(floorlabel);
			General.Interface.AddButton(floordelta);
			General.Interface.AddButton(ceillabel);
			General.Interface.AddButton(ceildelta);
			General.Interface.AddButton(reset);
			General.Interface.AddButton(separator2);
			General.Interface.AddButton(apply);
			General.Interface.AddButton(cancel);
			General.Interface.EndToolbarUpdate();
		}

		public void Unregister()
		{
			General.Interface.BeginToolbarUpdate();
			General.Interface.RemoveButton(cancel);
			General.Interface.RemoveButton(apply);
			General.Interface.RemoveButton(separator2);
			General.Interface.RemoveButton(reset);
			General.Interface.RemoveButton(ceildelta);
			General.Interface.RemoveButton(ceillabel);
			General.Interface.RemoveButton(floordelta);
			General.Interface.RemoveButton(floorlabel);
			General.Interface.RemoveButton(separator1);
			General.Interface.RemoveButton(flip);
			General.Interface.RemoveButton(distance);
			General.Interface.RemoveButton(distancelabel);
			General.Interface.EndToolbarUpdate();
		}

		#endregion

		#region ================== Events

		private void apply_Click(object sender, EventArgs e)
		{
			General.Editing.AcceptMode();
		}

		private void cancel_Click(object sender, EventArgs e)
		{
			General.Editing.CancelMode();
		}

		private void OnUIValuesChanged(object sender, EventArgs e)
		{
			if(!blockevents && OnValueChanged != null) OnValueChanged(this, EventArgs.Empty);
		}

		private void flip_Click(object sender, EventArgs e)
		{
			distance.Value = -distance.Value;
		}

		private void reset_Click(object sender, EventArgs e)
		{
			SetValues(ExtrudeLinedefsMode.DEFAULT_DISTANCE, 0, 0);
			if(OnValueChanged != null) OnValueChanged(this, EventArgs.Empty);
		}

		#endregion
	}
}
