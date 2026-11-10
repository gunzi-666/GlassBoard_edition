using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.Serialization.Json;
using System.Text;

namespace GlassBoard;

internal static class UpdaterProgram
{
	private const int ExitTimeoutMilliseconds = 30000;

	private static int Main(string[] args)
	{
		Console.OutputEncoding = Encoding.UTF8;
		Console.Title = "GlassBoard 更新";
		string log = null;
		try
		{
			if (args.Length != 1 || !File.Exists(args[0]))
			{
				throw new InvalidDataException("缺少更新计划");
			}
			string planPath = Path.GetFullPath(args[0]);
			log = Path.Combine(Path.GetDirectoryName(planPath), "update.log");
			UpdatePlan plan;
			using (FileStream stream = File.OpenRead(planPath))
			{
				plan = (UpdatePlan)new DataContractJsonSerializer(typeof(UpdatePlan)).ReadObject(stream);
			}
			if (plan == null || string.IsNullOrWhiteSpace(plan.Target) || string.IsNullOrWhiteSpace(plan.Stage) || string.IsNullOrWhiteSpace(plan.Backup))
			{
				throw new InvalidDataException("更新计划异常");
			}
			Write(log, "GlassBoard 正在更新到 v" + plan.Version + "，请勿关闭此窗口。");
			if (!WaitForExit(plan.Pid))
			{
				throw new IOException("软件尚未退出，更新已取消。");
			}
			UpdateApply.Install(plan, delegate(string name)
			{
				Write(log, "已更新：" + name);
			});
			Write(log, "更新完成，正在重新启动。");
			Process.Start(new ProcessStartInfo(UpdateApply.Safe(plan.Target, "GlassBoard.exe"))
			{
				UseShellExecute = true,
				WorkingDirectory = plan.Target
			});
			return 0;
		}
		catch (Exception ex)
		{
			for (Exception inner = ex; inner != null; inner = inner.InnerException)
			{
				Write(log, inner.Message);
			}
			if (log != null)
			{
				Append(log, ex.ToString());
			}
			Console.WriteLine();
			Console.WriteLine("按 Enter 关闭。若没有恢复，请使用备份文件夹手动恢复。");
			Console.ReadLine();
			return 1;
		}
	}

	private static bool WaitForExit(int pid)
	{
		if (pid <= 0 || pid == Process.GetCurrentProcess().Id)
		{
			return true;
		}
		try
		{
			using Process process = Process.GetProcessById(pid);
			return process.WaitForExit(ExitTimeoutMilliseconds);
		}
		catch (ArgumentException)
		{
			return true;
		}
		catch (InvalidOperationException)
		{
			return true;
		}
	}

	private static void Write(string log, string message)
	{
		Console.WriteLine(message);
		if (log != null)
		{
			Append(log, message);
		}
	}

	private static void Append(string log, string message)
	{
		try
		{
			File.AppendAllText(log, DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + "  " + message + Environment.NewLine, Encoding.UTF8);
		}
		catch
		{
		}
	}
}
