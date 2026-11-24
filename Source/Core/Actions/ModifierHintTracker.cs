#region ================== Namespaces

using System;
using System.Windows.Forms;

#endregion

namespace CodeImp.DoomBuilder.Actions
{
	// Decides when the Help panel shows what the held modifier keys do. MainForm owns the timer.
	internal sealed class ModifierHintTracker
	{
		#region ================== Variables

		private readonly System.Action restarttimer;
		private readonly System.Action stoptimer;
		private readonly System.Action<int> setoverlay;
		private readonly Func<bool> canshow;

		private int mask;      // Modifiers held according to the last key event
		private int shownmask; // Overlay on screen, 0 when none
		private bool chorded;  // A non-modifier key was pressed since the mask last changed

		#endregion

		#region ================== Properties

		public int HeldMask { get { return mask; } }

		#endregion

		#region ================== Constructor

		public ModifierHintTracker(System.Action restarttimer, System.Action stoptimer, System.Action<int> setoverlay, Func<bool> canshow)
		{
			this.restarttimer = restarttimer;
			this.stoptimer = stoptimer;
			this.setoverlay = setoverlay;
			this.canshow = canshow;
		}

		#endregion

		#region ================== Input

		public void KeyDown(Keys keydata)
		{
			int newmask = (int)keydata & HintsManager.MODIFIER_MASK;
			Keys keycode = keydata & Keys.KeyCode;
			if(keycode != Keys.None && !HintsManager.IsModifierKey(keycode))
			{
				if(newmask == 0)
				{
					SetMask(0);
					return;
				}

				// A chord such as Ctrl+S: the user knows what they want, so stay out of the way
				mask = newmask;
				chorded = true;
				stoptimer();
				Clear();
				return;
			}
			SetMask(newmask);
		}

		public void KeyUp(Keys keydata)
		{
			// The released modifier may still be reported as held
			int newmask = (int)keydata & HintsManager.MODIFIER_MASK;
			switch(keydata & Keys.KeyCode)
			{
				case Keys.ControlKey: case Keys.LControlKey: case Keys.RControlKey: newmask &= ~(int)Keys.Control; break;
				case Keys.ShiftKey: case Keys.LShiftKey: case Keys.RShiftKey: newmask &= ~(int)Keys.Shift; break;
				case Keys.Menu: case Keys.LMenu: case Keys.RMenu: newmask &= ~(int)Keys.Alt; break;
			}
			SetMask(newmask);
		}

		public void Release()
		{
			mask = 0;
			chorded = false;
			stoptimer();
			Clear();
		}

		public void TimerFired()
		{
			stoptimer();
			if(mask != 0 && !chorded && canshow()) Show(mask);
		}

		// Key-ups released inside modal dialogs or open menus never reach the main window
		public void Poll(Keys modifierkeys)
		{
			int current = (int)modifierkeys & HintsManager.MODIFIER_MASK;
			if(mask != 0 && current != mask) SetMask(current);
		}

		#endregion

		#region ================== Private

		private void SetMask(int newmask)
		{
			if(newmask == mask) return; // Auto-repeat of a held modifier
			mask = newmask;
			chorded = false;

			if(mask == 0)
			{
				stoptimer();
				Clear();
			}
			else if(shownmask != 0 && canshow())
			{
				// Already showing a reminder: follow the chord without another delay
				stoptimer();
				Show(mask);
			}
			else
			{
				Clear();
				restarttimer();
			}
		}

		private void Show(int newmask)
		{
			shownmask = newmask;
			setoverlay(newmask);
		}

		private void Clear()
		{
			if(shownmask == 0) return;
			shownmask = 0;
			setoverlay(0);
		}

		#endregion
	}
}
