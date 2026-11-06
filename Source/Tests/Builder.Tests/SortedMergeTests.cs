using CodeImp.DoomBuilder;
using System.Globalization;

namespace Builder.Tests
{
	public class SortedMergeTests
	{
		[Fact]
		public void EmptyListedSortsBatch()
		{
			List<string> batch = new List<string> { "c", "a", "b" };
			List<string> merged = SortedMerge.Merge(new string[0], batch, s => s, out int[] oldtonew);
			Assert.Equal(new List<string> { "a", "b", "c" }, merged);
			Assert.Equal(merged, batch);
			Assert.Empty(oldtonew);
		}

		[Fact]
		public void EmptyBatchPreservesListedAndIndices()
		{
			List<string> listed = new List<string> { "a", "b" };
			List<string> merged = SortedMerge.Merge(listed, new List<string>(), s => s, out int[] oldtonew);
			Assert.Equal(listed, merged);
			Assert.Equal(new int[] { 0, 1 }, oldtonew);
		}

		[Fact]
		public void EmptyInputsStayEmpty()
		{
			Assert.Empty(SortedMerge.Merge(new string[0], new List<string>(), s => s, out int[] oldtonew));
			Assert.Empty(oldtonew);
		}

		[Fact]
		public void InterleavedBatchMapsOldRows()
		{
			List<string> merged = SortedMerge.Merge(new string[] { "b", "d", "f" },
				new List<string> { "g", "e", "a", "c" }, s => s, out int[] oldtonew);
			Assert.Equal(new List<string> { "a", "b", "c", "d", "e", "f", "g" }, merged);
			Assert.Equal(new int[] { 1, 3, 5 }, oldtonew);
		}

		[Fact]
		public void EqualKeysKeepListedRowsFirstAndMapTheSameObjects()
		{
			object first = new object();
			object second = new object();
			object added = new object();
			object[] listed = { first, second };
			List<object> merged = SortedMerge.Merge(listed, new List<object> { added }, o => "same", out int[] oldtonew);
			Assert.Equal(new int[] { 0, 1 }, oldtonew);
			for(int i = 0; i < listed.Length; i++) Assert.True(ReferenceEquals(listed[i], merged[oldtonew[i]]));
			Assert.True(ReferenceEquals(added, merged[2]));
		}

		[Theory]
		[InlineData("en-US")]
		[InlineData("tr-TR")]
		public void MixedCaseOrderMatchesCurrentCultureCompareTo(string culture)
		{
			CultureInfo original = CultureInfo.CurrentCulture;
			try
			{
				CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo(culture);
				List<string> listed = new List<string> { "I", "a", "Z" };
				listed.Sort((a, b) => a.CompareTo(b));
				List<string> batch = new List<string> { "z", "i", "A", "b" };
				List<string> expected = new List<string>(listed);
				expected.AddRange(batch);
				expected.Sort((a, b) => a.CompareTo(b));
				List<string> merged = SortedMerge.Merge(listed, batch, s => s, out int[] oldtonew);
				Assert.Equal(expected, merged);
				for(int i = 0; i < listed.Count; i++) Assert.True(ReferenceEquals(listed[i], merged[oldtonew[i]]));
			}
			finally
			{
				CultureInfo.CurrentCulture = original;
			}
		}
	}
}
