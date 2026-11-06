using CodeImp.DoomBuilder;
using CodeImp.DoomBuilder.Compilers;

namespace Builder.Tests
{
	public class ScriptCompileErrorTests
	{
		// Save Map's warning for a compile that ran but failed
		[Fact]
		public void LumpNameDropsQuestionMarkAndLineIsOneBased()
		{
			List<CompilerError> errors = new List<CompilerError>
			{
				new CompilerError("syntax error: unexpected identifier", "?SCRIPTS", 19),
				new CompilerError("expecting `;` here", "?SCRIPTS", 19),
			};

			Assert.Equal("Scripts failed to compile (2 errors; first: SCRIPTS line 20: syntax error: unexpected identifier). "
				+ "Saving keeps the edited script text, but BEHAVIOR still holds the previous compiled scripts. "
				+ "Script compiler (Map Options): BCS (zt-bcc).",
				MapManager.DescribeScriptCompileErrors(errors, "BCS (zt-bcc)"));
		}

		[Fact]
		public void FilePathIsReducedToItsFileName()
		{
			List<CompilerError> errors = new List<CompilerError> { new CompilerError("Invalid declarator.", "/tmp/udb/source/inc.acs", 4) };

			string text = MapManager.DescribeScriptCompileErrors(errors, "ZDoom ACS");
			Assert.Contains("(1 error; first: inc.acs line 5: Invalid declarator.). ", text);
			Assert.DoesNotContain("/tmp/udb", text);
		}

		[Fact]
		public void NoLineNumberAndNoFileOmitLocationAndUseFirstLine()
		{
			List<CompilerError> errors = new List<CompilerError> { new CompilerError("zt-bcc crashed\nsecond line") };

			string text = MapManager.DescribeScriptCompileErrors(errors, null);
			Assert.Contains("(1 error; first: zt-bcc crashed). ", text);
			Assert.DoesNotContain("line ", text);
			Assert.DoesNotContain("second", text);
			Assert.DoesNotContain("Script compiler", text);
		}
	}
}
