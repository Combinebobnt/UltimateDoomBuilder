using CodeImp.DoomBuilder.Controls;

namespace Builder.Tests
{
	public class Arg0StrPolicyTests
	{
		[Theory]
		// Element's action and type unchanged: flag edit on an undefined thing type or an action 0 line keeps the name
		[InlineData(false, false, false, false, false)]
		[InlineData(true, false, false, false, false)]
		// Unchanged and can take a string: uniform lights, or mixed actions (empty action box) on named ACS lines
		[InlineData(false, false, false, true, false)]
		[InlineData(true, false, false, true, false)]
		// Changed to something that still takes a string: the name box decides (ForNameBox)
		[InlineData(false, false, true, true, false)]
		[InlineData(true, false, true, true, false)]
		// Changed to something that can't take one: action cleared or moved to a plain special,
		// or a light's type changed to a non-light under an empty action box
		[InlineData(false, false, true, false, true)]
		[InlineData(true, false, true, false, true)]
		// A number chosen in a set action box: numbered ACS script, or string checkbox unticked on a light
		[InlineData(false, true, false, true, true)]
		[InlineData(false, true, true, true, true)]
		[InlineData(false, true, false, false, true)]
		// An empty action box never chose a number for every element
		[InlineData(true, true, false, true, false)]
		public void ShouldRemove_DecisionTable(bool actionempty, bool numberchosen, bool elementchanged, bool elementtakesstr, bool expected)
		{
			Assert.Equal(expected, Arg0StrPolicy.ShouldRemove(actionempty, numberchosen, elementchanged, elementtakesstr));
		}

		[Theory]
		// A typed or kept name is written, reset or not
		[InlineData("linescript", false, "Write")]
		[InlineData("linescript", true, "Write")]
		// Blank from mixed names: each element keeps its own
		[InlineData("", false, "Leave")]
		[InlineData(" ", false, "Leave")]
		// Blank after an action change reset it: the name goes, never a one-space arg0str
		[InlineData("", true, "Clear")]
		[InlineData(" ", true, "Clear")]
		public void ForNameBox_DecisionTable(string text, bool resetbyaction, string expected)
		{
			Assert.Equal(expected, Arg0StrPolicy.ForNameBox(text, resetbyaction).ToString());
		}
	}
}
