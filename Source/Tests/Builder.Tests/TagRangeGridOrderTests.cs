using System;
using System.Collections.Generic;
using System.Linq;
using CodeImp.DoomBuilder.Geometry;
using CodeImp.DoomBuilder.TagRange;
using Xunit;

namespace Builder.Tests
{
	public class TagRangeGridOrderTests
	{
		[Fact]
		public void FormatCapabilitiesLimitTargets()
		{
			Assert.Equal(new[] { 0 }, GridOrder.GetTargets(false, true, false));
			Assert.Equal(new[] { 1, 2, 3, 4, 5 }, GridOrder.GetTargets(false, false, true));
			Assert.Equal(new[] { 0, 1, 2, 3, 4, 5 }, GridOrder.GetTargets(false, true, true));
			Assert.Empty(GridOrder.GetTargets(false, false, false));
			Assert.Equal(new[] { 0 }, GridOrder.GetTargets(true, false, true));
		}

		[Theory]
		[InlineData(GridOrder.Order.Rows, GridOrder.Corner.TopLeft)]
		[InlineData(GridOrder.Order.Rows, GridOrder.Corner.TopRight)]
		[InlineData(GridOrder.Order.Rows, GridOrder.Corner.BottomLeft)]
		[InlineData(GridOrder.Order.Rows, GridOrder.Corner.BottomRight)]
		[InlineData(GridOrder.Order.Columns, GridOrder.Corner.TopLeft)]
		[InlineData(GridOrder.Order.Columns, GridOrder.Corner.TopRight)]
		[InlineData(GridOrder.Order.Columns, GridOrder.Corner.BottomLeft)]
		[InlineData(GridOrder.Order.Columns, GridOrder.Corner.BottomRight)]
		public void EveryOrderAndCornerVisitsFourByThreeGrid(GridOrder.Order order, GridOrder.Corner corner)
		{
			List<Vector2D> anchors = Cells(4, 3);
			anchors.Reverse();
			GridOrder.Result grid = GridOrder.Sort(anchors, order, corner);
			GridOrder.RangeResult range = Range(grid, 7, 3, 29);
			GridOrder.RangeResult sequential = Range(grid, 7, 3);
			Assert.Equal(3, grid.RowCount);
			Assert.Equal(4, grid.ColumnCount);
			Assert.Equal(12, grid.Entries.Select(e => e.Index).Distinct().Count());
			for(int i = 0; i < 12; i++)
			{
				int row = order == GridOrder.Order.Columns ? i % 3 : i / 4;
				int col = order == GridOrder.Order.Columns ? i / 3 : i % 4;
				int physicalrow = corner == GridOrder.Corner.BottomLeft || corner == GridOrder.Corner.BottomRight ? 2 - row : row;
				int physicalcol = corner == GridOrder.Corner.TopRight || corner == GridOrder.Corner.BottomRight ? 3 - col : col;
				Assert.Equal(11 - (physicalrow * 4 + physicalcol), grid.Entries[i].Index);
				Assert.Equal(row, grid.Entries[i].Row);
				Assert.Equal(col, grid.Entries[i].Column);
				Assert.Equal(order == GridOrder.Order.Columns ? 7 + row * 3 + col * 29 : 7 + col * 3 + row * 29, range.Values[i]);
				Assert.Equal(7 + i * 3, sequential.Values[i]);
			}
		}

		[Fact]
		public void CellTagsMatchThirtyTwoByThirtyTwoLayout()
		{
			List<Vector2D> anchors = Cells(32, 32);
			anchors.Reverse();
			GridOrder.Result grid = GridOrder.Sort(anchors, GridOrder.Order.Rows, GridOrder.Corner.TopLeft);
			GridOrder.RangeResult range = Range(grid, 11001, 1, 32);
			Assert.False(range.OutOfRange);
			Assert.Equal(32, grid.RowCount);
			Assert.Equal(32, grid.ColumnCount);
			Assert.Equal(1024, range.Values.Count);
			for(int i = 0; i < range.Values.Count; i++)
			{
				Vector2D anchor = anchors[grid.Entries[i].Index];
				int x = (int)(anchor.x / 128);
				int y = (int)(-anchor.y / 128);
				// orcslay_grid.py cell_tag: MIN_TAG + N*y + x.
				Assert.Equal(11001 + 32 * y + x, range.Values[i]);
			}
		}

		[Theory]
		[InlineData(GridOrder.LineFilter.Horizontal)]
		[InlineData(GridOrder.LineFilter.Vertical)]
		public void BothLineFamiliesFilterAndNumberAllGridEdges(GridOrder.LineFilter filter)
		{
			List<Line2D> lines = new List<Line2D>();
			for(int y = 0; y <= 32; y++)
				for(int x = 0; x < 32; x++) lines.Add(new Line2D(x * 128, -y * 128, (x + 1) * 128, -y * 128));
			for(int y = 0; y < 32; y++)
				for(int x = 0; x <= 32; x++) lines.Add(new Line2D(x * 128, -y * 128, x * 128, -(y + 1) * 128));
			lines.Reverse();
			Assert.Equal(2112, lines.Count);
			Assert.Equal(2112, lines.Count(l => GridOrder.MatchesLine(l.v1, l.v2, GridOrder.LineFilter.All)));
			List<Vector2D> anchors = lines.Where(l => GridOrder.MatchesLine(l.v1, l.v2, filter)).Select(l => l.GetCoordinatesAt(0.5)).ToList();
			Assert.Equal(1056, anchors.Count);
			bool horizontal = filter == GridOrder.LineFilter.Horizontal;
			GridOrder.Result grid = GridOrder.Sort(anchors, GridOrder.Order.Rows, GridOrder.Corner.TopLeft);
			GridOrder.RangeResult range = Range(grid, horizontal ? 21000 : 21033, 1, 66);
			Assert.False(range.OutOfRange);
			Assert.Equal(horizontal ? 33 : 32, grid.RowCount);
			Assert.Equal(horizontal ? 32 : 33, grid.ColumnCount);
			for(int i = 0; i < range.Values.Count; i++)
			{
				Vector2D anchor = anchors[grid.Entries[i].Index];
				int x = (int)(anchor.x / 128);
				int y = (int)(-anchor.y / 128);
				// orcslay_grid.py line_id: horizontal base+x+66*y, vertical base+33+x+66*y.
				Assert.Equal(21000 + (horizontal ? 0 : 33) + x + 66 * y, range.Values[i]);
			}
		}

		[Fact]
		public void UnevenWidthsJitterAndMissingClustersUseCompressedRanks()
		{
			List<Vector2D> anchors = new List<Vector2D>
			{
				new Vector2D(400.5, 200), new Vector2D(10, 300), new Vector2D(100.5, 200.5),
				new Vector2D(400, 300), new Vector2D(10.5, 200), new Vector2D(100, 300)
			};
			GridOrder.Result grid = GridOrder.Sort(anchors, GridOrder.Order.Rows, GridOrder.Corner.TopLeft);
			Assert.Equal(2, grid.RowCount);
			Assert.Equal(3, grid.ColumnCount);
			Assert.Equal(new[] { 1, 5, 3, 4, 2, 0 }, grid.Entries.Select(e => e.Index).ToArray());
			Assert.Equal(new[] { 10, 11, 12, 20, 21, 22 }, Range(grid, 10, 1, 10).Values.ToArray());
		}

		[Fact]
		public void ToleranceUsesAdjacentGapsAndHasInclusiveBoundary()
		{
			Vector2D[] anchors = { new Vector2D(0, 0), new Vector2D(1, 0), new Vector2D(2, 0), new Vector2D(3.01, 0) };
			GridOrder.Result grid = GridOrder.Sort(anchors, GridOrder.Order.Rows, GridOrder.Corner.TopLeft);
			Assert.Equal(2, grid.ColumnCount);
			Assert.Equal(new[] { 0, 0, 0, 1 }, grid.Entries.Select(e => e.Column).ToArray());
			Assert.Equal(new[] { 0, 1, 2, 3 }, grid.Entries.Select(e => e.Index).ToArray());
		}

		[Theory]
		[InlineData(GridOrder.Order.Selection)]
		[InlineData(GridOrder.Order.Rows)]
		[InlineData(GridOrder.Order.Columns)]
		public void SingleAndEmptySelectionsHaveUsefulCounts(GridOrder.Order order)
		{
			GridOrder.Result grid = GridOrder.Sort(new[] { new Vector2D(42, -7) }, order, GridOrder.Corner.BottomRight);
			Assert.Equal(1, grid.RowCount);
			Assert.Equal(1, grid.ColumnCount);
			Assert.Equal(0, grid.Entries[0].Index);
			Assert.Equal(0, grid.Entries[0].Row);
			Assert.Equal(0, grid.Entries[0].Column);
			Assert.Equal(new[] { 8 }, Range(grid, 8, 3, 100).Values.ToArray());
			grid = GridOrder.Sort(new Vector2D[0], order, GridOrder.Corner.BottomRight);
			Assert.Equal(0, grid.RowCount);
			Assert.Equal(0, grid.ColumnCount);
			Assert.Empty(Range(grid, 8, 3).Values);
		}

		[Theory]
		[InlineData(GridOrder.Corner.TopLeft)]
		[InlineData(GridOrder.Corner.TopRight)]
		[InlineData(GridOrder.Corner.BottomLeft)]
		[InlineData(GridOrder.Corner.BottomRight)]
		public void SelectionIsIdentityAndDefaultFormulaIsUnchanged(GridOrder.Corner corner)
		{
			List<Vector2D> anchors = Cells(4, 3);
			anchors.Reverse();
			GridOrder.Result grid = GridOrder.Sort(anchors, GridOrder.Order.Selection, corner);
			Assert.Equal(1, grid.RowCount);
			Assert.Equal(12, grid.ColumnCount);
			GridOrder.RangeResult range = Range(grid, 200, -3);
			for(int i = 0; i < 12; i++)
			{
				Assert.Equal(i, grid.Entries[i].Index);
				Assert.Equal(i, grid.Entries[i].Column);
				Assert.Equal(0, grid.Entries[i].Row);
				Assert.Equal(200 - i * 3, range.Values[i]);
			}
		}

		[Theory]
		[InlineData(false, false, 2)]
		[InlineData(false, true, 2)]
		[InlineData(true, false, 2)]
		[InlineData(true, true, 2)]
		[InlineData(false, false, -2)]
		[InlineData(false, true, -2)]
		[InlineData(true, false, -2)]
		[InlineData(true, true, -2)]
		public void SelectionRangesMatchOriginalAlgorithm(bool relative, bool skip, int step)
		{
			int[] initial = { 20, 25, 30, 35, 40, 45 };
			HashSet<int> used = new HashSet<int> { 98, 100, 102, 120, 122, 140 };
			GridOrder.Result grid = GridOrder.Sort(Cells(3, 2), GridOrder.Order.Selection, GridOrder.Corner.TopLeft);
			GridOrder.RangeResult range = Range(grid, 100, step, null, initial, relative, skip, used);
			List<int> expected = new List<int>();
			int offset = 0;
			for(int i = 0; i < initial.Length; i++)
			{
				int value = 100 + offset + (relative ? initial[i] : 0);
				while(skip && used.Contains(value)) { value += step; offset += step; }
				expected.Add(value);
				offset += step;
			}
			Assert.Equal(expected, range.Values);
			Assert.Equal(!skip && expected.Any(used.Contains), range.TagsUsed);
			Assert.False(range.OutOfRange);
		}

		[Fact]
		public void RelativeValuesStayAlignedWhenElementsAreReordered()
		{
			Vector2D[] anchors = { new Vector2D(128, -64), new Vector2D(0, 64), new Vector2D(128, 64), new Vector2D(0, -64) };
			int[] initial = { 100, 200, 300, 400 };
			GridOrder.Result grid = GridOrder.Sort(anchors, GridOrder.Order.Rows, GridOrder.Corner.TopLeft);
			Assert.Equal(new[] { 210, 312, 430, 132 }, Range(grid, 10, 2, 20, initial, true).Values.ToArray());
			Assert.Equal(new[] { 210, 312, 414, 116 }, Range(grid, 10, 2, null, initial, true).Values.ToArray());
		}

		[Fact]
		public void StrideDisablesSkippingButKeepsUsedTagWarning()
		{
			GridOrder.Result grid = GridOrder.Sort(Cells(2, 2), GridOrder.Order.Rows, GridOrder.Corner.TopLeft);
			HashSet<int> used = new HashSet<int> { 11, 20 };
			GridOrder.RangeResult range = Range(grid, 10, 1, 10, null, false, true, used);
			Assert.Equal(new[] { 10, 11, 20, 21 }, range.Values.ToArray());
			Assert.True(range.TagsUsed);
			Assert.False(range.OutOfRange);
		}

		[Fact]
		public void ArgumentValuesIgnoreUsedTagsAndSkipping()
		{
			GridOrder.Result grid = GridOrder.Sort(Cells(2, 1), GridOrder.Order.Selection, GridOrder.Corner.TopLeft);
			GridOrder.RangeResult range = Range(grid, 10, 0, null, null, false, true);
			Assert.Equal(new[] { 10, 10 }, range.Values.ToArray());
			Assert.False(range.TagsUsed);
			Assert.False(range.OutOfRange);
		}

		[Theory]
		[InlineData(true, true, 0, 255)]
		[InlineData(true, false, int.MinValue, int.MaxValue)]
		[InlineData(false, true, 1, 32767)]
		[InlineData(false, false, 1, 32767)]
		public void TargetLimitsDistinguishHexenArgumentsFromTags(bool argument, bool hexen, int expectedmin, int expectedmax)
		{
			int minimum, maximum;
			GridOrder.GetValueLimits(argument, hexen, 1, 32767, out minimum, out maximum);
			Assert.Equal(expectedmin, minimum);
			Assert.Equal(expectedmax, maximum);
		}

		[Theory]
		[InlineData(255, 1, 0, 255)]
		[InlineData(0, -1, 0, 255)]
		[InlineData(int.MaxValue, 1, int.MinValue, int.MaxValue)]
		[InlineData(int.MinValue, -1, int.MinValue, int.MaxValue)]
		public void EverySequentialValueIsRangeCheckedWithoutWrapping(int start, int step, int minimum, int maximum)
		{
			GridOrder.Result grid = GridOrder.Sort(Cells(2, 1), GridOrder.Order.Selection, GridOrder.Corner.TopLeft);
			GridOrder.RangeResult range = Range(grid, start, step, null, null, false, false, null, minimum, maximum);
			Assert.True(range.OutOfRange);
			Assert.Equal(new[] { start }, range.Values.ToArray());
		}

		[Fact]
		public void StrideAndRelativeOverflowAreRejectedBeforeIntConversion()
		{
			GridOrder.Result grid = GridOrder.Sort(Cells(2, 2), GridOrder.Order.Rows, GridOrder.Corner.TopLeft);
			Assert.True(Range(grid, int.MaxValue, int.MaxValue, int.MaxValue).OutOfRange);
			Assert.True(Range(grid, int.MinValue, int.MinValue, int.MinValue).OutOfRange);
			Assert.True(Range(grid, int.MaxValue, 0, null, new[] { int.MaxValue, 0, 0, 0 }, true).OutOfRange);
			Assert.True(Range(grid, int.MinValue, 0, null, new[] { int.MinValue, 0, 0, 0 }, true).OutOfRange);
			Assert.True(Range(grid, 10, -3, -7, null, false, false, null, 1, 32767).OutOfRange);
			Assert.Equal(new[] { 10, 7, 3, 0 }, Range(grid, 10, -3, -7).Values.ToArray());
		}

		[Theory]
		[InlineData(GridOrder.Order.Rows, int.MaxValue)]
		[InlineData(GridOrder.Order.Columns, int.MaxValue)]
		[InlineData(GridOrder.Order.Rows, int.MinValue)]
		[InlineData(GridOrder.Order.Columns, int.MinValue)]
		public void StrideMultiplicationUsesLongBeforeCheckingBounds(GridOrder.Order order, int step)
		{
			GridOrder.Result grid = GridOrder.Sort(Cells(3, 3), order, GridOrder.Corner.TopLeft);
			GridOrder.RangeResult range = Range(grid, 0, step, 0);
			Assert.True(range.OutOfRange);
			Assert.Equal(new[] { 0, step }, range.Values.ToArray());
		}

		[Theory]
		[InlineData(-1, 0, 255)]
		[InlineData(256, 0, 255)]
		[InlineData(-1, 0, 32767)]
		[InlineData(32768, 0, 32767)]
		public void InvalidStartingValueIsRejected(int start, int minimum, int maximum)
		{
			GridOrder.Result grid = GridOrder.Sort(Cells(1, 1), GridOrder.Order.Selection, GridOrder.Corner.TopLeft);
			GridOrder.RangeResult range = Range(grid, start, 1, null, null, false, false, null, minimum, maximum);
			Assert.True(range.OutOfRange);
			Assert.Empty(range.Values);
		}

		[Theory]
		[InlineData(100, 200)]
		[InlineData(100, -200)]
		[InlineData(100, 0)]
		public void SkipCannotEscapeBoundsOrLoopWithZeroIncrement(int start, int step)
		{
			GridOrder.Result grid = GridOrder.Sort(Cells(1, 1), GridOrder.Order.Selection, GridOrder.Corner.TopLeft);
			GridOrder.RangeResult range = Range(grid, start, step, null, null, false, true, new HashSet<int> { start }, 0, 255);
			Assert.True(range.OutOfRange);
			Assert.Empty(range.Values);
		}

		[Fact]
		public void LineOrientationToleranceIsDirectionIndependent()
		{
			Assert.True(GridOrder.MatchesLine(new Vector2D(128, 0), new Vector2D(0, 0), GridOrder.LineFilter.Horizontal));
			Assert.True(GridOrder.MatchesLine(new Vector2D(0, -128), new Vector2D(0, 0), GridOrder.LineFilter.Vertical));
			Assert.True(GridOrder.MatchesLine(new Vector2D(0, 0), new Vector2D(128, 1), GridOrder.LineFilter.Horizontal));
			Assert.True(GridOrder.MatchesLine(new Vector2D(0, 0), new Vector2D(1, 128), GridOrder.LineFilter.Vertical));
			Assert.False(GridOrder.MatchesLine(new Vector2D(0, 0), new Vector2D(128, 3), GridOrder.LineFilter.Horizontal));
			Assert.False(GridOrder.MatchesLine(new Vector2D(0, 0), new Vector2D(3, 128), GridOrder.LineFilter.Vertical));
			Assert.False(GridOrder.MatchesLine(new Vector2D(0, 0), new Vector2D(128, 128), GridOrder.LineFilter.Horizontal));
			Assert.False(GridOrder.MatchesLine(new Vector2D(0, 0), new Vector2D(0, 0), GridOrder.LineFilter.Vertical));
		}

		private static List<Vector2D> Cells(int columns, int rows)
		{
			List<Vector2D> result = new List<Vector2D>();
			for(int y = 0; y < rows; y++)
				for(int x = 0; x < columns; x++) result.Add(new Vector2D(x * 128 + 64, -y * 128 - 64));
			return result;
		}

		private static GridOrder.RangeResult Range(GridOrder.Result grid, int start, int step, int? stride = null,
			int[] initial = null, bool relative = false, bool skip = false, ISet<int> used = null, int minimum = int.MinValue, int maximum = int.MaxValue)
		{
			return GridOrder.CreateRange(grid, initial ?? new int[grid.Entries.Length], start, step, stride, relative, skip, used, minimum, maximum);
		}
	}
}
