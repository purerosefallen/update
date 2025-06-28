/*
 * 由SharpDevelop创建。
 * 用户： Acer
 * 日期: 2014-9-25
 * 时间: 21:33
 * 
 */
using System;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.Linq;

namespace update
{
	/// <summary>
	/// Description of MyHttp.
	/// </summary>
	public class MyHttp
	{
		// 使用 SemaphoreSlim 替代手动计数，更安全地控制并发
		private static SemaphoreSlim _concurrencySemaphore;
		private static int _activeTaskCount = 0;
		private static int _totalTaskCount = 0;
		private static readonly object _taskCountLock = new object();
		
		// 线程ID管理
		private static readonly HashSet<int> _activeThreadIds = new HashSet<int>();
		private static readonly object _threadIdLock = new object();
		
		public static int NUM => _activeTaskCount;
		public static int MAX_NUM = 0x10;
		
		private string _url, _filename;
		private fileinfo _ff;
		private int _threadId;
		
		// 静态 HttpClient 实例，避免每次创建新实例
		private static HttpClient httpClient;
		private static MyHttpListener myhttplistiner = null;
		
		// 代理设置
		private static bool isProxy = false;
		private static string proxyip;
		private static int proxyport;
		
		public MyHttp(string url, string filename, fileinfo ff){
			this._url = url;
			this._filename = filename;
			this._ff = ff;
			this._threadId = GetNextAvailableThreadId();
		}
		
		/// <summary>
		/// 获取下一个可用的线程ID
		/// </summary>
		private static int GetNextAvailableThreadId()
		{
			lock (_threadIdLock)
			{
				// 查找1到MAX_NUM之间第一个未使用的ID
				for (int i = 1; i <= MAX_NUM; i++)
				{
					if (!_activeThreadIds.Contains(i))
					{
						_activeThreadIds.Add(i);
						return i;
					}
				}
				
				// 如果所有ID都在使用中，返回0（表示无可用ID）
				return 0;
			}
		}
		
		/// <summary>
		/// 释放线程ID
		/// </summary>
		private static void ReleaseThreadId(int threadId)
		{
			if (threadId > 0)
			{
				lock (_threadIdLock)
				{
					_activeThreadIds.Remove(threadId);
				}
			}
		}
		
		/// <summary>
		/// 异步启动下载任务
		/// </summary>
		public async void Start(){
			// 如果没有获取到有效的线程ID，直接等待信号量
			if (_threadId == 0)
			{
				await _concurrencySemaphore.WaitAsync();
				
				// 再次尝试获取线程ID
				_threadId = GetNextAvailableThreadId();
			}
			else
			{
				await _concurrencySemaphore.WaitAsync();
			}
			
			try
			{
				// 增加活动任务计数
				IncrementTaskCount();
				
				// 执行下载
				await DownLoadAsync(_url, _filename, _ff, _threadId);
			}
			finally
			{
				// 减少活动任务计数
				DecrementTaskCount();
				
				// 释放线程ID
				ReleaseThreadId(_threadId);
				
				// 释放信号量，允许其他任务执行
				_concurrencySemaphore.Release();
			}
		}
		
		/// <summary>
		/// 增加任务计数
		/// </summary>
		private static void IncrementTaskCount()
		{
			lock (_taskCountLock)
			{
				_activeTaskCount++;
				_totalTaskCount++;
			}
		}
		
		/// <summary>
		/// 减少任务计数
		/// </summary>
		private static void DecrementTaskCount()
		{
			lock (_taskCountLock)
			{
				_activeTaskCount--;
			}
		}
		
		/// <summary>
		/// 设置监听器
		/// </summary>
		public static void SetListner(MyHttpListener listiner){
			myhttplistiner = listiner;
		}
		
		/// <summary>
		/// 初始化下载系统
		/// </summary>
		public static void init(int max){
			ServicePointManager.DefaultConnectionLimit = 255;
			MAX_NUM = max;
			
			// 初始化信号量
			_concurrencySemaphore = new SemaphoreSlim(max, max);
			
			// 重置计数和线程ID
			lock (_taskCountLock)
			{
				_activeTaskCount = 0;
				_totalTaskCount = 0;
			}
			
			lock (_threadIdLock)
			{
				_activeThreadIds.Clear();
			}
			
			// 初始化 HttpClient
			if (httpClient != null)
			{
				httpClient.Dispose();
			}
			
			HttpClientHandler handler = new HttpClientHandler();
			if (isProxy)
			{
				handler.Proxy = new WebProxy(proxyip, proxyport);
				handler.UseProxy = true;
			}
			
			httpClient = new HttpClient(handler);
			httpClient.Timeout = TimeSpan.FromSeconds(30);
		}
		
		/// <summary>
		/// 设置代理
		/// </summary>
		public static void setProxy(bool isuse, string ip, int port){
			isProxy = isuse;
			proxyip = ip;
			proxyport = port;
			
			// 如果 HttpClient 已经初始化，则重新配置
			if (httpClient != null)
			{
				init(MAX_NUM);
			}
		}
		
		/// <summary>
		/// 检查所有任务是否完成
		/// </summary>
		public static bool isOK(){
			lock (_taskCountLock)
			{
				return _activeTaskCount == 0 && _totalTaskCount > 0;
			}
		}
		
		/// <summary>
		/// 获取剩余任务数
		/// </summary>
		public static int GetTask(){
			lock (_taskCountLock)
			{
				return _activeTaskCount;
			}
		}
		
		/// <summary>
		/// 获取当前活动的线程ID列表
		/// </summary>
		public static int[] GetActiveThreadIds()
		{
			lock (_threadIdLock)
			{
				return _activeThreadIds.ToArray();
			}
		}
		
		/// <summary>
		/// 下载文件（无文件信息）
		/// </summary>
		public static bool DownLoad(string url, string filename)
		{
			return DownLoadAsync(url, filename, null, 0).GetAwaiter().GetResult();
		}
		
		/// <summary>
		/// 下载文件（带文件信息）
		/// </summary>
		public static bool DownLoad(string url, string filename, fileinfo ff)
		{
			return DownLoadAsync(url, filename, ff, 0).GetAwaiter().GetResult();
		}
		
		/// <summary>
		/// 下载文件（带文件信息和线程ID）
		/// </summary>
		public static bool DownLoad(string url, string filename, fileinfo ff, int threadId)
		{
			return DownLoadAsync(url, filename, ff, threadId).GetAwaiter().GetResult();
		}
		
		/// <summary>
		/// 异步下载文件
		/// </summary>
		private static async Task<bool> DownLoadAsync(string url, string filename, fileinfo ff, int threadId)
		{
			if(myhttplistiner != null)
				myhttplistiner.OnStart(url, filename, threadId);
			
			bool isOK = false;
			
			try
			{
				// 确保 HttpClient 已初始化
				if (httpClient == null)
				{
					init(MAX_NUM);
				}
				
				// 确保目录存在
				if(File.Exists(filename))
					File.Delete(filename);
				else
					MyUtil.createDir(filename);
				
				// 发送 HEAD 请求获取文件大小
				long totalBytes = 0;
				using (HttpResponseMessage headResponse = await httpClient.SendAsync(new HttpRequestMessage(HttpMethod.Head, url)))
				{
					headResponse.EnsureSuccessStatusCode();
					if (headResponse.Content.Headers.ContentLength.HasValue)
					{
						totalBytes = headResponse.Content.Headers.ContentLength.Value;
					}
				}
				
				// 发送 GET 请求下载文件
				using (HttpResponseMessage response = await httpClient.GetAsync(url, HttpCompletionOption.ResponseHeadersRead))
				{
					response.EnsureSuccessStatusCode();
					
					using (Stream contentStream = await response.Content.ReadAsStreamAsync())
					using (FileStream fileStream = new FileStream(filename + ".tmp", FileMode.Create, FileAccess.Write, FileShare.None, 8192, true))
					{
						byte[] buffer = new byte[8192];
						long totalDownloadedBytes = 0;
						int bytesRead;
						
						while ((bytesRead = await contentStream.ReadAsync(buffer, 0, buffer.Length)) > 0)
						{
							await fileStream.WriteAsync(buffer, 0, bytesRead);
							
							totalDownloadedBytes += bytesRead;
							
							// 更新进度
							if (myhttplistiner != null && totalBytes > 0)
							{
								myhttplistiner.OnProgress(ff, threadId, totalDownloadedBytes, totalBytes);
							}
						}
					}
				}
				
				// 完成下载，重命名文件
				File.Delete(filename);
				File.Move(filename + ".tmp", filename);
				isOK = true;
			}
			catch (Exception ex)
			{
				Console.WriteLine($"Download error: {ex.Message}");
				isOK = false;
			}
			
			// 确认文件是否存在
			isOK = isOK && File.Exists(filename);
			
			if(myhttplistiner != null)
				myhttplistiner.OnEnd(ff, isOK, threadId);
			
			return isOK;
		}
	}
}