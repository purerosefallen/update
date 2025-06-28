/*
 * 由SharpDevelop创建。
 * 用户： Acer
 * 日期: 2014-9-30
 * 时间: 15:45
 * 
 */
using System;
using System.IO;
using System.Configuration;
using System.Collections.Generic;
using System.Text;
using System.Text.RegularExpressions;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using System.Collections.Concurrent;

namespace update
{
	/// <summary>
	/// Description of Download.
	/// </summary>
	public class Client : MyHttpListener, IDisposable
	{
		int all_num, num;
		List<fileinfo> errorlist;
		private Dictionary<int, DownloadProgress> progressTrackers;
		private int consoleStartLine;
		private object progressLock = new object();
		private string lastDownloadedFile = "";
		private CancellationTokenSource cancellationTokenSource;
		private bool uiUpdateRunning = false;
		private bool needsUiUpdate = false;
		
		// MD5计算的并行处理
		private SemaphoreSlim md5Semaphore;
		private ConcurrentQueue<MD5Task> md5Queue;
		private bool md5ProcessorRunning = false;
		
		private class MD5Task
		{
			public string FilePath { get; set; }
			public string ExpectedMD5 { get; set; }
			public string FileName { get; set; }
			public bool IgnoreSound { get; set; }
			public TaskCompletionSource<bool> CompletionSource { get; set; }
		}
		
		public class DownloadProgress
		{
			public string FileName { get; set; }
			public long DownloadedBytes { get; set; }
			public long TotalBytes { get; set; }
			public bool IsCompleted { get; set; }
			public bool IsSuccess { get; set; }
			
			public string GetProgressBar(int width)
			{
				if (TotalBytes <= 0) return "[Waiting...]";
				if (IsCompleted) return IsSuccess ? "[Completed]" : "[Failed]";
				
				int progressChars = (int)((double)DownloadedBytes / TotalBytes * width);
				StringBuilder bar = new StringBuilder("[");
				for (int i = 0; i < width; i++)
				{
					bar.Append(i < progressChars ? '=' : ' ');
				}
				bar.Append("]");
				double percent = (double)DownloadedBytes / TotalBytes * 100;
				bar.Append($" {percent:F1}%");
				return bar.ToString();
			}
		}
		
		public Client(string path, string url){
			if(string.IsNullOrEmpty(path))
				Config.setWorkPath(ConfigurationManager.AppSettings["path"], url);
			else
				Config.setWorkPath(path, url);
			
			errorlist = new List<fileinfo>();
			progressTrackers = new Dictionary<int, DownloadProgress>();
			cancellationTokenSource = new CancellationTokenSource();
			
			// 初始化MD5计算资源
			md5Semaphore = new SemaphoreSlim(Environment.ProcessorCount, Environment.ProcessorCount);
			md5Queue = new ConcurrentQueue<MD5Task>();

			//代理设置
			if(Config.useProxy){
				Console.WriteLine("USE PROXY:" + Config.proxyIP + ":" + Config.proxyPort);
				MyHttp.setProxy(true, Config.proxyIP, Config.proxyPort);
			}
			else{
				MyHttp.setProxy(false, "127.0.0.1", 80);
			}
		}
		
		// 启动MD5处理线程
		private async Task StartMD5ProcessorAsync()
		{
			if (md5ProcessorRunning)
				return;
				
			md5ProcessorRunning = true;
			
			try
			{
				List<Task> activeTasks = new List<Task>();
				
				while (!cancellationTokenSource.Token.IsCancellationRequested)
				{
					// 清理已完成的任务
					activeTasks.RemoveAll(t => t.IsCompleted);
					
					// 处理队列中的MD5任务
					while (md5Queue.TryDequeue(out MD5Task task))
					{
						await md5Semaphore.WaitAsync(cancellationTokenSource.Token);
						
						var md5Task = Task.Run(async () => {
							try
							{
								bool result = await ProcessMD5TaskAsync(task);
								task.CompletionSource.SetResult(result);
							}
							catch (Exception ex)
							{
								task.CompletionSource.SetException(ex);
							}
							finally
							{
								md5Semaphore.Release();
							}
						}, cancellationTokenSource.Token);
						
						activeTasks.Add(md5Task);
					}
					
					await Task.Delay(50, cancellationTokenSource.Token);
				}
				
				// 等待所有活动任务完成
				await Task.WhenAll(activeTasks);
			}
			catch (OperationCanceledException)
			{
				// 任务被取消
			}
			finally
			{
				md5ProcessorRunning = false;
			}
		}
		
		// 处理单个MD5任务
		private async Task<bool> ProcessMD5TaskAsync(MD5Task task)
		{
			// 检查是否忽略音频文件
			if(task.IgnoreSound && (task.FileName.EndsWith(".mp3", StringComparison.OrdinalIgnoreCase) || 
			                        task.FileName.EndsWith(".ogg", StringComparison.OrdinalIgnoreCase) || 
			                        task.FileName.EndsWith(".wav", StringComparison.OrdinalIgnoreCase))) {
				Console.WriteLine("SOUND IGNORED: " + task.FileName);
				showProcess(num++, all_num);
				return true;
			}
			
			// 计算MD5并比较
			string fileMd5 = await Task.Run(() => MyUtil.MD5_File(task.FilePath));
			if(fileMd5 == task.ExpectedMD5) {
				// MD5匹配，文件无需更新
				showProcess(num++, all_num);
				return true;
			} 
			else if(MyUtil.checkList(Config.ignores, task.FileName)) {
				// 文件在忽略列表中
				showProcess(num++, all_num);
				return true;
			}
			
			// 需要下载文件
			return false;
		}
		
		// 启动UI更新线程
		private async Task StartUiUpdateTaskAsync()
		{
			if (uiUpdateRunning)
				return;
				
			uiUpdateRunning = true;
			
			try
			{
				while (!cancellationTokenSource.Token.IsCancellationRequested)
				{
					if (needsUiUpdate)
					{
						UpdateProgressDisplayInternal();
						needsUiUpdate = false;
					}
					
					await Task.Delay(100, cancellationTokenSource.Token);
				}
			}
			catch (OperationCanceledException)
			{
				// 任务被取消
			}
			finally
			{
				uiUpdateRunning = false;
			}
		}
		
		// 标记需要更新UI
		private void RequestUiUpdate()
		{
			needsUiUpdate = true;
		}
		
		async Task<bool> DeleteAsync(){
			if(!await Task.Run(() => MyHttp.DownLoad(Config.url_delete, Config.deleteFile))){
				return false;
			}
			
			string[] lines = await Task.Run(() => File.ReadAllLines(Config.deleteFile, Encoding.UTF8));
			foreach(string line in lines){
				if(!line.StartsWith("#")){
					string file = Config.GetPath(line);
					if(File.Exists(file)){
						Console.WriteLine("DELETE FILE:" + line);
						await Task.Run(() => File.Delete(file));
					}
				}
			}
			return true;
		}
		
		async Task<bool> RenameAsync(){
			if(!await Task.Run(() => MyHttp.DownLoad(Config.url_rename, Config.renameFile))){
				return false;
			}
			
			string[] lines = await Task.Run(() => File.ReadAllLines(Config.renameFile, Encoding.UTF8));
			foreach(string line in lines){
				if(!line.StartsWith("#")){
					string[] files = line.Split('\t');
					if(files.Length >= 2){
						string file1 = Config.GetPath(files[0]);
						string file2 = Config.GetPath(files[1]);
						Console.WriteLine("RENAME:" + files[0] + "=>" + files[1]);
						await Task.Run(() => File.Move(file1, file2));
					}
				}
			}
			return true;
		}
		
		void Rename(){
			RenameAsync().GetAwaiter().GetResult();
		}
		
		public void OnStart(string name, string file){
			OnStart(name, file, 0);
		}
		
		public void OnStart(string name, string file, int threadId){
			if (threadId > 0) {
				lock(progressLock) {
					progressTrackers[threadId] = new DownloadProgress {
						FileName = name,
						DownloadedBytes = 0,
						TotalBytes = 0,
						IsCompleted = false
					};
				}
				RequestUiUpdate();
			}
		}
		
		public void OnProgress(fileinfo ff, int threadId, long downloadedBytes, long totalBytes) {
			if (threadId > 0) {
				lock(progressLock) {
					if (progressTrackers.ContainsKey(threadId)) {
						progressTrackers[threadId].DownloadedBytes = downloadedBytes;
						progressTrackers[threadId].TotalBytes = totalBytes;
					}
				}
				RequestUiUpdate();
			}
		}
		
		public void OnEnd(fileinfo ff, bool isOK){
			OnEnd(ff, isOK, 0);
		}
		
		public void OnEnd(fileinfo ff, bool isOK, int threadId){
			if(all_num > 0)
				showProcess(num++, all_num);
			
			if (threadId > 0) {
				lock(progressLock) {
					if (progressTrackers.ContainsKey(threadId)) {
						progressTrackers[threadId].IsCompleted = true;
						progressTrackers[threadId].IsSuccess = isOK;
						
						// 记录最后下载的文件
						if (ff != null) {
							lastDownloadedFile = ff.name;
						}
					}
				}
				RequestUiUpdate();
			}
			
			if(!isOK){
				if(ff != null){
					lock(progressLock) {
						Console.WriteLine("DOWNLOAD FAILED:" + Config.GetUrl(ff.name));
						errorlist.Add(ff);
					}
				}else{
					Console.WriteLine("DOWNLOAD FAILED");
				}
			}
		}
		
		private void InitProgressDisplay() {
			try {
				// 清除控制台
				Console.Clear();
				consoleStartLine = 0;
				
				// 为每个线程预留一行
				for (int i = 0; i < MyHttp.MAX_NUM; i++) {
					Console.WriteLine(new string(' ', Console.WindowWidth - 1));
				}
				
				// 为总体进度预留一行
				Console.WriteLine(new string(' ', Console.WindowWidth - 1));
				
				// 为日志输出预留空间
				for (int i = 0; i < 5; i++) {
					Console.WriteLine();
				}
				
				// 启动UI更新线程
				Task.Run(() => StartUiUpdateTaskAsync());
				
				// 启动MD5处理线程
				Task.Run(() => StartMD5ProcessorAsync());
			}
			catch (Exception) {
				// 忽略可能的控制台错误
			}
		}
		
		// 内部UI更新方法，只在UI更新线程中调用
		private void UpdateProgressDisplayInternal() {
			try {
				int currentTop = Console.CursorTop;
				int currentLeft = Console.CursorLeft;
				
				// 保存当前位置
				int savedTop = currentTop;
				int savedLeft = currentLeft;
				
				Dictionary<int, DownloadProgress> progressCopy;
				
				// 复制进度数据以避免长时间锁定
				lock(progressLock) {
					progressCopy = new Dictionary<int, DownloadProgress>(progressTrackers);
				}
				
				// 绘制线程进度条
				for (int i = 1; i <= MyHttp.MAX_NUM; i++) {
					Console.SetCursorPosition(0, consoleStartLine + i - 1);
					Console.Write(new string(' ', Console.WindowWidth - 1)); // 清除当前行
					Console.SetCursorPosition(0, consoleStartLine + i - 1);
					
					if (progressCopy.ContainsKey(i)) {
						var progress = progressCopy[i];
						string status = progress.IsCompleted ? (progress.IsSuccess ? "Completed" : "Failed") : "Downloading";
						Console.Write($"Thread {i:D2} [{status}]: {progress.FileName.PadRight(30)} ");
						Console.Write(progress.GetProgressBar(40));
					}
					else {
						Console.Write($"Thread {i:D2} [Idle]: Waiting for download task...");
					}
				}
				
				// 绘制总体进度
				Console.SetCursorPosition(0, consoleStartLine + MyHttp.MAX_NUM);
				Console.Write(new string(' ', Console.WindowWidth - 1)); // 清除当前行
				Console.SetCursorPosition(0, consoleStartLine + MyHttp.MAX_NUM);
				
				string progressInfo = $"{num}/{all_num} files processed. Last downloaded: {lastDownloadedFile}";
				Console.Write(progressInfo);
				
				// 恢复光标位置
				Console.SetCursorPosition(savedLeft, savedTop);
			}
			catch (Exception) {
				// 处理可能的控制台错误
			}
		}
		
		void showProcess(int i, int all) {
			Console.Title = string.Format("PROGRESS: {0}/{1}", i, all);
		}
		
		// 使用并行MD5计算的下载方法
		async Task<bool> DownloadAsync(string name, string md5, bool isHide, bool ignore_sound) {
			string file = Config.GetPath(name);
			
			// 如果文件存在，使用并行MD5处理
			if(File.Exists(file)) {
				var tcs = new TaskCompletionSource<bool>();
				
				// 创建MD5任务并加入队列
				var md5Task = new MD5Task {
					FilePath = file,
					ExpectedMD5 = md5,
					FileName = name,
					IgnoreSound = ignore_sound,
					CompletionSource = tcs
				};
				
				md5Queue.Enqueue(md5Task);
				
				// 等待MD5处理完成
				bool skipDownload = await tcs.Task;
				if (skipDownload) {
					return true;
				}
			} else if(ignore_sound && (name.EndsWith(".mp3", StringComparison.OrdinalIgnoreCase) || 
			                           name.EndsWith(".ogg", StringComparison.OrdinalIgnoreCase) || 
			                           name.EndsWith(".wav", StringComparison.OrdinalIgnoreCase))) {
				// 音频文件且需要忽略
				Console.WriteLine("SOUND IGNORED: " + name);
				showProcess(num++, all_num);
				return true;
			}
			
			// 需要下载文件
			var http = new MyHttp(Config.GetUrl(name), file, new fileinfo(name, md5));
			http.Start();
			return true;
		}
		
		async Task UpdateAsync(bool ignore_sound) {
			if(!File.Exists(Config.errorFile)) { //上一次下载是否失败
				Console.WriteLine("Downloading Filelist... ...");
				if(!await Task.Run(() => MyHttp.DownLoad(Config.url_filelist, Config.filelistFile)))
					return;
				Console.WriteLine("Starting Update... ...");
			} else {
				await Task.Run(() => {
					File.Delete(Config.filelistFile);
					File.Move(Config.errorFile, Config.filelistFile);
				});
				Console.WriteLine("Continuing Update... ...");
			}
			
			if(ignore_sound) {
				Console.WriteLine("The sound files will be ignored.");
			}
			
			// 初始化进度显示
			InitProgressDisplay();
			
			string[] lines = await Task.Run(() => File.ReadAllLines(Config.filelistFile, Encoding.UTF8));
			all_num = lines.Length;
			num = 0;
			showProcess(num++, all_num);
			
			// 创建下载任务列表
			List<Task> downloadTasks = new List<Task>();
			
			foreach(string line in lines) {
				if(!line.StartsWith("#")) {
					string[] words = line.Split('\t');
					if(words.Length >= 2) {
						var downloadTask = DownloadAsync(words[0], words[1], false, ignore_sound);
						downloadTasks.Add(downloadTask);
						
						// 限制并发任务数量，避免创建过多任务
						if (downloadTasks.Count >= MyHttp.MAX_NUM * 2) {
							// 等待任意一个任务完成
							await Task.WhenAny(downloadTasks);
							// 移除已完成的任务
							downloadTasks.RemoveAll(t => t.IsCompleted);
						}
					}
				}
			}
			
			// 等待所有下载任务提交完成
			await Task.WhenAll(downloadTasks);
			
			// 等待所有下载完成
			while(!MyHttp.isOK() && !cancellationTokenSource.Token.IsCancellationRequested) {
				await Task.Delay(100, cancellationTokenSource.Token);
			}
			
			if(errorlist.Count > 0) {
				Console.WriteLine("Some files failed to update... ...");
				await Task.Run(() => MyUtil.saveList(Config.errorFile, errorlist.ToArray()));
			}
		}
		
		public async Task RunAsync(bool ignore_sound) {
			Console.WriteLine("UPDATE FROM: " + Config.url_home);
			Console.WriteLine("DOWNLOAD TO: " + Config.workPath);
			Console.WriteLine("CONFIG FILE: " + Assembly.GetExecutingAssembly().Location + ".config");

			if(!File.Exists(Config.errorFile)) {
				Console.WriteLine("Getting New Version ... ...");
				//version
				await Task.Run(() => MyHttp.DownLoad(Config.url_version, Config.newVersionFile));
				//版本号一致
				string md5_1 = await Task.Run(() => MyUtil.MD5_File(Config.versionFile));
				string md5_2 = await Task.Run(() => MyUtil.MD5_File(Config.newVersionFile));
				if(md5_1 == md5_2 && md5_1.Length > 0) {
					Console.WriteLine("Your files are already up-to-date.");
					return;
				}
				Console.WriteLine("New Version Discovered... ...");
				//删除旧文件
				await DeleteAsync();
				//重命名文件
				await RenameAsync();
			}
			
			//filelist
			await UpdateAsync(ignore_sound);
			
			if(File.Exists(Config.newVersionFile)) {
				await Task.Run(() => {
					File.Delete(Config.versionFile);
					File.Move(Config.newVersionFile, Config.versionFile);
				});
			}
			
			// 更新最终显示
			Console.WriteLine("UPDATE COMPLETE!! You can safely close this window, press any key to quit.");
		}
		
		public void Run(bool ignore_sound) {
			RunAsync(ignore_sound).GetAwaiter().GetResult();
		}
		
		// 清理资源
		public void Dispose() {
			cancellationTokenSource?.Cancel();
			cancellationTokenSource?.Dispose();
			md5Semaphore?.Dispose();
		}
	}
}