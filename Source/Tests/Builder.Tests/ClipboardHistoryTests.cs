using CodeImp.DoomBuilder.Editing;
using CodeImp.DoomBuilder.Config;

namespace Builder.Tests
{
	public class ClipboardHistoryTests
	{
		private static byte[] Payload(int vertices = 1, int sectors = 0, int linedefs = 0, int things = 0, int length = 20)
		{
			byte[] bytes = new byte[length];
			using(BinaryWriter writer = new BinaryWriter(new MemoryStream(bytes)))
			{
				writer.Write(vertices);
				writer.Write(sectors);
				writer.Write(linedefs);
				writer.Write(things);
			}
			return bytes;
		}

		[Fact]
		public void DefaultConfigurationKeepsTwentyEntries()
		{
			Assert.Equal(20, new ProgramConfiguration().ClipboardHistorySize);
		}

		[Theory]
		[InlineData(-1, 0)]
		[InlineData(0, 0)]
		[InlineData(20, 20)]
		[InlineData(101, 100)]
		public void ConfigurationAndStoreClampCapacity(int requested, int expected)
		{
			ProgramConfiguration config = new ProgramConfiguration();
			config.ClipboardHistorySize = requested;
			Assert.Equal(expected, config.ClipboardHistorySize);
			Assert.Equal(expected, new ClipboardHistory(requested).Capacity);
		}

		[Fact]
		public void AddsNewestFirstAndParsesCounts()
		{
			ClipboardHistory history = new ClipboardHistory(3);
			DateTime now = DateTime.UtcNow;
			Assert.Equal(ClipboardHistoryAddResult.Added, history.Add(Payload(), "first", 1, 2, now));
			history.Add(Payload(4, 2, 8, 3), "second", 256, 128, now.AddSeconds(1));
			ClipboardHistoryEntry entry = history.Entries[0];
			Assert.Equal(2, history.Entries.Count);
			Assert.Equal("second", entry.SourceMap);
			Assert.Equal(4, entry.Vertices);
			Assert.Equal(2, entry.Sectors);
			Assert.Equal(8, entry.Linedefs);
			Assert.Equal(3, entry.Things);
			Assert.Equal(256.0, entry.Width);
			Assert.Equal(128.0, entry.Height);
			Assert.Equal(now.AddSeconds(1), entry.CopiedUtc);
			Assert.Equal("first", history.Entries[1].SourceMap);
		}

		[Fact]
		public void DuplicateMovesExistingEntryAndRefreshesMetadataWithoutGrowing()
		{
			ClipboardHistory history = new ClipboardHistory(3, 20, 40);
			DateTime now = DateTime.UtcNow;
			history.Add(Payload(), "first", 1, 2, now);
			ClipboardHistoryEntry original = history.Entries[0];
			history.Add(Payload(2), "second", 3, 4, now);
			Assert.Equal(ClipboardHistoryAddResult.MovedToTop, history.Add(Payload(), "new map", 5, 6, now.AddMinutes(1)));
			Assert.Equal(2, history.Entries.Count);
			Assert.True(ReferenceEquals(original, history.Entries[0]));
			Assert.Equal("new map", original.SourceMap);
			Assert.Equal(now.AddMinutes(1), original.CopiedUtc);
			Assert.Equal(5.0, original.Width);
			Assert.Equal(6.0, original.Height);
		}

		[Fact]
		public void CapacityEvictsOldestAndShrinkingPrunes()
		{
			ClipboardHistory history = new ClipboardHistory(2);
			for(int i = 1; i <= 3; i++) history.Add(Payload(i), i.ToString(), 0, 0, DateTime.UtcNow);
			Assert.Equal(2, history.Entries.Count);
			Assert.Equal("2", history.Entries[1].SourceMap);
			history.Capacity = 1;
			Assert.Equal("3", Assert.Single(history.Entries).SourceMap);
		}

		[Fact]
		public void ZeroCapacityClearsAndDisablesRecording()
		{
			ClipboardHistory history = new ClipboardHistory(2);
			history.Add(Payload(), "", 0, 0, DateTime.UtcNow);
			history.Capacity = 0;
			Assert.Empty(history.Entries);
			Assert.Equal(ClipboardHistoryAddResult.Disabled, history.Add(Payload(), "", 0, 0, DateTime.UtcNow));
		}

		[Fact]
		public void TotalBytesEvictOldestAndRemovalReleasesBudget()
		{
			ClipboardHistory history = new ClipboardHistory(10, 30, 40);
			history.Add(Payload(1), "one", 0, 0, DateTime.UtcNow);
			history.Add(Payload(2), "two", 0, 0, DateTime.UtcNow);
			history.Add(Payload(3, length: 24), "three", 0, 0, DateTime.UtcNow);
			Assert.Equal("three", Assert.Single(history.Entries).SourceMap);
			history.Remove(history.Entries[0]);
			history.Add(Payload(4), "four", 0, 0, DateTime.UtcNow);
			history.Add(Payload(5), "five", 0, 0, DateTime.UtcNow);
			Assert.Equal(2, history.Entries.Count);
		}

		[Fact]
		public void OversizeEntryIsNotRecorded()
		{
			ClipboardHistory history = new ClipboardHistory(2, 20, 40);
			Assert.Equal(ClipboardHistoryAddResult.TooLarge, history.Add(Payload(length: 21), "", 0, 0, DateTime.UtcNow));
			Assert.Empty(history.Entries);
		}

		[Theory]
		[InlineData(-1, 0, 0, 0)]
		[InlineData(0, -1, 0, 0)]
		[InlineData(0, 0, -1, 0)]
		[InlineData(0, 0, 0, -1)]
		public void NegativeCountsAreInvalid(int vertices, int sectors, int lines, int things)
		{
			ClipboardHistory history = new ClipboardHistory(2);
			Assert.Equal(ClipboardHistoryAddResult.Invalid, history.Add(Payload(vertices, sectors, lines, things), "", 0, 0, DateTime.UtcNow));
			Assert.Empty(history.Entries);
		}

		[Fact]
		public void ShortOrNullPayloadIsInvalid()
		{
			ClipboardHistory history = new ClipboardHistory(2);
			Assert.Equal(ClipboardHistoryAddResult.Invalid, history.Add(new byte[15], "", 0, 0, DateTime.UtcNow));
			Assert.Equal(ClipboardHistoryAddResult.Invalid, history.Add(null, "", 0, 0, DateTime.UtcNow));
		}

		[Fact]
		public void MutatorsNotifyOnceAndNoOpsDoNotNotify()
		{
			ClipboardHistory history = new ClipboardHistory(3);
			int changes = 0;
			history.Changed += (sender, e) => changes++;
			history.Add(Payload(1), "", 0, 0, DateTime.UtcNow);
			ClipboardHistoryEntry first = history.Entries[0];
			Assert.Equal(1, changes);
			history.Add(Payload(2), "", 0, 0, DateTime.UtcNow);
			Assert.Equal(2, changes);
			history.MoveToTop(first);
			Assert.Equal(3, changes);
			history.MoveToTop(first);
			Assert.Equal(3, changes);
			history.Add(Payload(1), "", 0, 0, DateTime.UtcNow);
			Assert.Equal(4, changes);
			history.Capacity = 1;
			Assert.Equal(5, changes);
			history.Remove(first);
			Assert.Equal(6, changes);
			history.Remove(first);
			history.Clear();
			Assert.Equal(6, changes);
			history.Add(Payload(3), "", 0, 0, DateTime.UtcNow);
			history.Clear();
			Assert.Equal(8, changes);
			history.Add(new byte[0], "", 0, 0, DateTime.UtcNow);
			Assert.Equal(8, changes);
		}
	}
}
