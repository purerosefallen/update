using System;

namespace update
{
	/// <summary>
	/// Description of MyHttpListener.
	/// </summary>
	public interface MyHttpListener
	{
		void OnStart(string name,string file);
		void OnStart(string name,string file, int threadId);
		void OnProgress(fileinfo ff, int threadId, long downloadedBytes, long totalBytes);
		void OnEnd(fileinfo ff,bool isOK);
		void OnEnd(fileinfo ff,bool isOK, int threadId);
	}
}