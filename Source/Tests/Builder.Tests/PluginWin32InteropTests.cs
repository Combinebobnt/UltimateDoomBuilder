using System.Reflection;
using System.Runtime.InteropServices;
using CodeImp.DoomBuilder.BuilderModes;

namespace Builder.Tests
{
	public class PluginWin32InteropTests
	{
		private const BindingFlags All = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly;
		private static readonly string[] Win32Libraries = { "user32", "gdi32", "kernel32", "comctl32", "shell32", "uxtheme" };

		// Mono has no real Win32: at best libMonoSupportW stubs a few user32 entry points, and without it a call throws.
		// A tripwire, not a proof: it only checks that OperatingSystem.Platform is read before the call in the same method.
		[Fact]
		public void BuilderModesChecksPlatformBeforeEveryWin32Call()
		{
			Module module = typeof(BuilderPlug).Module;
			List<MethodBase> methods = module.GetTypes().SelectMany(type => type.GetMethods(All).Cast<MethodBase>().Concat(type.GetConstructors(All))).ToList();
			Dictionary<int, MethodBase> pinvokes = methods.Where(m => (m.Attributes & MethodAttributes.PinvokeImpl) != 0 && IsWin32(m)).ToDictionary(m => m.MetadataToken);

			var unguarded = new List<string>();
			foreach(MethodBase method in methods)
			{
				byte[] code = method.GetMethodBody()?.GetILAsByteArray();
				if(code == null) continue;
				bool candidate = false;
				for(int i = 0; i + 4 < code.Length && !candidate; i++)
					candidate = code[i] == 0x28 && pinvokes.ContainsKey(BitConverter.ToInt32(code, i + 1));
				if(!candidate) continue;

				var calls = TexturePaletteTests.Calls(method);
				int platform = calls.Where(call => call.Value.DeclaringType == typeof(OperatingSystem) && call.Value.Name == "get_Platform")
					.Select(call => call.Key).DefaultIfEmpty(int.MaxValue).Min();
				foreach(var call in calls.Where(call => pinvokes.ContainsKey(call.Value.MetadataToken) && call.Value.Module == module))
					if(call.Key < platform) unguarded.Add(method.DeclaringType.Name + "." + method.Name + " -> " + call.Value.Name);
			}

			Assert.True(unguarded.Count == 0, "Win32 P/Invoke without a platform check: " + string.Join(", ", unguarded));
		}

		// A runtime that hides the DllImport pseudo-attribute counts every P/Invoke as Win32.
		private static bool IsWin32(MethodBase method)
		{
			DllImportAttribute import = method.GetCustomAttributes(typeof(DllImportAttribute), false).OfType<DllImportAttribute>().FirstOrDefault();
			if(import == null) return true;
			string name = Path.GetFileNameWithoutExtension(import.Value).ToLowerInvariant();
			return Win32Libraries.Contains(name);
		}
	}
}
