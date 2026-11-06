
#region ================== Copyright (c) 2007 Pascal vd Heiden

/*
 * Copyright (c) 2007 Pascal vd Heiden, www.codeimp.com
 * This program is released under GNU General Public License
 * 
 * This program is distributed in the hope that it will be useful,
 * but WITHOUT ANY WARRANTY; without even the implied warranty of
 * MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
 * GNU General Public License for more details.
 * 
 */

#endregion

#region ================== Namespaces

using System;
using System.Drawing;
using System.Windows.Forms;

#endregion

namespace CodeImp.DoomBuilder.Controls
{
	public class RenderTargetControl : Panel
	{
		#region ================== Constants

		#endregion

		#region ================== Variables

		private ToolTip tooltip; //mxd

		#if MONO_WINFORMS && !DEBUG
		private bool paintfaultpending;
		#endif

		#endregion

		#region ================== Properties
		
		public event KeyEventHandler OnKeyReleased; //mxd. Sometimes it's handeled here, not by MainForm
		public Point LocationAbs { get { return this.PointToScreen(new Point(-(General.MainWindow.Width - General.MainWindow.ClientSize.Width) / 2, 0)); } } //mxd

		#endregion

		#region ================== Constructor / Disposer

		// Constructor
		internal RenderTargetControl()
		{
			// Initialize
			this.SetStyle(ControlStyles.FixedWidth, true);
			this.SetStyle(ControlStyles.FixedHeight, true);

			//mxd. Create tooltip
			tooltip = new ToolTip { UseAnimation = false, UseFading = false, InitialDelay = 0, AutoPopDelay = 9000 };
		}

		//mxd
		protected override void Dispose(bool disposing)
		{
			if(disposing)
			{
				tooltip.Dispose();
				tooltip = null;
			}
			base.Dispose(disposing);
		}

		#endregion

		#region ================== Overrides
		
		#if MONO_WINFORMS
		// This workarounds menubar getting the focus all the time
		protected override bool IsInputChar(char charCode)
		{
			return true;
		}
		#endif

		#if MONO_WINFORMS && !DEBUG
		// Mono's NativeWindow.WndProc hides a control whose WM_PAINT throws, so faults must not leave it
		protected override void OnPaint(PaintEventArgs e)
		{
			try { base.OnPaint(e); }
			catch(Exception ex) { ReportPaintFault(ex); }
		}

		protected override void OnPaintBackground(PaintEventArgs e)
		{
			try { base.OnPaintBackground(e); }
			catch(Exception ex) { ReportPaintFault(ex); }
		}

		// Reported after WM_PAINT returns, one at a time; faults while one is pending are only logged
		private void ReportPaintFault(Exception ex)
		{
			if(paintfaultpending)
			{
				General.WriteLogLine("Paint exception suppressed while another is reported: " + ex.Message);
				return;
			}

			paintfaultpending = true;
			try
			{
				BeginInvoke(new Action(() =>
				{
					try { Application.OnThreadException(ex); }
					finally { paintfaultpending = false; }
				}));
			}
			catch(Exception)
			{
				paintfaultpending = false;
				Application.OnThreadException(ex);
			}
		}
		#endif
		
		//mxd
		protected override void OnKeyUp(KeyEventArgs e) 
		{
			if(OnKeyReleased != null) OnKeyReleased(this, e);
		}
		
		#endregion

		#region ================== Methods

		// This sets up the control to display the splash logo
		public void SetSplashLogoDisplay()
		{
			// Change display to show splash logo
			this.SetStyle(ControlStyles.SupportsTransparentBackColor, false);
			this.SetStyle(ControlStyles.ContainerControl, true);
			this.SetStyle(ControlStyles.OptimizedDoubleBuffer, true);
			this.SetStyle(ControlStyles.UserPaint, true);
			this.SetStyle(ControlStyles.AllPaintingInWmPaint, true);
			this.SetStyle(ControlStyles.Opaque, false);
			this.UpdateStyles();
            this.BackColor = Color.FromArgb(32, 32, 40);
			this.BackgroundImage = Properties.Resources.Splash3_trans;
			this.BackgroundImageLayout = ImageLayout.Center;
		}
		
		// This sets up the control for manual rendering
		public void SetManualRendering()
		{
			// Change display for rendering
			this.SetStyle(ControlStyles.SupportsTransparentBackColor, false);
			this.SetStyle(ControlStyles.ContainerControl, true);
			this.SetStyle(ControlStyles.OptimizedDoubleBuffer, false);
			this.SetStyle(ControlStyles.UserPaint, true);
			this.SetStyle(ControlStyles.AllPaintingInWmPaint, true);
			this.SetStyle(ControlStyles.Opaque, true);
			this.UpdateStyles();
			this.BackColor = Color.Black;
			this.BackgroundImage = null;
			this.BackgroundImageLayout = ImageLayout.None;
		}

		//mxd. This shows tooltip at given position
		public void ShowToolTip(string title, string text, int x, int y)
		{
			tooltip.ToolTipTitle = title;
			tooltip.Show(text, this, x, y);
		}

		//mxd. This hides it
		public void HideToolTip()
		{
			tooltip.Hide(this);
		}

		#endregion
	}
}
