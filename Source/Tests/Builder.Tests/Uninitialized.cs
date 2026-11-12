using System.Reflection;
using System.Runtime.CompilerServices;
using System.Windows.Forms;

namespace Builder.Tests
{
	// Skips constructors so controls need no display; .NET (not Mono) reads the property store
	// and native window wrapper on every handle check, so those are filled in where they exist
	internal static class Uninitialized
	{
		private const BindingFlags Fields = BindingFlags.Instance | BindingFlags.NonPublic;
		private static readonly FieldInfo? window = typeof(Control).GetField("_window", Fields);
		private static readonly FieldInfo? properties = typeof(Control).GetField("<Properties>k__BackingField", Fields);

		internal static T Create<T>()
		{
			T result = (T)RuntimeHelpers.GetUninitializedObject(typeof(T));
			GC.SuppressFinalize(result!);
			if(result is Control control && window != null && properties != null)
			{
				properties.SetValue(control, Activator.CreateInstance(properties.FieldType, true));
				object native = Activator.CreateInstance(window.FieldType, Fields, null, new object[] { control }, null)!;
				GC.SuppressFinalize(native);
				window.SetValue(control, native);
			}
			return result;
		}
	}
}
