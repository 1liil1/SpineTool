using System;
using System.Reflection;

namespace Spine.Unity.Editor {
	// Optional bridge across asmdef boundaries; all clocks/aggregation remain in the injected recorder.
	internal static class CiBuildPerfBridge {
		static bool checkedRecorder;
		static Func<string, IDisposable> scope, measure;
		static Action<string, string> note;

		static void Bind () {
			if (checkedRecorder) return;
			checkedRecorder = true;
			if (string.IsNullOrEmpty(Environment.GetEnvironmentVariable("CI_BUILD_PERF_DIR"))) return;
			try {
				foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies()) {
					var type = assembly.GetType("Hlwd.Editor.CiBuildPerf", false);
					if (type == null) continue;
					var flags = BindingFlags.NonPublic | BindingFlags.Static;
					scope = (Func<string, IDisposable>)Delegate.CreateDelegate(typeof(Func<string, IDisposable>), type.GetMethod("Scope", flags));
					measure = (Func<string, IDisposable>)Delegate.CreateDelegate(typeof(Func<string, IDisposable>), type.GetMethod("Measure", flags));
					note = (Action<string, string>)Delegate.CreateDelegate(typeof(Action<string, string>), type.GetMethod("Note", flags));
					break;
				}
			} catch (Exception) { scope = measure = null; note = null; }
		}

		internal static IDisposable Scope (string key) {
			try { Bind(); return scope == null ? null : scope(key); }
			catch (Exception) { return null; }
		}

		internal static IDisposable Measure (string key) {
			try { Bind(); return measure == null ? null : measure(key); }
			catch (Exception) { return null; }
		}

		internal static void Note (string key, string detail) {
			try { Bind(); if (note != null) note(key, detail); }
			catch (Exception) { }
		}
	}
}
