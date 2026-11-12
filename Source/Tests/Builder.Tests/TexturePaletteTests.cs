using System.Drawing;
using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.CompilerServices;
using System.Windows.Forms;
using CodeImp.DoomBuilder.BuilderModes;
using CodeImp.DoomBuilder;
using CodeImp.DoomBuilder.Controls;
using CodeImp.DoomBuilder.Data;
using CodeImp.DoomBuilder.Editing;
using CodeImp.DoomBuilder.Map;
using CodeImp.DoomBuilder.Windows;

namespace Builder.Tests
{
	[Collection("GeneralStatics")]
	public class TexturePaletteTests
	{
		private const BindingFlags Methods = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static;
		private static readonly Type Palette = typeof(BuilderPlug).Assembly.GetType("CodeImp.DoomBuilder.BuilderModes.TexturePalette");

		[Fact]
		public void TwoSidedMiddleTexturesAreNotPaletteTargetsAndNoOpCreatesNoUndo()
		{
			using(var map = new SndInfoParserTests.FakeMap("doom"))
			{
				Linedef line = Empty<Linedef>();
				Sector sector = Empty<Sector>();
				SetField(sector, "floortexname", "FLOOR");
				SetField(sector, "ceiltexname", "CEILING");
				SetField(sector, "ceilheight", 128);
				Sidedef front = Empty<Sidedef>();
				Sidedef back = Empty<Sidedef>();
				SetField(line, "front", front);
				SetField(line, "back", back);
				foreach(Sidedef side in new[] { front, back })
				{
					SetField(side, "linedef", line);
					SetField(side, "sector", sector);
					SetField(side, "texnamehigh", "-");
					SetField(side, "texnamelow", "-");
					SetField(side, "texnamemid", "GRATE");
				}
				// No undo manager exists: attempting an edit instead of a no-op must fail.
				Assert.Equal(0, (int)Palette.GetMethod("ApplyToLinedefs", Methods).Invoke(null, new object[] { "NEW", new[] { line } }));
				Assert.Equal("GRATE", front.MiddleTexture);
				Assert.Equal("GRATE", back.MiddleTexture);
				Assert.Equal("-", front.HighTexture);
				Assert.Equal("-", back.LowTexture);
				Assert.Equal(0, (int)Palette.GetMethod("ApplyToSectors", Methods).Invoke(null, new object[] { "FLOOR", new[] { sector }, true, false }));
				Assert.Equal("CEILING", sector.CeilTexture);
			}
		}

		[Fact]
		public void DockerUsesSelectionOnlyAndReturnsDisplayFocus()
		{
			var calls = Calls(typeof(TextureHistoryPanel).GetMethod("EntryClicked", Methods));
			Assert.True(calls.Any(call => call.Value.Name == "GetSelectedLinedefs"));
			Assert.True(calls.Any(call => call.Value.Name == "GetSelectedSectors"));
			Assert.False(calls.Any(call => call.Value.Name == "get_HighlightedObject" || call.Value.Name == "GetSelectedObjects"));
			Assert.True(InFinally(typeof(TextureHistoryPanel).GetMethod("EntryClicked", Methods), Offset(calls, "FocusDisplay")));
		}

		[Fact]
		public void MouseCaptureRepairDoesNotReclipDuringAnExclusiveInputBreak()
		{
			MainForm form = Empty<MainForm>();
			SetField(form, "mouseexclusive", true);
			SetField(form, "windowactive", true);
			SetField(form, "mouseinside", true);
			SetField(form, "mouseexclusivebreaklevel", 1);
			// No display exists: an attempted reclip would access it and throw.
			typeof(MainForm).GetMethod("MainForm_MouseCaptureChanged", Methods).Invoke(form, new object[] { null, EventArgs.Empty });
		}

		private static T Empty<T>()
		{
			T result = (T)RuntimeHelpers.GetUninitializedObject(typeof(T));
			GC.SuppressFinalize(result);
			return result;
		}

		private static void SetField(object target, string name, object value)
		{
			target.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic).SetValue(target, value);
		}

		[Theory]
		[InlineData("VisualMiddleSingle", TextureHistoryKind.Texture, false, true)]
		[InlineData("VisualMiddleSingle", TextureHistoryKind.Flat, false, false)]
		[InlineData("VisualFloor", TextureHistoryKind.Texture, false, false)]
		[InlineData("VisualFloor", TextureHistoryKind.Flat, false, true)]
		[InlineData("VisualCeiling", TextureHistoryKind.Texture, false, false)]
		[InlineData("VisualMiddleSingle", TextureHistoryKind.Flat, true, true)]
		[InlineData("VisualFloor", TextureHistoryKind.Texture, true, true)]
		[InlineData("BaseVisualThing", TextureHistoryKind.Texture, true, false)]
		public void VisualPicksOnlyAcceptTheirOwnNamespaceUnlessMixed(string classname, TextureHistoryKind kind, bool mixed, bool expected)
		{
			Type type = typeof(BuilderPlug).Assembly.GetType("CodeImp.DoomBuilder.BuilderModes." + classname);
			object target = RuntimeHelpers.GetUninitializedObject(type);
			GC.SuppressFinalize(target);
			Assert.Equal(expected, (bool)Palette.GetMethod("Accepts", Methods).Invoke(null, new[] { target, (object)kind, mixed }));
		}

		[Fact]
		public void PopupCapturesVisualTargetsAfterPreActionAndAlwaysPostActions()
		{
			MethodInfo method = typeof(BaseVisualMode).GetMethod("ShowTexturePalette", Methods);
			var calls = Calls(method);
			Assert.True(Offset(calls, "PreAction") < Offset(calls, "GetSelectedObjects"));
			Assert.True(Offset(calls, "GetSelectedObjects") < Offset(calls, "Choose"));
			Assert.True(Offset(calls, "Choose") < Offset(calls, "Accepts"));
			Assert.True(Offset(calls, "Accepts") < Offset(calls, "OnPasteTexture"));
			Assert.True(Offset(calls, "OnPasteTexture") < Offset(calls, "Touch"));
			Assert.False(calls.Any(call => call.Value.Name == "RecordTextureUse"));
			Assert.True(InFinally(method, Offset(calls, "PostAction")));
			Assert.Equal(1, calls.Count(call => call.Value.Name == "GetSelectedObjects"));
		}

		[Theory]
		[InlineData(typeof(LinedefsMode), "GetSelectedLinedefs")]
		[InlineData(typeof(SectorsMode), "GetSelectedSectors")]
		public void ClassicPopupCapturesSelectionBeforeOpeningAndDoesNotReacquireIt(Type mode, string selection)
		{
			var calls = Calls(mode.GetMethod("ShowTexturePalette", Methods));
			Assert.True(Offset(calls, selection) < Offset(calls, "Choose"));
			Assert.Equal(1, calls.Count(call => call.Value.Name == selection));
			Assert.True(Offset(calls, "Choose") < Offset(calls, "ApplyFromPalette"));
		}

		[Fact]
		public void PopupSuspendsProcessingAndBalancesMouseBreakInFinally()
		{
			MethodInfo method = typeof(MainForm).GetMethod("ShowPopupDialog", Methods);
			var calls = Calls(method);
			Assert.True(Offset(calls, "SuspendProcessing") < Offset(calls, "BreakExclusiveMouseInput"));
			Assert.True(Offset(calls, "BreakExclusiveMouseInput") < Offset(calls, "ReleaseAllKeys"));
			Assert.True(Offset(calls, "ReleaseAllKeys") < Offset(calls, "ShowDialog"));
			Assert.True(InFinally(method, Offset(calls, "ResumeExclusiveMouseInput")));
			Assert.True(InFinally(method, Offset(calls, "Activate")));
			Assert.True(Offset(calls, "ResumeExclusiveMouseInput") < Offset(calls, "Activate"));
			Assert.True(Offset(calls, "Activate") < Offset(calls, "FocusDisplay"));
			Assert.True(Offset(calls, "add_Deactivate") < Offset(calls, "ShowDialog"));
			Assert.True(InFinally(method, Offset(calls, "remove_Deactivate")));
			Assert.True(InFinally(method, Offset(calls, "Dispose")));
			Assert.False(calls.Any(call => call.Value.Name == "EnableProcessing" || call.Value.Name == "DisableProcessing"));
		}

		[Theory]
		[InlineData(250, 150, true, false)]
		[InlineData(150, 250, true, false)]
		[InlineData(50, 150, true, false)]
		[InlineData(150, 50, true, false)]
		[InlineData(250, 150, false, true)]
		[InlineData(150, 250, false, true)]
		[InlineData(50, 150, false, true)]
		[InlineData(150, 50, false, true)]
		[InlineData(150, 150, true, false)]
		[InlineData(150, 150, false, false)]
		public void OutsideClickCancelsUnlessTheTargetIsThePalettesOwnMenu(int x, int y, bool ownedmenu, bool cancel)
		{
			Type popup = typeof(BuilderPlug).Assembly.GetType("CodeImp.DoomBuilder.BuilderModes.TexturePaletteForm");
			bool result = (bool)popup.GetMethod("ShouldCancelOutsideClick", Methods).Invoke(null,
				new object[] { new Rectangle(100, 100, 100, 100), new Point(x, y), ownedmenu });
			Assert.Equal(cancel, result);
		}

		private sealed class HeadlessMenu : ContextMenuStrip
		{
			internal int Disposals;
			protected override void SetVisibleCore(bool value) { }
			protected override void Dispose(bool disposing) { if(disposing) Disposals++; }
		}

		[Fact]
		public void OwnedMenuLookupDoesNotCreateHandlesOrExemptAnUnopenedMenu()
		{
			var view = Empty<TextureHistoryView>();
			Assert.False(view.OwnsContextMenuInput(new IntPtr(123)));
			var menu = Empty<HeadlessMenu>();
			SetField(view, "contextmenu", menu);
			Assert.False(menu.IsHandleCreated);
			Assert.False(view.OwnsContextMenuInput(new IntPtr(123)));
			Assert.False(view.OwnsContextMenuInput(IntPtr.Zero));
			Assert.False(menu.IsHandleCreated);
		}

		[Fact]
		public void ClosingOwnedMenuClearsItAndRepeatedCloseIsHarmless()
		{
			var view = Empty<TextureHistoryView>();
			var menu = Empty<HeadlessMenu>();
			SetField(view, "contextmenu", menu);
			view.CloseContextMenu();
			Assert.Null(typeof(TextureHistoryView).GetField("contextmenu", Methods).GetValue(view));
			Assert.Equal(1, menu.Disposals);
			view.CloseContextMenu();
			Assert.Equal(1, menu.Disposals);
		}

		[Fact]
		public void LateClosedNotificationCannotClearTheReplacementOwnedMenu()
		{
			var view = Empty<TextureHistoryView>();
			var old = Empty<HeadlessMenu>();
			var current = Empty<HeadlessMenu>();
			SetField(view, "contextmenu", current);
			typeof(TextureHistoryView).GetMethod("EntryMenuClosed", Methods).Invoke(view,
				new object[] { old, new ToolStripDropDownClosedEventArgs(ToolStripDropDownCloseReason.CloseCalled) });
			Assert.Same(current, typeof(TextureHistoryView).GetField("contextmenu", Methods).GetValue(view));
			Assert.Equal(1, old.Disposals);
			Assert.Equal(0, current.Disposals);
			view.CloseContextMenu();
			Assert.Equal(1, current.Disposals);
		}

		[Fact]
		public void OwnedMenuGuardRunsBeforeCancelAndCleanupPrecedesPopupCloseOrRebind()
		{
			Type popup = typeof(BuilderPlug).Assembly.GetType("CodeImp.DoomBuilder.BuilderModes.TexturePaletteForm");
			var filter = Calls(popup.GetMethod("PreFilterMessage", Methods));
			Assert.True(Offset(filter, "OwnsContextMenuInput") < Offset(filter, "ShouldCancelOutsideClick"));
			Assert.True(Offset(filter, "ShouldCancelOutsideClick") < Offset(filter, "Cancel"));
			var closed = Calls(popup.GetMethod("OnFormClosed", Methods));
			Assert.True(Offset(closed, "CloseContextMenu") < Offset(closed, "RemoveMessageFilter"));
			Assert.True(Offset(closed, "CloseContextMenu") < Offset(closed, "OnFormClosed"));
			var entries = Calls(typeof(TextureHistoryView).GetMethod("SetEntries", Methods));
			Assert.True(Offset(entries, "CloseContextMenu") < Offset(entries, "remove_Changed"));
			var dispose = Calls(typeof(TextureHistoryView).GetMethod("Dispose", Methods, null, new[] { typeof(bool) }, null));
			Assert.True(Offset(dispose, "CloseContextMenu") < Offset(dispose, "Dispose"));
			var lookup = Calls(typeof(TextureHistoryView).GetMethod("OwnsContextMenuInput", Methods));
			Assert.True(Offset(lookup, "get_IsHandleCreated") < Offset(lookup, "FromHandle"));
			Assert.True(Offset(lookup, "get_IsHandleCreated") < Offset(lookup, "FromChildHandle"));
			Assert.False(lookup.Any(call => call.Value.Name == "get_Handle"));
		}

		[Fact]
		public void MenuCommandsCaptureTheirOwnerNamesAndKindsInsteadOfTheRebindableView()
		{
			var calls = Calls(typeof(TextureHistoryView).GetMethod("ShowEntryMenu", Methods));
			Type closure = calls.Single(call => call.Value is ConstructorInfo && call.Value.DeclaringType.DeclaringType == typeof(TextureHistoryView)).Value.DeclaringType;
			var fields = closure.GetFields(Methods);
			Assert.True(fields.Any(field => field.FieldType == typeof(TextureHistory)));
			Assert.True(fields.Any(field => field.FieldType == typeof(TextureFavorites)));
			Assert.True(fields.Any(field => field.FieldType == typeof(TextureHistoryKind)));
			Assert.True(fields.Any(field => field.FieldType == typeof(string)));
			Assert.False(fields.Any(field => field.FieldType == typeof(TextureHistoryView)));
			Assert.True(Offset(calls, "CloseContextMenu") < calls.First(call => call.Value is ConstructorInfo && call.Value.DeclaringType == typeof(ContextMenuStrip)).Key);
		}

		[Theory]
		[InlineData(typeof(LinedefEditForm))]
		[InlineData(typeof(LinedefEditFormUDMF))]
		[InlineData(typeof(SectorEditForm))]
		[InlineData(typeof(SectorEditFormUDMF))]
		public void OnlySuccessfulEditFormApplyRecordsTextureUse(Type form)
		{
			Assert.True(Calls(form.GetMethod("apply_Click", Methods)).Any(call => call.Value.Name == "RecordTextureUse"));
			Assert.False(Calls(form.GetMethod("cancel_Click", Methods)).Any(call => call.Value.Name == "RecordTextureUse"));
			Assert.False(Calls(form.GetMethod("Setup", Methods)).Any(call => call.Value.Name == "RecordTextureUse"));
		}

		private sealed class ClickPanel : ImageSelectorPanel
		{
			internal void Down(MouseButtons button, int x) { OnMouseDown(new MouseEventArgs(button, 1, x, 5, 0)); }
			internal void Up(MouseButtons button, int x) { OnMouseUp(new MouseEventArgs(button, 1, x, 5, 0)); }
		}

		[Fact]
		public void ItemClickRequiresMatchingDownAndUpOnTheSameItemAndButton()
		{
			// Exercise the real input handlers without constructing a window or opening a display.
			ClickPanel panel = (ClickPanel)RuntimeHelpers.GetUninitializedObject(typeof(ClickPanel));
			GC.SuppressFinalize(panel);
			var scroll = (VScrollBar)RuntimeHelpers.GetUninitializedObject(typeof(VScrollBar));
			GC.SuppressFinalize(scroll);
			ImageBrowserItem first = (ImageBrowserItem)RuntimeHelpers.GetUninitializedObject(typeof(ImageBrowserItem));
			ImageBrowserItem second = (ImageBrowserItem)RuntimeHelpers.GetUninitializedObject(typeof(ImageBrowserItem));
			SetPanel(panel, "scrollbar", scroll);
			SetPanel(panel, "items", new List<ImageBrowserItem> { first, second });
			SetPanel(panel, "rectangles", new List<Rectangle> { new Rectangle(2, 2, 40, 40), new Rectangle(52, 2, 40, 40) });
			int clicks = 0;
			panel.ItemClicked += (item, e) => clicks++;
			panel.Up(MouseButtons.Left, 5);
			Assert.Equal(0, clicks);
			panel.Down(MouseButtons.Left, 5);
			panel.Up(MouseButtons.Left, 55);
			Assert.Equal(0, clicks);
			panel.Down(MouseButtons.Right, 5);
			panel.Up(MouseButtons.Left, 5);
			Assert.Equal(0, clicks);
			panel.Down(MouseButtons.Left, 5);
			panel.Up(MouseButtons.Left, 5);
			Assert.Equal(1, clicks);
			panel.Up(MouseButtons.Left, 5);
			Assert.Equal(1, clicks);
			panel.Down(MouseButtons.Right, 55);
			panel.Up(MouseButtons.Right, 55);
			Assert.Equal(2, clicks);
		}

		private static void SetPanel(object panel, string field, object value)
		{
			typeof(ImageSelectorPanel).GetField(field, BindingFlags.NonPublic | BindingFlags.Instance).SetValue(panel, value);
		}

		private static int Offset(List<KeyValuePair<int, MethodBase>> calls, string name)
		{
			return calls.First(call => call.Value.Name == name).Key;
		}

		private static bool InFinally(MethodInfo method, int offset)
		{
			return method.GetMethodBody().ExceptionHandlingClauses.Any(clause => clause.Flags == ExceptionHandlingClauseOptions.Finally
				&& offset >= clause.HandlerOffset && offset < clause.HandlerOffset + clause.HandlerLength);
		}

		private static List<KeyValuePair<int, MethodBase>> Calls(MethodInfo method)
		{
			var opcodes = typeof(OpCodes).GetFields(BindingFlags.Public | BindingFlags.Static)
				.Where(field => field.FieldType == typeof(OpCode)).Select(field => (OpCode)field.GetValue(null)).ToDictionary(op => op.Value);
			var result = new List<KeyValuePair<int, MethodBase>>();
			byte[] code = method.GetMethodBody().GetILAsByteArray();
			for(int i = 0; i < code.Length;)
			{
				int offset = i;
				short value = code[i++];
				if(value == 0xFE) value = (short)(0xFE00 | code[i++]);
				OpCode opcode = opcodes[value];
				if(opcode.OperandType == OperandType.InlineMethod)
					result.Add(new KeyValuePair<int, MethodBase>(offset, method.Module.ResolveMethod(BitConverter.ToInt32(code, i))));
				switch(opcode.OperandType)
				{
					case OperandType.InlineNone: break;
					case OperandType.ShortInlineBrTarget: case OperandType.ShortInlineI: case OperandType.ShortInlineVar: i++; break;
					case OperandType.InlineVar: i += 2; break;
					case OperandType.InlineI8: case OperandType.InlineR: i += 8; break;
					case OperandType.InlineSwitch: i += 4 + 4 * BitConverter.ToInt32(code, i); break;
					default: i += 4; break;
				}
			}
			return result;
		}
	}
}
