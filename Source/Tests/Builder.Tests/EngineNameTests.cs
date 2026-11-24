using CodeImp.DoomBuilder.Windows;

namespace Builder.Tests
{
	// The test engine name the Game Configurations dialog derives from a program path
	public class EngineNameTests
	{
		[Fact]
		public void UnixUsesTheProgramNameNotItsSharedFolder()
		{
			Assert.Equal("uzdoom", ConfigForm.EngineNameFromPath("/usr/games/uzdoom", true));
			Assert.Equal("gzdoom", ConfigForm.EngineNameFromPath("/usr/bin/gzdoom", true));
			Assert.Equal("uzdoom", ConfigForm.EngineNameFromPath("uzdoom", true));
		}

		[Fact]
		public void WindowsKeepsTheEngineFolderName()
		{
			string program = Path.Combine("C:", "Games", "GZDoom 4.11", "gzdoom.exe");
			Assert.Equal("GZDoom 4.11", ConfigForm.EngineNameFromPath(program, false));
			Assert.Equal("gzdoom", ConfigForm.EngineNameFromPath("gzdoom.exe", false));
		}
	}
}
