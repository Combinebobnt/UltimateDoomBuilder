using System;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.Serialization;
using CodeImp.DoomBuilder;
using CodeImp.DoomBuilder.Actions;
using CodeImp.DoomBuilder.IO;
using CodeImp.DoomBuilder.Plugins;
using Xunit;
using UdbAction = CodeImp.DoomBuilder.Actions.Action;

namespace Builder.Tests
{
	[Collection("GeneralStatics")]
	public class ActionAlternateKeyTests
	{
		private const int X = 88;
		private const int Y = 89;
		private const int Shift = 0x10000;
		private const int Control = 0x20000;
		private const int Alt = 0x40000;

		private static UdbAction Create(int primary, int alternate, string options = "", string name = "test")
		{
			Configuration cfg = new Configuration();
			cfg.InputConfiguration("test { " + options + " }");
			Assert.False(cfg.ErrorResult);
			return new UdbAction(cfg, name, "test", primary, alternate);
		}

		[Fact]
		public void EitherSlotMatchesButUnboundAndUnrelatedKeysDoNot()
		{
			UdbAction action = Create(X, Y);
			Assert.True(action.KeyMatches(X));
			Assert.True(action.KeyMatches(Y));
			Assert.False(action.KeyMatches(90));
			Assert.False(action.KeyMatches(0));
			Assert.False(Create(0, 0).KeyMatches(0));
			Assert.False(Create(-1, 0).KeyMatches(-1));
		}

		[Theory]
		[InlineData(Shift, "disregardshift")]
		[InlineData(Control, "disregardcontrol")]
		[InlineData(Alt, "disregardalt")]
		public void DisregardedModifiersAreNormalizedAndMatchInEitherSlot(int modifier, string option)
		{
			UdbAction action = Create(X | modifier, Y | modifier, option + " = true;");
			Assert.Equal(X, action.ShortcutKey);
			Assert.Equal(Y, action.AlternateShortcutKey);
			Assert.True(action.KeyMatches(X | modifier));
			Assert.True(action.KeyMatches(Y | modifier));
		}

		[Fact]
		public void RequiredModifiersAndSpecialInputsMatchExactly()
		{
			UdbAction action = Create(Control | X, (int)SpecialKeys.MScrollDown | Alt);
			Assert.False(action.KeyMatches(X));
			Assert.False(action.KeyMatches(Control | Shift | X));
			Assert.True(action.KeyMatches(Control | X));
			Assert.True(action.KeyMatches((int)SpecialKeys.MScrollDown | Alt));
			Assert.False(action.KeyMatches((int)SpecialKeys.MScrollDown));
		}

		[Fact]
		public void DuplicateSlotsCollapseAfterMasking()
		{
			UdbAction action = Create(X, Shift | X, "disregardshift = true;");
			Assert.Equal(0, action.AlternateShortcutKey);
			action.SetShortcutKeys(Y, Y);
			Assert.Equal(Y, action.ShortcutKey);
			Assert.Equal(0, action.AlternateShortcutKey);
		}

		[Fact]
		public void AlternateOnlyPromotesButUnsetPrimaryWaitsForDefaults()
		{
			UdbAction action = Create(0, Y);
			Assert.Equal(Y, action.ShortcutKey);
			Assert.Equal(0, action.AlternateShortcutKey);
			action.SetShortcutKeys(-1, Y);
			Assert.Equal(-1, action.ShortcutKey);
			Assert.Equal(Y, action.AlternateShortcutKey);
			action.SetShortcutKey(X);
			Assert.Equal(Y, action.AlternateShortcutKey);
			action.SetShortcutKey(0);
			Assert.Equal(Y, action.ShortcutKey);
			Assert.Equal(0, action.AlternateShortcutKey);
		}

		[Fact]
		public void DescriptionsShowBothOneOrNoBindings()
		{
			Assert.Equal("X, Y", Create(X, Y).ShortcutKeysDesc);
			Assert.Equal("Y", Create(0, Y).ShortcutKeysDesc);
			Assert.Equal("", Create(0, 0).ShortcutKeysDesc);
		}

		[Fact]
		public void SettingsRoundTripAndClearedAlternateIsDeleted()
		{
			Configuration cfg = new Configuration();
			UdbAction action = Create(X, Y);
			ActionManager.WriteKeys(cfg, action);
			Configuration loaded = new Configuration();
			loaded.InputConfiguration(cfg.OutputConfiguration());
			int primary, alternate;
			ActionManager.ReadKeys(loaded, action.Name, out primary, out alternate);
			Assert.Equal(X, primary);
			Assert.Equal(Y, alternate);
			action.SetShortcutKeys(X, 0);
			ActionManager.WriteKeys(loaded, action);
			Assert.False(loaded.SettingExists("shortcutsalt.test"));
			ActionManager.ReadKeys(loaded, action.Name, out primary, out alternate);
			Assert.Equal(X, primary);
			Assert.Equal(0, alternate);
		}

		[Fact]
		public void LegacyAndFreshSettingsDoNotCreateAnAlternateStructure()
		{
			Configuration cfg = new Configuration();
			int primary, alternate;
			ActionManager.ReadKeys(cfg, "test", out primary, out alternate);
			Assert.Equal(-1, primary);
			Assert.Equal(0, alternate);
			cfg.InputConfiguration("shortcuts { test = 88; }");
			ActionManager.ReadKeys(cfg, "test", out primary, out alternate);
			Assert.Equal(X, primary);
			Assert.Equal(0, alternate);
			ActionManager.WriteKeys(cfg, Create(X, 0));
			Assert.False(cfg.SettingExists("shortcutsalt"));
		}

		[Fact]
		public void DefaultsDoNotStealAnAlternateAndFreeDefaultsStillApply()
		{
			UdbAction bound = Create(X, Y);
			UdbAction blocked = Create(-1, 0, "default = 89;", "blocked");
			UdbAction free = Create(-1, 0, "default = 90;", "free");
			ActionManager.ApplyDefaultShortcutKeys(new[] { bound, blocked, free });
			Assert.Equal(0, blocked.ShortcutKey);
			Assert.Equal(90, free.ShortcutKey);
			Assert.Equal(Y, bound.AlternateShortcutKey);
		}

		[Fact]
		public void DefaultWithAnAlternateNormalizesOnceResolved()
		{
			UdbAction action = Create(-1, Y, "default = 89;");
			ActionManager.ApplyDefaultShortcutKeys(new[] { action });
			Assert.Equal(Y, action.ShortcutKey);
			Assert.Equal(0, action.AlternateShortcutKey);
		}

		[Fact]
		public void ConflictMatchingUsesEitherNonzeroSlotAndTheSuppliedMask()
		{
			Assert.True(UdbAction.SlotsUseKey(X, Control | Y, Y, ~Control));
			Assert.True(UdbAction.SlotsUseKey(Control | X, Y, X, ~Control));
			Assert.False(UdbAction.SlotsUseKey(0, 0, Shift, ~Shift));
			Assert.False(UdbAction.SlotsUseKey(X, Y, 0, -1));
			Assert.False(UdbAction.SlotsUseKey(X, Y, Control | X, -1));
		}

		[Theory]
		[InlineData(false, false)]
		[InlineData(false, true)]
		[InlineData(true, false)]
		[InlineData(true, true)]
		public void HoldingBothBindingsBeginsOnceUnlessRepeatIsAllowedAndEndsOnLastRelease(bool repeat, bool reverse)
		{
			Dispatch(repeat, reverse, false, false);
		}

		[Fact]
		public void BindingAfterFirstKeyDoesNotSuppressTheFirstBeginOnTheAlternate()
		{
			Dispatch(false, false, true, false);
		}

		[Fact]
		public void EndOnlyActionsStillEndOnLastRelease()
		{
			Dispatch(false, false, false, true);
		}

		[Fact]
		public void ReentrantReleaseDoesNotLeaveStaleBeginsForLaterActions()
		{
			UdbAction first = Create(X, 0, "", "first");
			UdbAction second = Create(X, Y, "", "second");
			WithManager(new[] { first, second }, manager =>
			{
				int begins = 0;
				first.BindBegin(() => manager.ReleaseAllKeys());
				second.BindBegin(() => begins++);
				manager.KeyPressed(X);
				Assert.Equal(0, begins);
				Assert.Empty((HashSet<UdbAction>)typeof(ActionManager)
					.GetField("begunactions", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(manager));
				manager.KeyPressed(Y);
				Assert.Equal(1, begins);
				manager.ReleaseAllKeys();
				manager.KeyPressed(Y);
				Assert.Equal(2, begins);
			});
		}

		[Fact]
		public void ModifierChangesEndAndRestartOnlyOnMatchingBindings()
		{
			UdbAction action = Create(X, Shift | Y);
			WithManager(new[] { action }, manager =>
			{
				int begins = 0, ends = 0;
				action.BindBegin(() => begins++);
				action.BindEnd(() => ends++);
				manager.KeyPressed(X);
				manager.UpdateModifiers(Shift);
				Assert.Equal(1, ends);
				manager.KeyPressed(Shift | Y);
				Assert.Equal(2, begins);
				manager.UpdateModifiers(0);
				manager.KeyReleased(Y);
				Assert.Equal(1, ends);
				manager.KeyReleased(X);
				Assert.Equal(2, ends);
			});
		}

		private static void WithManager(UdbAction[] actions, System.Action<ActionManager> run)
		{
			// Drive the real dispatcher without loading the editor or its plugins.
			ActionManager manager = (ActionManager)FormatterServices.GetUninitializedObject(typeof(ActionManager));
			Dictionary<string, UdbAction> dictionary = new Dictionary<string, UdbAction>();
			foreach(UdbAction action in actions) dictionary.Add(action.Name, action);
			typeof(ActionManager).GetField("actions", BindingFlags.Instance | BindingFlags.NonPublic)
				.SetValue(manager, dictionary);
			typeof(ActionManager).GetField("pressedkeys", BindingFlags.Instance | BindingFlags.NonPublic)
				.SetValue(manager, new List<int>());
			typeof(ActionManager).GetField("activeactions", BindingFlags.Instance | BindingFlags.NonPublic)
				.SetValue(manager, new List<UdbAction>());
			typeof(ActionManager).GetField("begunactions", BindingFlags.Instance | BindingFlags.NonPublic)
				.SetValue(manager, new HashSet<UdbAction>());
			FieldInfo actionsfield = typeof(General).GetField("actions", BindingFlags.Static | BindingFlags.NonPublic);
			FieldInfo pluginsfield = typeof(General).GetField("plugins", BindingFlags.Static | BindingFlags.NonPublic);
			object oldactions = actionsfield.GetValue(null);
			object oldplugins = pluginsfield.GetValue(null);
			try
			{
				actionsfield.SetValue(null, manager);
				pluginsfield.SetValue(null, new PluginManager());
				run(manager);
			}
			finally
			{
				actionsfield.SetValue(null, oldactions);
				pluginsfield.SetValue(null, oldplugins);
			}
		}

		private static void Dispatch(bool repeat, bool reverse, bool bindlate, bool endonly)
		{
			UdbAction action = Create(X, Y, "repeat = " + (repeat ? "true" : "false") + ";");
			int begins = 0, ends = 0;
			if(!bindlate && !endonly) action.BindBegin(() => begins++);
			if(!bindlate) action.BindEnd(() => ends++);
			WithManager(new[] { action }, manager =>
			{
				int first = reverse ? Y : X;
				int second = reverse ? X : Y;
				Assert.True(manager.KeyPressed(first));
				Assert.True(manager.KeyPressed(first));
				if(bindlate)
				{
					Assert.Equal(0, begins);
					action.BindBegin(() => begins++);
					action.BindEnd(() => ends++);
				}
				Assert.True(manager.KeyPressed(second));
				Assert.Equal(endonly ? 0 : repeat ? 3 : 1, begins);
				Assert.False(manager.KeyReleased(first));
				Assert.Equal(0, ends);
				Assert.True(manager.KeyReleased(second));
				Assert.Equal(1, ends);
				manager.KeyPressed(second);
				Assert.Equal(endonly ? 0 : repeat ? 4 : 2, begins);
				manager.ReleaseAllKeys();
				Assert.Equal(2, ends);
			});
		}
	}
}
