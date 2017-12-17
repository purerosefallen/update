/*
 * 由SharpDevelop创建。
 * 用户： Acer
 * 日期: 2014-9-30
 * 时间: 15:31
 * 
 */
using System;

namespace update
{
	class Program
	{
		public static void Main(string[] args)
		{
			Console.Title="AUTOUPDATE";
			Config.Init(null,null);

			if(args.Length>0){
				switch(args[0]){
					case "-m":UpdateList(args);break;
					case "-ci":UpdateList(args);return;
					case "--ignore-sound":Download(null,null,true);break;
				case "-d":
					if(args.Length==2)
						Download(args[1],null,false);
					else{
						if(args[2]=="--ignore-sound")
							Download(args[1],null,true);
						else if(args[3]=="--ignore-sound")
							Download(args[1],args[2],true);
					}
					break;
				}
			}else{
				Download(null,null,false);
			}
			Console.WriteLine("Press Any Key to continue ... ... ");
			Console.ReadKey(true);
		}
		private static void UpdateList(string[] args){
			if(args.Length>=2){
				Config.setWorkPath(args[1],null);
			}
			bool ci_run = false;
			if(args[0] == "-ci")
				ci_run = true;
			Server server=new Server();
			server.Run(ci_run);//更新文件列表
		}
		private static void Download(string path,string url,bool ignore_sound){
			//线程数
			MyHttp.init(Config.ThreadNum);
			Client client=new Client(path,url);
			MyHttp.SetListner(client);
			client.Run(ignore_sound);//开始更新
		}
	}
}
