/*
 * 由SharpDevelop创建。
 * 用户： Acer
 * 日期: 2014-9-30
 * 时间: 15:45
 * 
 */
using System;
using System.IO;
using System.Text;
using System.Collections.Generic;
using System.Reflection;

namespace update
{
	/// <summary>
	/// Description of Server.
	/// </summary>
	public class Server
	{
		List<fileinfo> list;	//文件信息列表
		bool ci;

		public Server(){
			list=new List<fileinfo>();
		}

		public void Run(bool ci_run){
			ci=ci_run;
			if(ci)
				Console.WriteLine("Updating Filelist on CI... ...");				
			else
				Console.WriteLine("Updating Filelist... ...");
			list.Clear();
			AddDir(Config.workPath);//当前目录所有文件
			//版本
			MyUtil.saveText(Config.versionFile, DateTime.Now.ToString());
			//重命名列表
			MyUtil.saveText(Config.renameFile,"# Rename List (Codepage is UTF-8, please use TAB to seperate entries, Use Relative Address.)"
			                +Environment.NewLine
			                +"# An example of renaming a file from 123456.jpg to 456789.jpg"
			                +Environment.NewLine
			                +"# pics/123456.jpg	pics/456789.jpg");
			//删除列表
			MyUtil.saveText(Config.deleteFile,"# Delete List (Codepage is UTF-8, please use TAB to seperate entries, Use Relative Address.)");
			//文件列表
			MyUtil.saveList(Config.filelistFile, list.ToArray());//文件列表
			Console.WriteLine("Filelist Updated!!");
		}
		void AddDir(string dir){
			//所有文件
			string[] files=Directory.GetFiles(dir);
			foreach(string file in files){
				AddFile(file);
			}
			//获取所有子目录
			string[] dirs=Directory.GetDirectories(dir);
			
			foreach(string d in dirs){
				if(!d.EndsWith(Path.DirectorySeparatorChar+".git",StringComparison.OrdinalIgnoreCase)
					&& !d.EndsWith(Path.DirectorySeparatorChar+"gframe",StringComparison.OrdinalIgnoreCase)
					&& !d.EndsWith(Path.DirectorySeparatorChar+"ocgcore",StringComparison.OrdinalIgnoreCase)
					&& !d.EndsWith(Path.DirectorySeparatorChar+"premake",StringComparison.OrdinalIgnoreCase)
				)
					AddDir(d);//添加子目录的所有文件
			}
		}
		void AddFile(string file){
			string filename =Path.GetFileName(file);
			string exename = System.Diagnostics.Process.GetCurrentProcess().MainModule.FileName;
			if(filename.EndsWith("Thumbs.db",StringComparison.OrdinalIgnoreCase)
			   || filename.EndsWith(".gitignore",StringComparison.OrdinalIgnoreCase)
			   || filename.EndsWith("LICENSE",StringComparison.OrdinalIgnoreCase)
			   || filename.EndsWith("appveyor.yml",StringComparison.OrdinalIgnoreCase)
			   || filename.EndsWith(".travis.yml",StringComparison.OrdinalIgnoreCase)
			   || filename.EndsWith("circle.yml",StringComparison.OrdinalIgnoreCase)
			   || filename.EndsWith("README.md",StringComparison.OrdinalIgnoreCase)
			   || filename.EndsWith("web.config",StringComparison.OrdinalIgnoreCase)
			   || filename.EndsWith("update-server.bat",StringComparison.OrdinalIgnoreCase)
			   || filename.EndsWith("desktop.ini",StringComparison.OrdinalIgnoreCase)
			   || filename.EndsWith("start.htm",StringComparison.OrdinalIgnoreCase)
			   || filename.EndsWith("update.exe.config",StringComparison.OrdinalIgnoreCase)
			   || filename.EndsWith("premake4.lua",StringComparison.OrdinalIgnoreCase)
			   || filename.EndsWith("premake5.lua",StringComparison.OrdinalIgnoreCase)
			   || file == exename
			   || file == exename+".config"
			  )
				return;
			//处理名字
			string name=file.Replace(Config.workPath,"");
			name=name.Replace(Path.DirectorySeparatorChar,'/');
			
			if(name.IndexOf('/')==0)
				name=name.Substring(1);
			
			if(MyUtil.checkList(Config.ignores, name)){
				return;
			}
			string md5=MyUtil.MD5_File(file);
			if(!ci) {
				Console.WriteLine("FILE:	"+name);
				Console.WriteLine("MD5:	"+md5);
			}
			list.Add(new fileinfo(name, md5));
		}
	}
}
