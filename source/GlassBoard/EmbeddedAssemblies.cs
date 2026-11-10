using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;

namespace GlassBoard;

// 托盘服务需要真实的模块句柄（GetHINSTANCE）来注册窗口类，所以只能从磁盘加载，不能用 Assembly.Load(byte[])。
public static class EmbeddedAssemblies
{
	private const string Prefix = "runtime/";

	private static readonly object gate = new object();

	private static readonly Dictionary<string, Assembly> loaded = new Dictionary<string, Assembly>(StringComparer.OrdinalIgnoreCase);

	private static string folder;

	public static void Install()
	{
		AppDomain.CurrentDomain.AssemblyResolve += Resolve;
	}

	private static Assembly Resolve(object sender, ResolveEventArgs args)
	{
		string name = new AssemblyName(args.Name).Name;
		lock (gate)
		{
			if (loaded.TryGetValue(name, out var assembly))
			{
				return assembly;
			}
			Assembly self = typeof(EmbeddedAssemblies).Assembly;
			string resource = Prefix + name + ".dll";
			if (!self.GetManifestResourceNames().Contains(resource))
			{
				return null;
			}
			try
			{
				string path = Extract(self, resource, name + ".dll");
				assembly = Assembly.LoadFrom(path);
				loaded[name] = assembly;
				return assembly;
			}
			catch (Exception ex)
			{
				Store.Log(ex);
				return null;
			}
		}
	}

	private static string Extract(Assembly self, string resource, string fileName)
	{
		byte[] bytes;
		using (Stream stream = self.GetManifestResourceStream(resource))
		{
			using MemoryStream memoryStream = new MemoryStream();
			stream.CopyTo(memoryStream);
			bytes = memoryStream.ToArray();
		}
		string path = Path.Combine(Folder(self), fileName);
		if (File.Exists(path) && SameBytes(File.ReadAllBytes(path), bytes))
		{
			return path;
		}
		string temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
		File.WriteAllBytes(temporary, bytes);
		try
		{
			if (File.Exists(path))
			{
				File.Delete(path);
			}
			File.Move(temporary, path);
		}
		finally
		{
			if (File.Exists(temporary))
			{
				File.Delete(temporary);
			}
		}
		return path;
	}

	private static string Folder(Assembly self)
	{
		if (folder != null)
		{
			return folder;
		}
		string root = Path.Combine(Store.Folder, "runtime");
		string build = self.ManifestModule.ModuleVersionId.ToString("N");
		folder = Path.Combine(root, build);
		Directory.CreateDirectory(folder);
		foreach (string old in Directory.GetDirectories(root))
		{
			if (!string.Equals(Path.GetFileName(old), build, StringComparison.OrdinalIgnoreCase))
			{
				try
				{
					Directory.Delete(old, recursive: true);
				}
				catch
				{
				}
			}
		}
		return folder;
	}

	private static bool SameBytes(byte[] a, byte[] b)
	{
		return a.Length == b.Length && a.SequenceEqual(b);
	}
}
