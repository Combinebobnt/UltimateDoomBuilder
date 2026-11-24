#region ================== Namespaces

using System;
using System.Collections.Generic;
using CodeImp.DoomBuilder.Geometry;
using CodeImp.DoomBuilder.IO;

#endregion

namespace CodeImp.DoomBuilder.Editing
{
	// A user-placed map-space point that tools can use as their origin (rotation pivot, mirror axis).
	// Saved per map in the .dbs, not part of undo history, and placing it doesn't mark the map changed.
	public sealed class Cursor2D
	{
		#region ================== Variables

		private bool isset;
		private Vector2D position;
		private int version;

		#endregion

		#region ================== Properties

		public bool IsSet { get { return isset; } }

		// Meaningless while IsSet is false
		public Vector2D Position { get { return position; } }

		// Bumped on every change, including config reads that raise no Changed event
		public int Version { get { return version; } }

		#endregion

		#region ================== Events

		public event EventHandler Changed;

		#endregion

		#region ================== Methods

		// Places the cursor, rounded to the current map format's vertex precision
		public void Place(Vector2D pos)
		{
			int decimals = (General.Map != null && General.Map.FormatInterface != null) ? General.Map.FormatInterface.VertexDecimals : 3;
			Place(pos, decimals);
		}

		internal void Place(Vector2D pos, int decimals)
		{
			if(!pos.IsFinite()) throw new ArgumentException("The 2D cursor position must be finite.");
			position = new Vector2D(Math.Round(pos.x, decimals), Math.Round(pos.y, decimals));
			isset = true;
			version++;
			RaiseChanged();
		}

		public void Clear()
		{
			isset = false;
			version++;
			RaiseChanged();
		}

		// Center of the box around the points and each thing padded by its size (key: position, value:
		// Thing.Size), the same box Edit Selection mode builds. False when there is nothing to enclose.
		internal static bool TryGetSelectionCenter(IEnumerable<Vector2D> points, IEnumerable<KeyValuePair<Vector2D, double>> things, out Vector2D center)
		{
			double left = double.MaxValue, top = double.MaxValue;
			double right = double.MinValue, bottom = double.MinValue;
			bool any = false;

			foreach(Vector2D p in points)
			{
				left = Math.Min(left, p.x);
				top = Math.Min(top, p.y);
				right = Math.Max(right, p.x);
				bottom = Math.Max(bottom, p.y);
				any = true;
			}

			foreach(KeyValuePair<Vector2D, double> t in things)
			{
				left = Math.Min(left, t.Key.x - t.Value);
				top = Math.Min(top, t.Key.y - t.Value);
				right = Math.Max(right, t.Key.x + t.Value);
				bottom = Math.Max(bottom, t.Key.y + t.Value);
				any = true;
			}

			center = any ? new Vector2D((left + right) * 0.5, (top + bottom) * 0.5) : new Vector2D();
			return any;
		}

		internal void WriteToConfig(Configuration cfg, string path)
		{
			cfg.WriteSetting(path + ".set", isset);
			if(isset)
			{
				cfg.WriteSetting(path + ".x", position.x);
				cfg.WriteSetting(path + ".y", position.y);
			}
			else
			{
				cfg.DeleteSetting(path + ".x");
				cfg.DeleteSetting(path + ".y");
			}
		}

		// Raises no Changed event: this runs mid map load or switch, and the load's own redraw shows it
		internal void ReadFromConfig(Configuration cfg, string path)
		{
			isset = false;
			if(cfg.ReadSetting(path + ".set", false))
			{
				Vector2D pos = new Vector2D(cfg.ReadSetting(path + ".x", double.NaN), cfg.ReadSetting(path + ".y", double.NaN));
				if(pos.IsFinite())
				{
					position = pos;
					isset = true;
				}
			}
			version++;
		}

		private void RaiseChanged()
		{
			EventHandler handler = Changed;
			if(handler != null) handler(this, EventArgs.Empty);
		}

		#endregion
	}
}
