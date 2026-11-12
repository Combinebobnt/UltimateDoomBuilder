using System;
using System.Collections.Generic;
using CodeImp.DoomBuilder.Geometry;

namespace CodeImp.DoomBuilder.TagRange
{
	public static class GridOrder
	{
		public enum Order { Selection, Rows, Columns }
		public enum Corner { TopLeft, TopRight, BottomLeft, BottomRight }
		public enum LineFilter { All, Horizontal, Vertical }

		public struct Entry
		{
			public readonly int Index;
			public readonly int Row;
			public readonly int Column;

			internal Entry(int index, int row, int column)
			{
				Index = index;
				Row = row;
				Column = column;
			}
		}

		public sealed class Result
		{
			public Entry[] Entries { get; private set; }
			public int RowCount { get; private set; }
			public int ColumnCount { get; private set; }
			public Order Mode { get; private set; }

			internal Result(Entry[] entries, int rows, int columns, Order mode)
			{
				Entries = entries;
				RowCount = rows;
				ColumnCount = columns;
				Mode = mode;
			}
		}

		public sealed class RangeResult
		{
			public List<int> Values { get; private set; }
			public bool TagsUsed { get; internal set; }
			public bool OutOfRange { get; internal set; }

			internal RangeResult(int count) { Values = new List<int>(count); }
		}

		public static Result Sort(IList<Vector2D> anchors, Order order, Corner corner)
		{
			Entry[] entries = new Entry[anchors.Count];
			if(order == Order.Selection)
			{
				for(int i = 0; i < entries.Length; i++) entries[i] = new Entry(i, 0, i);
				return new Result(entries, entries.Length > 0 ? 1 : 0, entries.Length, order);
			}

			int columns, rows;
			int[] xrank = Rank(anchors, true, out columns);
			int[] yrank = Rank(anchors, false, out rows);
			bool right = corner == Corner.TopRight || corner == Corner.BottomRight;
			bool top = corner == Corner.TopLeft || corner == Corner.TopRight;
			for(int i = 0; i < entries.Length; i++)
				entries[i] = new Entry(i, top ? rows - 1 - yrank[i] : yrank[i], right ? columns - 1 - xrank[i] : xrank[i]);

			Array.Sort(entries, (a, b) =>
			{
				int primary = order == Order.Columns ? a.Column.CompareTo(b.Column) : a.Row.CompareTo(b.Row);
				if(primary != 0) return primary;
				int secondary = order == Order.Columns ? a.Row.CompareTo(b.Row) : a.Column.CompareTo(b.Column);
				return secondary != 0 ? secondary : a.Index.CompareTo(b.Index);
			});
			return new Result(entries, rows, columns, order);
		}

		private static int[] Rank(IList<Vector2D> anchors, bool xaxis, out int count)
		{
			int[] indices = new int[anchors.Count];
			int[] ranks = new int[anchors.Count];
			for(int i = 0; i < indices.Length; i++) indices[i] = i;
			Array.Sort(indices, (a, b) => (xaxis ? anchors[a].x : anchors[a].y).CompareTo(xaxis ? anchors[b].x : anchors[b].y));
			count = 0;
			double previous = 0;
			foreach(int index in indices)
			{
				double value = xaxis ? anchors[index].x : anchors[index].y;
				if(count == 0 || value - previous > 1.0) count++;
				ranks[index] = count - 1;
				previous = value;
			}
			return ranks;
		}

		public static bool MatchesLine(Vector2D start, Vector2D end, LineFilter filter)
		{
			if(filter == LineFilter.All) return true;
			double dx = Math.Abs(end.x - start.x);
			double dy = Math.Abs(end.y - start.y);
			if(dx == 0 && dy == 0) return false;
			// One degree from either axis, independent of line direction and length.
			double tolerance = Math.Tan(Math.PI / 180.0);
			return filter == LineFilter.Horizontal ? dy <= dx * tolerance : dx <= dy * tolerance;
		}

		public static void GetValueLimits(bool argument, bool hexen, int mintag, int maxtag, out int minimum, out int maximum)
		{
			minimum = argument ? (hexen ? 0 : int.MinValue) : mintag;
			maximum = argument ? (hexen ? 255 : int.MaxValue) : maxtag;
		}

		public static int[] GetTargets(bool sector, bool hastag, bool hasargs)
		{
			if(sector) return new[] { 0 };
			if(hasargs) return hastag ? new[] { 0, 1, 2, 3, 4, 5 } : new[] { 1, 2, 3, 4, 5 };
			return hastag ? new[] { 0 } : new int[0];
		}

		public static RangeResult CreateRange(Result grid, IList<int> initialvalues, int start, int step, int? rowstride,
			bool relative, bool skipused, ISet<int> usedtags, int minimum, int maximum)
		{
			RangeResult result = new RangeResult(grid.Entries.Length);
			skipused = skipused && !rowstride.HasValue && usedtags != null;
			long offset = 0;
			foreach(Entry entry in grid.Entries)
			{
				long value = start;
				if(rowstride.HasValue)
					value += grid.Mode == Order.Columns
						? (long)entry.Row * step + (long)entry.Column * rowstride.Value
						: (long)entry.Column * step + (long)entry.Row * rowstride.Value;
				else
					value += offset;
				if(relative) value += initialvalues[entry.Index];

				while(true)
				{
					if(value < minimum || value > maximum)
					{
						result.OutOfRange = true;
						return result;
					}
					bool used = usedtags != null && usedtags.Contains((int)value);
					if(!skipused || !used)
					{
						result.TagsUsed |= used;
						break;
					}
					if(step == 0)
					{
						result.OutOfRange = true;
						return result;
					}
					value += step;
					offset += step;
				}
				result.Values.Add((int)value);
				offset += step;
			}
			return result;
		}
	}
}
