using System;
using System.Drawing;
using System.Windows.Forms;
using CodeImp.DoomBuilder.Controls;

namespace CodeImp.DoomBuilder.BuilderModes
{
	internal partial class TexturePaletteForm : Form, IMessageFilter
	{
		private bool ready;
		private readonly bool visual;
		private readonly Point cursororigin;
		internal TextureHistoryClickEventArgs Pick { get; private set; }

		internal TexturePaletteForm(bool showtextures, bool showflats, bool visual)
		{
			InitializeComponent();
			this.visual = visual;
			cursororigin = Cursor.Position;
			view.SetEntries(General.Map.TextureHistory, showtextures, showflats);
			view.EntryClicked += (sender, e) =>
			{
				Pick = e;
				DialogResult = DialogResult.OK;
				Close();
			};
			view.MouseUp += (sender, e) => Cancel();
			if(view.EntryCount == 0)
			{
				var empty = new Label { Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleCenter,
					Text = General.Map.TextureHistory.Capacity == 0 ? "Disabled (texturehistorysize = 0)" : "No recent textures.\nChoose or copy a texture first." };
				empty.MouseUp += (sender, e) => Cancel();
				view.Visible = false;
				Controls.Add(empty);
				empty.BringToFront();
			}
			Size preferred = view.EntryCount == 0 ? new Size(280, 100) : view.GetPaletteSize(6, 3);
			Rectangle display = General.Interface.Display.RectangleToScreen(General.Interface.Display.ClientRectangle);
			Point origin = visual ? new Point(display.Left + display.Width / 2, display.Top + display.Height / 2) : cursororigin;
			Rectangle screen = Screen.FromPoint(origin).WorkingArea;
			ClientSize = new Size(Math.Min(screen.Width, preferred.Width + Padding.Horizontal), Math.Min(screen.Height, preferred.Height + Padding.Vertical));
			if(visual) origin.Offset(-Width / 2, -Height / 2);
			Location = new Point(Math.Max(screen.Left, Math.Min(origin.X, screen.Right - Width)), Math.Max(screen.Top, Math.Min(origin.Y, screen.Bottom - Height)));
		}

		internal static TextureHistoryClickEventArgs Choose(bool showtextures, bool showflats, bool visual)
		{
			using(TexturePaletteForm form = new TexturePaletteForm(showtextures, showflats, visual))
				return General.Interface.ShowPopupDialog(form) == DialogResult.OK ? form.Pick : null;
		}

		protected override void OnShown(EventArgs e)
		{
			base.OnShown(e);
			ready = true;
			Application.AddMessageFilter(this);
			if(view.EntryCount > 0)
			{
				if(visual) Cursor.Position = view.PointToScreen(view.FirstEntryLocation);
				else
				{
					Point offset = PointToClient(view.PointToScreen(view.FirstEntryLocation));
					Rectangle screen = Screen.FromPoint(cursororigin).WorkingArea;
					Location = new Point(Math.Max(screen.Left, Math.Min(cursororigin.X - offset.X, screen.Right - Width)),
						Math.Max(screen.Top, Math.Min(cursororigin.Y - offset.Y, screen.Bottom - Height)));
				}
			}
		}

		private void Cancel()
		{
			if(!ready || IsDisposed || DialogResult == DialogResult.OK) return;
			DialogResult = DialogResult.Cancel;
			Close();
		}

		protected override void OnDeactivate(EventArgs e) { base.OnDeactivate(e); Cancel(); }
		protected override void OnMouseUp(MouseEventArgs e) { base.OnMouseUp(e); Cancel(); }

		protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
		{
			if(keyData == Keys.Escape) { Cancel(); return true; }
			if(keyData >= Keys.D1 && keyData <= Keys.D9 && view.PickEntry((int)keyData - (int)Keys.D1, MouseButtons.Left)) return true;
			return base.ProcessCmdKey(ref msg, keyData);
		}

		public bool PreFilterMessage(ref Message message)
		{
			if(!ready || !(message.Msg == 0x201 || message.Msg == 0x204 || message.Msg == 0x207 || message.Msg == 0xA1)
				|| !ShouldCancelOutsideClick(Bounds, Cursor.Position, view.OwnsContextMenuInput(message.HWnd))) return false;
			// Swallow the cancelling click so it doesn't also act on the map
			Cancel();
			return true;
		}

		internal static bool ShouldCancelOutsideClick(Rectangle palettebounds, Point cursor, bool ownedmenuinput)
		{
			return !ownedmenuinput && !palettebounds.Contains(cursor);
		}

		protected override void OnFormClosed(FormClosedEventArgs e)
		{
			ready = false;
			view.CloseContextMenu();
			Application.RemoveMessageFilter(this);
			base.OnFormClosed(e);
		}
	}
}
