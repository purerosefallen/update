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

namespace update
{
	/// <summary>
	/// Description of Download.
	/// </summary>
	public class Client : MyHttpListener
	{
		int all_num,num;
		List<fileinfo> errorlist;
		
		public Client(string path,string url){
			if(string.IsNullOrEmpty(path))
				Config.setWorkPath(ConfigurationManager.AppSettings["path"],url);
			else
				Config.setWorkPath(path,url);
			
			errorlist=new List<fileinfo>();

			//代理设置
			if(Config.useProxy){
				Console.WriteLine("USE PROXY:"+Config.proxyIP+":"+Config.proxyPort);
				MyHttp.setProxy(true, Config.proxyIP, Config.proxyPort);
			}
			else{
				MyHttp.setProxy(false, "127.0.0.1",80);
			}
		}
		
		void Delete(){
			if(!MyHttp.DownLoad(Config.url_delete, Config.deleteFile)){
				return;
			}
			string[] lines=File.ReadAllLines(Config.deleteFile, Encoding.UTF8);
			foreach(string line in lines){
				if(!line.StartsWith("#")){
					string file=Config.GetPath(line);
					if(File.Exists(file)){
						Console.WriteLine("DELETE FILE:"+line);
						File.Delete(file);
					}
				}
			}
		}
		
		void Rename(){
			if(!MyHttp.DownLoad(Config.url_rename, Config.renameFile)){
				return;
			}
			string[] lines=File.ReadAllLines(Config.renameFile, Encoding.UTF8);
			foreach(string line in lines){
				if(!line.StartsWith("#")){
					string[] files=line.Split('\t');
					if(files.Length>=2){
						string file1=Config.GetPath(files[0]);
						string file2=Config.GetPath(files[1]);
						Console.WriteLine("RENAME:"+files[0]+"=>"+files[1]);
						File.Move(file1,file2);
					}
				}
			}
		}
		
		public void OnStart(string name,string file){
			//Console.WriteLine("开始下载："+name);
			//Console.WriteLine("保存到："+file);
		}
		
		public void OnEnd(fileinfo ff,bool isOK){
			if(all_num>0)
				showProcess(num++,all_num);
			if(!isOK){
				if(ff!=null){
					Console.WriteLine("DOWNLOAD FAILED:"+Config.GetUrl(ff.name));
					errorlist.Add(ff);
				}else{
					Console.WriteLine("DOWNLOAD FAILED");
				}
			}else{
				if(ff!=null){
					Console.WriteLine("DOWNLOAD COMPLETE:"+ff.name);
				}
			}
		}
		
		void showProcess(int i,int all){
			Console.Title=string.Format("PROGRESS:{0}/{1}",i,all);
		}
		
		bool Download(string name,string md5,bool isHide){
			string file=Config.GetPath(name);
			
			if(File.Exists(file)){
				if(md5==MyUtil.MD5_File(file)){//一致
					Console.WriteLine("SKIPPED:"+name);
					showProcess(num++,all_num);
					return true;
				}
				else{
					if(MyUtil.checkList(Config.ignores,name)){//忽略更新
						Console.WriteLine("IGNORED:"+name);
						showProcess(num++,all_num);
						return true;
					}
				}
			}
			//线程已满
			while(MyHttp.NUM>=MyHttp.MAX_NUM){
				//System.Threading.Thread.Sleep(100);
			}
			//下载文件
			new MyHttp(Config.GetUrl(name), file, new fileinfo(name,md5)).Start();
			return true;
			//return MyHttp.DownLoad(url_download+name,file);
		}
		
		void Update(){
			if(!File.Exists(Config.errorFile)){//上一次下载是否失败
				Console.WriteLine("Downloading Filelist... ...");
				if(!MyHttp.DownLoad(Config.url_filelist, Config.filelistFile))
					return;
				Console.WriteLine("Starting Update... ...");
			}else{
				File.Delete(Config.filelistFile);
				File.Move(Config.errorFile, Config.filelistFile);
				Console.WriteLine("Continuing Update... ...");
			}

			string[] lines=File.ReadAllLines(Config.filelistFile, Encoding.UTF8);
			all_num=lines.Length;
			num=0;
			showProcess(num++,all_num);
			foreach(string line in lines){
				if(!line.StartsWith("#")){
					string[] words=line.Split('\t');
					if(words.Length>=2){
						Download(words[0], words[1],false);
					}
				}
			}
			while(!MyHttp.isOK()){

			}
			if(errorlist.Count>0){
				Console.WriteLine("Some of files failed to update... ...");
				MyUtil.saveList(Config.errorFile, errorlist.ToArray());
			}
		}
		void ShowTask(int n){
			if(n==0)
				return;
			Console.WriteLine(string.Format("{0} Files Remaining... ...", n));
		}
		
		public void Run(){
			Console.WriteLine("UPDATE FROM:"+Config.url_home);
			Console.WriteLine("DOWNLOAD TO:"+Config.workPath);
			Console.WriteLine("CONFIG FILE:"+Assembly.GetExecutingAssembly().Location+".config");

			if(!File.Exists(Config.errorFile)){
				Console.WriteLine("Getting New Version ... ...");
				//version
				MyHttp.DownLoad(Config.url_version, Config.newVersionFile);
				//版本号一致
				string md5_1=MyUtil.MD5_File(Config.versionFile);
				string md5_2=MyUtil.MD5_File(Config.newVersionFile);
				if(md5_1 == md5_2 && md5_1.Length>0){
					Console.WriteLine("Your files are already up-to-date.");
					return;
				}
				Console.WriteLine("New Version Discovered... ...");
				//删除旧文件
				Delete();
				//重命名文件
				Rename();
			}
			Console.Clear();
			//filelist
			Update();
			if(File.Exists(Config.newVersionFile)){
				File.Delete(Config.versionFile);
				File.Move(Config.newVersionFile, Config.versionFile);
			}
			Console.WriteLine("UPDATE COMPLETE!! You can safely close this window, press any key to quit.");
		}
	}
}
