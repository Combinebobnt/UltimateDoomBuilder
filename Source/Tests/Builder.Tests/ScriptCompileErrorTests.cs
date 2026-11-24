using CodeImp.DoomBuilder;
using CodeImp.DoomBuilder.Compilers;
using CodeImp.DoomBuilder.Config;
using CodeImp.DoomBuilder.IO;

namespace Builder.Tests
{
	public class ScriptCompileErrorTests : IDisposable
	{
		private readonly string directory = Path.Combine(Path.GetTempPath(), "script-compile-" + Guid.NewGuid().ToString("N"));

		public ScriptCompileErrorTests() { Directory.CreateDirectory(directory); }

		public void Dispose() { Directory.Delete(directory, true); }

		// Map Options flags a rebuild only when the compiler changed and there is a script lump to build
		[Theory]
		[InlineData("UZDoom_DoomUDMF.cfg", "zdoom_acc", "zt_bcc", true, true)]
		[InlineData("UZDoom_DoomUDMF.cfg", "zdoom_acc", "zt_bcc", false, false)]
		[InlineData("UZDoom_DoomUDMF.cfg", "", "zdoom_acc", false, false)]
		[InlineData("UZDoom_DoomUDMF.cfg", "zdoom_acc", "zdoom_acc", true, false)]
		[InlineData("Doom_Doom2Doom.cfg", "zdoom_acc", "zt_bcc", true, false)]
		public void CompilerChangeRebuildsOnlyAScriptLumpThatExists(string configname, string oldcompiler, string newcompiler, bool hasscripts, bool expected)
		{
			List<string> names = new List<string> { "TEMPMAP", "TEXTMAP" };
			if(hasscripts) names.Add("SCRIPTS");
			names.Add("ENDMAP");
			using(var wad = new WAD(Path.Combine(directory, Guid.NewGuid().ToString("N") + ".wad")))
			{
				for(int i = 0; i < names.Count; i++) wad.Insert(names[i], i, 0);
				Dictionary<string, MapLumpInfo> lumps = LightmapRetentionTests.MapLumps(configname);
				Assert.Equal(expected, MapManager.ScriptCompilerChangeNeedsRebuild(oldcompiler, newcompiler, lumps.Values, wad));
			}
		}

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
