using CodeImp.DoomBuilder;
using CodeImp.DoomBuilder.Editing;

namespace Builder.Tests
{
	public class UndoSnapshotWriteTests
	{
		// No General.Map means no temp path, so the write fails after compressing, like a full disk would
		[Fact]
		public void FailedWriteLeavesTheSnapshotInMemoryAndReadable()
		{
			Assert.Null(General.Map);
			byte[] data = new byte[4096];
			for(int i = 0; i < data.Length; i++) data[i] = (byte)(i * 7);
			UndoSnapshot snapshot = new UndoSnapshot("t016", new MemoryStream(data), 1);
			snapshot.StoreOnDisk = true;

			bool threw = false;
			try { snapshot.WriteToFile(); }
			catch(NullReferenceException) { threw = true; }

			Assert.True(threw);
			Assert.False(snapshot.IsOnDisk);
			Assert.Equal(data, snapshot.GetStream().ToArray());
		}
	}
}
